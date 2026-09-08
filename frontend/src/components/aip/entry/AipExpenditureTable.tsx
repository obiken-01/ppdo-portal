"use client";

/**
 * Expenditure lines under one activity — stage three of entry (V18-42 / PPDO-52), with the
 * multi-fund toggle (V18-43 / PPDO-53).
 *
 * ⚠️ **One funding source per line, always.** The toggle changes the FORM, never the data model.
 * Multi-fund is expressed as several lines, never as a fund list on one line. 60% of the FY2027
 * file's money rows name several funds against a single un-split amount, which is exactly what
 * makes a General-Fund ceiling uncomputable — one fund per line is what makes fund-level checks
 * possible at all.
 *
 * ⚠️ **Default single** (whiteboard W8). The toggle exists so the multi-fund case is *possible*,
 * not so every encoder meets it. Most activities draw on one fund and should stay one field.
 *
 * ⚠️ **Amounts are typed in PESOS and shown in ₱000.** The inputs post exactly what was typed;
 * only the saved cells divide. Each input carries a live `= x ₱000` echo, because an input and
 * the cell it saves into legitimately show different numbers and nothing else on screen says so.
 */

import { Fragment, useEffect, useMemo, useState } from "react";
import AipMoneyInput from "@/components/aip/AipMoneyInput";
import AipProcurementItemTable from "@/components/aip/entry/AipProcurementItemTable";
import Lookup from "@/components/ui/Lookup";
import { fmtThousands, fmtPesos } from "@/lib/aip-units";
import {
  addAipExpenditure, updateAipExpenditure, deleteAipExpenditure, aipErrorMessage,
} from "@/lib/aip";
import type {
  AipExpenditure, AipExpenditureWriteResult, AccountResponse, FundingSourceResponse,
  PriceIndexPickerItem, SaveAipProcurementItemRequest,
} from "@/types";

interface Draft {
  accountId: string;
  fundingSourceId: string;
  ps: number | null;
  mooe: number | null;
  co: number | null;
  /**
   * Procurement items for this line (V18-80). Non-empty means the amount is DERIVED — the three
   * money inputs go read-only and the server routes the items' total into the column the account's
   * expense class names.
   */
  procurementItems: SaveAipProcurementItemRequest[];
}

const EMPTY: Draft = {
  accountId: "", fundingSourceId: "", ps: null, mooe: null, co: null, procurementItems: [],
};

// ── Picker accessors ────────────────────────────────────────────────────────
//
// ⚠️ Accounts and funds are picked with `Lookup`, the shared type-to-filter combobox the WFP
// expenditure form uses — NOT a plain <select>. There are ~148 accounts in the config, and a
// native select over that many is a scroll-and-hunt with no way to search. Same component, same
// behaviour as WFP, so an encoder who knows one form knows the other.
//
// ⚠️ Defined at module scope, not inline per render: the three pickers below must label and search
// an account identically, and three inline copies is how "3-11-010" finds a row in one picker and
// nothing in another.
// ⚠️ The expense class is IN the label, not only in the search text. It decides which column an
// itemised line's money lands in, so an encoder needs it while choosing — not only after. It was
// already searchable, which meant the one field that changes the outcome was findable but invisible.
const accountLabel  = (a: AccountResponse) =>
  `${a.accountNumber} · ${a.accountTitle}${a.expenseClass ? ` — ${a.expenseClass}` : ""}`;
const accountSearch = (a: AccountResponse) => `${a.accountNumber} ${a.accountTitle} ${a.expenseClass}`;

/**
 * Where this account's money goes, said out loud (PPDO-58 follow-up).
 *
 * ⚠️ **The wording differs by whether the line is itemised, because the account's authority does.**
 * On an itemised line the class is *determinative* — the server routes Σ line-total into that one
 * column and zeroes the other two. On a typed line the encoder still fills all three columns by
 * hand, so the class is only *advisory*. Saying "goes to MOOE" on a typed line would be a claim the
 * form does not enforce, and an encoder who believed it would stop checking which box they were in.
 */
function ExpenseClassNote({ expenseClass, itemised }: { expenseClass?: string; itemised: boolean }) {
  const known = expenseClass?.trim().toUpperCase();
  const recognised = known === "PS" || known === "MOOE" || known === "CO";

  // ⚠️ Silent on an unrecognised class, deliberately. The itemised case is already explained by the
  // amber banner in the procurement panel — which says what to DO about it — and two messages for
  // one condition trains people to read neither. On a typed line it is harmless and needs no note.
  if (!recognised) return null;

  return (
    <span className="flex items-center gap-1 whitespace-nowrap text-[11px] text-slate-600">
      <span className="rounded-full bg-green-100 px-1.5 py-0.5 font-semibold text-green-800">
        {known}
      </span>
      {itemised ? "← items total goes here" : "account class"}
    </span>
  );
}
const fundSearch    = (f: FundingSourceResponse) => `${f.code} ${f.name}`;

/** The fund's label, marking the one fund the ceiling actually checks. */
function fundLabel(f: FundingSourceResponse, generalFundId: number | null): string {
  return `${f.code}${f.id === generalFundId ? " (ceiling applies)" : ""}`;
}

/** Distinct funds actually in use, ignoring lines that name none. */
function distinctFunds(lines: AipExpenditure[]): number[] {
  return Array.from(new Set(lines.map((l) => l.fundingSourceId).filter((id): id is number => id != null)));
}

/**
 * Which column an itemised line's total lands in, for the on-screen preview only.
 *
 * ⚠️ **The server is authoritative** — `AipProcurementRouting` decides for real, and refuses when
 * the class is missing or unrecognised. This mirror exists so the encoder sees the figure move
 * before saving; it deliberately returns null in the same cases rather than guessing MOOE, so the
 * preview never shows a number the save is about to reject.
 */
function routedPreview(
  expenseClass: string | undefined, total: number,
): { ps: number; mooe: number; co: number } | null {
  switch (expenseClass?.trim().toUpperCase()) {
    case "PS":   return { ps: total, mooe: 0, co: 0 };
    case "MOOE": return { ps: 0, mooe: total, co: 0 };
    case "CO":   return { ps: 0, mooe: 0, co: total };
    default:     return null;
  }
}

const itemsTotal = (items: SaveAipProcurementItemRequest[]) =>
  items.reduce((sum, i) => sum + i.qty * i.unitPrice * i.numberOfDays, 0);

/**
 * A saved line's procurement items, read-only (V18-80).
 *
 * ⚠️ Rendered from the SNAPSHOTTED name / unit / price on the row, never re-resolved against the
 * current Price Index — a plan costed in September must still show September's prices after the
 * catalogue is updated. That is the whole reason those columns are stored.
 *
 * This is the display half the review tab reuses: it renders identically whether or not the caller
 * can edit, so a reviewer and an encoder read the same figures.
 */
function ProcurementItemsReadOnly({ items, columns }: {
  items: AipExpenditure["procurementItems"];
  columns: number;
}) {
  return (
    <tr className="border-t border-slate-100 bg-slate-50/60">
      <td colSpan={columns} className="px-3 py-2">
        <table className="w-full text-[11px]">
          <thead>
            <tr className="text-left text-slate-600">
              <th className="py-0.5 font-medium">Item</th>
              <th className="py-0.5 font-medium">Unit</th>
              <th className="py-0.5 text-right font-medium">Unit price</th>
              <th className="py-0.5 text-right font-medium">Qty</th>
              <th className="py-0.5 text-right font-medium">Days</th>
              <th className="py-0.5 text-right font-medium">Line total</th>
            </tr>
          </thead>
          <tbody>
            {items.map((i) => (
              <tr key={i.id} className="border-t border-slate-100">
                <td className="py-0.5 text-slate-800">{i.name}</td>
                <td className="py-0.5 text-slate-600">{i.unit || "—"}</td>
                <td className="py-0.5 text-right tabular-nums text-slate-600">{fmtPesos(i.unitPrice)}</td>
                <td className="py-0.5 text-right tabular-nums text-slate-600">{i.qty}</td>
                <td className="py-0.5 text-right tabular-nums text-slate-600">{i.numberOfDays}</td>
                <td className="py-0.5 text-right tabular-nums font-medium text-slate-800">{fmtPesos(i.lineTotal)}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <p className="mt-1 text-[10px] text-slate-600">
          Prices are as costed, not the Price Index&apos;s current ones.
        </p>
      </td>
    </tr>
  );
}

export default function AipExpenditureTable({
  activityId, lines, accounts, fundingSources, canEdit, generalFundId,
  priceIndex, priceIndexLoading, onChanged,
}: {
  activityId: number;
  lines: AipExpenditure[];
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  canEdit: boolean;
  /** Marked in the fund list, because it is the only fund the ceiling checks. */
  generalFundId: number | null;
  /** The ~6,400-row catalogue, fetched off the page's critical path (RAL-231). */
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  onChanged: (result: AipExpenditureWriteResult) => void;
}) {
  const [adding, setAdding]       = useState(false);
  const [draft, setDraft]         = useState<Draft>(EMPTY);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [busy, setBusy]           = useState(false);
  const [error, setError]         = useState<string | null>(null);
  // Which saved lines have their procurement items expanded. Collapsed by default: an activity with
  // several itemised lines would otherwise open as a wall of item rows.
  const [expanded, setExpanded]   = useState<Set<number>>(new Set());

  const funds = useMemo(() => distinctFunds(lines), [lines]);

  // ⚠️ The mode is DERIVED from the data on first render, not defaulted blindly to single. An
  // activity whose lines already span two funds cannot be shown as single-fund: the one field
  // would have to pick a winner and would misrepresent every other line.
  const [multiFund, setMultiFund] = useState(() => funds.length > 1);

  // Single-fund mode's one field. Empty when the lines disagree or there are none yet.
  const [activityFund, setActivityFund] = useState<string>(
    () => (funds.length === 1 ? String(funds[0]) : "")
  );

  // Keep the derived state honest when lines change underneath (a delete can collapse two funds
  // into one, which makes single-fund mode available again).
  useEffect(() => {
    if (funds.length > 1) setMultiFund(true);
    if (funds.length === 1) setActivityFund(String(funds[0]));
  }, [funds]);

  /** ⚠️ Switching back to single would have to rewrite every line's fund — so it is not offered. */
  const lockedToMulti = funds.length > 1;

  async function save(existingId: number | null) {
    setBusy(true);
    setError(null);
    try {
      // The one place the toggle matters. In single mode every line takes the activity's fund; in
      // multi mode the line carries its own. Either way exactly one fund reaches the server.
      const fundingSourceId = multiFund
        ? (draft.fundingSourceId ? Number(draft.fundingSourceId) : null)
        : (activityFund ? Number(activityFund) : null);

      const body = {
        accountId: draft.accountId ? Number(draft.accountId) : null,
        fundingSourceId,
        // ⚠️ Posted as typed. These are already pesos — multiplying here is decision P2-a's
        // reversed half, and it is what stored ₱4,657,655,000 for a typed 4,657,655.
        //
        // ⚠️ When the line is itemised the server DISCARDS these three and derives them from the
        // items instead. They are still sent so an un-itemised line behaves exactly as before, and
        // so that removing every item leaves a line with figures rather than nothing.
        ps:   draft.ps   ?? 0,
        mooe: draft.mooe ?? 0,
        co:   draft.co   ?? 0,
        // Always sent — an empty array is an explicit "this line has no items", which is what
        // returns an itemised line to a typed amount.
        procurementItems: draft.procurementItems,
      };
      const result = existingId === null
        ? await addAipExpenditure(activityId, body)
        : await updateAipExpenditure(existingId, body);

      onChanged(result);
      setAdding(false);
      setEditingId(null);
      setDraft(EMPTY);
    } catch (e) {
      setError(aipErrorMessage(e, "Could not save the expenditure line."));
    } finally {
      setBusy(false);
    }
  }

  /**
   * Changing the activity's fund in single mode.
   *
   * ⚠️ It rewrites the EXISTING lines too, not just future ones. In single-fund mode the field
   * means "this activity is funded by X" — leaving old lines on the previous fund would silently
   * make the activity multi-fund and contradict the one field the encoder is looking at. Few lines
   * per activity, so the loop is cheap; the alternative is a lie on screen.
   */
  async function changeActivityFund(next: string) {
    const previous = activityFund;
    setActivityFund(next);
    if (lines.length === 0 || next === previous) return;

    setBusy(true);
    setError(null);
    try {
      let last: AipExpenditureWriteResult | null = null;
      for (const line of lines) {
        last = await updateAipExpenditure(line.id, {
          accountId: line.accountId,
          fundingSourceId: next ? Number(next) : null,
          ps: line.ps, mooe: line.mooe, co: line.co,
          // ⚠️ `procurementItems` is deliberately ABSENT, not empty. Omitting it leaves each line's
          // items untouched; an empty array here would silently delete every itemised line's costing
          // just because the encoder changed the activity's fund.
        });
      }
      if (last) onChanged(last);
    } catch (e) {
      setActivityFund(previous);
      setError(aipErrorMessage(e, "Could not change this activity's funding source."));
    } finally {
      setBusy(false);
    }
  }

  async function remove(id: number) {
    setBusy(true);
    setError(null);
    try {
      // ⚠️ The result is used, not discarded. Deleting the last line takes the activity's total to
      // 0, and the parent must render that rather than keep showing the pre-delete figure.
      onChanged(await deleteAipExpenditure(id));
    } catch (e) {
      setError(aipErrorMessage(e, "Could not delete the expenditure line."));
    } finally {
      setBusy(false);
    }
  }

  function beginEdit(line: AipExpenditure) {
    setEditingId(line.id);
    setAdding(false);
    setDraft({
      accountId: line.accountId != null ? String(line.accountId) : "",
      fundingSourceId: line.fundingSourceId != null ? String(line.fundingSourceId) : "",
      // Pesos in, pesos out — the draft holds exactly what the row stores.
      ps:   line.ps,
      mooe: line.mooe,
      co:   line.co,
      // The stored ids are dropped: the draft carries the SAVE shape, which has no id and no
      // lineTotal, because the server recomputes both.
      procurementItems: line.procurementItems.map((i) => ({
        priceIndexItemId: i.priceIndexItemId,
        name: i.name,
        unit: i.unit,
        unitPrice: i.unitPrice,
        qty: i.qty,
        numberOfDays: i.numberOfDays,
      })),
    });
  }

  /**
   * Price-index items used by the activity's OTHER lines — what makes the duplicate warning
   * activity-scoped rather than line-scoped (the re-derivation of RAL-153; see
   * `AipProcurementItemTable`).
   */
  function siblingItemIds(currentLineId: number | null): number[] {
    return lines
      .filter((l) => l.id !== currentLineId)
      .flatMap((l) => l.procurementItems.map((i) => i.priceIndexItemId))
      .filter((id): id is number => id != null);
  }

  const editing = adding || editingId !== null;

  return (
    <div className="border-l-2 border-slate-200 bg-slate-50 px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-800">
          Expenditures <span className="font-normal text-slate-600">(in thousand pesos)</span>
        </h4>
        {canEdit && !editing && (
          <button type="button" onClick={() => { setAdding(true); setDraft(EMPTY); }}
            className="text-xs font-medium text-green-700 hover:underline">
            + Add line
          </button>
        )}
      </div>

      {/* ── Fund mode ────────────────────────────────────────────────────── */}
      {canEdit && (
        <div className="mt-2 flex flex-wrap items-center gap-3">
          <label className="flex items-center gap-2 text-xs text-slate-600">
            <input type="checkbox" checked={multiFund} disabled={lockedToMulti || busy}
              onChange={(e) => setMultiFund(e.target.checked)} />
            This activity draws on several funds
          </label>

          {lockedToMulti && (
            // ⚠️ Explains rather than silently disabling. Switching back would have to rewrite
            // every line's fund, and the encoder cannot tell that from a greyed checkbox.
            <span className="text-xs text-slate-600">
              — its lines already use {funds.length} funds, so single-fund entry is not available
            </span>
          )}

          {!multiFund && (
            <label className="flex items-center gap-2 text-xs text-slate-600">
              Funding source
              <Lookup
                items={fundingSources}
                value={activityFund ? Number(activityFund) : null}
                onChange={(id) => void changeActivityFund(id == null ? "" : String(id))}
                getId={(f) => f.id}
                getLabel={(f) => fundLabel(f, generalFundId)}
                getSearchText={fundSearch}
                allOptionLabel="— Fund —"
                placeholder="Search funds…"
                disabled={busy}
                className="w-56"
              />
            </label>
          )}
        </div>
      )}

      {error && (
        <p className="mt-2 border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>
      )}

      {lines.length === 0 && !adding ? (
        // Names what is missing and why it matters — cheaper than finding out at the submit gate.
        <p className="mt-2 text-xs text-slate-600">
          No expenditure lines yet. An activity must carry at least one to be submitted.
        </p>
      ) : (
        <table className="mt-2 w-full text-xs">
          <thead>
            <tr className="text-left text-slate-600">
              <th className="py-1 font-medium">Account</th>
              {/* The fund column exists only in multi-fund mode — in single mode it would repeat
                  the same value down every row. */}
              {multiFund && <th className="py-1 font-medium">Fund</th>}
              <th className="py-1 text-right font-medium">PS</th>
              <th className="py-1 text-right font-medium">MOOE</th>
              <th className="py-1 text-right font-medium">CO</th>
              <th className="py-1 text-right font-medium">Total</th>
              <th className="py-1" />
            </tr>
          </thead>
          <tbody>
            {lines.map((line) =>
              editingId === line.id ? (
                <EditRow key={line.id} draft={draft} setDraft={setDraft} accounts={accounts}
                  fundingSources={fundingSources} generalFundId={generalFundId}
                  showFund={multiFund} busy={busy}
                  priceIndex={priceIndex} priceIndexLoading={priceIndexLoading}
                  siblingPriceIndexItemIds={siblingItemIds(line.id)}
                  onSave={() => save(line.id)} onCancel={() => setEditingId(null)} />
              ) : (
                <Fragment key={line.id}>
                <tr className="border-t border-slate-200">
                  <td className="py-1.5 text-slate-800">
                    {line.accountTitle ?? "—"}
                    {/* Says the amount is derived rather than typed, on the row where someone would
                        otherwise wonder why it cannot be edited to. */}
                    {line.procurementItems.length > 0 && (
                      <button
                        type="button"
                        onClick={() => setExpanded((prev) => {
                          const next = new Set(prev);
                          if (next.has(line.id)) next.delete(line.id); else next.add(line.id);
                          return next;
                        })}
                        className="ml-1.5 text-[10px] text-slate-600 hover:underline"
                      >
                        · {line.procurementItems.length} item
                        {line.procurementItems.length === 1 ? "" : "s"}
                        {expanded.has(line.id) ? " ▴" : " ▾"}
                      </button>
                    )}
                  </td>
                  {multiFund && <td className="py-1.5 text-slate-600">{line.fundingSourceCode ?? "—"}</td>}
                  <td className="py-1.5 text-right tabular-nums text-slate-800">{fmtThousands(line.ps)}</td>
                  <td className="py-1.5 text-right tabular-nums text-slate-800">{fmtThousands(line.mooe)}</td>
                  <td className="py-1.5 text-right tabular-nums text-slate-800">{fmtThousands(line.co)}</td>
                  <td className="py-1.5 text-right font-semibold tabular-nums text-slate-800">{fmtThousands(line.total)}</td>
                  <td className="py-1.5 text-right">
                    {canEdit && (
                      <>
                        <button type="button" onClick={() => beginEdit(line)}
                          className="text-slate-600 hover:underline">Edit</button>
                        <button type="button" onClick={() => remove(line.id)} disabled={busy}
                          className="ml-2 text-red-600 hover:underline disabled:opacity-50">Delete</button>
                      </>
                    )}
                  </td>
                </tr>
                {expanded.has(line.id) && line.procurementItems.length > 0 && (
                  <ProcurementItemsReadOnly
                    items={line.procurementItems}
                    columns={multiFund ? 7 : 6} />
                )}
                </Fragment>
              )
            )}
            {adding && (
              <EditRow draft={draft} setDraft={setDraft} accounts={accounts}
                fundingSources={fundingSources} generalFundId={generalFundId}
                showFund={multiFund} busy={busy}
                priceIndex={priceIndex} priceIndexLoading={priceIndexLoading}
                siblingPriceIndexItemIds={siblingItemIds(null)}
                onSave={() => save(null)} onCancel={() => setAdding(false)} />
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}

function EditRow({
  draft, setDraft, accounts, fundingSources, generalFundId, showFund, busy,
  priceIndex, priceIndexLoading, siblingPriceIndexItemIds, onSave, onCancel,
}: {
  draft: Draft;
  setDraft: (d: Draft) => void;
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  generalFundId: number | null;
  /** False in single-fund mode — the activity's own field supplies the fund. */
  showFund: boolean;
  busy: boolean;
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  siblingPriceIndexItemIds: number[];
  onSave: () => void;
  onCancel: () => void;
}) {
  const itemised = draft.procurementItems.length > 0;
  const accountId = draft.accountId ? Number(draft.accountId) : null;
  const expenseClass = accounts.find((a) => a.id === accountId)?.expenseClass;

  // ⚠️ Only consulted while itemised. An un-itemised line keeps the typed values untouched — the
  // whole pre-PPDO-54 path is unchanged.
  const routed = itemised ? routedPreview(expenseClass, itemsTotal(draft.procurementItems)) : null;
  const shown = routed ?? { ps: draft.ps ?? 0, mooe: draft.mooe ?? 0, co: draft.co ?? 0 };

  // The column count, so the procurement panel's cell spans the whole row.
  const columns = showFund ? 7 : 6;

  function toggleItemised(on: boolean) {
    setDraft(on
      ? {
          ...draft,
          procurementItems: [
            { priceIndexItemId: null, name: "", unit: "", unitPrice: 0, qty: 1, numberOfDays: 1 },
          ],
        }
      // ⚠️ Turning it off empties the items AND leaves the typed figures where they are, so the
      // line falls back to what the encoder had entered rather than to zero.
      : { ...draft, procurementItems: [] });
  }

  return (
    <>
      <tr className="border-t border-slate-200 bg-amber-50">
        <td className="py-1.5 pr-2 min-w-[14rem] align-top">
          <Lookup
            items={accounts}
            value={accountId}
            onChange={(id) => setDraft({ ...draft, accountId: id == null ? "" : String(id) })}
            getId={(a) => a.id}
            getLabel={accountLabel}
            getSearchText={accountSearch}
            allOptionLabel="— Account —"
            placeholder="Search accounts…"
            disabled={busy}
          />
          {/* Directly under the picker that determines it, so the answer is where the question
              was asked — not in a legend elsewhere on the row. */}
          {accountId !== null && (
            <div className="mt-1">
              <ExpenseClassNote expenseClass={expenseClass} itemised={itemised} />
            </div>
          )}
        </td>
        {showFund && (
          <td className="py-1.5 pr-2 min-w-[10rem]">
            {/* ⚠️ One fund per line even here. A second fund is a second line. */}
            <Lookup
              items={fundingSources}
              value={draft.fundingSourceId ? Number(draft.fundingSourceId) : null}
              onChange={(id) => setDraft({ ...draft, fundingSourceId: id == null ? "" : String(id) })}
              getId={(f) => f.id}
              getLabel={(f) => fundLabel(f, generalFundId)}
              getSearchText={fundSearch}
              allOptionLabel="— Fund —"
              placeholder="Search funds…"
              disabled={busy}
            />
          </td>
        )}

        {/* ⚠️ Read-only once itemised. The amount is the items' amount — an editable field beside a
            derived figure is an invitation to type a number that the next save overwrites. */}
        {itemised ? (
          <>
            <td className="py-1.5 pr-1 text-right tabular-nums text-slate-600">{fmtPesos(shown.ps)}</td>
            <td className="py-1.5 pr-1 text-right tabular-nums text-slate-600">{fmtPesos(shown.mooe)}</td>
            <td className="py-1.5 pr-1 text-right tabular-nums text-slate-600">{fmtPesos(shown.co)}</td>
          </>
        ) : (
          <>
            <td className="py-1.5 pr-1"><AipMoneyInput value={draft.ps}   onChange={(v) => setDraft({ ...draft, ps: v })} /></td>
            <td className="py-1.5 pr-1"><AipMoneyInput value={draft.mooe} onChange={(v) => setDraft({ ...draft, mooe: v })} /></td>
            <td className="py-1.5 pr-1"><AipMoneyInput value={draft.co}   onChange={(v) => setDraft({ ...draft, co: v })} /></td>
          </>
        )}

        <td className="py-1.5 text-right align-top tabular-nums text-slate-600">
          {/* ⚠️ PESOS while editing, not thousands. This sums the three figures immediately to its
              left, so it has to agree with them; the row reverts to thousands once saved. */}
          {fmtPesos(shown.ps + shown.mooe + shown.co)}
          <span className="mt-0.5 block text-[10px] leading-3 text-slate-600">pesos</span>
        </td>
        <td className="py-1.5 text-right whitespace-nowrap">
          <button type="button" onClick={onSave} disabled={busy}
            className="font-medium text-green-700 hover:underline disabled:opacity-50">Save</button>
          <button type="button" onClick={onCancel} disabled={busy}
            className="ml-2 text-slate-600 hover:underline disabled:opacity-50">Cancel</button>
        </td>
      </tr>

      {/* ── Procurement items (V18-80) ──────────────────────────────────────── */}
      <tr className="bg-amber-50">
        <td colSpan={columns} className="px-1 pb-3">
          <label className="flex items-center gap-2 text-xs text-slate-600">
            <input type="checkbox" checked={itemised} disabled={busy}
              onChange={(e) => toggleItemised(e.target.checked)} />
            Cost this line from the Price Index
          </label>

          {itemised && (
            <div className="mt-2">
              {/* ⚠️ Named before the save fails. The server refuses an itemised line whose account
                  has no expense class rather than guessing MOOE, so saying so here saves the
                  encoder a round trip into an error they cannot act on from the message alone. */}
              {routed === null && (
                <p className="mb-2 border border-amber-200 bg-amber-100 px-3 py-2 text-[11px] text-amber-800">
                  {accountId === null
                    ? "Pick an account first — it decides whether these items are PS, MOOE or Capital Outlay."
                    : "This account has no PS / MOOE / CO expense class, so an itemised total has no "
                      + "column to go in. Fix it in Configuration → Accounts, or pick another account."}
                </p>
              )}

              <AipProcurementItemTable
                accountId={accountId}
                items={draft.procurementItems}
                onItemsChange={(procurementItems) => setDraft({ ...draft, procurementItems })}
                priceIndex={priceIndex}
                priceIndexLoading={priceIndexLoading}
                siblingPriceIndexItemIds={siblingPriceIndexItemIds}
              />
            </div>
          )}
        </td>
      </tr>
    </>
  );
}
