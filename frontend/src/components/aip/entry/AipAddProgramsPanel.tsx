"use client";

/**
 * Stage 1 of entry: pick programs from the office's LDIP (V18-42 / PPDO-52).
 *
 * ⚠️ **The sub-office group is NOT typed here — it comes from the LDIP.** ↩️ This panel used to
 * lift `LdipForm.tsx`'s interaction wholesale: choose a sector, then *name* the group in a free-text
 * box with a datalist of existing names, blank meaning "the default block". That was wrong twice
 * over. The grouping is already settled in the LDIP (the province's SOCIAL sector really does hold
 * `OFFICE OF THE GOVERNOR - WARDEN`, `- AKAP-HUB`, `- HOUSING` and `- LOCAL SCHOOL BOARD`), so
 * asking an encoder to retype it invited a printed block matching no LDIP row; and a base record
 * that already follows the LDIP's grouping leaves nothing for them to invent. Programs are now
 * listed under their own group heading and the server derives the group from the ticked ids.
 *
 * ⚠️ **The sub-office group is still not the division.** Both attach at program level and they are
 * orthogonal: the group is the `(Sector, Name)` pair on `AipOffice` and it **prints** as an office
 * row with its own shaded subtotal; a division is `ProgramDivision`, host-office only, and never
 * appears on the form.
 *
 * ⚠️ **Programs are a closed list.** They come from the office's LDIP and cannot be typed. There is
 * no "propose a new program" path — if one is missing it is missing from the LDIP, and that is
 * where it has to be added.
 */

import { useEffect, useMemo, useState } from "react";
import { addAipProgramsWithGroup, getAipAddablePrograms, aipErrorMessage } from "@/lib/aip";
import type { AipOfficeDetail, AipAddablePrograms } from "@/types";

const SECTORS = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"] as const;

export default function AipAddProgramsPanel({
  aipRecordId, officeConfigId, onAdded,
}: {
  aipRecordId: number;
  officeConfigId: number;
  onAdded: (office: AipOfficeDetail) => void;
}) {
  const [open, setOpen]       = useState(false);
  const [sector, setSector]   = useState<string>(SECTORS[0]);
  const [checked, setChecked] = useState<Set<number>>(new Set());

  const [addable, setAddable] = useState<AipAddablePrograms | null>(null);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);
  const [noLdip, setNoLdip]   = useState(false);

  // What this office may add for the chosen sector. Re-runs when the sector changes, because the
  // closed list is per sector.
  //
  // ⚠️ The SERVER resolves which LDIP this is, and the client must not. The rule is two-tier — the
  // office's own LDIP first, then a multi-office bulk LDIP matched on ref code — and an earlier
  // version of this panel reimplemented it, picked a different record from the one the add path
  // resolved, and every add came back "LDIP program id(s) … do not belong to this office's GENERAL
  // LDIP". Both halves were individually right. Found by live-testing.
  useEffect(() => {
    let cancelled = false;
    async function load() {
      setLoading(true);
      setError(null);
      setNoLdip(false);
      setAddable(null);
      setChecked(new Set());
      try {
        const result = await getAipAddablePrograms(officeConfigId, sector);
        if (!cancelled) setAddable(result);
      } catch (e) {
        // "no LDIP for this sector" comes back as a 400 with a sentence naming the LDIP — show it
        // as the empty state rather than as an error, because it is a prerequisite, not a fault.
        if (!cancelled) {
          const message = aipErrorMessage(e, "Could not load this office's LDIP.");
          if (message.includes("has no LDIP")) setNoLdip(true); else setError(message);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    if (open) void load();
    return () => { cancelled = true; };
  }, [open, sector, officeConfigId]);

  // ⚠️ Memoised, not `addable?.groups ?? []` inline: the fallback allocates a fresh array on
  // every render, which would make the selection memo below recompute every time.
  const groups = useMemo(() => addable?.groups ?? [], [addable]);

  /**
   * Which group the current selection belongs to, or null when it spans several.
   *
   * ⚠️ The server refuses a cross-group selection rather than splitting it — each group is its own
   * printed row — so the panel says so *before* the request instead of surfacing a 400 the encoder
   * has to decode. The same rule, stated in the two places it is felt.
   */
  const selectedGroupNames = useMemo(() => {
    const names = groups
      .filter((g) => g.programs.some((p) => checked.has(p.ldipProgramId)))
      .map((g) => g.groupName);
    return names;
  }, [groups, checked]);

  const spansGroups = selectedGroupNames.length > 1;

  async function add() {
    setSaving(true);
    setError(null);
    try {
      const office = await addAipProgramsWithGroup(aipRecordId, {
        officeConfigId,
        sector,
        ldipProgramIds: Array.from(checked),
      });
      onAdded(office);
      setChecked(new Set());
      setOpen(false);
    } catch (e) {
      setError(aipErrorMessage(e, "Could not add the programs."));
    } finally {
      setSaving(false);
    }
  }

  if (!open) {
    return (
      <button type="button" onClick={() => setOpen(true)}
        className="border border-green-700 px-4 py-2 text-sm font-medium text-green-700 transition-colors hover:bg-green-50">
        + Add programs
      </button>
    );
  }

  return (
    <div className="border border-slate-200 bg-white">
      <div className="border-b border-slate-200 px-4 py-3">
        <h3 className="text-sm font-semibold uppercase tracking-wide text-slate-800">
          Add programs
        </h3>
        <p className="mt-0.5 text-xs text-slate-600">
          Programs come from this office&rsquo;s LDIP, and each stays in the sub-office group the
          LDIP puts it in.
        </p>
      </div>

      <div className="p-4">
        <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
          Sector
        </label>
        <select value={sector} onChange={(e) => setSector(e.target.value)}
          className="w-full border border-slate-300 bg-white px-2 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600 sm:w-64">
          {SECTORS.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>

      {/* ── The closed list, grouped ─────────────────────────────────────── */}
      <div className="border-t border-slate-200 px-4 py-3">
        {loading ? (
          // Skeleton rows rather than a spinner, so the panel does not jump when they land.
          <div className="space-y-2">
            {[0, 1, 2].map((i) => <div key={i} className="h-5 w-full animate-pulse bg-slate-100" />)}
          </div>
        ) : noLdip ? (
          // ⚠️ Names the LDIP as the prerequisite. A blank picker would leave the encoder with no
          // idea what to do next — spec §3.2's empty-LDIP case.
          <p className="text-sm text-slate-600">
            This office has no {sector} LDIP, so there are no programs to add. The AIP cannot contain
            a program the LDIP does not — add it to the LDIP first.
          </p>
        ) : groups.length > 0 ? (
          <>
            {/* ⚠️ Names the source record. The resolver's second tier is a multi-office LDIP
                owned by no single office, so without this the encoder has no way to tell which
                document their closed list came from — which is exactly the question that sent
                someone hunting through the LDIP page. */}
            {addable?.ldipRefCode && (
              <p className="mb-2 text-xs text-slate-600">
                From <span className="font-mono text-slate-800">{addable.ldipRefCode}</span>
                {addable.ldipTitle ? ` — ${addable.ldipTitle}` : ""}
                {addable.isSharedLdip && (
                  <span className="ml-1">
                    {" · "}a shared multi-office LDIP, so it is not listed under your own office
                  </span>
                )}
              </p>
            )}

            <div className="max-h-72 space-y-3 overflow-y-auto">
              {groups.map((group) => (
                <div key={`${group.groupRefCode}|${group.groupName}`}>
                  {/* The group heading IS the sub-office. Several groups share a ref code, so the
                      name is what tells them apart — showing the code alone would render four
                      identical headings on PGO's SOCIAL sector. */}
                  <p className="text-xs font-semibold uppercase tracking-wide text-slate-800">
                    {group.groupName}
                  </p>
                  <p className="font-mono text-xs text-slate-600">{group.groupRefCode}</p>

                  {group.programs.length === 0 ? (
                    <p className="mt-1 text-xs text-slate-600">No programs in this group.</p>
                  ) : (
                    <ul className="mt-1 space-y-1">
                      {group.programs.map((p) => (
                        <li key={p.ldipProgramId}>
                          <label className="flex cursor-pointer items-start gap-2 py-1 text-sm text-slate-800">
                            {/* ⚠️ ldipProgramId, not any AIP id — this is what the add endpoint
                                expects, and it is also what tells the server which group. */}
                            <input type="checkbox" checked={checked.has(p.ldipProgramId)} className="mt-1"
                              onChange={(e) => {
                                const next = new Set(checked);
                                if (e.target.checked) next.add(p.ldipProgramId);
                                else next.delete(p.ldipProgramId);
                                setChecked(next);
                              }} />
                            <span>
                              <span className="mr-2 font-mono text-xs text-slate-600">{p.refCode}</span>
                              {p.name}
                            </span>
                          </label>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              ))}
            </div>
          </>
        ) : (
          <p className="text-sm text-slate-600">This LDIP sector has no programs yet.</p>
        )}
      </div>

      {spansGroups && (
        <p className="mx-4 mb-3 border border-amber-200 bg-amber-50 px-3 py-2 text-xs text-slate-800">
          You have picked programs from {selectedGroupNames.length} groups (
          {selectedGroupNames.join(", ")}). Each group is its own row on the AIP form, so add them
          one group at a time.
        </p>
      )}

      {error && (
        <p className="mx-4 mb-3 border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>
      )}

      <div className="flex justify-end gap-2 border-t border-slate-200 px-4 py-3">
        <button type="button" onClick={() => setOpen(false)} disabled={saving}
          className="border border-slate-300 px-4 py-2 text-sm text-slate-600 hover:bg-slate-50 disabled:opacity-50">
          Cancel
        </button>
        <button type="button" onClick={add} disabled={saving || checked.size === 0 || spansGroups}
          className="bg-green-700 px-4 py-2 text-sm font-medium text-white hover:bg-green-800 disabled:cursor-not-allowed disabled:bg-slate-300">
          {saving ? "Adding…" : `Add ${checked.size || ""} program${checked.size === 1 ? "" : "s"}`}
        </button>
      </div>
    </div>
  );
}
