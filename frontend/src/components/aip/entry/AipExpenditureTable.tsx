"use client";

/**
 * Expenditure lines under one activity — the third stage of entry (V18-42 / PPDO-52).
 *
 * ⚠️ **One funding source per line.** Multi-fund is expressed as several lines, never as one line
 * naming two funds. 60% of the FY2027 file's money rows name several funds against a single
 * un-split amount, which is exactly what makes a General-Fund ceiling uncomputable — the whole
 * reason the rule exists. PPDO-53's toggle changes how many lines the form offers, not this shape.
 *
 * ⚠️ **Amounts are typed and shown in ₱000 and stored in pesos.** The conversion happens here, at
 * the edge, in both directions — `toDisplayUnits` when drawing, `toStorageUnits` when saving. One
 * direction without the other divides the record by a thousand on the next save, and it looks
 * entirely plausible on screen.
 */

import { useState } from "react";
import MoneyInput from "@/components/ui/MoneyInput";
import { fmt, toDisplayUnits, toStorageUnits } from "@/lib/aip-units";
import { selectCls } from "@/components/aip/AipTreeCells";
import { addAipExpenditure, updateAipExpenditure, deleteAipExpenditure, aipErrorMessage } from "@/lib/aip";
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

export default function AipExpenditureTable({
  activityId, lines, accounts, fundingSources, canEdit, onChanged,
}: {
  activityId: number;
  lines: AipExpenditure[];
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  canEdit: boolean;
  /** Hands back the recomputed activity so the parent updates its totals without a refetch. */
  onChanged: (result: AipExpenditureWriteResult) => void;
}) {
  const [adding, setAdding]   = useState(false);
  const [draft, setDraft]     = useState<Draft>(EMPTY);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [busy, setBusy]       = useState(false);
  const [error, setError]     = useState<string | null>(null);

  async function save(existingId: number | null) {
    setBusy(true);
    setError(null);
    try {
      const body = {
        accountId: draft.accountId ? Number(draft.accountId) : null,
        fundingSourceId: draft.fundingSourceId ? Number(draft.fundingSourceId) : null,
        // ⚠️ Both directions or neither — see the file header.
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

  return (
    <div className="border-l-2 border-slate-200 bg-slate-50 px-4 py-3">
      <div className="flex items-center justify-between">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-800">
          Expenditures <span className="font-normal text-slate-600">(in ₱000)</span>
        </h4>
        {canEdit && !adding && editingId === null && (
          <button
            type="button"
            onClick={() => { setAdding(true); setDraft(EMPTY); }}
            className="text-xs font-medium text-green-700 hover:underline"
          >
            + Add line
          </button>
        )}
      </div>

      {error && (
        <p className="mt-2 border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>
      )}

      {lines.length === 0 && !adding ? (
        // ⚠️ Names what is missing and why it matters. An activity with no lines cannot be
        // submitted, and saying so here is cheaper than finding out at the submit gate.
        <p className="mt-2 text-xs text-slate-600">
          No expenditure lines yet. An activity must carry at least one to be submitted.
        </p>
      ) : (
        <table className="mt-2 w-full text-xs">
          <thead>
            <tr className="text-left text-slate-600">
              <th className="py-1 font-medium">Account</th>
              <th className="py-1 font-medium">Fund</th>
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
                  fundingSources={fundingSources} busy={busy}
                  onSave={() => save(line.id)} onCancel={() => setEditingId(null)} />
              ) : (
                <tr key={line.id} className="border-t border-slate-200">
                  <td className="py-1.5 text-slate-800">{line.accountTitle ?? "—"}</td>
                  <td className="py-1.5 text-slate-600">{line.fundingSourceCode ?? "—"}</td>
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
                fundingSources={fundingSources} busy={busy}
                onSave={() => save(null)} onCancel={() => setAdding(false)} />
            )}
          </tbody>
        </table>
      )}
    </div>
  );
}

function EditRow({
  draft, setDraft, accounts, fundingSources, busy, onSave, onCancel,
}: {
  draft: Draft;
  setDraft: (d: Draft) => void;
  accounts: AccountResponse[];
  fundingSources: FundingSourceResponse[];
  busy: boolean;
  onSave: () => void;
  onCancel: () => void;
}) {
  return (
    <tr className="border-t border-slate-200 bg-amber-50">
      <td className="py-1.5 pr-2">
        <select value={draft.accountId} className={selectCls}
          onChange={(e) => setDraft({ ...draft, accountId: e.target.value })}>
          <option value="">— Account —</option>
          {accounts.map((a) => (
            <option key={a.id} value={a.id}>{a.accountNumber} · {a.accountTitle}</option>
          ))}
        </select>
      </td>
      <td className="py-1.5 pr-2">
        {/* ⚠️ One fund per line. A second fund is a second line. */}
        <select value={draft.fundingSourceId} className={selectCls}
          onChange={(e) => setDraft({ ...draft, fundingSourceId: e.target.value })}>
          <option value="">— Fund —</option>
          {fundingSources.map((f) => (
            <option key={f.id} value={f.id}>{f.code}</option>
          ))}
        </select>
      </td>
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
