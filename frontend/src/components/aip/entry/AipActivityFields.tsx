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
 *
 * PPDO-112 (V18-64) — while the form is open, what is typed is mirrored to IndexedDB (debounced),
 * so a crashed or closed tab can offer it back the next time this activity opens. Only when the
 * caller passes `draftUserId` (AIP Entry does; the review screens do not). See `lib/activity-drafts`.
 */

import { useEffect, useMemo, useRef, useState } from "react";
import { useAipUnsavedChange } from "./AipUnsavedChanges";
import AipMoneyInput from "@/components/aip/AipMoneyInput";
import AipActivityNameCounter from "@/components/aip/AipActivityNameCounter";
import { updateAipActivityDetails, aipConflict, aipErrorMessage } from "@/lib/aip";
import AipConflictPanel, { type AipConflictField } from "@/components/aip/AipConflictPanel";
import { fmtThousands } from "@/lib/aip-units";
import { AIP_MONTHS } from "@/lib/aipConstants";
import { useAipCodeOptions } from "@/hooks/useAipCodeOptions";
import AipCodeSelect from "@/components/aip/AipCodeSelect";
import AipCodeLookup from "@/components/aip/AipCodeLookup";
import { useAutoGrowTextarea } from "@/lib/useAutoGrowTextarea";
import { inputCls, selectCls } from "@/components/aip/AipTreeCells";
import MultiLookup, {
  joinCodes, withProponent, withoutProponent,
} from "@/components/ui/MultiLookup";
import {
  createDraftSession, decideDraft, draftKey, readDraft, deleteDraft,
  type ActivityDraft, type ActivityDraftFields,
} from "@/lib/activity-drafts";
import type { AipActivityDetail, AipConflict, OfficeResponse } from "@/types";

/** The form's values for a saved activity — exactly what `beginEdit` loads. */
function formFromActivity(activity: AipActivityDetail, proponentOfficeCode: string | null): ActivityDraftFields {
  return {
    name: activity.name,
    esreCode: activity.esreCode ?? "",
    implementingOffices: withoutProponent(activity.implementingOffice, proponentOfficeCode),
    startDate: activity.startDate ?? "",
    endDate: activity.endDate ?? "",
    expectedOutputs: activity.expectedOutputs ?? "",
    ccAdaptation: activity.ccAdaptation,
    ccMitigation: activity.ccMitigation,
    ccTypologyCode: activity.ccTypologyCode ?? "",
  };
}

export default function AipActivityFields({
  activity, canEdit, onSaved, offices, proponentOfficeCode, draftUserId = null,
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
  /**
   * PPDO-112 — the signed-in user, which turns the local draft mirror on. ⚠️ Part of the draft's
   * key: on a shared office PC, a key without the user would offer one encoder another's unsaved
   * text. Null (the review screens) means no draft is read or written.
   */
  draftUserId?: string | null;
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
  // Config-backed eSRE and CC typology lists, fetched once per page load (Demo 2.2 / 2.3).
  const codeOptions = useAipCodeOptions();
  // ⚠️ PESOS, both on screen and on the wire — the input shows exactly what is stored. Only the
  // read-only cells divide (see lib/aip-units).
  const [ccAdaptation, setCcAdaptation] = useState<number | null>(activity.ccAdaptation);
  const [ccMitigation, setCcMitigation] = useState<number | null>(activity.ccMitigation);

  const nameRef = useAutoGrowTextarea(name);

  // PPDO-191 (V18-71) — set when the save was refused because someone else saved this activity
  // after it was loaded. Same flow as AipActivityRow, the detail page's inline editor.
  const [conflict, setConflict] = useState<AipConflict<AipActivityDetail> | null>(null);

  // ── PPDO-112: the local draft mirror ────────────────────────────────────
  const key = draftKey(draftUserId, activity.id);
  const drafts = useMemo(() => createDraftSession(key), [key]);
  /** A draft found on open, offered in the banner until Restore, Discard or new typing. */
  const [offer, setOffer] = useState<{ draft: ActivityDraft; changedSince: boolean } | null>(null);
  /** The rowVersion this edit began against — stored with every draft write. */
  const baseRowVersion = useRef<string | null>(null);
  /**
   * Set by restoring a draft typed against an older version: Save checks against THAT version, so
   * an edit someone made since reaches the conflict panel rather than being silently overwritten.
   */
  const restoredBase = useRef<string | null>(null);
  /** Whether the stored draft is this edit's own (written or restored here), so ours to delete. */
  const ownsDraft = useRef(false);

  function loadForm(f: ActivityDraftFields) {
    setName(f.name);
    setEsreCode(f.esreCode);
    setImplementingOffices(f.implementingOffices);
    setStartDate(f.startDate);
    setEndDate(f.endDate);
    setExpectedOutputs(f.expectedOutputs);
    setCcTypologyCode(f.ccTypologyCode);
    setCcAdaptation(f.ccAdaptation);
    setCcMitigation(f.ccMitigation);
  }

  function beginEdit() {
    loadForm(formFromActivity(activity, proponentOfficeCode));
    baseRowVersion.current = activity.rowVersion ?? null;
    restoredBase.current = null;
    ownsDraft.current = false;
    setError(null);
    setConflict(null);
    setEditing(true);
  }

  /** Restore: the draft becomes the open form, dirty, exactly as if it had just been typed. */
  function restoreDraft() {
    if (!offer) return;
    loadForm(offer.draft.fields);
    baseRowVersion.current = offer.draft.baseRowVersion;
    restoredBase.current = offer.changedSince ? offer.draft.baseRowVersion : null;
    ownsDraft.current = true;
    setOffer(null);
    setError(null);
    setConflict(null);
    setEditing(true);
  }

  /** Discard: gone from IndexedDB at once; the form (if open) still shows the server's values. */
  function discardDraft() {
    setOffer(null);
    ownsDraft.current = false;
    void drafts.discarded();
  }

  // On open: is there a draft worth offering? One that only repeats the server is deleted silently.
  // Read once per activity and user — the activity prop changing afterwards is this page's own save.
  useEffect(() => {
    if (!key || !canEdit) return;
    let cancelled = false;
    void readDraft(key).then((draft) => {
      // ⚠️ Already typing in this panel? Then the store holds THIS edit's text, not the old draft.
      if (cancelled || !draft || ownsDraft.current) return;
      const decision = decideDraft(
        draft, formFromActivity(activity, proponentOfficeCode), activity.rowVersion ?? null);
      if (decision === "none") void deleteDraft(key);
      else setOffer({ draft, changedSince: decision === "restore-changed" });
    });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, canEdit]);

  // A pending write goes out now when the tab is hidden or closed, or this panel unmounts —
  // otherwise the last half-second of typing is the part a crash loses.
  useEffect(() => {
    const flush = () => { void drafts.flush(); };
    const onVisibility = () => { if (document.visibilityState === "hidden") flush(); };
    window.addEventListener("pagehide", flush);
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      window.removeEventListener("pagehide", flush);
      document.removeEventListener("visibilitychange", onVisibility);
      flush();
    };
  }, [drafts]);

  /**
   * @param rowVersion the version to save against: the activity as this page holds it, or, for an
   *   Overwrite, the version from the conflict payload, so the retry is one request.
   *   ⚠️ The page must hold the activity's CURRENT version: an expenditure write bumps it (the
   *   totals recompute), and the page stores the new one from that write's result.
   */
  async function save(rowVersion: string | null = restoredBase.current ?? activity.rowVersion ?? null) {
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
        rowVersion,
      });
      setConflict(null);
      // ⚠️ PPDO-112 — every successful save clears the draft, the Overwrite retry included: a
      // stale draft resurfacing after a real save is worse than having no draft at all.
      ownsDraft.current = false;
      void drafts.saved();
      onSaved(updated);
      setEditing(false);
    } catch (e) {
      // ⚠️ Conflict first, and never as an ordinary error: the panel is the only way to Overwrite
      // or Discard. The form is NOT cleared; what the user typed is what this protects.
      const clash = aipConflict<AipActivityDetail>(e);
      if (clash) {
        // ⚠️ PPDO-112 — and the draft is KEPT, written through now: the 409 is exactly when the
        // typed text must survive a closed tab.
        void drafts.conflicted();
        setConflict(clash);
        return;
      }
      setError(aipErrorMessage(e, "Could not save the activity details."));
    } finally {
      setSaving(false);
    }
  }

  /** Only the fields that differ, as the user sees them on this form. */
  function conflictFields(theirs: AipActivityDetail): AipConflictField[] {
    const rows: AipConflictField[] = [];
    const add = (label: string, mine: string | null, that: string | null | undefined) => {
      const a = mine?.trim() || "—";
      const b = that?.trim() || "—";
      if (a !== b) rows.push({ label, mine: a, theirs: b });
    };
    const pesos = (v: number | null | undefined) => (v == null ? null : fmtThousands(v));
    add("Description", name, theirs.name);
    add("eSRE code", esreCode, theirs.esreCode);
    add("Implementing office", joinCodes(withProponent(implementingOffices, proponentOfficeCode)),
      theirs.implementingOffice);
    add("Start", startDate, theirs.startDate);
    add("End", endDate, theirs.endDate);
    add("Expected outputs", expectedOutputs, theirs.expectedOutputs);
    add("CC typology", ccTypologyCode, theirs.ccTypologyCode);
    add("CC adaptation", pesos(ccAdaptation), pesos(theirs.ccAdaptation));
    add("CC mitigation", pesos(ccMitigation), pesos(theirs.ccMitigation));
    return rows;
  }

  // PPDO-166 — unsaved = the open form differs from the saved activity (what `beginEdit` loaded).
  // Compared field by field against the activity itself, so reverting a change is not "unsaved".
  const changed =
    name !== activity.name
    || esreCode !== (activity.esreCode ?? "")
    || joinCodes(implementingOffices)
       !== joinCodes(withoutProponent(activity.implementingOffice, proponentOfficeCode))
    || startDate !== (activity.startDate ?? "")
    || endDate !== (activity.endDate ?? "")
    || expectedOutputs !== (activity.expectedOutputs ?? "")
    || ccTypologyCode !== (activity.ccTypologyCode ?? "")
    || ccAdaptation !== activity.ccAdaptation
    || ccMitigation !== activity.ccMitigation;
  useAipUnsavedChange(editing && changed, "the activity details", () => {
    // The user chose to throw these edits away, so their draft goes with them.
    if (ownsDraft.current) { ownsDraft.current = false; void drafts.discarded(); }
    setEditing(false);
    setError(null);
  });

  // PPDO-112 — mirror the open form. Typing over an offered draft replaces it (the banner goes);
  // typing back to the saved values removes our own draft, so it cannot offer text since deleted.
  useEffect(() => {
    if (!editing || !key) return;
    if (changed) {
      ownsDraft.current = true;
      setOffer(null);
      drafts.changed({
        name, esreCode, implementingOffices, startDate, endDate, expectedOutputs,
        ccAdaptation, ccMitigation, ccTypologyCode,
      }, baseRowVersion.current);
    } else if (ownsDraft.current) {
      ownsDraft.current = false;
      void drafts.discarded();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [editing, key, changed, name, esreCode, implementingOffices, startDate, endDate,
      expectedOutputs, ccAdaptation, ccMitigation, ccTypologyCode]);

  // Not offered on a row this user cannot edit (a lock that landed after the draft was read).
  const banner = offer && canEdit && (
    <DraftBanner savedAt={offer.draft.savedAt} changedSince={offer.changedSince}
      onRestore={restoreDraft} onDiscard={discardDraft} />
  );

  // ── Read view ───────────────────────────────────────────────────────────
  if (!editing) {
    return (
      <div className="border-b border-slate-200 px-4 py-3">
        {banner}
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
      {banner}
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
          <AipCodeSelect
            value={esreCode}
            onChange={setEsreCode}
            options={codeOptions.esre}
            loaded={codeOptions.loaded}
            className={selectCls}
            ariaLabel="eSRE code"
          />
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
              activity with no climate-change component.

              ↩️ Was free text until Demo 2.3 (PPDO-125). The reason given then — "there is no
              canonical list of typology codes in the config, so inventing a select here would
              reject codes the province actually uses" — no longer holds: config carries 60
              typologies behind a CRUD page, and the free-text field is why nobody could tell a
              typo from a real code. The config table IS the canonical list now, by decision.

              ⚠️ The old concern survives in one form, so it is worth naming: a code the province
              uses but config lacks can no longer be typed in. That is deliberate — the fix belongs
              in Config → CC Typologies, where it helps every office, not in one encoder's row.
              AipCodeSelect still keeps an ALREADY-SAVED code selectable, so this cannot silently
              blank existing data. */}
          <Label>CC typology code</Label>
          <AipCodeLookup
            value={ccTypologyCode}
            onChange={setCcTypologyCode}
            options={codeOptions.ccTypology}
            loaded={codeOptions.loaded}
          />
        </div>
      </div>

      {error && (
        <p className="mt-2 border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>
      )}

      {/* PPDO-191 — in place, below the form the user is comparing against (spec §6). */}
      {conflict && (
        <div className="mt-3">
          <AipConflictPanel
            conflict={conflict}
            noun="activity"
            fields={conflictFields(conflict.current)}
            busy={saving}
            onOverwrite={() => save(conflict.currentRowVersion)}
            onDiscard={() => {
              // Their values win, by the user's choice. `current` is the row as it now stands.
              // The user discarded their text here, so its draft goes too (PPDO-112).
              ownsDraft.current = false;
              void drafts.discarded();
              setConflict(null);
              onSaved(conflict.current);
              setEditing(false);
            }}
          />
        </div>
      )}

      <div className="mt-3 flex justify-end gap-2">
        <button type="button" disabled={saving} onClick={() => {
          // Cancel throws this edit away, so its own draft goes with it (PPDO-112). A draft still
          // only OFFERED in the banner is not this edit's, and is left for the banner to decide.
          if (ownsDraft.current) { ownsDraft.current = false; void drafts.discarded(); }
          setConflict(null);
          setEditing(false);
        }}
          className="border border-slate-300 bg-white px-3 py-1.5 text-xs text-slate-600 hover:bg-slate-50 disabled:opacity-50">
          Cancel
        </button>
        <button type="button" onClick={() => save()} disabled={saving}
          className="bg-green-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-800 disabled:bg-slate-300">
          {saving ? "Saving…" : "Save details"}
        </button>
      </div>
    </div>
  );
}

/**
 * PPDO-112 — the restore prompt, inline at the top of the panel (spec §6), worded as WFP's. Amber:
 * a caution that blocks nothing. The time is Manila time, as everywhere in the portal.
 */
function DraftBanner({
  savedAt, changedSince, onRestore, onDiscard,
}: { savedAt: string; changedSince: boolean; onRestore: () => void; onDiscard: () => void }) {
  const when = new Date(savedAt).toLocaleString("en-PH", {
    timeZone: "Asia/Manila", dateStyle: "medium", timeStyle: "short",
  });
  return (
    <div role="status" className="mb-3 flex flex-wrap items-center justify-between gap-2 border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
      <p>
        A local draft from {when} was found for this activity. Restore it?
        {/* ⚠️ Said, because Save will then stop on the conflict panel: the row moved on after the
            draft began (someone saved it, possibly this user in another tab). */}
        {changedSince && (
          <span className="block text-xs">
            This activity has been saved since the draft was made. If you restore it, saving will
            show you what changed first.
          </span>
        )}
      </p>
      <div className="flex gap-2">
        <button type="button" onClick={onDiscard}
          className="border border-slate-300 bg-white px-3 py-1 text-xs text-slate-600 hover:bg-slate-50">
          Discard
        </button>
        <button type="button" onClick={onRestore}
          className="bg-green-700 px-3 py-1 text-xs font-medium text-white hover:bg-green-800">
          Restore
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
