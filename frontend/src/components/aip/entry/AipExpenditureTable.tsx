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
 * ⚠️ **Amounts are typed and shown in ₱000 and stored in pesos**, converted here at the edge in
 * both directions. One direction without the other divides the record by a thousand on the next
 * save, and it looks entirely plausible on screen.
 */

import { useEffect, useMemo, useState } from "react";
import MoneyInput from "@/components/ui/MoneyInput";
import Lookup from "@/components/ui/Lookup";
import { fmt, toDisplayUnits, toStorageUnits } from "@/lib/aip-units";
import {
  addAipExpenditure, updateAipExpenditure, deleteAipExpenditure, aipErrorMessage,
} from "@/lib/aip";
import type {
  AipExpenditure, AipExpenditureWriteResult, AccountResponse, FundingSourceResponse,
} from "@/types";

interface Draft {
  accountId: string;
  fundingSourceId: string;
  ps: number | null;
  mooe: number | null;
  co: number | null;
}

const EMPTY: Draft = { accountId: "", fundingSourceId: "", ps: null, mooe: null, co: null };

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
const accountLabel  = (a: AccountResponse) => `${a.accountNumber} · ${a.accountTitle}`;
const accountSearch = (a: AccountResponse) => `${a.accountNumber} ${a.accountTitle} ${a.expenseClass}`;
const fundSearch    = (f: FundingSourceResponse) => `${f.code} ${f.name}`;

/** The fund's label, marking the one fund the ceiling actually checks. */
function fundLabel(f: FundingSourceResponse, generalFundId: number | null): string {
  return `${f.code}${f.id === generalFundId ? " (ceiling applies)" : ""}`;
}

/** Distinct funds actually in use, ignoring lines that name none. */
function distinctFunds(lines: AipExpenditure[]): number[] {
  return Array.from(new Set(lines.map((l) => l.fundingSourceId).filter((id): id is number => id != null)));
}

export default function AipExpenditureTable({
  activityId, lines, accounts, fundingSources, canEdit, generalFundId, onChanged,
}: {
  activityId: number;
  lines: AipExpenditure[];
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  canEdit: boolean;
  /** Marked in the fund list, because it is the only fund the ceiling checks. */
  generalFundId: number | null;
  onChanged: (result: AipExpenditureWriteResult) => void;
}) {
  const [adding, setAdding]       = useState(false);
  const [draft, setDraft]         = useState<Draft>(EMPTY);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [busy, setBusy]           = useState(false);
  const [error, setError]         = useState<string | null>(null);

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
        ps:   toStorageUnits(draft.ps)   ?? 0,
        mooe: toStorageUnits(draft.mooe) ?? 0,
        co:   toStorageUnits(draft.co)   ?? 0,
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
      ps:   toDisplayUnits(line.ps),
      mooe: toDisplayUnits(line.mooe),
      co:   toDisplayUnits(line.co),
    });
  }

  const editing = adding || editingId !== null;

  return (
    <div className="border-l-2 border-slate-200 bg-slate-50 px-4 py-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-800">
          Expenditures <span className="font-normal text-slate-600">(in ₱000)</span>
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
                  onSave={() => save(line.id)} onCancel={() => setEditingId(null)} />
              ) : (
                <tr key={line.id} className="border-t border-slate-200">
                  <td className="py-1.5 text-slate-800">{line.accountTitle ?? "—"}</td>
                  {multiFund && <td className="py-1.5 text-slate-600">{line.fundingSourceCode ?? "—"}</td>}
                  <td className="py-1.5 text-right tabular-nums text-slate-800">{fmt(toDisplayUnits(line.ps))}</td>
                  <td className="py-1.5 text-right tabular-nums text-slate-800">{fmt(toDisplayUnits(line.mooe))}</td>
                  <td className="py-1.5 text-right tabular-nums text-slate-800">{fmt(toDisplayUnits(line.co))}</td>
                  <td className="py-1.5 text-right font-semibold tabular-nums text-slate-800">{fmt(toDisplayUnits(line.total))}</td>
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
              )
            )}
            {adding && (
              <EditRow draft={draft} setDraft={setDraft} accounts={accounts}
                fundingSources={fundingSources} generalFundId={generalFundId}
                showFund={multiFund} busy={busy}
                onSave={() => save(null)} onCancel={() => setAdding(false)} />
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}

function EditRow({
  draft, setDraft, accounts, fundingSources, generalFundId, showFund, busy, onSave, onCancel,
}: {
  draft: Draft;
  setDraft: (d: Draft) => void;
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  generalFundId: number | null;
  /** False in single-fund mode — the activity's own field supplies the fund. */
  showFund: boolean;
  busy: boolean;
  onSave: () => void;
  onCancel: () => void;
}) {
  return (
    <tr className="border-t border-slate-200 bg-amber-50">
      <td className="py-1.5 pr-2 min-w-[14rem]">
        <Lookup
          items={accounts}
          value={draft.accountId ? Number(draft.accountId) : null}
          onChange={(id) => setDraft({ ...draft, accountId: id == null ? "" : String(id) })}
          getId={(a) => a.id}
          getLabel={accountLabel}
          getSearchText={accountSearch}
          allOptionLabel="— Account —"
          placeholder="Search accounts…"
          disabled={busy}
        />
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
      <td className="py-1.5 pr-1"><MoneyInput value={draft.ps}   onChange={(v) => setDraft({ ...draft, ps: v })} /></td>
      <td className="py-1.5 pr-1"><MoneyInput value={draft.mooe} onChange={(v) => setDraft({ ...draft, mooe: v })} /></td>
      <td className="py-1.5 pr-1"><MoneyInput value={draft.co}   onChange={(v) => setDraft({ ...draft, co: v })} /></td>
      <td className="py-1.5 text-right tabular-nums text-slate-600">
        {fmt((draft.ps ?? 0) + (draft.mooe ?? 0) + (draft.co ?? 0))}
      </td>
      <td className="py-1.5 text-right whitespace-nowrap">
        <button type="button" onClick={onSave} disabled={busy}
          className="font-medium text-green-700 hover:underline disabled:opacity-50">Save</button>
        <button type="button" onClick={onCancel} disabled={busy}
          className="ml-2 text-slate-600 hover:underline disabled:opacity-50">Cancel</button>
      </td>
    </tr>
  );
}
