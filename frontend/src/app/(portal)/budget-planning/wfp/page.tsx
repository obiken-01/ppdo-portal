"use client";

/**
 * WFP (Work and Financial Plan) page — RAL-68.
 *
 * Shows the full AIP activity hierarchy for a selected AIP + config office pair.
 * The config office is matched to the AIP office hierarchy using `officeRefCode`
 * (the last segment of the AIP office ref code, e.g. "013" from "3000-000-1-01-013").
 * Set `officeRefCode` in Config → Offices for each office before using this page.
 *
 * Users enter PS/MOOE/CO expenditure lines per activity via a popup modal.
 * Draft lines persist to localStorage (keyed by aipId + officeId).
 *
 * Access: canAccessBudgetPlanning. Unlock requires canManageConfig.
 *
 * Endpoints (WfpFunctions.cs, { data, error, message } envelope):
 *   GET  /api/budget-planning/wfp?aipRecordId=&officeId=
 *   GET  /api/budget-planning/wfp/{id}
 *   POST /api/budget-planning/wfp
 *   POST /api/budget-planning/wfp/{id}/finalize
 *   POST /api/budget-planning/wfp/{id}/unlock
 */

import { Fragment, Suspense, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import { getAipSummary, listAip } from "@/lib/aip";
import {
  downloadWfpReport,
  finalizeWfp,
  getWfpById,
  listWfp,
  saveWfp,
  unlockWfp,
  wfpErrorMessage,
} from "@/lib/wfp";
import { listAccounts, listDivisions, listFundingSources, listOffices } from "@/lib/config";
import { getSetupStatus, getAllocations, getPrograms } from "@/lib/allocation";
import Modal from "@/components/ui/Modal";
import MoneyInput from "@/components/ui/MoneyInput";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import { formatMoney } from "@/lib/money";
import type {
  AipActivitySummary,
  AipRecordSummary,
  AipRecordResponse,
  AccountResponse,
  AllocationSetupStatusDto,
  DivisionAllocationDto,
  DivisionResponse,
  ProgramAssignmentDto,
  ExpenditureType,
  FundingSourceResponse,
  OfficeResponse,
  SaveWfpLine,
  WfpRecord,
} from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function officeRefSuffix(refCode: string): string {
  const parts = refCode.split("-");
  return parts.length >= 2 ? parts.slice(-2).join("-") : refCode;
}

function resolveDefaultFundingSourceId(
  snapshot: string | null,
  fundingSources: FundingSourceResponse[]
): number | null {
  if (!snapshot?.trim()) return null;
  if (/[,/]/.test(snapshot)) return null;
  const q = snapshot.trim().toLowerCase();
  return (
    fundingSources.find((f) => f.code.toLowerCase() === q)?.id ??
    fundingSources.find((f) => f.name.toLowerCase() === q)?.id ??
    fundingSources.find((f) =>
      f.description
        ?.split(";")
        .some((alias) => alias.trim().toLowerCase() === q)
    )?.id ??
    null
  );
}

function fmtCurrency(n: number): string {
  if (n === 0) return "—";
  return n.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function computeNet(line: SaveWfpLine): number {
  return line.applyReserve
    ? Math.round(line.totalAppropriation * 0.9 * 100) / 100
    : line.totalAppropriation;
}

// ---------------------------------------------------------------------------
// ExpenditurePopup
// ---------------------------------------------------------------------------

interface PopupProps {
  activity: AipActivitySummary;
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  initialLines: SaveWfpLine[];
  readonly: boolean;
  onSave: (lines: SaveWfpLine[]) => void;
  onClose: () => void;
}

const TABS: ExpenditureType[] = ["PS", "MOOE", "CO"];

const ACCOUNT_PREFIX: Record<ExpenditureType, string> = {
  PS: "5-01-",
  MOOE: "5-02-",
  CO: "5-03-",
};

const TAB_COLORS: Record<ExpenditureType, { active: string; bg: string; addBtn: string; borderB: string }> = {
  PS:   { active: "border-sky-600 text-sky-700 bg-sky-50",          bg: "bg-sky-50",    addBtn: "text-sky-700 hover:text-sky-600",    borderB: "border-sky-300" },
  MOOE: { active: "border-amber-500 text-amber-700 bg-amber-50",    bg: "bg-amber-50",  addBtn: "text-amber-700 hover:text-amber-600", borderB: "border-amber-300" },
  CO:   { active: "border-violet-600 text-violet-700 bg-violet-50", bg: "bg-violet-50", addBtn: "text-violet-700 hover:text-violet-600", borderB: "border-violet-300" },
};

const CF_FIELDS: [keyof SaveWfpLine, string][] = [
  ["resourcesNeeded", "Resources Needed"],
  ["responsibleUnit", "Responsible Unit / Division"],
  ["successIndicator", "Success Indicator"],
  ["meansOfVerification", "Means of Verification"],
];

// Name cell with 2-line clamp + "more/less" toggle (only shown when actually clamped)
function ClampedName({ name }: { name: string }) {
  const [expanded, setExpanded] = useState(false);
  const [isClamped, setIsClamped] = useState(false);
  const spanRef = useRef<HTMLSpanElement>(null);

  useLayoutEffect(() => {
    if (expanded) return;
    const el = spanRef.current;
    if (el) setIsClamped(el.scrollHeight > el.clientHeight);
  }, [name, expanded]);

  return (
    <>
      <span
        ref={spanRef}
        className={expanded ? undefined : "line-clamp-2"}
        title={!expanded && isClamped ? name : undefined}
      >
        {name}
      </span>
      {(isClamped || expanded) && (
        <button
          type="button"
          onClick={(e) => { e.stopPropagation(); setExpanded((p) => !p); }}
          className="ml-1 text-xs text-green-700 hover:underline whitespace-nowrap"
        >
          {expanded ? "less" : "more"}
        </button>
      )}
    </>
  );
}

// Account search combobox — replaces the plain <select> for object of expenditure
function AccountCombobox({
  accounts,
  value,
  onChange,
}: {
  accounts: AccountResponse[];
  value: number | null;
  onChange: (id: number | null) => void;
}) {
  const selected = accounts.find((a) => a.id === value) ?? null;
  const [query, setQuery] = useState(
    selected ? `${selected.accountTitle} (${selected.accountNumber})` : ""
  );
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const found = accounts.find((a) => a.id === value) ?? null;
    setQuery(found ? `${found.accountTitle} (${found.accountNumber})` : "");
  }, [value, accounts]);

  useEffect(() => {
    function onMouseDown(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
        const found = accounts.find((a) => a.id === value) ?? null;
        setQuery(found ? `${found.accountTitle} (${found.accountNumber})` : "");
      }
    }
    document.addEventListener("mousedown", onMouseDown);
    return () => document.removeEventListener("mousedown", onMouseDown);
  }, [value, accounts]);

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return accounts.slice(0, 30);
    return accounts
      .filter(
        (a) =>
          a.accountTitle.toLowerCase().includes(q) ||
          a.accountNumber.toLowerCase().includes(q)
      )
      .slice(0, 30);
  }, [query, accounts]);

  return (
    <div ref={containerRef} className="relative">
      <input
        type="text"
        value={query}
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setQuery(e.target.value);
          setOpen(true);
          if (!e.target.value) onChange(null);
        }}
        placeholder="Search account…"
        className="w-full border border-slate-200 text-xs px-2 py-0.5 focus:outline-none focus:ring-1 focus:ring-green-500"
      />
      {open && filtered.length > 0 && (
        <div className="absolute z-50 top-full left-0 w-80 bg-white border border-slate-200 shadow-lg max-h-52 overflow-y-auto">
          {filtered.map((a) => (
            <button
              key={a.id}
              type="button"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => {
                onChange(a.id);
                setQuery(`${a.accountTitle} (${a.accountNumber})`);
                setOpen(false);
              }}
              className="w-full text-left px-3 py-1.5 text-xs hover:bg-green-50 hover:text-green-800 border-b border-slate-50 last:border-0"
            >
              <span className="font-mono text-slate-400 mr-2">{a.accountNumber}</span>
              {a.accountTitle}
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function ExpenditurePopup({
  activity,
  accounts,
  fundingSources,
  initialLines,
  readonly,
  onSave,
  onClose,
}: PopupProps) {
  const [activeTab, setActiveTab] = useState<ExpenditureType>("PS");
  const [localLines, setLocalLines] = useState<SaveWfpLine[]>(() => [...initialLines]);

  const validationErrors = useMemo(() => {
    const errs: string[] = [];
    localLines.forEach((l, i) => {
      const net = computeNet(l);
      const quarterly = l.q1 + l.q2 + l.q3 + l.q4;
      if (net > 0 && quarterly > net + 0.001)
        errs.push(`Line ${i + 1} (${l.expenditureType}): quarterly total ${formatMoney(quarterly)} exceeds net appropriation ${formatMoney(net)}.`);
    });
    const aipBudget = activity.total != null ? activity.total * 1000 : null;
    if (aipBudget != null) {
      const totalApprop = localLines.reduce((sum, l) => sum + l.totalAppropriation, 0);
      if (totalApprop > aipBudget + 0.001)
        errs.push(`Total appropriation ${formatMoney(totalApprop)} exceeds the AIP budget of ${formatMoney(aipBudget)}.`);
    }
    return errs;
  }, [localLines, activity.total]);

  const tabAccounts = useMemo(
    () => accounts.filter((a) => a.accountNumber.startsWith(ACCOUNT_PREFIX[activeTab])),
    [accounts, activeTab]
  );

  const tabLines = useMemo(
    () =>
      localLines
        .map((line, idx) => ({ line, idx }))
        .filter(({ line }) => line.expenditureType === activeTab),
    [localLines, activeTab]
  );

  function addLine() {
    const sortOrder = localLines.filter((l) => l.expenditureType === activeTab).length;
    setLocalLines((prev) => [
      ...prev,
      {
        expenditureType: activeTab,
        resourcesNeeded: null,
        responsibleUnit: null,
        successIndicator: null,
        meansOfVerification: null,
        accountId: null,
        totalAppropriation: 0,
        applyReserve: false,
        q1: 0,
        q2: 0,
        q3: 0,
        q4: 0,
        fundingSourceId: resolveDefaultFundingSourceId(activity.fundingSourceSnapshot, fundingSources),
        sortOrder,
      },
    ]);
  }

  function removeLine(idx: number) {
    setLocalLines((prev) => prev.filter((_, i) => i !== idx));
  }

  function updateLine(idx: number, updates: Partial<SaveWfpLine>) {
    setLocalLines((prev) =>
      prev.map((l, i) => (i === idx ? { ...l, ...updates } : l))
    );
  }

  function handleSave() {
    if (validationErrors.length > 0) return;
    onSave(localLines.map((l, i) => ({ ...l, sortOrder: i })));
  }

  const colCount = readonly ? 11 : 12;

  const hasErrors = validationErrors.length > 0;

  return (
    <Modal
      title={
        <div>
          <div className="font-mono text-xs font-normal text-slate-500 mb-0.5 tracking-wide">
            {activity.refCode}
          </div>
          <div className="text-base font-semibold text-slate-800 truncate max-w-2xl">
            {activity.name}
          </div>
        </div>
      }
      size="2xl"
      fixedHeight
      onClose={onClose}
      footer={
        readonly ? (
          <Modal.SecondaryButton onClick={onClose}>Close</Modal.SecondaryButton>
        ) : (
          <>
            <Modal.SecondaryButton onClick={onClose}>Cancel</Modal.SecondaryButton>
            <Modal.PrimaryButton onClick={handleSave} disabled={hasErrors}>
              Save Changes
            </Modal.PrimaryButton>
          </>
        )
      }
    >
      {/* Validation errors */}
      {hasErrors && (
        <div className="mb-3 px-3 py-2 bg-red-50 border border-red-200 text-xs text-red-700">
          {validationErrors.map((e, i) => <p key={i}>{e}</p>)}
        </div>
      )}

      {/* AIP Budget summary */}
      <p className="mb-4 text-sm text-slate-600">
        AIP Budget:{" "}
        <span className="font-semibold tabular-nums text-slate-800">
          {activity.total != null ? formatMoney(activity.total * 1000) : "—"}
        </span>
      </p>

      {/* Tabs */}
      <div className={`flex border-b mb-0 -mx-6 px-6 ${TAB_COLORS[activeTab].borderB}`}>
        {TABS.map((tab) => (
          <button
            key={tab}
            onClick={() => setActiveTab(tab)}
            className={`px-5 py-2 text-sm font-medium border-b-2 transition-colors ${
              activeTab === tab
                ? TAB_COLORS[tab].active
                : "border-transparent text-slate-500 hover:text-slate-700"
            }`}
          >
            {tab}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div className={`-mx-6 px-6 pt-4 pb-3 ${TAB_COLORS[activeTab].bg}`}>
      <div className="overflow-x-auto overflow-y-hidden">
        <table className="w-full text-xs border-collapse min-w-[900px]">
          <thead>
            <tr className="bg-slate-50 text-slate-600 text-left border-b border-slate-200">
              <th className="px-2 py-1.5 whitespace-nowrap">ACCT CODE</th>
              <th className="px-2 py-1.5 whitespace-nowrap w-56">OBJECT OF EXPENDITURE</th>
              <th className="px-2 py-1.5 text-right whitespace-nowrap">TOTAL APPROP</th>
              <th className="px-2 py-1.5 text-center whitespace-nowrap">RESERVE</th>
              <th className="px-2 py-1.5 text-right whitespace-nowrap">NET</th>
              <th className="px-2 py-1.5 text-right">Q1</th>
              <th className="px-2 py-1.5 text-right">Q2</th>
              <th className="px-2 py-1.5 text-right">Q3</th>
              <th className="px-2 py-1.5 text-right">Q4</th>
              <th className="px-2 py-1.5 text-right whitespace-nowrap">LINE TOTAL</th>
              <th className="px-2 py-1.5 whitespace-nowrap">SOURCE</th>
              {!readonly && <th className="px-2 py-1.5 w-6" />}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {tabLines.length === 0 ? (
              <tr>
                <td colSpan={colCount} className="px-3 py-6 text-slate-400 text-center">
                  No {activeTab} lines.{!readonly && ' Click "+ Add Line" to add one.'}
                </td>
              </tr>
            ) : (
              tabLines.map(({ line, idx }) => {
                const net = computeNet(line);
                const quarterly = line.q1 + line.q2 + line.q3 + line.q4;
                const isOver = net > 0 && quarterly > net + 0.001;
                const selectedAccount = accounts.find((a) => a.id === line.accountId);

                return (
                  <Fragment key={idx}>
                    <tr className={isOver ? "bg-red-50" : "hover:bg-slate-50"}>
                      {/* ACCT CODE */}
                      <td className="px-2 py-1.5 font-mono text-slate-600 whitespace-nowrap">
                        {selectedAccount?.accountNumber ?? "—"}
                      </td>

                      {/* OBJECT OF EXPENDITURE */}
                      <td className="px-2 py-1.5">
                        {readonly ? (
                          <span className="text-slate-700">
                            {selectedAccount
                              ? `${selectedAccount.accountTitle} (${selectedAccount.accountNumber})`
                              : "—"}
                          </span>
                        ) : (
                          <AccountCombobox
                            accounts={tabAccounts}
                            value={line.accountId}
                            onChange={(id) => updateLine(idx, { accountId: id })}
                          />
                        )}
                      </td>

                      {/* TOTAL APPROP */}
                      <td className="px-2 py-1.5 text-right">
                        {readonly ? (
                          <span>{formatMoney(line.totalAppropriation)}</span>
                        ) : (
                          <MoneyInput
                            value={line.totalAppropriation === 0 ? null : line.totalAppropriation}
                            onChange={(v) => updateLine(idx, { totalAppropriation: v ?? 0 })}
                            min={0}
                            className="w-28 text-xs"
                          />
                        )}
                      </td>

                      {/* RESERVE */}
                      <td className="px-2 py-1.5 text-center">
                        <input
                          type="checkbox"
                          checked={line.applyReserve}
                          disabled={readonly}
                          onChange={(e) => updateLine(idx, { applyReserve: e.target.checked })}
                        />
                      </td>

                      {/* NET */}
                      <td className="px-2 py-1.5 text-right font-medium text-slate-700 whitespace-nowrap">
                        {formatMoney(net)}
                      </td>

                      {/* Q1–Q4 */}
                      {(["q1", "q2", "q3", "q4"] as const).map((q) => (
                        <td key={q} className="px-2 py-1.5 text-right">
                          {readonly ? (
                            <span>{formatMoney(line[q])}</span>
                          ) : (
                            <MoneyInput
                              value={line[q] === 0 ? null : line[q]}
                              onChange={(v) => updateLine(idx, { [q]: v ?? 0 })}
                              min={0}
                              className="w-24 text-xs"
                            />
                          )}
                        </td>
                      ))}

                      {/* LINE TOTAL */}
                      <td
                        className={`px-2 py-1.5 text-right font-medium whitespace-nowrap ${
                          isOver ? "text-red-600" : "text-slate-700"
                        }`}
                      >
                        {formatMoney(quarterly)}
                      </td>

                      {/* SOURCE */}
                      <td className="px-2 py-1.5">
                        {readonly ? (
                          <span>
                            {fundingSources.find((f) => f.id === line.fundingSourceId)?.name ?? "—"}
                          </span>
                        ) : (
                          <select
                            value={line.fundingSourceId ?? ""}
                            onChange={(e) =>
                              updateLine(idx, {
                                fundingSourceId: e.target.value ? Number(e.target.value) : null,
                              })
                            }
                            className="border border-slate-200 text-xs px-1 py-0.5 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                          >
                            <option value="">— source —</option>
                            {fundingSources.map((f) => (
                              <option key={f.id} value={f.id}>
                                {f.name}
                              </option>
                            ))}
                          </select>
                        )}
                      </td>

                      {/* Delete */}
                      {!readonly && (
                        <td className="px-2 py-1.5 text-center">
                          <button
                            onClick={() => removeLine(idx)}
                            className="text-slate-400 hover:text-red-500 transition-colors text-base leading-none"
                            title="Remove line"
                          >
                            ×
                          </button>
                        </td>
                      )}
                    </tr>

                    {/* C–F detail fields — always visible */}
                    <tr className="bg-slate-50 border-b border-slate-100">
                      <td colSpan={colCount} className="px-3 pb-3 pt-1.5">
                        <div className="grid grid-cols-2 gap-x-6 gap-y-2">
                          {CF_FIELDS.map(([key, label]) => (
                            <div key={key} className="flex flex-col gap-0.5">
                              <span className="text-[10px] font-medium text-slate-400 uppercase tracking-wide">
                                {label}
                              </span>
                              {readonly ? (
                                <span className="text-xs text-slate-700">
                                  {(line[key] as string | null) ?? "—"}
                                </span>
                              ) : (
                                <input
                                  type="text"
                                  value={(line[key] as string | null) ?? ""}
                                  onChange={(e) =>
                                    updateLine(idx, { [key]: e.target.value || null })
                                  }
                                  placeholder={label}
                                  className="border border-slate-200 text-xs px-2 py-0.5 focus:outline-none focus:ring-1 focus:ring-green-500 bg-white"
                                />
                              )}
                            </div>
                          ))}
                        </div>
                      </td>
                    </tr>
                  </Fragment>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {/* Add Line */}
      {!readonly && (
        <button
          onClick={addLine}
          className={`mt-3 text-xs font-medium hover:underline ${TAB_COLORS[activeTab].addBtn}`}
        >
          + Add {activeTab} Line
        </button>
      )}
      </div>{/* end tab content */}

    </Modal>
  );
}

// ---------------------------------------------------------------------------
// WfpPageInner — needs useSearchParams → must be inside a Suspense boundary
// ---------------------------------------------------------------------------

function WfpPageInner() {
  const searchParams = useSearchParams();
  const { toast } = useToast();

  // ── Auth ────────────────────────────────────────────────────────────────

  const me = useMe((m) => m.canAccessBudgetPlanning);

  // ── Selector state ───────────────────────────────────────────────────────

  const [aipList, setAipList] = useState<AipRecordResponse[]>([]);
  const [officeList, setOfficeList] = useState<OfficeResponse[]>([]);
  const [divisionList, setDivisionList] = useState<DivisionResponse[]>([]);
  const [selectedAipId, setSelectedAipId] = useState<number | null>(null);
  const [selectedOfficeId, setSelectedOfficeId] = useState<number | null>(null);
  const [selectedDivisionId, setSelectedDivisionId] = useState<number | null>(null);

  // ── Loaded data ──────────────────────────────────────────────────────────

  const [aipDetail, setAipDetail] = useState<AipRecordSummary | null>(null);
  const [wfp, setWfp] = useState<WfpRecord | null>(null);
  const [accounts, setAccounts] = useState<AccountResponse[]>([]);
  const [fundingSources, setFundingSources] = useState<FundingSourceResponse[]>([]);

  // ── Division setup status + budget banner ─────────────────────────────────
  const [setupStatus, setSetupStatus] = useState<AllocationSetupStatusDto | null>(null);
  const [divisionAllocation, setDivisionAllocation] = useState<DivisionAllocationDto | null>(null);
  const [programAssignments, setProgramAssignments] = useState<ProgramAssignmentDto[]>([]);

  // ── Draft ────────────────────────────────────────────────────────────────

  const [draftLines, setDraftLines] = useState<Record<number, SaveWfpLine[]>>({});
  const [popupActivityId, setPopupActivityId] = useState<number | null>(null);
  const [hasUnsaved, setHasUnsaved] = useState(false);
  const [collapsed, setCollapsed] = useState<Set<string>>(new Set());

  function toggleCollapse(key: string) {
    setCollapsed((prev) => {
      const s = new Set(prev);
      if (s.has(key)) s.delete(key); else s.add(key);
      return s;
    });
  }

  // ── UI flags ─────────────────────────────────────────────────────────────

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [finalizing, setFinalizing] = useState(false);
  const [exporting, setExporting] = useState(false);
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  const restoreConfirmed = useRef(false);
  const fundingSourcesLoaded = useRef(false);
  const accountsLoaded = useRef(false);

  // ── Effect A: Load selector lists on mount (accounts/funding deferred) ───

  useEffect(() => {
    const urlAipId = searchParams.get("aipId");
    const urlOfficeId = searchParams.get("officeId");

    Promise.all([listAip(), listOffices({ active: "true" })])
      .then(([aips, offices]) => {
        setAipList(aips);
        setOfficeList(offices);

        if (urlAipId) setSelectedAipId(Number(urlAipId));
        // Pre-fill office from URL ?officeId= (me.officeId pre-fill handled in Effect A2)
        if (urlOfficeId) setSelectedOfficeId(Number(urlOfficeId));
      })
      .catch(() => {
        toast.error("Load failed", "Could not load AIP / office data.");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // ── Effect A2: Pre-fill office + division from me when no URL param ─────────

  useEffect(() => {
    if (!me) return;
    if (me.officeId != null && !searchParams.get("officeId")) setSelectedOfficeId(me.officeId);
    if (me.divisionId != null) setSelectedDivisionId(me.divisionId);
  }, [me, searchParams]);

  // ── Effect A3: Load division list when office changes ─────────────────────

  useEffect(() => {
    if (selectedOfficeId == null) { setDivisionList([]); return; }
    listDivisions({ active: "true", officeId: selectedOfficeId }).then(setDivisionList).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedOfficeId]);

  // ── Effect B: Load AIP detail + WFP when selectors change ───────────────

  useEffect(() => {
    if (selectedAipId == null || selectedOfficeId == null) {
      setAipDetail(null);
      setWfp(null);
      setDraftLines({});
      setHasUnsaved(false);
      setSetupStatus(null);
      setDivisionAllocation(null);
      setProgramAssignments([]);
      return;
    }

    const aipId = selectedAipId;
    const officeId = selectedOfficeId;
    const divisionId = selectedDivisionId;
    let cancelled = false;

    async function load() {
      setLoading(true);
      setAipDetail(null);
      setWfp(null);
      setDraftLines({});
      setHasUnsaved(false);

      try {
        const fetchFunds: Promise<FundingSourceResponse[] | null> = !fundingSourcesLoaded.current
          ? listFundingSources({ active: "true" })
          : Promise.resolve(null);
        const fetchAccts: Promise<AccountResponse[] | null> = !accountsLoaded.current
          ? listAccounts({ active: "true" })
          : Promise.resolve(null);

        const wfpParams = divisionId != null
          ? { aipRecordId: aipId, officeId, divisionId }
          : { aipRecordId: aipId, officeId };

        const [detail, wfpList, newFunds, newAccts] = await Promise.all([
          getAipSummary(aipId),
          listWfp(wfpParams),
          fetchFunds,
          fetchAccts,
        ]);
        if (cancelled) return;

        setAipDetail(detail);
        if (newFunds) { setFundingSources(newFunds); fundingSourcesLoaded.current = true; }
        if (newAccts) { setAccounts(newAccts); accountsLoaded.current = true; }

        // Load setup gate + division budget + program assignments when a division is selected
        if (divisionId != null) {
          const [status, allocs, assignments] = await Promise.all([
            getSetupStatus(officeId, detail.fiscalYear, divisionId),
            getAllocations(officeId, detail.fiscalYear),
            getPrograms(officeId, detail.fiscalYear),
          ]);
          if (!cancelled) {
            setSetupStatus(status);
            setDivisionAllocation(allocs.find((a) => a.divisionId === divisionId) ?? null);
            setProgramAssignments(assignments);
          }
        } else {
          setSetupStatus(null);
          setDivisionAllocation(null);
          setProgramAssignments([]);
        }

        const record = wfpList[0] ?? null;
        setWfp(record);

        if (record) {
          const fullDetail = await getWfpById(record.id);
          if (cancelled) return;

          const initial: Record<number, SaveWfpLine[]> = {};
          for (const act of fullDetail.activities) {
            initial[act.aipActivityId] = act.lines.map((l) => ({
              expenditureType: l.expenditureType,
              resourcesNeeded: l.resourcesNeeded,
              responsibleUnit: l.responsibleUnit,
              successIndicator: l.successIndicator,
              meansOfVerification: l.meansOfVerification,
              accountId: l.accountId,
              totalAppropriation: l.totalAppropriation,
              applyReserve: l.applyReserve,
              q1: l.q1,
              q2: l.q2,
              q3: l.q3,
              q4: l.q4,
              fundingSourceId: l.fundingSourceId,
              sortOrder: l.sortOrder,
            }));
          }
          setDraftLines(initial);

          // Check localStorage for a newer local draft
          const lsKey = `wfp_draft_${aipId}_${officeId}_${divisionId ?? "null"}`;
          const stored = localStorage.getItem(lsKey);
          if (stored) {
            try {
              const parsed = JSON.parse(stored) as {
                savedAt: string;
                draftLines: Record<string, SaveWfpLine[]>;
              };
              const serverTs = record.updatedAt ?? record.createdAt;
              if (new Date(parsed.savedAt) > new Date(serverTs)) {
                const restoredLines = Object.fromEntries(
                  Object.entries(parsed.draftLines).map(([k, v]) => [Number(k), v])
                ) as Record<number, SaveWfpLine[]>;

                restoreConfirmed.current = false;
                setConfirm({
                  title: "Restore Unsaved Draft",
                  message: `A local draft was saved at ${new Date(
                    parsed.savedAt
                  ).toLocaleString("en-PH")}. Restore it?`,
                  confirmLabel: "Restore",
                  cancelLabel: "Discard",
                  variant: "primary",
                  onConfirm: () => {
                    restoreConfirmed.current = true;
                    setDraftLines(restoredLines);
                    setHasUnsaved(true);
                  },
                  onClose: () => {
                    if (!restoreConfirmed.current) localStorage.removeItem(lsKey);
                    restoreConfirmed.current = false;
                    setConfirm(null);
                  },
                });
              }
            } catch {
              // Ignore corrupt localStorage entry
            }
          }
        }
      } catch (err) {
        if (!cancelled) {
          toast.error("Load failed", wfpErrorMessage(err, "Could not load WFP data."));
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    load();
    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedAipId, selectedOfficeId, selectedDivisionId]);

  // ── Derived: match config office to AIP office via officeRefCode ──────────

  const selectedConfigOffice = officeList.find((o) => o.id === selectedOfficeId) ?? null;

  const aipOffices = useMemo(() => {
    if (!aipDetail || !selectedConfigOffice?.officeRefCode) return [];
    const offices = aipDetail.offices.filter(
      (o) => officeRefSuffix(o.refCode) === selectedConfigOffice.officeRefCode
    );

    // When no division selected (bypass user), show all programs
    if (selectedDivisionId == null) return offices;

    // Filter programs to only those assigned to this division
    const assignedRefs = new Set(
      programAssignments
        .filter((a) => a.divisionIds.includes(selectedDivisionId))
        .map((a) => a.programRefCode)
    );
    return offices
      .map((o) => ({ ...o, programs: o.programs.filter((p) => assignedRefs.has(p.refCode)) }))
      .filter((o) => o.programs.length > 0);
  }, [aipDetail, selectedConfigOffice, selectedDivisionId, programAssignments]);

  // ── Popup activity lookup ─────────────────────────────────────────────────

  const popupActivity = useMemo<AipActivitySummary | null>(() => {
    if (popupActivityId === null || !aipDetail) return null;
    for (const office of aipDetail.offices) {
      for (const program of office.programs) {
        for (const project of program.projects) {
          const act = project.activities.find((a) => a.id === popupActivityId);
          if (act) return act;
        }
      }
    }
    return null;
  }, [popupActivityId, aipDetail]);

  // ── Grid helpers ─────────────────────────────────────────────────────────

  function sumNet(activityId: number, type: ExpenditureType): number {
    return (draftLines[activityId] ?? [])
      .filter((l) => l.expenditureType === type)
      .reduce((sum, l) => sum + computeNet(l), 0);
  }

  function sumQ(activityId: number, quarter: "q1" | "q2" | "q3" | "q4"): number {
    return (draftLines[activityId] ?? []).reduce((sum, l) => sum + l[quarter], 0);
  }

  // ── Popup save ────────────────────────────────────────────────────────────

  function handlePopupSave(lines: SaveWfpLine[]) {
    if (popupActivityId === null) return;
    const newDraft = { ...draftLines, [popupActivityId]: lines };
    setDraftLines(newDraft);
    setPopupActivityId(null);
    setHasUnsaved(true);
    if (selectedAipId != null && selectedOfficeId != null) {
      localStorage.setItem(
        `wfp_draft_${selectedAipId}_${selectedOfficeId}_${selectedDivisionId ?? "null"}`,
        JSON.stringify({ savedAt: new Date().toISOString(), draftLines: newDraft })
      );
    }
  }

  // ── Page-level Save ───────────────────────────────────────────────────────

  async function handleSave() {
    if (!aipDetail || selectedAipId == null || selectedOfficeId == null) return;

    const activities = Object.entries(draftLines)
      .filter(([, lines]) => lines.length > 0)
      .map(([id, lines]) => ({ aipActivityId: Number(id), lines }));

    setSaving(true);
    try {
      const saved = await saveWfp({
        aipRecordId: selectedAipId,
        officeId: selectedOfficeId,
        fiscalYear: aipDetail.fiscalYear,
        divisionId: selectedDivisionId,
        activities,
      });
      setWfp(saved);
      localStorage.removeItem(`wfp_draft_${selectedAipId}_${selectedOfficeId}_${selectedDivisionId ?? "null"}`);
      setHasUnsaved(false);
      toast.success("Saved", "WFP draft saved successfully.");
    } catch (err) {
      toast.error("Save failed", wfpErrorMessage(err, "Could not save WFP."));
    } finally {
      setSaving(false);
    }
  }

  // ── Finalize ──────────────────────────────────────────────────────────────

  function handleFinalize() {
    if (!wfp) return;
    setConfirm({
      title: "Finalize WFP",
      message:
        "Finalize this WFP? Once finalized, no further edits can be made unless an admin unlocks it.",
      confirmLabel: "Finalize",
      cancelLabel: "Cancel",
      variant: "primary",
      onConfirm: async () => {
        setConfirm(null);
        setFinalizing(true);
        try {
          const updated = await finalizeWfp(wfp.id);
          setWfp(updated);
          toast.success("Finalized", "WFP is now Final.");
        } catch (err) {
          toast.error("Failed", wfpErrorMessage(err, "Could not finalize WFP."));
        } finally {
          setFinalizing(false);
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  // ── Unlock ────────────────────────────────────────────────────────────────

  function handleUnlock() {
    if (!wfp) return;
    setConfirm({
      title: "Unlock WFP",
      message: "Unlock this WFP to allow further editing?",
      confirmLabel: "Unlock",
      cancelLabel: "Cancel",
      variant: "warning",
      onConfirm: async () => {
        setConfirm(null);
        try {
          const updated = await unlockWfp(wfp.id);
          setWfp(updated);
          toast.success("Unlocked", "WFP is now editable again.");
        } catch (err) {
          toast.error("Failed", wfpErrorMessage(err, "Could not unlock WFP."));
        }
      },
      onClose: () => setConfirm(null),
    });
  }

  // ── Export Excel ──────────────────────────────────────────────────────────

  async function handleExport() {
    if (!wfp) return;
    setExporting(true);
    try {
      const officeCode = selectedConfigOffice?.officeCode ?? "PPDO";
      await downloadWfpReport(wfp.id, `${officeCode}-WFP-FY${wfp.fiscalYear}.xlsx`);
    } catch {
      toast.error("Failed", "Could not export WFP report.");
    } finally {
      setExporting(false);
    }
  }

  // ── Derived flags ─────────────────────────────────────────────────────────

  const isFinal = wfp?.status === "Final";
  const isOfficeUser = me != null && me.officeId != null;
  const canBypassDivision =
    me?.role === "SuperAdmin" || me?.role === "Admin" || me?.canManageAllocation === true;

  // Gross total of all draft expenditure lines (in pesos — no ×1000 here).
  // AIP totals are stored in thousands; division allocation is in pesos.
  // Per D5: validation uses GROSS (totalAppropriation, not net).
  const divisionGrossTotal = useMemo(
    () =>
      Object.values(draftLines)
        .flat()
        .reduce((sum, l) => sum + (l.totalAppropriation ?? 0), 0),
    [draftLines]
  );

  const setupComplete =
    setupStatus == null ||
    (setupStatus.hasCeiling && setupStatus.hasAllocation && setupStatus.hasProgramAssignment);

  const canSave =
    aipDetail != null &&
    selectedAipId != null &&
    selectedOfficeId != null &&
    !isFinal &&
    !saving &&
    (canBypassDivision || selectedDivisionId != null) &&
    setupComplete;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col min-h-full">
      <div className="p-6 max-w-screen-xl mx-auto w-full flex-1">

        {/* Header */}
        <div className="flex items-start justify-between mb-5">
          <div>
            <h1 className="text-xl font-bold text-slate-800">Work and Financial Plan</h1>
            {aipDetail && selectedConfigOffice && (
              <p className="text-sm text-slate-500 mt-0.5">
                AIP FY{aipDetail.fiscalYear} — {selectedConfigOffice.officeName}
                {wfp && (
                  <span
                    className={`ml-2 px-2 py-0.5 text-xs font-medium ${
                      isFinal
                        ? "bg-green-100 text-green-700"
                        : "bg-amber-100 text-amber-700"
                    }`}
                  >
                    {wfp.status}
                  </span>
                )}
              </p>
            )}
          </div>

          <div className="flex items-center gap-2">
            {wfp && (
              <button
                onClick={handleExport}
                disabled={exporting}
                className="px-4 py-2 text-sm border border-slate-300 text-slate-600 bg-white hover:bg-slate-50 transition-colors font-medium flex items-center gap-2"
              >
                {exporting && (
                  <span className="w-4 h-4 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" />
                )}
                Export Excel
              </button>
            )}
            {me?.canManageConfig && isFinal && wfp && (
              <button
                onClick={handleUnlock}
                className="px-4 py-2 text-sm border border-slate-300 text-slate-600 bg-white hover:bg-slate-50 transition-colors font-medium"
              >
                Unlock
              </button>
            )}
            {!isFinal && wfp && (
              <button
                onClick={handleFinalize}
                disabled={finalizing}
                className="px-4 py-2 bg-green-700 text-white text-sm font-medium hover:bg-green-800 transition-colors disabled:opacity-60 flex items-center gap-2"
              >
                {finalizing && (
                  <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
                )}
                Finalize
              </button>
            )}
          </div>
        </div>

        {/* Selector row */}
        <div className="flex flex-wrap items-center gap-3 mb-5">
          {/* AIP selector */}
          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-600 font-medium whitespace-nowrap">AIP</label>
            <select
              value={selectedAipId ?? ""}
              onChange={(e) => {
                setSelectedAipId(e.target.value ? Number(e.target.value) : null);
              }}
              disabled={isFinal}
              className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:opacity-60"
            >
              <option value="">— select AIP —</option>
              {aipList.map((a) => (
                <option key={a.id} value={a.id}>
                  AIP FY{a.fiscalYear} ({a.status})
                </option>
              ))}
            </select>
          </div>

          {/* Office selector (config offices) */}
          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-600 font-medium whitespace-nowrap">Office</label>
            <select
              value={selectedOfficeId ?? ""}
              onChange={(e) => {
                setSelectedOfficeId(e.target.value ? Number(e.target.value) : null);
                setSelectedDivisionId(null);
              }}
              disabled={isFinal || isOfficeUser}
              className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:opacity-60"
            >
              <option value="">— select office —</option>
              {officeList.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.officeCode} — {o.officeName}
                </option>
              ))}
            </select>
          </div>

          {/* Division selector */}
          {selectedOfficeId != null && (
            <div className="flex items-center gap-2">
              <label className="text-sm text-slate-600 font-medium whitespace-nowrap">Division</label>
              {canBypassDivision ? (
                <select
                  value={selectedDivisionId ?? ""}
                  onChange={(e) => setSelectedDivisionId(e.target.value ? Number(e.target.value) : null)}
                  className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
                >
                  <option value="">— all divisions —</option>
                  {divisionList.map((d) => (
                    <option key={d.id} value={d.id}>{d.name}</option>
                  ))}
                </select>
              ) : (
                <span className="text-sm text-slate-700 px-2 py-1.5 border border-slate-200 bg-slate-50">
                  {divisionList.find((d) => d.id === selectedDivisionId)?.name ?? me?.division ?? "—"}
                </span>
              )}
            </div>
          )}
        </div>

        {/* Setup-incomplete banner */}
        {setupStatus != null && !setupComplete && (
          <div className="mb-4 px-4 py-3 bg-amber-50 border border-amber-300 text-amber-800 text-sm flex flex-col gap-1">
            <span className="font-semibold">WFP entry is blocked — allocation setup incomplete:</span>
            <ul className="list-disc list-inside">
              {!setupStatus.hasCeiling && <li>No budget ceiling set for this office and fiscal year.</li>}
              {!setupStatus.hasAllocation && <li>No division allocation set for this division.</li>}
              {!setupStatus.hasProgramAssignment && <li>No programs have been assigned to this division.</li>}
            </ul>
            <span className="text-xs text-amber-700">Go to Budget Planning → Allocation to complete setup.</span>
          </div>
        )}

        {/* Division-budget banner */}
        {divisionAllocation != null && selectedDivisionId != null && (
          <div className="mb-4 px-4 py-2.5 bg-blue-50 border border-blue-200 text-blue-800 text-sm flex flex-wrap items-center gap-x-6 gap-y-1">
            <span className="font-semibold">Division Budget:</span>
            <span>Allocated: <strong>{formatMoney(divisionAllocation.amount)}</strong></span>
            <span>Used (gross): <strong>{formatMoney(divisionGrossTotal)}</strong></span>
            <span className={divisionGrossTotal > divisionAllocation.amount ? "text-red-600 font-semibold" : ""}>
              Remaining: <strong>{formatMoney(divisionAllocation.amount - divisionGrossTotal)}</strong>
            </span>
            {divisionGrossTotal > divisionAllocation.amount && (
              <span className="text-red-600 font-semibold">⚠ Exceeds allocation</span>
            )}
          </div>
        )}

        {/* Grid area */}
        {loading ? (
          <div className="flex items-center gap-2 text-slate-500 text-sm py-8">
            <span className="w-4 h-4 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
            Loading…
          </div>
        ) : !selectedAipId || !selectedOfficeId ? (
          <p className="text-slate-400 text-sm py-8">
            Select an AIP and an office to view the WFP grid.
          </p>
        ) : !setupComplete ? (
          <p className="text-slate-400 text-sm py-8">
            Complete the allocation setup for this division to start entering WFP data.
          </p>
        ) : aipOffices.length === 0 ? (
          <p className="text-slate-400 text-sm py-8">
            {aipDetail
              ? selectedConfigOffice?.officeRefCode
                ? "This office has no activities in the selected AIP."
                : "AIP ref code suffix not configured for this office. Set it in Config → Offices, then try again."
              : "Loading AIP details…"}
          </p>
        ) : (
          <div className="border border-slate-200">
            <table className="w-full text-sm border-collapse min-w-[960px]">
              <thead className="sticky top-0 z-10">
                <tr className="bg-slate-50 text-xs text-slate-600 text-left border-b border-slate-200">
                  <th className="px-3 py-2 whitespace-nowrap w-36">AIP REF CODE</th>
                  <th className="px-3 py-2">PROGRAM / PROJECT / ACTIVITY</th>
                  <th className="px-3 py-2 whitespace-nowrap">FUND SOURCE</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">PS</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">MOOE</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">CO</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">TOTAL</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">AIP BUDGET</th>
                  <th className="px-3 py-2 text-right">Q1</th>
                  <th className="px-3 py-2 text-right">Q2</th>
                  <th className="px-3 py-2 text-right">Q3</th>
                  <th className="px-3 py-2 text-right">Q4</th>
                  <th className="px-3 py-2 text-center sticky right-0 bg-slate-50 border-l border-slate-200">ACTIONS</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {aipOffices.map((aipOffice) => {
                  const sKey = `sector-${aipOffice.id}`;
                  const oKey = `office-${aipOffice.id}`;
                  return (
                    <Fragment key={aipOffice.id}>
                      {/* Sector header */}
                      <tr className="bg-slate-200">
                        <td
                          colSpan={12}
                          className="px-3 py-1.5 text-xs font-bold text-slate-700 uppercase tracking-wide"
                        >
                          <button
                            onClick={() => toggleCollapse(sKey)}
                            className="mr-2 text-slate-500 hover:text-slate-700 leading-none"
                          >
                            {collapsed.has(sKey) ? "▶" : "▼"}
                          </button>
                          {aipOffice.sector}
                        </td>
                        <td className="sticky right-0 bg-slate-200 border-l border-slate-300" />
                      </tr>

                      {!collapsed.has(sKey) && (
                        <>
                          {/* Office row */}
                          <tr className="bg-slate-100">
                            <td className="px-3 py-2 font-mono text-xs text-slate-600 whitespace-nowrap">
                              {aipOffice.refCode}
                            </td>
                            <td className="px-3 py-2 font-semibold text-slate-800">
                              <button
                                onClick={() => toggleCollapse(oKey)}
                                className="mr-1.5 text-slate-400 hover:text-slate-600 leading-none"
                              >
                                {collapsed.has(oKey) ? "▶" : "▼"}
                              </button>
                              {aipOffice.name}
                            </td>
                            <td className="px-3 py-2" />
                            {Array.from({ length: 9 }, (_, i) => (
                              <td key={i} className="px-3 py-2 text-right text-slate-400 text-xs">—</td>
                            ))}
                            <td className="sticky right-0 bg-slate-100 border-l border-slate-200" />
                          </tr>

                          {/* Programs → Projects → Activities */}
                          {!collapsed.has(oKey) && aipOffice.programs.map((program) => {
                            const pKey = `program-${program.id}`;
                            return (
                              <Fragment key={program.id}>
                                <tr className="bg-blue-50">
                                  <td className="px-3 py-2 font-mono text-xs text-slate-600 whitespace-nowrap">
                                    {program.refCode}
                                  </td>
                                  <td className="px-3 py-2 pl-6 font-semibold text-slate-700">
                                    <div className="flex items-start gap-1">
                                      <button
                                        onClick={() => toggleCollapse(pKey)}
                                        className="mt-0.5 shrink-0 text-slate-400 hover:text-slate-600 leading-none"
                                      >
                                        {collapsed.has(pKey) ? "▶" : "▼"}
                                      </button>
                                      <ClampedName name={program.name} />
                                    </div>
                                  </td>
                                  <td className="px-3 py-2" />
                                  {Array.from({ length: 9 }, (_, i) => (
                                    <td key={i} className="px-3 py-2 text-right text-slate-400 text-xs">—</td>
                                  ))}
                                  <td className="sticky right-0 bg-blue-50 border-l border-slate-200" />
                                </tr>

                                {!collapsed.has(pKey) && program.projects.map((project) => {
                                  const prKey = `project-${project.id}`;
                                  return (
                                    <Fragment key={project.id}>
                                      <tr className="bg-green-50">
                                        <td className="px-3 py-2 font-mono text-xs text-slate-600 whitespace-nowrap">
                                          {project.refCode}
                                        </td>
                                        <td className="px-3 py-2 pl-10 font-medium text-slate-700">
                                          <div className="flex items-start gap-1">
                                            <button
                                              onClick={() => toggleCollapse(prKey)}
                                              className="mt-0.5 shrink-0 text-slate-400 hover:text-slate-600 leading-none"
                                            >
                                              {collapsed.has(prKey) ? "▶" : "▼"}
                                            </button>
                                            <ClampedName name={project.name} />
                                          </div>
                                        </td>
                                        <td className="px-3 py-2" />
                                        {Array.from({ length: 9 }, (_, i) => (
                                          <td key={i} className="px-3 py-2 text-right text-slate-400 text-xs">—</td>
                                        ))}
                                        <td className="sticky right-0 bg-green-50 border-l border-slate-200" />
                                      </tr>

                                      {!collapsed.has(prKey) && project.activities.map((activity) => {
                                        const ps = sumNet(activity.id, "PS");
                                        const mooe = sumNet(activity.id, "MOOE");
                                        const co = sumNet(activity.id, "CO");
                                        const total = ps + mooe + co;
                                        const hasLines = (draftLines[activity.id]?.length ?? 0) > 0;

                                        return (
                                          <tr key={activity.id} className="group bg-white hover:bg-slate-50">
                                            <td className="px-3 py-2 font-mono text-xs text-slate-500 leading-tight whitespace-nowrap">
                                              {activity.refCode}
                                            </td>
                                            <td className="px-3 py-2 pl-14 text-slate-700">
                                              <ClampedName name={activity.name} />
                                            </td>
                                            <td className="px-3 py-2 text-xs text-slate-500 whitespace-nowrap">
                                              {activity.fundingSourceSnapshot ?? "—"}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums">
                                              {fmtCurrency(ps)}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums">
                                              {fmtCurrency(mooe)}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums">
                                              {fmtCurrency(co)}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums font-medium">
                                              {fmtCurrency(total)}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums text-slate-400 text-xs">
                                              {activity.total != null ? formatMoney(activity.total * 1000) : "—"}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums">
                                              {fmtCurrency(sumQ(activity.id, "q1"))}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums">
                                              {fmtCurrency(sumQ(activity.id, "q2"))}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums">
                                              {fmtCurrency(sumQ(activity.id, "q3"))}
                                            </td>
                                            <td className="px-3 py-2 text-right tabular-nums">
                                              {fmtCurrency(sumQ(activity.id, "q4"))}
                                            </td>
                                            <td className="px-3 py-2 text-center sticky right-0 bg-white group-hover:bg-slate-50 border-l border-slate-200">
                                              {!isFinal ? (
                                                <button
                                                  onClick={() => setPopupActivityId(activity.id)}
                                                  className="text-green-700 text-sm hover:underline"
                                                >
                                                  Edit
                                                </button>
                                              ) : hasLines ? (
                                                <button
                                                  onClick={() => setPopupActivityId(activity.id)}
                                                  className="text-slate-500 text-sm hover:underline"
                                                >
                                                  View
                                                </button>
                                              ) : null}
                                            </td>
                                          </tr>
                                        );
                                      })}
                                    </Fragment>
                                  );
                                })}
                              </Fragment>
                            );
                          })}
                        </>
                      )}
                    </Fragment>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Sticky Save footer */}
      <div className="sticky bottom-0 bg-white border-t border-slate-200 px-6 py-3 flex items-center justify-between">
        <span className="text-sm text-amber-600 font-medium">
          {hasUnsaved ? "You have unsaved changes." : ""}
        </span>
        <button
          onClick={handleSave}
          disabled={!canSave}
          className="px-5 py-2 bg-green-600 text-white text-sm font-medium hover:bg-green-500 transition-colors disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
        >
          {saving && (
            <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
          )}
          Save
        </button>
      </div>

      {/* Expenditure Popup */}
      {popupActivityId != null && popupActivity != null && (
        <ExpenditurePopup
          activity={popupActivity}
          accounts={accounts}
          fundingSources={fundingSources}
          initialLines={draftLines[popupActivityId] ?? []}
          readonly={isFinal}
          onSave={handlePopupSave}
          onClose={() => setPopupActivityId(null)}
        />
      )}

      {/* Confirm dialogs */}
      {confirm && <ConfirmDialog {...confirm} />}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page export — wraps inner component in Suspense (required for useSearchParams)
// ---------------------------------------------------------------------------

export default function WfpPage() {
  return (
    <Suspense fallback={<div className="p-6 text-slate-500 text-sm">Loading…</div>}>
      <WfpPageInner />
    </Suspense>
  );
}
