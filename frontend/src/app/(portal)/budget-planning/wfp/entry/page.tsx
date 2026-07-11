"use client";

/**
 * WFP Entry Wizard (v1.4 WFP Rework — RAL-123).
 *
 * New Program -> Project -> Activity context picker + per-expenditure wizard, built on
 * RAL-120's wfp_expenditures schema/pipeline, RAL-121's reserve rule, and RAL-122's live
 * ceiling checks. Lives at a separate route from the classic `/budget-planning/wfp` page
 * (untouched) since this ticket intentionally ends at "routes to the right amount-entry UI
 * based on nature" — RAL-124 (this ticket) fills in the Non-Procurement/Combined periods
 * grid (`@/components/wfp/WfpFrequencyGrid`); the Procurement branch still stubs to a ₱0
 * placeholder pending RAL-125's line-item table.
 * Both routes can coexist during the v1.4 transition; do not edit the same WFP record from
 * both at once (the classic page's Save replaces ALL activities under a record — see
 * WfpService.SaveAsync — which would clobber activities this wizard created via
 * EnsureActivityAsync).
 *
 * Backend enabler added alongside this ticket (not originally in RAL-120/121/122's scope,
 * but required for the wizard to obtain a wfp_activity_id safely):
 *   POST /api/budget-planning/wfp/activities/ensure  — find-or-create, never deletes/replaces
 *   GET  /api/budget-planning/wfp/expenditures?wfpActivityId=  — "added so far" list
 *
 * Endpoints (RAL-120/121/122, { data, error, message } envelope):
 *   GET  /api/budget-planning/wfp/ceilings?activityId=&divisionId=&fiscalYear=
 *   GET  /api/budget-planning/wfp/reserve-rate
 *   POST /api/budget-planning/wfp/expenditures
 *
 * Access: canAccessBudgetPlanning, same as the classic page.
 */

import { Suspense, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import {
  getAipSummary,
  listAip,
  updateAipProgramFunctionBand,
  updateAipActivityIsCreation,
  aipErrorMessage,
} from "@/lib/aip";
import { listAccounts, listDivisions, listFundingSources, listOffices, listPriceIndex } from "@/lib/config";
import { getAllocations, getCeilingStatus, getPrograms, getSetupStatus } from "@/lib/allocation";
import {
  computeWfpRollUpPreview,
  deleteWfpExpenditure,
  ensureWfpActivity,
  getReserveRate,
  listWfpExpenditures,
  mergeWfpPeriodAndItemAmounts,
  saveWfpExpenditure,
  wfpErrorMessage,
} from "@/lib/wfp";
import WfpFrequencyGrid from "@/components/wfp/WfpFrequencyGrid";
import WfpProcurementItemTable from "@/components/wfp/WfpProcurementItemTable";
import Lookup from "@/components/ui/Lookup";
import OfficeSelect from "@/components/ui/OfficeSelect";
import Modal from "@/components/ui/Modal";
import MoneyInput from "@/components/ui/MoneyInput";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import { formatMoney } from "@/lib/money";
import type {
  AccountResponse,
  AipActivitySummary,
  AipProgramSummary,
  AipProjectSummary,
  AipRecordResponse,
  AipRecordSummary,
  AllocationSetupStatusDto,
  DivisionAllocationDto,
  DivisionResponse,
  FundingSourceResponse,
  OfficeResponse,
  PriceIndexItemResponse,
  ProgramAssignmentDto,
  SaveWfpExpenditurePeriodRequest,
  SaveWfpProcurementItemRequest,
  WfpActivityRefDto,
  WfpCeilingStatusDto,
  WfpExpenditureDto,
  WfpExpenditureFrequency,
  WfpExpenditureNature,
} from "@/types";

// ---------------------------------------------------------------------------
// Helpers (duplicated from the classic wfp/page.tsx — that page is untouched by
// this ticket, and these are small pure functions, not worth a shared export
// mid-transition).
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
      f.description?.split(";").some((alias) => alias.trim().toLowerCase() === q)
    )?.id ??
    null
  );
}

const FREQUENCIES: { value: WfpExpenditureFrequency; label: string }[] = [
  { value: "M", label: "Monthly — 12 periods" },
  { value: "Q", label: "Quarterly — 4 periods" },
  { value: "B", label: "Bi-annual — 2 periods" },
  { value: "A", label: "Annual — 1 period" },
];

const NATURES: WfpExpenditureNature[] = ["Procurement", "Non-Procurement", "Combined"];

const PROJECT_FIELDS: [string, string][] = [
  ["resourcesNeeded", "Resources Needed"],
  ["responsibleUnit", "Responsible Person / Unit"],
  ["successIndicator", "Success Indicator"],
  ["meansOfVerification", "Means of Verification"],
  ["outcomeIndicator", "Outcome Indicator"],
  ["targetBeneficiaries", "Target Beneficiaries"],
];

function progressBar(used: number, total: number, overClass = "bg-red-500", okClass = "bg-green-600") {
  const pct = total > 0 ? Math.min(100, (used / total) * 100) : 0;
  const over = total > 0 && used > total;
  return (
    <div className="h-1.5 w-full bg-slate-200 overflow-hidden">
      <div className={`h-full ${over ? overClass : okClass}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

// ---------------------------------------------------------------------------
// Live ceiling check — shared by the sticky header (on mount/after save) and
// the expenditure wizard (debounced while a pending amount changes). §8/§4.2.
// ---------------------------------------------------------------------------

function useCeilingStatus(
  aipActivityId: number | null,
  divisionId: number | null,
  fiscalYear: number | null,
  pendingTotal: number,
  refreshKey: number,
  // When editing an existing expenditure, `status.aipUsed`/`divisionAllocation - divisionRemaining`
  // already include that expenditure's OLD total (RAL-122's ledger/aggregate is a snapshot of
  // everything saved so far). Subtract it here so the preview reflects OLD -> NEW, not OLD + NEW
  // double-counted — the server's ValidateExpenditureSaveAsync already excludes it via
  // excludeExpenditureId; this mirrors that exclusion client-side for the live preview (RAL-129).
  excludeCurrentTotal = 0
) {
  const [status, setStatus] = useState<WfpCeilingStatusDto | null>(null);
  const [checking, setChecking] = useState(false);

  useEffect(() => {
    if (aipActivityId == null || divisionId == null || fiscalYear == null) {
      setStatus(null);
      return;
    }
    let cancelled = false;
    setChecking(true);
    // Debounced — this fires on every pendingTotal change while the user types
    // an amount (RAL-124/125 will feed pendingTotal the running expenditure total;
    // this ticket's amounts stub keeps it fixed at 0).
    const timer = setTimeout(() => {
      getCeilingStatus(aipActivityId, divisionId, fiscalYear)
        .then((s) => {
          if (!cancelled) setStatus(s);
        })
        .catch(() => {
          if (!cancelled) setStatus(null);
        })
        .finally(() => {
          if (!cancelled) setChecking(false);
        });
    }, 400);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [aipActivityId, divisionId, fiscalYear, pendingTotal, refreshKey]);

  const wouldBeAipUsed = status != null ? status.aipUsed - excludeCurrentTotal + pendingTotal : null;
  const wouldBeDivisionUsed =
    status != null
      ? status.divisionAllocation - status.divisionRemaining - excludeCurrentTotal + pendingTotal
      : null;
  const overAip = status != null && wouldBeAipUsed != null && wouldBeAipUsed > status.aipBudget;
  const overDivision =
    status != null && wouldBeDivisionUsed != null && wouldBeDivisionUsed > status.divisionAllocation;

  return { status, checking, overAip, overDivision, wouldBeAipUsed, wouldBeDivisionUsed };
}

// ---------------------------------------------------------------------------
// ExpenditureWizard — Account -> Nature -> Frequency -> Fund source -> Reserve
// -> Amounts (stub, RAL-124/125 build the real UI). §4.1 steps 4-10.
// ---------------------------------------------------------------------------

interface ExpenditureWizardProps {
  activityRef: WfpActivityRefDto;
  aipActivityId: number;
  divisionId: number;
  fiscalYear: number;
  defaultFundingSourceId: number | null;
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  priceIndex: PriceIndexItemResponse[];
  reserveRate: number;
  editingExpenditure: WfpExpenditureDto | null;
  onSaved: (saved: WfpExpenditureDto) => void;
  onClose: () => void;
}

function ExpenditureWizard({
  activityRef,
  aipActivityId,
  divisionId,
  fiscalYear,
  defaultFundingSourceId,
  accounts,
  fundingSources,
  priceIndex,
  reserveRate,
  editingExpenditure,
  onSaved,
  onClose,
}: ExpenditureWizardProps) {
  const { toast } = useToast();
  const isEditing = editingExpenditure != null;

  // Seed state from the expenditure being edited, else the Add-new defaults. Read once at
  // mount — a new ExpenditureWizard instance is mounted per open (wizardOpen gates it), so
  // there's no stale-prop risk from editingExpenditure changing under an already-open modal.
  const [accountId, setAccountId] = useState<number | null>(editingExpenditure?.accountId ?? null);
  const [nature, setNature] = useState<WfpExpenditureNature>(editingExpenditure?.nature ?? "Non-Procurement");
  const [frequency, setFrequency] = useState<WfpExpenditureFrequency>(editingExpenditure?.frequency ?? "Q");
  const [fundingSourceId, setFundingSourceId] = useState<number | null>(
    editingExpenditure?.fundingSourceId ?? defaultFundingSourceId
  );
  const [applyReserve, setApplyReserve] = useState(editingExpenditure?.applyReserve ?? false);
  const [reserveAmount, setReserveAmount] = useState<number | null>(
    editingExpenditure?.applyReserve ? editingExpenditure.reserveAmount : null
  );
  const [annualQuarterChoice, setAnnualQuarterChoice] = useState(editingExpenditure?.annualQuarterChoice ?? 1);
  const [periods, setPeriods] = useState<SaveWfpExpenditurePeriodRequest[]>(
    editingExpenditure?.periods.map((p) => ({ periodNo: p.periodNo, amount: p.amount })) ?? []
  );
  const [procurementItems, setProcurementItems] = useState<SaveWfpProcurementItemRequest[]>(
    editingExpenditure?.procurementItems.map((i) => ({
      periodNo: i.periodNo,
      priceIndexItemId: i.priceIndexItemId,
      name: i.name,
      unit: i.unit,
      unitPrice: i.unitPrice,
      qty: i.qty,
      numberOfDays: i.numberOfDays,
    })) ?? []
  );
  // Combined-nature entry toggles (RAL-131) — which of the two sub-sections are open. Default
  // open when editing an expenditure that already has data in that section.
  const [showProcurementSection, setShowProcurementSection] = useState(
    (editingExpenditure?.procurementItems.length ?? 0) > 0
  );
  const [showNonProcurementSection, setShowNonProcurementSection] = useState(
    (editingExpenditure?.periods.length ?? 0) > 0
  );
  const [saving, setSaving] = useState(false);
  const [confirm, setConfirm] = useState<ConfirmDialogProps | null>(null);

  const selectedAccount = accounts.find((a) => a.id === accountId) ?? null;

  // The frequency grid (RAL-124) drives typed periods; the procurement item table (RAL-125)
  // drives procurementItems — merged the same way the backend does (§5.3: no nature-specific
  // branching) so pendingTotal is correct for Non-Procurement, Procurement, AND Combined alike.
  const pendingTotal = computeWfpRollUpPreview(
    frequency,
    mergeWfpPeriodAndItemAmounts(periods, procurementItems),
    applyReserve ? reserveAmount ?? 0 : 0,
    annualQuarterChoice
  ).total;

  // Combined-nature summary strip (RAL-131) — same merge as pendingTotal above, but resolves
  // an unset reserveAmount to the rate x Net default for display, matching how
  // WfpFrequencyGrid/WfpProcurementItemTable each already display their own (partial) totals.
  const combinedNet = computeWfpRollUpPreview(
    frequency,
    mergeWfpPeriodAndItemAmounts(periods, procurementItems),
    0,
    annualQuarterChoice
  ).net;
  const combinedResolvedReserve = applyReserve ? reserveAmount ?? combinedNet * reserveRate : 0;
  const combinedTotal = combinedNet + combinedResolvedReserve;

  const { status, checking, overAip, overDivision, wouldBeAipUsed, wouldBeDivisionUsed } =
    useCeilingStatus(
      aipActivityId, divisionId, fiscalYear, pendingTotal, 0,
      editingExpenditure?.totalAppropriation ?? 0
    );

  function handleAccountChange(id: number | null) {
    setAccountId(id);
    const acct = accounts.find((a) => a.id === id);
    if (acct) {
      if (acct.defaultNature) setNature(acct.defaultNature);
      setApplyReserve(acct.defaultApplyReserve);
    }
  }

  // Frequency changes the period grain (12/4/2/1) — stale period/item numbers from a different
  // frequency would silently corrupt the roll-up (e.g. a Q-grain "period 4" surviving a
  // switch to Monthly gets misread as April). Clear on change, same as a nature switch.
  function handleFrequencyChange(next: WfpExpenditureFrequency) {
    if (next === frequency) return;
    setFrequency(next);
    setPeriods([]);
    setProcurementItems([]);
  }

  // Nature-switch mid-entry: REQUIRED confirm before discarding any entered items (§4/§5.3).
  function handleNatureChange(next: WfpExpenditureNature) {
    if (next === nature) return;
    if (periods.length > 0 || procurementItems.length > 0) {
      setConfirm({
        title: "Switch Nature?",
        message:
          "Switching nature will clear the items you've entered for this expenditure. Continue?",
        confirmLabel: "Switch & Clear",
        cancelLabel: "Keep Current Nature",
        variant: "warning",
        onConfirm: () => {
          setNature(next);
          setPeriods([]);
          setProcurementItems([]);
          setShowProcurementSection(false);
          setShowNonProcurementSection(false);
        },
        onClose: () => setConfirm(null),
      });
    } else {
      setNature(next);
      setShowProcurementSection(false);
      setShowNonProcurementSection(false);
    }
  }

  async function handleSave() {
    if (accountId == null) {
      toast.error("Account required", "Pick an account before saving.");
      return;
    }
    if (overAip || overDivision) return; // Save is disabled below too — belt and suspenders.

    setSaving(true);
    try {
      const saved = await saveWfpExpenditure({
        id: editingExpenditure?.id ?? null,
        wfpActivityId: activityRef.wfpActivityId,
        accountId,
        nature,
        frequency,
        fundingSourceId,
        applyReserve,
        reserveAmount,
        annualQuarterChoice: frequency === "A" ? annualQuarterChoice : null,
        periods,
        procurementItems,
      });
      toast.success(
        isEditing ? "Expenditure updated" : "Expenditure saved",
        `Total: ₱${formatMoney(saved.totalAppropriation)}.`
      );
      onSaved(saved);
    } catch (err) {
      toast.error("Save failed", wfpErrorMessage(err, "Could not save expenditure."));
    } finally {
      setSaving(false);
    }
  }

  const canSave = accountId != null && !overAip && !overDivision && !saving;

  return (
    <Modal
      title={isEditing ? "Edit Expenditure" : "Add Expenditure"}
      size="lg"
      onClose={onClose}
      footer={
        <>
          <Modal.SecondaryButton onClick={onClose}>Cancel</Modal.SecondaryButton>
          <Modal.PrimaryButton onClick={handleSave} disabled={!canSave} loading={saving}>
            {isEditing ? "Save Changes" : "Save Expenditure"}
          </Modal.PrimaryButton>
        </>
      }
    >
      <div className="space-y-4">
        {/* Live ceiling warning */}
        {(overAip || overDivision) && (
          <div className="px-3 py-2 bg-red-50 border border-red-200 text-xs text-red-700 space-y-1">
            {overAip && status && wouldBeAipUsed != null && (
              <p>
                Exceeds AIP budget by {formatMoney(wouldBeAipUsed - status.aipBudget)} (budget{" "}
                {formatMoney(status.aipBudget)}).
              </p>
            )}
            {overDivision && status && wouldBeDivisionUsed != null && (
              <p>
                Exceeds division allocation by{" "}
                {formatMoney(wouldBeDivisionUsed - status.divisionAllocation)} (allocation{" "}
                {formatMoney(status.divisionAllocation)}).
              </p>
            )}
          </div>
        )}

        {/* 4. Account */}
        <div>
          <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
            Account
          </label>
          <Lookup
            items={accounts}
            value={accountId}
            onChange={handleAccountChange}
            getId={(a) => a.id}
            getLabel={(a) => `${a.accountTitle} (${a.accountNumber})`}
            getSearchText={(a) => `${a.accountTitle} ${a.accountNumber}`}
            placeholder="Search account…"
          />
          {selectedAccount && (
            <div className="mt-1 flex gap-1.5 text-[10px]">
              {selectedAccount.defaultNature && (
                <span className="px-1.5 py-0.5 bg-blue-50 text-blue-700 border border-blue-200">
                  Default nature: {selectedAccount.defaultNature}
                </span>
              )}
              {selectedAccount.defaultApplyReserve && (
                <span className="px-1.5 py-0.5 bg-amber-50 text-amber-700 border border-amber-200">
                  Defaults to reserve
                </span>
              )}
            </div>
          )}
        </div>

        {/* 5. Nature */}
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
              Nature
            </label>
            <select
              value={nature}
              onChange={(e) => handleNatureChange(e.target.value as WfpExpenditureNature)}
              className="w-full border border-slate-200 bg-white text-sm px-3 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-500"
            >
              {NATURES.map((n) => (
                <option key={n} value={n}>{n}</option>
              ))}
            </select>
          </div>

          {/* 6. Frequency */}
          <div>
            <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
              Frequency
            </label>
            <select
              value={frequency}
              onChange={(e) => handleFrequencyChange(e.target.value as WfpExpenditureFrequency)}
              className="w-full border border-slate-200 bg-white text-sm px-3 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-500"
            >
              {FREQUENCIES.map((f) => (
                <option key={f.value} value={f.value}>{f.label}</option>
              ))}
            </select>
          </div>
        </div>

        {/* 7. Fund source */}
        <div>
          <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
            Fund Source
          </label>
          <Lookup
            items={fundingSources}
            value={fundingSourceId}
            onChange={setFundingSourceId}
            getId={(f) => f.id}
            getLabel={(f) => f.name}
            getSearchText={(f) => `${f.name} ${f.code}`}
            placeholder="Search fund source…"
          />
        </div>

        {/* 8. Reserve — shown for EVERY account, no eligibility gate (RAL-117/121) */}
        <div className="flex items-start gap-2 px-3 py-2 bg-slate-50 border border-slate-200">
          <input
            type="checkbox"
            id="applyReserve"
            checked={applyReserve}
            onChange={(e) => {
              setApplyReserve(e.target.checked);
              if (!e.target.checked) setReserveAmount(null);
            }}
            className="mt-0.5"
          />
          <div className="flex-1">
            <label htmlFor="applyReserve" className="text-sm text-slate-700 font-medium cursor-pointer">
              Apply reserve
            </label>
            <p className="text-xs text-slate-500">
              Rate: {(reserveRate * 100).toFixed(0)}% of net appropriation. Leave amount blank to
              use the default.
            </p>
            {applyReserve && (
              <MoneyInput
                value={reserveAmount}
                onChange={setReserveAmount}
                placeholder="Default (10% of net)"
                className="mt-1.5 w-48"
              />
            )}
          </div>
        </div>

        {/* 9. Amounts — frequency grid (RAL-124) for Non-Procurement periods; procurement item
               table (RAL-125) for Procurement. Combined (RAL-131, resolving §11 Q2) lets the
               user open either or both sections under one expenditure — the backend already
               merges periods + procurement items unconditionally (WfpExpenditureCalculator). */}
        {nature === "Combined" ? (
          <div className="space-y-4">
            <div className="flex flex-wrap gap-2">
              {!showProcurementSection && (
                <button
                  type="button"
                  onClick={() => setShowProcurementSection(true)}
                  className="px-3 py-1.5 text-sm font-medium text-green-700 border border-green-600 hover:bg-green-50"
                >
                  + Add Procurement Items
                </button>
              )}
              {!showNonProcurementSection && (
                <button
                  type="button"
                  onClick={() => setShowNonProcurementSection(true)}
                  className="px-3 py-1.5 text-sm font-medium text-green-700 border border-green-600 hover:bg-green-50"
                >
                  + Add Non-Procurement Amounts
                </button>
              )}
            </div>

            {!showProcurementSection && !showNonProcurementSection && (
              <p className="px-4 py-6 border border-dashed border-slate-300 bg-slate-50 text-center text-sm text-slate-400">
                Add procurement items, non-procurement amounts, or both under this expenditure.
              </p>
            )}

            {showProcurementSection && (
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide">
                    Procurement Items
                  </label>
                  <button
                    type="button"
                    onClick={() => {
                      setShowProcurementSection(false);
                      setProcurementItems([]);
                    }}
                    className="text-xs text-danger-500 hover:underline"
                  >
                    Remove section
                  </button>
                </div>
                <WfpProcurementItemTable
                  frequency={frequency}
                  accountId={accountId}
                  procurementItems={procurementItems}
                  onProcurementItemsChange={setProcurementItems}
                  priceIndex={priceIndex}
                  annualQuarterChoice={annualQuarterChoice}
                  onAnnualQuarterChoiceChange={setAnnualQuarterChoice}
                  applyReserve={applyReserve}
                  reserveAmount={reserveAmount}
                  reserveRate={reserveRate}
                />
              </div>
            )}

            {showNonProcurementSection && (
              <div>
                <div className="flex items-center justify-between mb-1">
                  <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide">
                    Non-Procurement Amounts
                  </label>
                  <button
                    type="button"
                    onClick={() => {
                      setShowNonProcurementSection(false);
                      setPeriods([]);
                    }}
                    className="text-xs text-danger-500 hover:underline"
                  >
                    Remove section
                  </button>
                </div>
                <WfpFrequencyGrid
                  frequency={frequency}
                  periods={periods}
                  onPeriodsChange={setPeriods}
                  annualQuarterChoice={annualQuarterChoice}
                  onAnnualQuarterChoiceChange={setAnnualQuarterChoice}
                  applyReserve={applyReserve}
                  reserveAmount={reserveAmount}
                  reserveRate={reserveRate}
                />
              </div>
            )}

            {(showProcurementSection || showNonProcurementSection) && (
              <div className="flex items-center justify-end gap-4 pt-2 border-t border-slate-200 text-sm">
                <span className="text-slate-500">
                  Net{" "}
                  <span className="font-mono tabular-nums text-slate-700">
                    ₱{formatMoney(combinedNet)}
                  </span>
                </span>
                {applyReserve && (
                  <span className="text-slate-500">
                    Reserved{" "}
                    <span className="font-mono tabular-nums text-slate-700">
                      ₱{formatMoney(combinedResolvedReserve)}
                    </span>
                  </span>
                )}
                <span className="font-semibold text-slate-800">
                  Combined Total{" "}
                  <span className="font-mono tabular-nums">₱{formatMoney(combinedTotal)}</span>
                </span>
              </div>
            )}
          </div>
        ) : nature === "Procurement" ? (
          <div>
            <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
              Items
            </label>
            <WfpProcurementItemTable
              frequency={frequency}
              accountId={accountId}
              procurementItems={procurementItems}
              onProcurementItemsChange={setProcurementItems}
              priceIndex={priceIndex}
              annualQuarterChoice={annualQuarterChoice}
              onAnnualQuarterChoiceChange={setAnnualQuarterChoice}
              applyReserve={applyReserve}
              reserveAmount={reserveAmount}
              reserveRate={reserveRate}
            />
          </div>
        ) : (
          <div>
            <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
              Amounts
            </label>
            <WfpFrequencyGrid
              frequency={frequency}
              periods={periods}
              onPeriodsChange={setPeriods}
              annualQuarterChoice={annualQuarterChoice}
              onAnnualQuarterChoiceChange={setAnnualQuarterChoice}
              applyReserve={applyReserve}
              reserveAmount={reserveAmount}
              reserveRate={reserveRate}
            />
          </div>
        )}

        {checking && <p className="text-xs text-slate-400">Checking ceilings…</p>}
      </div>

      {confirm && <ConfirmDialog {...confirm} />}
    </Modal>
  );
}

// ---------------------------------------------------------------------------
// WfpEntryPageInner — needs useSearchParams -> must be inside a Suspense boundary
// ---------------------------------------------------------------------------

function WfpEntryPageInner() {
  const searchParams = useSearchParams();
  const { toast } = useToast();
  const me = useMe((m) => m.canAccessBudgetPlanning);

  // ── Selector state ───────────────────────────────────────────────────────

  const [aipList, setAipList] = useState<AipRecordResponse[]>([]);
  const [officeList, setOfficeList] = useState<OfficeResponse[]>([]);
  const [divisionList, setDivisionList] = useState<DivisionResponse[]>([]);
  const [selectedAipId, setSelectedAipId] = useState<number | null>(null);
  const [selectedOfficeId, setSelectedOfficeId] = useState<number | null>(null);
  const [selectedDivisionId, setSelectedDivisionId] = useState<number | null>(null);

  // ── Loaded reference data ────────────────────────────────────────────────

  const [aipDetail, setAipDetail] = useState<AipRecordSummary | null>(null);
  const [accounts, setAccounts] = useState<AccountResponse[]>([]);
  const [fundingSources, setFundingSources] = useState<FundingSourceResponse[]>([]);
  const [priceIndex, setPriceIndex] = useState<PriceIndexItemResponse[]>([]);
  const [programAssignments, setProgramAssignments] = useState<ProgramAssignmentDto[]>([]);
  const [divisionAllocation, setDivisionAllocation] = useState<DivisionAllocationDto | null>(null);
  const [setupStatus, setSetupStatus] = useState<AllocationSetupStatusDto | null>(null);
  const [reserveRate, setReserveRate] = useState(0.10);

  // ── Context picker state (§4.1 — sticky, chosen once, reused across entries) ──

  const [selectedProgramId, setSelectedProgramId] = useState<number | null>(null);
  const [selectedProjectId, setSelectedProjectId] = useState<number | null>(null);
  const [selectedActivityId, setSelectedActivityId] = useState<number | null>(null);

  // ── Function band / Creation flag (v1.4 Q1/Q2 — captured here, during WFP entry) ──

  const [savingProgramId, setSavingProgramId] = useState<number | null>(null);
  const [savingActivityId, setSavingActivityId] = useState<number | null>(null);
  const [projectFieldsOpen, setProjectFieldsOpen] = useState(false);
  const [projectFields, setProjectFields] = useState<Record<string, string>>({});

  // ── Activity-scoped state (loaded once Activity is picked) ───────────────

  const [activityRef, setActivityRef] = useState<WfpActivityRefDto | null>(null);
  const [expenditures, setExpenditures] = useState<WfpExpenditureDto[]>([]);
  const [loadingActivity, setLoadingActivity] = useState(false);
  const [wizardOpen, setWizardOpen] = useState(false);
  const [ceilingRefresh, setCeilingRefresh] = useState(0);
  const [editingExpenditure, setEditingExpenditure] = useState<WfpExpenditureDto | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  const [deleteConfirm, setDeleteConfirm] = useState<ConfirmDialogProps | null>(null);

  const [loading, setLoading] = useState(false);

  // ── Effect A: load selector lists on mount ───────────────────────────────

  useEffect(() => {
    const urlAipId = searchParams.get("aipId");
    const urlOfficeId = searchParams.get("officeId");

    Promise.all([listAip(), listOffices({ active: "true" }), getReserveRate(), listPriceIndex({ active: "true" })])
      .then(([aips, offices, rate, items]) => {
        setAipList(aips);
        setOfficeList(offices);
        setReserveRate(rate.rate);
        setPriceIndex(items);
        if (urlAipId) setSelectedAipId(Number(urlAipId));
        if (urlOfficeId) setSelectedOfficeId(Number(urlOfficeId));
      })
      .catch(() => toast.error("Load failed", "Could not load AIP / office data."));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!me) return;
    if (me.officeId != null && !searchParams.get("officeId")) setSelectedOfficeId(me.officeId);
    if (me.divisionId != null) setSelectedDivisionId(me.divisionId);
  }, [me, searchParams]);

  useEffect(() => {
    if (selectedOfficeId == null) {
      setDivisionList([]);
      return;
    }
    listDivisions({ active: "true", officeId: selectedOfficeId }).then(setDivisionList).catch(() => {});
  }, [selectedOfficeId]);

  // ── Effect B: load AIP detail + accounts/funds + division setup ──────────

  useEffect(() => {
    if (selectedAipId == null || selectedOfficeId == null || selectedDivisionId == null) {
      setAipDetail(null);
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
      try {
        const [detail, accts, funds] = await Promise.all([
          getAipSummary(aipId),
          listAccounts({ active: "true" }),
          listFundingSources({ active: "true" }),
        ]);
        if (cancelled) return;
        setAipDetail(detail);
        setAccounts(accts);
        setFundingSources(funds);

        const [status, allocs, assignments] = await Promise.all([
          getSetupStatus(officeId, detail.fiscalYear, divisionId).catch(() => null),
          getAllocations(officeId, detail.fiscalYear),
          getPrograms(officeId, detail.fiscalYear),
        ]);
        if (cancelled) return;
        setSetupStatus(status);
        setDivisionAllocation(allocs.find((a) => a.divisionId === divisionId) ?? null);
        setProgramAssignments(assignments);
      } catch (err) {
        if (!cancelled) toast.error("Load failed", wfpErrorMessage(err, "Could not load WFP context data."));
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

  // ── Derived: match config office to AIP office via officeRefCode ─────────

  const selectedConfigOffice = officeList.find((o) => o.id === selectedOfficeId) ?? null;

  const aipOffice = useMemo(() => {
    if (!aipDetail || !selectedConfigOffice?.officeRefCode) return null;
    return (
      aipDetail.offices.find((o) => officeRefSuffix(o.refCode) === selectedConfigOffice.officeRefCode) ??
      null
    );
  }, [aipDetail, selectedConfigOffice]);

  const assignedPrograms = useMemo<AipProgramSummary[]>(() => {
    if (!aipOffice) return [];
    if (selectedDivisionId == null) return aipOffice.programs;
    const assignedRefs = new Set(
      programAssignments
        .filter((a) => a.divisionIds.includes(selectedDivisionId))
        .map((a) => a.programRefCode)
    );
    return aipOffice.programs.filter((p) => assignedRefs.has(p.refCode));
  }, [aipOffice, selectedDivisionId, programAssignments]);

  const selectedProgram = assignedPrograms.find((p) => p.id === selectedProgramId) ?? null;
  const projects: AipProjectSummary[] = selectedProgram?.projects ?? [];
  const selectedProject = projects.find((p) => p.id === selectedProjectId) ?? null;
  const activities: AipActivitySummary[] = selectedProject?.activities ?? [];
  const selectedActivity = activities.find((a) => a.id === selectedActivityId) ?? null;

  // A Final WFP record is locked — the server rejects add/edit/delete on its expenditures
  // (RAL-129); mirror that client-side so the affordances are disabled, not just error toasts.
  const isWfpLocked = activityRef?.wfpStatus === "Final";

  // ── Function band / Creation flag (v1.4 Q1/Q2) — optimistic patch of aipDetail ──

  async function handleFunctionBandChange(programId: number, value: string) {
    const nextBand = value || null;
    let prevBand: string | null = null;
    setAipDetail((prev) => {
      if (!prev) return prev;
      return {
        ...prev,
        offices: prev.offices.map((o) => ({
          ...o,
          programs: o.programs.map((p) => {
            if (p.id !== programId) return p;
            prevBand = p.functionBand;
            return { ...p, functionBand: nextBand };
          }),
        })),
      };
    });
    setSavingProgramId(programId);
    try {
      await updateAipProgramFunctionBand(programId, nextBand);
    } catch (err) {
      setAipDetail((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          offices: prev.offices.map((o) => ({
            ...o,
            programs: o.programs.map((p) => (p.id === programId ? { ...p, functionBand: prevBand } : p)),
          })),
        };
      });
      toast.error("Failed", aipErrorMessage(err, "Could not update function band."));
    } finally {
      setSavingProgramId(null);
    }
  }

  async function handleIsCreationChange(activityId: number, checked: boolean) {
    setAipDetail((prev) => {
      if (!prev) return prev;
      return {
        ...prev,
        offices: prev.offices.map((o) => ({
          ...o,
          programs: o.programs.map((p) => ({
            ...p,
            projects: p.projects.map((j) => ({
              ...j,
              activities: j.activities.map((a) => (a.id === activityId ? { ...a, isCreation: checked } : a)),
            })),
          })),
        })),
      };
    });
    setSavingActivityId(activityId);
    try {
      await updateAipActivityIsCreation(activityId, checked);
    } catch (err) {
      setAipDetail((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          offices: prev.offices.map((o) => ({
            ...o,
            programs: o.programs.map((p) => ({
              ...p,
              projects: p.projects.map((j) => ({
                ...j,
                activities: j.activities.map((a) => (a.id === activityId ? { ...a, isCreation: !checked } : a)),
              })),
            })),
          })),
        };
      });
      toast.error("Failed", aipErrorMessage(err, "Could not update creation flag."));
    } finally {
      setSavingActivityId(null);
    }
  }

  // ── Project descriptive fields — localStorage only (no backend yet, §3) ──

  useEffect(() => {
    if (selectedProjectId == null) {
      setProjectFields({});
      return;
    }
    const stored = localStorage.getItem(`wfp_entry_project_fields_${selectedProjectId}`);
    setProjectFields(stored ? JSON.parse(stored) : {});
  }, [selectedProjectId]);

  function saveProjectField(key: string, value: string) {
    if (selectedProjectId == null) return;
    const next = { ...projectFields, [key]: value };
    setProjectFields(next);
    localStorage.setItem(`wfp_entry_project_fields_${selectedProjectId}`, JSON.stringify(next));
  }

  // ── Effect C: when Activity is picked, ensure the WFP record/activity exist ──

  useEffect(() => {
    if (
      selectedActivityId == null ||
      selectedAipId == null ||
      selectedOfficeId == null ||
      selectedDivisionId == null ||
      aipDetail == null
    ) {
      setActivityRef(null);
      setExpenditures([]);
      return;
    }

    let cancelled = false;
    setLoadingActivity(true);

    ensureWfpActivity({
      aipRecordId: selectedAipId,
      officeId: selectedOfficeId,
      divisionId: selectedDivisionId,
      fiscalYear: aipDetail.fiscalYear,
      aipActivityId: selectedActivityId,
    })
      .then(async (ref) => {
        if (cancelled) return;
        setActivityRef(ref);
        const list = await listWfpExpenditures(ref.wfpActivityId);
        if (!cancelled) setExpenditures(list);
      })
      .catch((err) => {
        if (!cancelled) {
          toast.error("Could not open activity", wfpErrorMessage(err, "Could not prepare this activity for entry."));
          setSelectedActivityId(null);
        }
      })
      .finally(() => {
        if (!cancelled) setLoadingActivity(false);
      });

    return () => {
      cancelled = true;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selectedActivityId, selectedAipId, selectedOfficeId, selectedDivisionId, aipDetail]);

  const { status: ceilingStatus } = useCeilingStatus(
    selectedActivityId,
    selectedDivisionId,
    aipDetail?.fiscalYear ?? null,
    0,
    ceilingRefresh
  );

  function handleExpenditureSaved(saved: WfpExpenditureDto) {
    setExpenditures((prev) => {
      const idx = prev.findIndex((e) => e.id === saved.id);
      if (idx === -1) return [...prev, saved];
      const next = [...prev];
      next[idx] = saved;
      return next;
    });
    setWizardOpen(false);
    setEditingExpenditure(null);
    setCeilingRefresh((n) => n + 1);
  }

  function handleEditExpenditure(e: WfpExpenditureDto) {
    setEditingExpenditure(e);
    setWizardOpen(true);
  }

  function handleCloseWizard() {
    setWizardOpen(false);
    setEditingExpenditure(null);
  }

  function handleDeleteExpenditure(e: WfpExpenditureDto) {
    setDeleteConfirm({
      title: "Delete Expenditure?",
      message: `Delete this ${e.accountTitleSnapshot ?? "expenditure"} entry (₱${formatMoney(
        e.totalAppropriation
      )})? This cannot be undone.`,
      confirmLabel: "Delete",
      cancelLabel: "Cancel",
      variant: "danger",
      onConfirm: async () => {
        setDeletingId(e.id);
        try {
          await deleteWfpExpenditure(e.id);
          setExpenditures((prev) => prev.filter((x) => x.id !== e.id));
          setCeilingRefresh((n) => n + 1);
          toast.success("Expenditure deleted", `Removed the ${e.accountTitleSnapshot ?? "entry"}.`);
        } catch (err) {
          toast.error("Delete failed", wfpErrorMessage(err, "Could not delete this expenditure."));
        } finally {
          setDeletingId(null);
          setDeleteConfirm(null);
        }
      },
      onClose: () => setDeleteConfirm(null),
    });
  }

  function handleChangeActivity() {
    setSelectedActivityId(null);
    setActivityRef(null);
    setExpenditures([]);
  }

  function handleDone() {
    setSelectedProgramId(null);
    setSelectedProjectId(null);
    setSelectedActivityId(null);
    setActivityRef(null);
    setExpenditures([]);
  }

  // ── Derived flags ─────────────────────────────────────────────────────────

  const isOfficeUser = me != null && me.officeId != null;
  const canBypassDivision =
    me?.role === "SuperAdmin" || me?.role === "Admin" || me?.canManageAllocation === true;

  const setupComplete =
    setupStatus == null || (setupStatus.hasAllocation && setupStatus.hasProgramAssignment);

  const defaultFundingSourceId = selectedActivity
    ? resolveDefaultFundingSourceId(selectedActivity.fundingSourceSnapshot, fundingSources)
    : null;

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div className="p-6 max-w-screen-lg mx-auto w-full">
      {/* Header */}
      <div className="mb-5">
        <div className="flex items-center gap-2">
          <h1 className="text-xl font-bold text-slate-800">WFP Entry Wizard</h1>
          <span className="px-2 py-0.5 text-xs font-medium bg-amber-100 text-amber-700">v1.4 preview</span>
        </div>
        <p className="text-sm text-slate-500 mt-0.5">
          New context picker + expenditure wizard.{" "}
          <a href="/budget-planning/wfp" className="text-green-700 hover:underline">
            Switch to classic view
          </a>
        </p>
      </div>

      {/* Selector row */}
      <div className="flex flex-wrap items-center gap-3 mb-5">
        <div className="flex items-center gap-2">
          <label className="text-sm text-slate-600 font-medium whitespace-nowrap">AIP</label>
          <select
            value={selectedAipId ?? ""}
            onChange={(e) => setSelectedAipId(e.target.value ? Number(e.target.value) : null)}
            className="border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
          >
            <option value="">— select AIP —</option>
            {aipList.map((a) => (
              <option key={a.id} value={a.id}>AIP FY{a.fiscalYear} ({a.status})</option>
            ))}
          </select>
        </div>

        <div className="flex items-center gap-2">
          <label className="text-sm text-slate-600 font-medium whitespace-nowrap">Office</label>
          <OfficeSelect
            offices={officeList}
            value={selectedOfficeId}
            onChange={(officeId) => {
              setSelectedOfficeId(officeId);
              setSelectedDivisionId(null);
            }}
            placeholder="— select office —"
            disabled={isOfficeUser}
          />
        </div>

        {selectedOfficeId != null && (
          <div className="flex items-center gap-2">
            <label className="text-sm text-slate-600 font-medium whitespace-nowrap">Division</label>
            {canBypassDivision ? (
              <select
                value={selectedDivisionId ?? ""}
                onChange={(e) => setSelectedDivisionId(e.target.value ? Number(e.target.value) : null)}
                className="w-48 truncate border border-slate-300 bg-white text-sm px-2 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-600"
              >
                <option value="">— select division —</option>
                {divisionList.map((d) => (
                  <option key={d.id} value={d.id}>{d.code ? `${d.code} — ${d.name}` : d.name}</option>
                ))}
              </select>
            ) : (
              <span className="w-48 truncate text-sm text-slate-700 px-2 py-1.5 border border-slate-200 bg-slate-50">
                {(() => {
                  const div = divisionList.find((d) => d.id === selectedDivisionId);
                  return div ? (div.code ? `${div.code} — ${div.name}` : div.name) : me?.division ?? "—";
                })()}
              </span>
            )}
          </div>
        )}
      </div>

      {selectedDivisionId == null ? (
        <p className="text-slate-400 text-sm py-8">
          Select an AIP, office, and division to start entering WFP expenditures. This wizard is
          division-scoped — a specific division must be chosen even for admin/finance users.
        </p>
      ) : !setupComplete ? (
        <div className="px-4 py-3 bg-amber-50 border border-amber-300 text-amber-800 text-sm">
          Allocation setup is incomplete for this division. Go to Budget Planning → Allocation
          first.
        </div>
      ) : loading ? (
        <div className="flex items-center gap-2 text-slate-500 text-sm py-8">
          <span className="w-4 h-4 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
          Loading…
        </div>
      ) : (
        <>
          {/* Sticky ceiling header (§4.2) */}
          <div className="sticky top-0 z-10 mb-5 bg-white border border-slate-200 px-4 py-3 space-y-2 shadow-sm">
            <p className="text-sm font-semibold text-slate-700">
              FY {aipDetail?.fiscalYear} · {selectedConfigOffice?.officeName} ·{" "}
              {divisionList.find((d) => d.id === selectedDivisionId)?.name ?? me?.division}
            </p>

            {divisionAllocation && (
              <div>
                <div className="flex justify-between text-xs text-slate-600">
                  <span>
                    Division allocation: {formatMoney(divisionAllocation.amount)} original ·{" "}
                    {ceilingStatus
                      ? `${formatMoney(ceilingStatus.divisionRemaining)} remaining`
                      : "…"}
                  </span>
                </div>
                {ceilingStatus &&
                  progressBar(
                    divisionAllocation.amount - ceilingStatus.divisionRemaining,
                    divisionAllocation.amount
                  )}
              </div>
            )}

            {selectedActivity && ceilingStatus && (
              <div>
                <div className="flex justify-between text-xs text-slate-600">
                  <span>
                    AIP budget — this activity: {formatMoney(ceilingStatus.aipBudget)} · WFP
                    entered: {formatMoney(ceilingStatus.aipUsed)}
                  </span>
                </div>
                {progressBar(ceilingStatus.aipUsed, ceilingStatus.aipBudget)}
              </div>
            )}
          </div>

          {/* Context picker (§4.1 steps 1-3) */}
          <div className="grid grid-cols-3 gap-3 mb-4">
            <div>
              <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
                Program
              </label>
              <Lookup
                items={assignedPrograms}
                value={selectedProgramId}
                onChange={(id) => {
                  setSelectedProgramId(id);
                  setSelectedProjectId(null);
                  setSelectedActivityId(null);
                }}
                getId={(p) => p.id}
                getLabel={(p) => `${p.refCode} — ${p.name}`}
                getSearchText={(p) => `${p.name} ${p.refCode}`}
                renderOption={(p) => (
                  <>
                    <span className="font-mono text-xs text-slate-400 mr-2">{p.refCode}</span>
                    {p.name}
                  </>
                )}
                placeholder="Search program…"
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
                Project
              </label>
              <Lookup
                items={projects}
                value={selectedProjectId}
                onChange={(id) => {
                  setSelectedProjectId(id);
                  setSelectedActivityId(null);
                }}
                getId={(p) => p.id}
                getLabel={(p) => `${p.refCode} — ${p.name}`}
                getSearchText={(p) => `${p.name} ${p.refCode}`}
                renderOption={(p) => (
                  <>
                    <span className="font-mono text-xs text-slate-400 mr-2">{p.refCode}</span>
                    {p.name}
                  </>
                )}
                placeholder={selectedProgramId == null ? "Pick a program first" : "Search project…"}
                disabled={selectedProgramId == null}
              />
            </div>
            <div>
              <label className="block text-xs font-medium text-slate-500 uppercase tracking-wide mb-1">
                Activity
              </label>
              <Lookup
                items={activities}
                value={selectedActivityId}
                onChange={setSelectedActivityId}
                getId={(a) => a.id}
                getLabel={(a) => `${a.refCode} — ${a.name}`}
                getSearchText={(a) => `${a.name} ${a.refCode}`}
                renderOption={(a) => (
                  <>
                    <span className="font-mono text-xs text-slate-400 mr-2">{a.refCode}</span>
                    {a.name}
                  </>
                )}
                placeholder={selectedProjectId == null ? "Pick a project first" : "Search activity…"}
                disabled={selectedProjectId == null}
              />
            </div>
          </div>

          {/* Function Band (Q1) / Creation flag (Q2) — v1.4 WFP Rework, captured here during entry */}
          {(selectedProgram || selectedActivity) && (
            <div className="flex flex-wrap items-center gap-6 mb-4 px-3 py-2 bg-slate-50 border border-slate-200 text-xs">
              {selectedProgram && (
                <label className="flex items-center gap-2">
                  <span className="font-medium text-slate-500 uppercase tracking-wide">Function Band</span>
                  <select
                    value={selectedProgram.functionBand ?? ""}
                    onChange={(e) => handleFunctionBandChange(selectedProgram.id, e.target.value)}
                    disabled={savingProgramId === selectedProgram.id}
                    className="border border-slate-300 text-xs px-2 py-1 bg-white disabled:opacity-50"
                  >
                    <option value="">— none —</option>
                    <option value="CORE">Core</option>
                    <option value="STRATEGIC">Strategic</option>
                    <option value="SUPPORT">Support</option>
                  </select>
                </label>
              )}
              {selectedActivity && (
                <label className="flex items-center gap-2 cursor-pointer">
                  <input
                    type="checkbox"
                    checked={selectedActivity.isCreation}
                    onChange={(e) => handleIsCreationChange(selectedActivity.id, e.target.checked)}
                    disabled={savingActivityId === selectedActivity.id}
                  />
                  <span className="font-medium text-slate-500 uppercase tracking-wide">
                    Mark as &ldquo;…-CREATION&rdquo; (GF, PS, position-creation only)
                  </span>
                </label>
              )}
            </div>
          )}

          {/* Project descriptive fields accordion — optional, §3 (localStorage only for now) */}
          {selectedProject && (
            <div className="mb-4 border border-slate-200">
              <button
                onClick={() => setProjectFieldsOpen((o) => !o)}
                className="w-full flex items-center justify-between px-3 py-2 text-sm font-medium text-slate-600 hover:bg-slate-50"
              >
                <span>Project details (optional)</span>
                <span>{projectFieldsOpen ? "▲" : "▼"}</span>
              </button>
              {projectFieldsOpen && (
                <div className="grid grid-cols-2 gap-3 px-3 py-3 border-t border-slate-100">
                  {PROJECT_FIELDS.map(([key, label]) => (
                    <div key={key}>
                      <label className="block text-[10px] font-medium text-slate-400 uppercase tracking-wide mb-0.5">
                        {label}
                      </label>
                      <input
                        type="text"
                        value={projectFields[key] ?? ""}
                        onChange={(e) => saveProjectField(key, e.target.value)}
                        className="w-full border border-slate-200 text-sm px-2 py-1 focus:outline-none focus:ring-1 focus:ring-green-500"
                      />
                    </div>
                  ))}
                  <p className="col-span-2 text-[10px] text-slate-400">
                    Saved locally in this browser for now — no backend field exists for
                    project-level descriptive data yet.
                  </p>
                </div>
              )}
            </div>
          )}

          {/* Expenditures under the selected activity */}
          {selectedActivityId != null && (
            <div className="border border-slate-200">
              <div className="px-3 py-2 bg-slate-50 border-b border-slate-200 flex items-center justify-between">
                <span className="text-sm font-semibold text-slate-700">
                  Expenditures — {selectedActivity?.name}
                </span>
                {loadingActivity && (
                  <span className="w-3.5 h-3.5 border-2 border-slate-300 border-t-green-600 rounded-full animate-spin" />
                )}
              </div>

              {isWfpLocked && (
                <p className="px-3 py-2 text-xs text-amber-700 bg-amber-50 border-b border-amber-200">
                  This WFP is Final and locked. An admin must Unlock it before expenditures can be
                  added, edited, or deleted.
                </p>
              )}

              {expenditures.length === 0 ? (
                <p className="px-3 py-6 text-sm text-slate-400 text-center">
                  No expenditures added yet.
                </p>
              ) : (
                <table className="w-full text-xs">
                  <thead>
                    <tr className="bg-slate-50 text-slate-500 text-left border-b border-slate-100">
                      <th className="px-3 py-1.5">Account</th>
                      <th className="px-3 py-1.5">Nature</th>
                      <th className="px-3 py-1.5">Frequency</th>
                      <th className="px-3 py-1.5 text-right">Total</th>
                      <th className="px-3 py-1.5 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {expenditures.map((e) => (
                      <tr key={e.id}>
                        <td className="px-3 py-1.5">{e.accountTitleSnapshot ?? "—"}</td>
                        <td className="px-3 py-1.5">{e.nature}</td>
                        <td className="px-3 py-1.5">{e.frequency}</td>
                        <td className="px-3 py-1.5 text-right tabular-nums">
                          {formatMoney(e.totalAppropriation)}
                        </td>
                        <td className="px-3 py-1.5 text-right whitespace-nowrap">
                          <button
                            onClick={() => handleEditExpenditure(e)}
                            disabled={isWfpLocked || deletingId === e.id}
                            className="text-green-700 hover:underline disabled:opacity-40 disabled:cursor-not-allowed"
                          >
                            Edit
                          </button>
                          <span className="mx-1.5 text-slate-300">·</span>
                          <button
                            onClick={() => handleDeleteExpenditure(e)}
                            disabled={isWfpLocked || deletingId === e.id}
                            className="text-danger-500 hover:text-red-600 hover:underline disabled:opacity-40 disabled:cursor-not-allowed"
                          >
                            {deletingId === e.id ? "Deleting…" : "Delete"}
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}

              <div className="px-3 py-2.5 border-t border-slate-200 flex items-center justify-between">
                <button
                  onClick={() => {
                    setEditingExpenditure(null);
                    setWizardOpen(true);
                  }}
                  disabled={activityRef == null || isWfpLocked}
                  className="text-sm font-medium text-green-700 hover:underline disabled:opacity-50"
                >
                  + Add expenditure
                </button>
                <div className="flex gap-3">
                  <button onClick={handleChangeActivity} className="text-sm text-slate-500 hover:underline">
                    Change activity
                  </button>
                  <button onClick={handleDone} className="text-sm text-slate-500 hover:underline">
                    Done
                  </button>
                </div>
              </div>
            </div>
          )}
        </>
      )}

      {wizardOpen && activityRef != null && selectedActivityId != null && selectedDivisionId != null && aipDetail && (
        <ExpenditureWizard
          activityRef={activityRef}
          aipActivityId={selectedActivityId}
          divisionId={selectedDivisionId}
          fiscalYear={aipDetail.fiscalYear}
          defaultFundingSourceId={defaultFundingSourceId}
          accounts={accounts}
          fundingSources={fundingSources}
          priceIndex={priceIndex}
          reserveRate={reserveRate}
          editingExpenditure={editingExpenditure}
          onSaved={handleExpenditureSaved}
          onClose={handleCloseWizard}
        />
      )}

      {deleteConfirm && <ConfirmDialog {...deleteConfirm} />}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page export — wraps inner component in Suspense (required for useSearchParams)
// ---------------------------------------------------------------------------

export default function WfpEntryPage() {
  return (
    <Suspense fallback={<div className="p-6 text-slate-500 text-sm">Loading…</div>}>
      <WfpEntryPageInner />
    </Suspense>
  );
}
