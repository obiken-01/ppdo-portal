"use client";

/**
 * Office Ceilings — PPDO finance sets every office's ceiling for a fiscal year (PPDO-106).
 *
 * ↩️ **Moved off the Allocation page**, which set one office's ceiling at a time behind an office
 * picker. Since 2026-09-15 ceiling authority belongs to PPDO finance for *every* office
 * (`Office_Setup_Spec.md` D3), and setting twenty offices one picker-selection at a time is the
 * wrong shape for that job. Allocation still SHOWS the ceiling, read-only.
 *
 * ⚠️ **One card per office** (v1.9.0 — PPDO-217, Ralph 2026-10-14; replaced the per-fund tabs). Each
 * card lists the General Fund, then each fund finance flagged "Enforce AIP ceiling" that the office
 * can encode on (`fundsForOffice`): every province-wide one and the office's own. A fund that needs no
 * ceiling never appears (D4 still holds), and a card with only the GF is the common case. Cards grow
 * with their fund count; one Save per card saves every changed line.
 *
 * ⚠️ **A ceiling cut below what the office has already encoded is allowed** (A5-b, non-destructive).
 * The line turns amber and the office fails its own submit gate — the alternative, refusing the save,
 * would leave finance unable to record a cut that has actually happened.
 *
 * ⚠️ **Two reads per office, issued in parallel.** `ceilings` is the authority on the amount (per
 * fund); `ceiling-usage` is what the office has encoded against the GF and each enforced fund
 * (`otherFunds`, PPDO-216), and is the *same* figure the office's own submit gate uses, so the two screens cannot disagree. The office band's `costedInAip`
 * was not used for that column: it is an all-funds, PS-inclusive total and would disagree. Same
 * precedent as `BulkCeilingModal`; if the province grows past a few dozen offices, this wants one
 * batch endpoint rather than a loop (`PERFORMANCE_GUIDELINES.md`).
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import MoneyInput from "@/components/ui/MoneyInput";
import { useToast } from "@/components/ui/Toast";
import BulkCeilingModal from "../BulkCeilingModal";
import { allocationErrorMessage, getCeilingUsage, getCeilings, upsertCeiling } from "@/lib/allocation";
import { getDashboardOffices, getFiscalYears } from "@/lib/budget-planning";
import { budgetPlanningFallback, canOpenOfficeCeilings } from "@/lib/budget-planning-access";
import { findGeneralFund, listFundingSources } from "@/lib/config";
import { encodedForFund, fundsForOffice } from "@/lib/fund-ceilings";
import { useMe } from "@/lib/me-cache";
import { formatMoney } from "@/lib/money";
import type { FundingSourceResponse, OfficeSummary } from "@/types";

interface Line {
  fundId: number;
  code: string;
  name: string;
  /** Null = no ceiling set for this fund. ⚠️ Rendered as "Not set", never ₱0.00. */
  ceiling: number | null;
  /** What the office has encoded against this fund's ceiling. Null = no AIP record for the year. */
  encoded: number | null;
  /** Unsaved edit, or undefined when the line is untouched. */
  draft?: number | null;
  error?: string | null;
}

interface Card {
  officeId: number;
  officeCode: string;
  officeName: string;
  lines: Line[];
  saving?: boolean;
  saved?: boolean;
}

type Filter = "all" | "notSet" | "over";

const isOver = (l: Line) => l.ceiling != null && l.encoded != null && l.encoded > l.ceiling;
const isDirty = (l: Line) => l.draft !== undefined && l.draft != null && l.draft !== l.ceiling;

export default function OfficeCeilingsPage() {
  const me = useMe(canOpenOfficeCeilings, budgetPlanningFallback);
  const { toast } = useToast();

  const [fiscalYear, setFiscalYear] = useState<number | null>(null);
  const [availableFiscalYears, setAvailableFiscalYears] = useState<number[]>([]);
  const [funds, setFunds] = useState<FundingSourceResponse[] | null>(null);
  const [cards, setCards] = useState<Card[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [bulkOpen, setBulkOpen] = useState(false);
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);

  useEffect(() => {
    if (!me) return;
    getFiscalYears()
      .then((data) => {
        setAvailableFiscalYears(data.availableFiscalYears);
        setFiscalYear((current) => current ?? data.fiscalYear);
      })
      .catch(() => setError("Could not load fiscal years."));
    listFundingSources({ active: "true" })
      .then(setFunds)
      .catch(() => setError("Could not load funding sources."));
  }, [me]);

  const load = useCallback(() => {
    if (fiscalYear == null || funds == null) return;
    setLoading(true);
    setError(null);
    getDashboardOffices(fiscalYear)
      .then((offices) =>
        Promise.all(
          offices.map(async (office: OfficeSummary) => {
            const [ceilings, usage] = await Promise.all([
              getCeilings(office.officeId, fiscalYear),
              getCeilingUsage(office.officeId, fiscalYear),
            ]);
            return {
              officeId: office.officeId,
              officeCode: office.officeCode,
              officeName: office.officeName,
              lines: fundsForOffice(funds, office.officeId).map((fund) => ({
                fundId: fund.id,
                code: fund.code,
                name: fund.name,
                ceiling: ceilings.find((c) => c.fundingSourceId === fund.id)?.amount ?? null,
                encoded: encodedForFund(usage, fund.id),
              })),
            } satisfies Card;
          })
        )
      )
      .then(setCards)
      .catch(() => setError("Could not load this year's ceilings."))
      .finally(() => setLoading(false));
  }, [fiscalYear, funds]);

  useEffect(load, [load]);

  const patchCard = (officeId: number, update: (c: Card) => Card) =>
    setCards((current) => current.map((c) => (c.officeId === officeId ? update(c) : c)));

  const patchLine = (officeId: number, fundId: number, changes: Partial<Line>) =>
    patchCard(officeId, (c) => ({
      ...c,
      saved: false,
      lines: c.lines.map((l) => (l.fundId === fundId ? { ...l, ...changes } : l)),
    }));

  // Saves every changed line on the card, one request per fund. ⚠️ Not atomic: a line that fails
  // keeps its draft and its own error while the lines that saved show as saved, so nothing reads
  // as applied that was not.
  const save = async (card: Card) => {
    if (fiscalYear == null) return;
    const dirty = card.lines.filter(isDirty);
    if (dirty.length === 0) return;
    patchCard(card.officeId, (c) => ({ ...c, saving: true, saved: false }));
    let failed = 0;
    for (const line of dirty) {
      try {
        const saved = await upsertCeiling({
          officeId: card.officeId,
          fiscalYear,
          fundingSourceId: line.fundId,
          amount: line.draft as number,
        });
        patchLine(card.officeId, line.fundId, { ceiling: saved.amount, draft: undefined, error: null });
      } catch (err) {
        failed++;
        const message = allocationErrorMessage(err, `Could not save ${card.officeCode}'s ${line.code} ceiling.`);
        patchLine(card.officeId, line.fundId, { error: message });
        toast.error("Save failed", message);
      }
    }
    patchCard(card.officeId, (c) => ({ ...c, saving: false, saved: failed === 0 }));
    if (failed === 0) toast.success("Saved", `${card.officeCode} ceilings saved.`);
  };

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    return cards.filter((c) => {
      if (q && !c.officeCode.toLowerCase().includes(q) && !c.officeName.toLowerCase().includes(q)) return false;
      if (filter === "notSet") return c.lines.some((l) => l.ceiling == null);
      if (filter === "over") return c.lines.some(isOver);
      return true;
    });
  }, [cards, search, filter]);

  const counts: Record<Filter, number> = {
    all: cards.length,
    notSet: cards.filter((c) => c.lines.some((l) => l.ceiling == null)).length,
    over: cards.filter((c) => c.lines.some(isOver)).length,
  };

  // Carry-forward works on the General Fund (BulkCeilingModal), so it offers offices without a GF ceiling.
  const gfId = funds ? findGeneralFund(funds)?.id : undefined;
  const officesWithoutCeiling = cards.filter((c) => c.lines.find((l) => l.fundId === gfId)?.ceiling == null);

  if (!me) return null;

  const chip = (key: Filter, label: string) => (
    <button
      key={key}
      type="button"
      onClick={() => setFilter(key)}
      aria-pressed={filter === key}
      className={`rounded-full px-3 py-1 text-xs ${
        filter === key ? "bg-green-600 text-white" : "border border-slate-200 text-slate-600 hover:bg-slate-50"
      }`}
    >
      {label} {counts[key]}
    </button>
  );

  return (
    <div className="p-6 space-y-4">
      <ConfigPageHeader
        title="Office Ceilings"
        description="One card per office: the General Fund, plus any fund PPDO finance has set to enforce a ceiling that the office can encode on."
        actions={
          <div className="flex items-center gap-2">
            {/* ⚠️ `aria-label`, never an `sr-only` label: Tailwind's `sr-only` is `position:
                absolute`, and the portal shell is not `relative`, so one escaped its clipping and
                grew the document by 349px (PPDO-104). Do not reintroduce one inside the shell. */}
            <select
              aria-label="Fiscal year"
              value={fiscalYear ?? ""}
              onChange={(e) => setFiscalYear(Number(e.target.value))}
              className="border border-slate-200 px-3 py-2 text-sm text-slate-800"
            >
              {availableFiscalYears.map((year) => (
                <option key={year} value={year}>
                  FY {year}
                </option>
              ))}
            </select>
            <button
              type="button"
              onClick={() => setBulkOpen(true)}
              disabled={fiscalYear == null || officesWithoutCeiling.length === 0}
              className="px-3 py-2 border border-slate-200 text-sm text-slate-800 hover:bg-slate-50 disabled:text-slate-400 transition-colors"
            >
              Carry forward from FY {fiscalYear != null ? fiscalYear - 1 : "—"}
            </button>
          </div>
        }
      />

      <div className="flex flex-wrap items-center gap-2">
        <input
          type="search"
          aria-label="Search office"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search office code or name"
          className="w-72 border border-slate-200 px-3 py-2 text-sm text-slate-800"
        />
        {chip("all", "All")}
        {chip("notSet", "Not set")}
        {chip("over", "Over ceiling")}
      </div>

      {bulkNotice && <p className="text-sm text-green-700">{bulkNotice}</p>}

      {error ? (
        <div className="border border-red-200 bg-red-50 p-4 text-sm text-red-700">
          {error}{" "}
          <button type="button" onClick={load} className="underline">
            Retry
          </button>
        </div>
      ) : loading ? (
        <div className="grid grid-cols-1 items-start gap-3 md:grid-cols-2 xl:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <div key={i} className="h-36 animate-pulse border border-slate-200 bg-slate-50" />
          ))}
        </div>
      ) : visible.length === 0 ? (
        <p className="border border-slate-200 bg-white p-6 text-center text-sm text-slate-600">
          {cards.length === 0 ? "No offices to show for this fiscal year." : "No office matches this search or filter."}
        </p>
      ) : (
        <div className="grid grid-cols-1 items-start gap-3 md:grid-cols-2 xl:grid-cols-3">
          {visible.map((card) => {
            const over = card.lines.filter(isOver);
            const gfNotSet = card.lines.find((l) => l.fundId === gfId)?.ceiling == null;
            const dirtyCount = card.lines.filter(isDirty).length;
            return (
              <section
                key={card.officeId}
                aria-label={`${card.officeCode} ceilings`}
                className={`border bg-white p-4 ${over.length > 0 ? "border-amber-400" : "border-slate-200"}`}
              >
                <header className="mb-2 flex items-start justify-between gap-2">
                  <h2 className="text-sm text-slate-800">
                    <span className="font-mono text-xs text-slate-600">{card.officeCode}</span>
                    <span className="ml-2 font-medium">{card.officeName}</span>
                  </h2>
                  {over.length > 0 ? (
                    <span className="shrink-0 rounded-full bg-amber-100 px-2 py-0.5 text-xs text-amber-800">
                      Over on {over.map((l) => l.code).join(", ")}
                    </span>
                  ) : gfNotSet ? (
                    <span className="shrink-0 rounded-full bg-slate-100 px-2 py-0.5 text-xs text-slate-600">
                      GF not set
                    </span>
                  ) : null}
                </header>
                <table className="w-full text-sm">
                  <thead>
                    <tr className="text-xs text-slate-600">
                      <th className="py-1 text-left font-normal">Fund</th>
                      <th className="py-1 text-right font-normal">Ceiling</th>
                      <th className="py-1 text-right font-normal">Encoded</th>
                      <th className="py-1 text-right font-normal">Remaining</th>
                    </tr>
                  </thead>
                  <tbody>
                    {card.lines.map((l) => {
                      const remaining = l.ceiling == null || l.encoded == null ? null : l.ceiling - l.encoded;
                      return (
                        <tr key={l.fundId} className="border-t border-slate-100 align-top">
                          <td className="py-1.5 pr-2 font-mono text-xs text-slate-800" title={l.name}>
                            {l.code}
                          </td>
                          <td className="py-1.5">
                            <MoneyInput
                              value={l.draft !== undefined ? l.draft : l.ceiling}
                              onChange={(value) => patchLine(card.officeId, l.fundId, { draft: value, error: null })}
                              disabled={card.saving}
                              placeholder="Not set"
                              className="w-full text-right"
                            />
                            {l.error && <p className="mt-1 text-xs text-red-600">{l.error}</p>}
                          </td>
                          {/* ⚠️ Null is absence, not zero: no AIP record for the year. */}
                          <td className="py-1.5 pl-2 text-right tabular-nums text-slate-600">
                            {l.encoded == null ? "—" : formatMoney(l.encoded)}
                          </td>
                          <td
                            className={`py-1.5 pl-2 text-right tabular-nums ${
                              remaining != null && remaining < 0 ? "font-medium text-amber-700" : "text-slate-600"
                            }`}
                          >
                            {remaining == null ? "—" : formatMoney(remaining)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
                <div className="mt-2 flex items-center justify-end gap-2">
                  {dirtyCount > 0 && !card.saving && (
                    <span className="text-xs text-slate-600">
                      {dirtyCount} unsaved change{dirtyCount === 1 ? "" : "s"}
                    </span>
                  )}
                  {card.saved && dirtyCount === 0 && <span className="text-xs text-green-700">Saved</span>}
                  <button
                    type="button"
                    onClick={() => save(card)}
                    disabled={dirtyCount === 0 || card.saving}
                    className="px-3 py-1.5 bg-green-600 hover:bg-green-500 disabled:bg-slate-200 disabled:text-slate-500 text-white text-xs font-medium transition-colors"
                  >
                    {card.saving ? "Saving…" : "Save"}
                  </button>
                </div>
              </section>
            );
          })}
        </div>
      )}

      <p className="text-xs text-slate-500">
        A ceiling below what an office has already encoded is allowed — the office is shown as over
        its ceiling and cannot submit until the two agree. The division split lives on{" "}
        <Link href="/budget-planning/allocation" className="text-green-800 underline">
          Allocation
        </Link>
        .
      </p>

      {bulkOpen && fiscalYear != null && (
        <BulkCeilingModal
          offices={officesWithoutCeiling.map((c) => ({
            officeId: c.officeId,
            officeCode: c.officeCode,
            officeName: c.officeName,
          }))}
          fiscalYear={fiscalYear}
          priorFiscalYear={fiscalYear - 1}
          onClose={() => setBulkOpen(false)}
          onApplied={(created) => {
            setBulkOpen(false);
            setBulkNotice(`Published ${created} ceiling${created === 1 ? "" : "s"} for FY ${fiscalYear}.`);
            load();
          }}
        />
      )}
    </div>
  );
}
