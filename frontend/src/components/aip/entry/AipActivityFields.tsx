"use client";

/**
 * An entered-year activity's descriptive fields, on the AIP Entry page (V18-42 / PPDO-52).
 *
 * ⚠️ **This exists because the entry page was a dead end without it.** It created activities with
 * `esreCode` hardcoded to null and offered no editor, while `AipSubmitService` blocks submit on it
 * (`missing-esre`). An encoder working only from this page could never satisfy their own gate.
 * eSRE is marked "needed to submit" here for that reason — the checklist at the top of the page
 * says *what* is missing; this says it where it is fixed.
 *
 * ⚠️ **CC typology is NOT one of them and must not be labelled as one** (PPDO-81). It was, and the
 * gate refused a blank — but column (14) is filled only for an activity that actually carries a
 * climate-change component, and most do not. Marking it required made encoders invent a code for
 * every ordinary operating activity, which puts fiction in a column the province reports on.
 *
 * ⚠️ **No PS / MOOE / CO / funding source, deliberately.** On an entered year those come from the
 * activity's expenditure lines below — the server recomputes them on every line write, and the
 * fund is per line. `updateAipActivityDetails` posts to an endpoint that cannot accept them;
 * `updateAipActivity` (the detail page's whole-row edit) *does* own them and would zero the
 * costing if it were used here.
 *
 * ↩️ The field set and its controls are lifted from `AipActivityRow.tsx` (the detail page's inline
 * edit) rather than redesigned — same eSRE options, same month selects — so an encoder who knows
 * one knows the other, and the two forms cannot drift apart in what they accept.
 *
 * ⚠️ **The field ORDER is the printed form's column order** (PPDO-80), not a grouping by kind:
 * description (2) → eSRE → implementing office (3) → start (4) → end (5) → expected outputs (6) →
 * CC adaptation (12) → CC mitigation (13) → CC typology (14). An encoder works with the Annex B
 * sheet open beside this form and fills it left-to-right; any other order makes them hunt. **The
 * read view and the edit view must stay in the same order as each other** — they are the same
 * fields, and a reader who expands a row and then clicks Edit must not have them move.
 */

import { useState } from "react";
import AipMoneyInput from "@/components/aip/AipMoneyInput";
import AipActivityNameCounter from "@/components/aip/AipActivityNameCounter";
import { updateAipActivityDetails, aipErrorMessage } from "@/lib/aip";
import { fmtThousands } from "@/lib/aip-units";
import { AIP_ESRE_OPTIONS, AIP_MONTHS } from "@/lib/aipConstants";
import { useAutoGrowTextarea } from "@/lib/useAutoGrowTextarea";
import { inputCls, selectCls } from "@/components/aip/AipTreeCells";
import MultiLookup, {
  joinCodes, withProponent, withoutProponent,
} from "@/components/ui/MultiLookup";
import type { AipActivityDetail, OfficeResponse } from "@/types";

export default function AipActivityFields({
  activity, canEdit, onSaved, offices, proponentOfficeCode,
}: {
  activity: AipActivityDetail;
  canEdit: boolean;
  onSaved: (updated: AipActivityDetail) => void;
  /**
   * The configured offices the implementing-office picker chooses from (PPDO-100). Passed in
   * already fetched, like every other list on this page.
   */
  offices: OfficeResponse[];
  /**
   * The office whose AIP this is — its own code is ALWAYS part of the saved value and prints first
   * (Ralph, 2026-09-16), but is never shown as a chip.
   *
   * ⚠️ The office being EDITED, not the signed-in user's: the review modal edits one named office,
   * and reading `me.officeCode` there would stamp the reviewer's own office onto someone else's row.
   * Null leaves the value exactly as picked.
   */
  proponentOfficeCode: string | null;
}) {
  const [editing, setEditing] = useState(false);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const [name, setName]                             = useState(activity.name);
  const [esreCode, setEsreCode]                     = useState(activity.esreCode ?? "");
  // ⚠️ A LIST now, not a string (PPDO-100). The column still stores one `/`-joined value — that is
  // what the form prints — so `splitCodes`/`joinCodes` are the only place the two shapes meet.
  //
  // ⚠️ The proponent office is stripped on the way IN and prepended on the way OUT. It is always
  // saved and always prints first, and is deliberately not a chip: it is not a choice, so offering
  // an × next to it would invite removing something the next save puts straight back.
  const [implementingOffices, setImplementingOffices] =
    useState<string[]>(withoutProponent(activity.implementingOffice, proponentOfficeCode));
  const [startDate, setStartDate]                   = useState(activity.startDate ?? "");
  const [endDate, setEndDate]                       = useState(activity.endDate ?? "");
  const [expectedOutputs, setExpectedOutputs]       = useState(activity.expectedOutputs ?? "");
  const [ccTypologyCode, setCcTypologyCode]         = useState(activity.ccTypologyCode ?? "");
  // ⚠️ PESOS, both on screen and on the wire — the input shows exactly what is stored. Only the
  // read-only cells divide (see lib/aip-units).
  const [ccAdaptation, setCcAdaptation] = useState<number | null>(activity.ccAdaptation);
  const [ccMitigation, setCcMitigation] = useState<number | null>(activity.ccMitigation);

  const nameRef = useAutoGrowTextarea(name);

  function beginEdit() {
    setName(activity.name);
    setEsreCode(activity.esreCode ?? "");
    setImplementingOffices(withoutProponent(activity.implementingOffice, proponentOfficeCode));
    setStartDate(activity.startDate ?? "");
    setEndDate(activity.endDate ?? "");
    setExpectedOutputs(activity.expectedOutputs ?? "");
    setCcTypologyCode(activity.ccTypologyCode ?? "");
    setCcAdaptation(activity.ccAdaptation);
    setCcMitigation(activity.ccMitigation);
    setError(null);
    setEditing(true);
  }

  async function save() {
    if (!name.trim()) { setError("The activity description is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipActivityDetails(activity.id, {
        name: name.trim(),
        esreCode: esreCode || null,
        implementingOffice: joinCodes(withProponent(implementingOffices, proponentOfficeCode)),
        startDate: startDate || null,
        endDate: endDate || null,
        expectedOutputs: expectedOutputs.trim() || null,
        ccAdaptation,
        ccMitigation,
        ccTypologyCode: ccTypologyCode.trim() || null,
      });
      onSaved(updated);
      setEditing(false);
    } catch (e) {
      setError(aipErrorMessage(e, "Could not save the activity details."));
    } finally {
      setSaving(false);
    }
  }

  // ── Read view ───────────────────────────────────────────────────────────
  if (!editing) {
    return (
      <div className="border-b border-slate-200 px-4 py-3">
        <div className="flex items-start justify-between gap-3">
          {/* ⚠️ The printed form's column order, and the same order as the edit view below —
              see this file's header. Expected outputs sits in the middle rather than at the end
              because that is where column (6) is; it spans the row only because it is free text. */}
          <dl className="grid flex-1 grid-cols-2 gap-x-6 gap-y-2 sm:grid-cols-3">
            <Field label="eSRE code" value={activity.esreCode} required />
            {/* Spans two columns so Start and End always share the next row — schedule (4) and (5)
                read as one pair, and splitting them across rows looked like two unrelated fields. */}
            <div className="sm:col-span-2">
              <Field label="Implementing office" value={activity.implementingOffice} />
            </div>
            <Field label="Start" value={activity.startDate} />
            <Field label="End" value={activity.endDate} />
            <div className="col-span-2 sm:col-span-3">
              <Field label="Expected outputs" value={activity.expectedOutputs} />
            </div>
            <Field label="CC adaptation (in thousand pesos)" value={money(activity.ccAdaptation)} />
            <Field label="CC mitigation (in thousand pesos)" value={money(activity.ccMitigation)} />
            {/* ⚠️ Not `required` — an em dash here means "no climate-change component", which is
                the normal case, not an omission (PPDO-81). */}
            <Field label="CC typology" value={activity.ccTypologyCode} />
          </dl>
          {canEdit && (
            <button type="button" onClick={beginEdit}
              className="whitespace-nowrap text-xs font-medium text-green-700 hover:underline">
              Edit details
            </button>
          )}
        </div>
      </div>
    );
  }

  // ── Edit view ───────────────────────────────────────────────────────────
  return (
    <div className="border-b border-slate-200 bg-amber-50 px-4 py-3">
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div className="sm:col-span-3">
          <Label>Activity description</Label>
          <textarea ref={nameRef} value={name} onChange={(e) => setName(e.target.value)} rows={2}
            className={`${inputCls} overflow-y-auto`} />
          <AipActivityNameCounter name={name} />
        </div>

        <div>
          {/* ⚠️ One of the two the submit gate blocks on. */}
          <Label hint="needed to submit">eSRE code</Label>
          <select value={esreCode} onChange={(e) => setEsreCode(e.target.value)} className={selectCls}>
            <option value="">—</option>
            {AIP_ESRE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </div>

        {/* Spans two columns so Start and End share the row below — see the read view. */}
        <div className="sm:col-span-2">
          {/* ↩️ **Was free text until PPDO-100.** The old note argued a select could not express a
              joint row like `OPV/LFC/HRMO` — true of a SINGLE select, which is why this is a
              multi-select that joins with the same `/` the form prints. Strict: only configured
              offices (Ralph, 2026-09-16). `MultiLookup` can take a typed value, and this call
              deliberately does not turn that on.

              ⚠️ The encoder's own office is NOT prefilled any more. It is the PROPONENT; this
              column names who IMPLEMENTS, and the two are different often enough that a default
              was quietly wrong (PPDO-80's prefill, reversed). */}
          <Label>Implementing office</Label>
          <MultiLookup
            // ⚠️ The proponent office is filtered OUT of the options, not just deduped on save — it
            // is already implied, so offering it invites picking something that then does not appear
            // as a chip, which reads as the picker ignoring the click.
            items={offices.filter((o) =>
              o.officeCode.toLowerCase() !== (proponentOfficeCode ?? "").trim().toLowerCase())}
            value={implementingOffices}
            onChange={setImplementingOffices}
            getValue={(o) => o.officeCode}
            getLabel={(o) => `${o.officeCode} — ${o.officeName}`}
            getSearchText={(o) => `${o.officeCode} ${o.officeName}`}
            placeholder="Search offices…"
            // ⚠️ Always shows the value that will actually be SAVED, proponent included. It is the
            // only place the encoder can see that their own office is in there, since it is not a
            // chip — without it, "PTO is missing" is the obvious and wrong conclusion.
            hint={`Prints as ${joinCodes(withProponent(implementingOffices, proponentOfficeCode)) ?? "—"}`}
          />
        </div>

        <div>
          <Label>Start</Label>
          <select value={startDate} onChange={(e) => setStartDate(e.target.value)} className={selectCls}>
            <option value="">—</option>
            {AIP_MONTHS.map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
        </div>

        <div>
          <Label>End</Label>
          <select value={endDate} onChange={(e) => setEndDate(e.target.value)} className={selectCls}>
            <option value="">—</option>
            {AIP_MONTHS.map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
        </div>

        <div className="sm:col-span-3">
          <Label>Expected outputs</Label>
          <textarea value={expectedOutputs} onChange={(e) => setExpectedOutputs(e.target.value)} rows={2}
            className={`${inputCls} resize-vertical`} />
        </div>

        <div>
          {/* ⚠️ CC amounts live on the activity, not on the expenditure lines — lines carry only
              PS/MOOE/CO, so this form is the only place they can be entered. */}
          {/* ⚠️ The label says pesos while the read view above says thousands — both are true, and
              the input's own `= x ₱000` echo is what joins them. Inputs are pesos everywhere on an
              AIP surface (see lib/aip-units); only read-only cells divide. */}
          <Label>CC adaptation (pesos)</Label>
          <AipMoneyInput value={ccAdaptation} onChange={setCcAdaptation} />
        </div>

        <div>
          <Label>CC mitigation (pesos)</Label>
          <AipMoneyInput value={ccMitigation} onChange={setCcMitigation} />
        </div>

        <div>
          {/* ⚠️ Optional — the submit gate does NOT block on this (PPDO-81); leave it blank on an
              activity with no climate-change component. Free text on both this form and the detail
              page's — there is no canonical list of typology codes in the config, so inventing a
              select here would reject codes the province actually uses. */}
          <Label>CC typology code</Label>
          <input value={ccTypologyCode} onChange={(e) => setCcTypologyCode(e.target.value)}
            className={inputCls} />
        </div>
      </div>

      {error && (
        <p className="mt-2 border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>
      )}

      <div className="mt-3 flex justify-end gap-2">
        <button type="button" onClick={() => setEditing(false)} disabled={saving}
          className="border border-slate-300 bg-white px-3 py-1.5 text-xs text-slate-600 hover:bg-slate-50 disabled:opacity-50">
          Cancel
        </button>
        <button type="button" onClick={save} disabled={saving}
          className="bg-green-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-800 disabled:bg-slate-300">
          {saving ? "Saving…" : "Save details"}
        </button>
      </div>
    </div>
  );
}

function Label({ children, hint }: { children: React.ReactNode; hint?: string }) {
  return (
    <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
      {children}
      {hint && <span className="ml-1 font-normal normal-case text-amber-700">— {hint}</span>}
    </label>
  );
}

/**
 * One read-only field. A missing value on a `required` field is called out rather than shown as a
 * neutral em dash: "—" reads as "not applicable", and these two are the reason submit is blocked.
 */
function Field({
  label, value, required = false,
}: { label: string; value: string | null | undefined; required?: boolean }) {
  const missing = value == null || value === "";
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-600">{label}</dt>
      <dd className={`text-sm ${missing && required ? "text-amber-700" : "text-slate-800"}`}>
        {missing ? (required ? "Not set — needed to submit" : "—") : value}
      </dd>
    </div>
  );
}

/**
 * Thousands of pesos, matching every other read-only money figure on this page.
 *
 * Returns null rather than an em dash for absent values, because `Field` distinguishes "—" from
 * "Not set — needed to submit" itself.
 */
function money(pesos: number | null | undefined): string | null {
  const rendered = fmtThousands(pesos);
  return rendered === "—" ? null : rendered;
}
