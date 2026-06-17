"use client";

/**
 * WFP (Work and Financial Plan) page — RAL-68.
 *
 * Shows the full AIP activity hierarchy for a selected AIP + office pair.
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

import { Fragment, Suspense, useEffect, useMemo, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import api from "@/lib/api";
import { getAipById, listAip } from "@/lib/aip";
import {
  finalizeWfp,
  getWfpById,
  listWfp,
  saveWfp,
  unlockWfp,
  wfpErrorMessage,
} from "@/lib/wfp";
import { listAccounts, listFundingSources, listOffices } from "@/lib/config";
import Modal from "@/components/ui/Modal";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import type {
  AipActivityDetail,
  AipRecordDetail,
  AipRecordResponse,
  AccountResponse,
  ExpenditureType,
  FundingSourceResponse,
  MeResponse,
  OfficeResponse,
  SaveWfpLine,
  WfpRecord,
} from "@/types";

// ---------------------------------------------------------------------------
// Format helpers
// ---------------------------------------------------------------------------

function fmtCurrency(n: number): string {
  if (n === 0) return "—";
  return n.toLocaleString("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function fmtNum(n: number): string {
  return n.toLocaleString("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
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
  activity: AipActivityDetail;
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

const CF_FIELDS: [keyof SaveWfpLine, string][] = [
  ["resourcesNeeded", "Resources Needed"],
  ["responsibleUnit", "Responsible Unit / Division"],
  ["successIndicator", "Success Indicator"],
  ["meansOfVerification", "Means of Verification"],
];

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
  const [expandedRows, setExpandedRows] = useState<Set<number>>(new Set());
  const [validationError, setValidationError] = useState<string | null>(null);

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
        fundingSourceId: fundingSources[0]?.id ?? null,
        sortOrder,
      },
    ]);
  }

  function removeLine(idx: number) {
    setLocalLines((prev) => prev.filter((_, i) => i !== idx));
    setExpandedRows((prev) => {
      const s = new Set(prev);
      s.delete(idx);
      return s;
    });
  }

  function updateLine(idx: number, updates: Partial<SaveWfpLine>) {
    setLocalLines((prev) =>
      prev.map((l, i) => (i === idx ? { ...l, ...updates } : l))
    );
  }

  function toggleExpand(idx: number) {
    setExpandedRows((prev) => {
      const s = new Set(prev);
      if (s.has(idx)) s.delete(idx);
      else s.add(idx);
      return s;
    });
  }

  function handleSave() {
    const errors: string[] = [];
    localLines.forEach((l, i) => {
      const net = computeNet(l);
      const quarterly = l.q1 + l.q2 + l.q3 + l.q4;
      if (quarterly > net + 0.001) {
        errors.push(
          `Line ${i + 1} (${l.expenditureType}): quarterly total ${fmtNum(quarterly)} exceeds net ${fmtNum(net)}.`
        );
      }
    });
    if (errors.length > 0) {
      setValidationError(errors.join(" "));
      return;
    }
    setValidationError(null);
    onSave(localLines.map((l, i) => ({ ...l, sortOrder: i })));
  }

  const colCount = readonly ? 12 : 13;

  return (
    <Modal
      title={`Expenditure Lines — ${activity.name}`}
      size="xl"
      onClose={onClose}
      footer={
        readonly ? (
          <Modal.SecondaryButton onClick={onClose}>Close</Modal.SecondaryButton>
        ) : (
          <>
            <Modal.SecondaryButton onClick={onClose}>Cancel</Modal.SecondaryButton>
            <Modal.PrimaryButton onClick={handleSave}>Save Changes</Modal.PrimaryButton>
          </>
        )
      }
    >
      {/* Tabs */}
      <div className="flex border-b border-slate-200 mb-4 -mx-6 px-6">
        {TABS.map((tab) => (
          <button
            key={tab}
            onClick={() => setActiveTab(tab)}
            className={`px-5 py-2 text-sm font-medium border-b-2 transition-colors ${
              activeTab === tab
                ? "border-green-600 text-green-700"
                : "border-transparent text-slate-500 hover:text-slate-700"
            }`}
          >
            {tab}
          </button>
        ))}
      </div>

      {/* Table */}
      <div className="overflow-x-auto">
        <table className="w-full text-xs border-collapse min-w-[900px]">
          <thead>
            <tr className="bg-slate-50 text-slate-600 text-left border-b border-slate-200">
              <th className="px-2 py-1.5 w-6" />
              <th className="px-2 py-1.5 whitespace-nowrap">ACCT CODE</th>
              <th className="px-2 py-1.5 whitespace-nowrap w-48">OBJECT OF EXPENDITURE</th>
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
                const isExpanded = expandedRows.has(idx);

                return (
                  <Fragment key={idx}>
                    <tr className={isOver ? "bg-red-50" : "hover:bg-slate-50"}>
                      {/* Expand toggle (C–F detail fields) */}
                      <td className="px-2 py-1.5 text-center">
                        <button
                          onClick={() => toggleExpand(idx)}
                          className="text-slate-400 hover:text-slate-600 text-xs"
                          title="Toggle detail fields (Resources Needed, Responsible Unit, etc.)"
                        >
                          {isExpanded ? "▴" : "▾"}
                        </button>
                      </td>

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
                          <select
                            value={line.accountId ?? ""}
                            onChange={(e) =>
                              updateLine(idx, {
                                accountId: e.target.value ? Number(e.target.value) : null,
                              })
                            }
                            className="w-full border border-slate-200 text-xs px-1 py-0.5 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                          >
                            <option value="">— select account —</option>
                            {tabAccounts.map((a) => (
                              <option key={a.id} value={a.id}>
                                {a.accountTitle} ({a.accountNumber})
                              </option>
                            ))}
                          </select>
                        )}
                      </td>

                      {/* TOTAL APPROP */}
                      <td className="px-2 py-1.5 text-right">
                        {readonly ? (
                          <span>{fmtNum(line.totalAppropriation)}</span>
                        ) : (
                          <input
                            type="number"
                            min={0}
                            step="0.01"
                            value={line.totalAppropriation || ""}
                            onChange={(e) =>
                              updateLine(idx, {
                                totalAppropriation: parseFloat(e.target.value) || 0,
                              })
                            }
                            className="w-24 text-right border border-slate-200 text-xs px-1 py-0.5 focus:outline-none focus:ring-1 focus:ring-green-500"
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

                      {/* NET (auto-computed) */}
                      <td className="px-2 py-1.5 text-right font-medium text-slate-700 whitespace-nowrap">
                        {fmtNum(net)}
                      </td>

                      {/* Q1–Q4 */}
                      {(["q1", "q2", "q3", "q4"] as const).map((q) => (
                        <td key={q} className="px-2 py-1.5 text-right">
                          {readonly ? (
                            <span>{fmtNum(line[q])}</span>
                          ) : (
                            <input
                              type="number"
                              min={0}
                              step="0.01"
                              value={line[q] || ""}
                              onChange={(e) =>
                                updateLine(idx, { [q]: parseFloat(e.target.value) || 0 })
                              }
                              className="w-20 text-right border border-slate-200 text-xs px-1 py-0.5 focus:outline-none focus:ring-1 focus:ring-green-500"
                            />
                          )}
                        </td>
                      ))}

                      {/* LINE TOTAL (auto-computed) */}
                      <td
                        className={`px-2 py-1.5 text-right font-medium whitespace-nowrap ${
                          isOver ? "text-red-600" : "text-slate-700"
                        }`}
                      >
                        {fmtNum(quarterly)}
                      </td>

                      {/* SOURCE */}
                      <td className="px-2 py-1.5">
                        {readonly ? (
                          <span>
                            {fundingSources.find((f) => f.id === line.fundingSourceId)?.code ??
                              "—"}
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
                                {f.code}
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

                    {/* Expanded C–F detail row */}
                    {isExpanded && (
                      <tr className="bg-slate-50 border-b border-slate-100">
                        <td />
                        <td colSpan={colCount - 1} className="px-3 pb-2 pt-1">
                          <div className="grid grid-cols-2 gap-x-6 gap-y-2">
                            {CF_FIELDS.map(([key, label]) => (
                              <div key={key} className="flex flex-col gap-0.5">
                                <span className="text-xs font-medium text-slate-500">
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
                                    className="border border-slate-200 text-xs px-2 py-0.5 focus:outline-none focus:ring-1 focus:ring-green-500"
                                  />
                                )}
                              </div>
                            ))}
                          </div>
                        </td>
                      </tr>
                    )}
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
          className="mt-3 text-xs text-green-700 hover:text-green-600 hover:underline font-medium"
        >
          + Add {activeTab} Line
        </button>
      )}

      {/* Validation error */}
      {validationError && (
        <p className="mt-3 text-xs text-red-600 bg-red-50 border border-red-200 px-3 py-2">
          {validationError}
        </p>
      )}
    </Modal>
  );
}

// ---------------------------------------------------------------------------
// WfpPageInner — needs useSearchParams → must be inside a Suspense boundary
// ---------------------------------------------------------------------------

function WfpPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { toast } = useToast();

  // ── Auth ────────────────────────────────────────────────────────────────

  const [me, setMe] = useState<MeResponse | null>(null);

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canAccessBudgetPlanning) {
        router.replace("/dashboard");
        return;
      }
      setMe(data);
    });
  }, [router]);

  // ── Selector state ───────────────────────────────────────────────────────

  const [aipList, setAipList] = useState<AipRecordResponse[]>([]);
  const [officeList, setOfficeList] = useState<OfficeResponse[]>([]);
  const [selectedAipId, setSelectedAipId] = useState<number | null>(null);
  const [selectedOfficeId, setSelectedOfficeId] = useState<number | null>(null);

  // ── Loaded data ──────────────────────────────────────────────────────────

  const [aipDetail, setAipDetail] = useState<AipRecordDetail | null>(null);
  const [wfp, setWfp] = useState<WfpRecord | null>(null);
  const [accounts, setAccounts] = useState<AccountResponse[]>([]);
  const [fundingSources, setFundingSources] = useState<FundingSourceResponse[]>([]);

  // ── Draft ────────────────────────────────────────────────────────────────

  const [draftLines, setDraftLines] = useState<Record<number, SaveWfpLine[]>>({});
  const [popupActivityId, setPopupActivityId] = useState<number | null>(null);
  const [hasUnsaved, setHasUnsaved] = useState(false);

  // ── UI flags ─────────────────────────────────────────────────────────────

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [finalizing, setFinalizing] = useState(false);
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  // Track whether the user clicked "Restore" in the draft restore dialog
  const restoreConfirmed = useRef(false);

  // ── Load selectors once on auth ──────────────────────────────────────────

  useEffect(() => {
    if (!me) return;

    const urlAipId = searchParams.get("aipId");
    const urlOfficeId = searchParams.get("officeId");

    Promise.all([
      listAip(),
      listOffices({ active: "true" }),
      listAccounts({ active: "true" }),
      listFundingSources({ active: "true" }),
    ])
      .then(([aips, offices, accts, funds]) => {
        setAipList(aips);
        setOfficeList(offices);
        setAccounts(accts);
        setFundingSources(funds);

        if (urlAipId) setSelectedAipId(Number(urlAipId));

        // Office users are locked to their own office
        if (me.officeId != null) {
          setSelectedOfficeId(me.officeId);
        } else if (urlOfficeId) {
          setSelectedOfficeId(Number(urlOfficeId));
        }
      })
      .catch(() => {
        toast.error("Load failed", "Could not load AIP / office data.");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [me]);

  // ── Load AIP detail + WFP when selection changes ─────────────────────────

  useEffect(() => {
    if (selectedAipId == null || selectedOfficeId == null) return;

    const aipId = selectedAipId;
    const officeId = selectedOfficeId;
    let cancelled = false;

    async function load() {
      setLoading(true);
      setAipDetail(null);
      setWfp(null);
      setDraftLines({});
      setHasUnsaved(false);

      try {
        const [detail, wfpList] = await Promise.all([
          getAipById(aipId),
          listWfp({ aipRecordId: aipId, officeId }),
        ]);
        if (cancelled) return;

        setAipDetail(detail);
        const record = wfpList[0] ?? null;
        setWfp(record);

        if (record) {
          const fullDetail = await getWfpById(record.id);
          if (cancelled) return;

          const initial: Record<number, SaveWfpLine[]> = {};
          for (const act of fullDetail.activities) {
            initial[act.aipActivityId] = act.expenditureLines.map((l) => ({
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
          const stored = localStorage.getItem(`wfp_draft_${aipId}_${officeId}`);
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
                  message: `A local draft was saved at ${new Date(parsed.savedAt).toLocaleString(
                    "en-PH"
                  )}. Restore it?`,
                  confirmLabel: "Restore",
                  cancelLabel: "Discard",
                  variant: "primary",
                  onConfirm: () => {
                    restoreConfirmed.current = true;
                    setDraftLines(restoredLines);
                    setHasUnsaved(true);
                  },
                  onClose: () => {
                    if (!restoreConfirmed.current) {
                      localStorage.removeItem(`wfp_draft_${aipId}_${officeId}`);
                    }
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
  }, [selectedAipId, selectedOfficeId]);

  // ── Popup activity lookup ─────────────────────────────────────────────────

  const popupActivity = useMemo<AipActivityDetail | null>(() => {
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
        `wfp_draft_${selectedAipId}_${selectedOfficeId}`,
        JSON.stringify({ savedAt: new Date().toISOString(), draftLines: newDraft })
      );
    }
  }

  // ── Page-level Save ───────────────────────────────────────────────────────

  async function handleSave() {
    if (!aipDetail || selectedAipId == null || selectedOfficeId == null) return;

    const activities = Object.entries(draftLines)
      .filter(([, lines]) => lines.length > 0)
      .map(([id, lines]) => ({ aipActivityId: Number(id), expenditureLines: lines }));

    setSaving(true);
    try {
      const saved = await saveWfp({
        aipRecordId: selectedAipId,
        officeId: selectedOfficeId,
        fiscalYear: aipDetail.fiscalYear,
        activities,
      });
      setWfp(saved);
      localStorage.removeItem(`wfp_draft_${selectedAipId}_${selectedOfficeId}`);
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

  // ── Derived state ─────────────────────────────────────────────────────────

  const selectedConfigOffice = officeList.find((o) => o.id === selectedOfficeId) ?? null;

  const aipOffice = useMemo(() => {
    if (!aipDetail || !selectedConfigOffice) return null;
    return (
      aipDetail.offices.find(
        (o) =>
          o.refCode === selectedConfigOffice.officeCode ||
          o.name === selectedConfigOffice.officeName
      ) ?? null
    );
  }, [aipDetail, selectedConfigOffice]);

  const isFinal = wfp?.status === "Final";
  const isOfficeUser = me != null && me.officeId != null;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col min-h-full">
      <div className="p-6 max-w-screen-xl mx-auto w-full flex-1">

        {/* Header */}
        <div className="flex items-start justify-between mb-5">
          <div>
            <p className="text-xs text-slate-400 mb-1">Planning / WFP</p>
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
          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-600 font-medium whitespace-nowrap">AIP</label>
            <select
              value={selectedAipId ?? ""}
              onChange={(e) => setSelectedAipId(e.target.value ? Number(e.target.value) : null)}
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

          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-600 font-medium whitespace-nowrap">
              Office
            </label>
            <select
              value={selectedOfficeId ?? ""}
              onChange={(e) =>
                setSelectedOfficeId(e.target.value ? Number(e.target.value) : null)
              }
              disabled={isFinal || isOfficeUser}
              className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600 disabled:opacity-60"
            >
              <option value="">— select office —</option>
              {officeList.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.officeName}
                </option>
              ))}
            </select>
          </div>
        </div>

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
        ) : !aipOffice ? (
          <p className="text-slate-400 text-sm py-8">
            {aipDetail
              ? "This office has no activities in the selected AIP."
              : "Loading AIP details…"}
          </p>
        ) : (
          <div className="overflow-x-auto border border-slate-200">
            <table className="w-full text-sm border-collapse min-w-[960px]">
              <thead>
                <tr className="bg-slate-50 text-xs text-slate-600 text-left border-b border-slate-200">
                  <th className="px-3 py-2 whitespace-nowrap w-36">AIP REF CODE</th>
                  <th className="px-3 py-2">PROGRAM / PROJECT / ACTIVITY</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">PS</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">MOOE</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">CO</th>
                  <th className="px-3 py-2 text-right whitespace-nowrap">TOTAL</th>
                  <th className="px-3 py-2 text-right">Q1</th>
                  <th className="px-3 py-2 text-right">Q2</th>
                  <th className="px-3 py-2 text-right">Q3</th>
                  <th className="px-3 py-2 text-right">Q4</th>
                  <th className="px-3 py-2 text-center">ACTIONS</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">

                {/* Sector header */}
                <tr className="bg-slate-200">
                  <td
                    colSpan={11}
                    className="px-3 py-1.5 text-xs font-bold text-slate-700 uppercase tracking-wide"
                  >
                    {aipOffice.sector}
                  </td>
                </tr>

                {/* Office row */}
                <tr className="bg-slate-100">
                  <td className="px-3 py-2 font-mono text-xs text-slate-600 whitespace-nowrap">
                    {aipOffice.refCode}
                  </td>
                  <td className="px-3 py-2 font-semibold text-slate-800">{aipOffice.name}</td>
                  {Array.from({ length: 9 }, (_, i) => (
                    <td key={i} className="px-3 py-2 text-right text-slate-400 text-xs">
                      —
                    </td>
                  ))}
                </tr>

                {/* Programs → Projects → Activities */}
                {aipOffice.programs.map((program) => (
                  <Fragment key={program.id}>
                    <tr className="bg-blue-50">
                      <td className="px-3 py-2 font-mono text-xs text-slate-600 whitespace-nowrap">
                        {program.refCode}
                      </td>
                      <td className="px-3 py-2 pl-6 font-semibold text-slate-700">
                        {program.name}
                      </td>
                      {Array.from({ length: 9 }, (_, i) => (
                        <td key={i} className="px-3 py-2 text-right text-slate-400 text-xs">
                          —
                        </td>
                      ))}
                    </tr>

                    {program.projects.map((project) => (
                      <Fragment key={project.id}>
                        <tr className="bg-green-50">
                          <td className="px-3 py-2 font-mono text-xs text-slate-600 whitespace-nowrap">
                            {project.refCode}
                          </td>
                          <td className="px-3 py-2 pl-10 font-medium text-slate-700">
                            {project.name}
                          </td>
                          {Array.from({ length: 9 }, (_, i) => (
                            <td key={i} className="px-3 py-2 text-right text-slate-400 text-xs">
                              —
                            </td>
                          ))}
                        </tr>

                        {project.activities.map((activity) => {
                          const ps = sumNet(activity.id, "PS");
                          const mooe = sumNet(activity.id, "MOOE");
                          const co = sumNet(activity.id, "CO");
                          const total = ps + mooe + co;
                          const hasLines = (draftLines[activity.id]?.length ?? 0) > 0;

                          return (
                            <tr key={activity.id} className="bg-white hover:bg-slate-50">
                              <td className="px-3 py-2 font-mono text-xs text-slate-500 leading-tight whitespace-nowrap">
                                {activity.refCode}
                              </td>
                              <td className="px-3 py-2 pl-14 text-slate-700">
                                {activity.name}
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
                              <td className="px-3 py-2 text-center">
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
                    ))}
                  </Fragment>
                ))}
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
          disabled={
            !aipDetail ||
            selectedAipId == null ||
            selectedOfficeId == null ||
            isFinal ||
            saving
          }
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

      {/* Confirm dialogs (finalize / unlock / restore draft) */}
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
