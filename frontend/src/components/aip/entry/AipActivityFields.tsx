"use client";

/**
 * An entered-year activity's descriptive fields, on the AIP Entry page (V18-42 / PPDO-52).
 *
 * ⚠️ **This exists because the entry page was a dead end without it.** It created activities with
 * `esreCode` and `ccTypologyCode` hardcoded to null and offered no editor, while
 * `AipSubmitService` blocks submit on both (`missing-esre`, `missing-cc-typology`). An encoder
 * working only from this page could never satisfy their own gate. The two fields are marked
 * "needed to submit" here for that reason — the checklist at the top of the page says *what* is
 * missing; this says it where it is fixed.
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
import { updateAipActivityDetails, aipErrorMessage } from "@/lib/aip";
import { fmtThousands } from "@/lib/aip-units";
import { AIP_ESRE_OPTIONS, AIP_MONTHS } from "@/lib/aipConstants";
import { inputCls, selectCls } from "@/components/aip/AipTreeCells";
import type { AipActivityDetail } from "@/types";

export default function AipActivityFields({
  activity, canEdit, onSaved, defaultImplementingOffice = null,
}: {
  activity: AipActivityDetail;
  canEdit: boolean;
  onSaved: (updated: AipActivityDetail) => void;
  /**
   * The reader's own office CODE, used when this activity carries no implementing office yet
   * (PPDO-80). Codes, not names — the form's column (3) prints `OPV`, and `OPV/LFC/HRMO` where an
   * activity is run jointly, which is why the field stays freely editable text.
   *
   * ⚠️ A prefill, never a lock: it fills an EMPTY field and never overwrites a value already
   * there. An encoder who typed a joint office and then reopened the form would otherwise find
   * their own office silently back in the box.
   */
  defaultImplementingOffice?: string | null;
}) {
  const [editing, setEditing] = useState(false);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const [name, setName]                             = useState(activity.name);
  const [esreCode, setEsreCode]                     = useState(activity.esreCode ?? "");
  const [implementingOffice, setImplementingOffice] =
    useState(activity.implementingOffice ?? defaultImplementingOffice ?? "");
  const [startDate, setStartDate]                   = useState(activity.startDate ?? "");
  const [endDate, setEndDate]                       = useState(activity.endDate ?? "");
  const [expectedOutputs, setExpectedOutputs]       = useState(activity.expectedOutputs ?? "");
  const [ccTypologyCode, setCcTypologyCode]         = useState(activity.ccTypologyCode ?? "");
  // ⚠️ PESOS, both on screen and on the wire — the input shows exactly what is stored. Only the
  // read-only cells divide (see lib/aip-units).
  const [ccAdaptation, setCcAdaptation] = useState<number | null>(activity.ccAdaptation);
  const [ccMitigation, setCcMitigation] = useState<number | null>(activity.ccMitigation);

  function beginEdit() {
    setName(activity.name);
    setEsreCode(activity.esreCode ?? "");
    setImplementingOffice(activity.implementingOffice ?? defaultImplementingOffice ?? "");
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
        implementingOffice: implementingOffice.trim() || null,
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
            <Field label="Implementing office" value={activity.implementingOffice} />
            <Field label="Start" value={activity.startDate} />
            <Field label="End" value={activity.endDate} />
            <div className="col-span-2 sm:col-span-3">
              <Field label="Expected outputs" value={activity.expectedOutputs} />
            </div>
            <Field label="CC adaptation (in thousand pesos)" value={money(activity.ccAdaptation)} />
            <Field label="CC mitigation (in thousand pesos)" value={money(activity.ccMitigation)} />
            <Field label="CC typology" value={activity.ccTypologyCode} required />
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
          <textarea value={name} onChange={(e) => setName(e.target.value)} rows={2}
            className={`${inputCls} resize-vertical`} />
        </div>

        <div>
          {/* ⚠️ One of the two the submit gate blocks on. */}
          <Label hint="needed to submit">eSRE code</Label>
          <select value={esreCode} onChange={(e) => setEsreCode(e.target.value)} className={selectCls}>
            <option value="">—</option>
            {AIP_ESRE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
        </div>

        <div>
          {/* ⚠️ Free text, and it must stay free text even though it is prefilled from the
              reader's own office. The form prints joint implementations as `OPV/LFC/HRMO`, so a
              select over the office list could not express a real row. */}
          <Label>Implementing office</Label>
          <input value={implementingOffice} onChange={(e) => setImplementingOffice(e.target.value)}
            className={inputCls} />
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
          {/* ⚠️ The other field the submit gate blocks on. Free text on both this form and the
              detail page's — there is no canonical list of typology codes in the config, so
              inventing a select here would reject codes the province actually uses. */}
          <Label hint="needed to submit">CC typology code</Label>
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
