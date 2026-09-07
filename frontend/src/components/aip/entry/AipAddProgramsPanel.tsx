"use client";

/**
 * Stage 1 of entry: the sub-office group and its programs, in one form (V18-42 / PPDO-52).
 *
 * ⚠️ **The interaction is LIFTED from `LdipForm.tsx` (RAL-61), not redesigned.** Pick a sector →
 * see the office-level ref-code preview → name the group, choosing an existing name from the
 * suggestions to add to it or typing a new one to start another → tick programs → Add. Redesigning
 * it is how the two forms end up subtly different, and the AIP one is the one that prints.
 *
 * ⚠️ **The sub-office group is NOT the division.** Both attach at program level and they are
 * orthogonal: the group is the `(Sector, Name)` pair on `AipOffice` and it **prints** as an office
 * row with its own shaded subtotal; a division is `ProgramDivision`, host-office only, and never
 * appears on the form. Real example — three `3000-000-1-01-001` rows on the province's FY2027
 * SOCIAL sheet: `OFFICE OF THE GOVERNOR - WARDEN`, `- AKAP-HUB`, `- HOUSING`.
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
  aipRecordId, officeConfigId, existingGroupNames, onAdded,
}: {
  aipRecordId: number;
  officeConfigId: number;
  /** Names already used under the chosen sector — the datalist that lets a user re-target a group. */
  existingGroupNames: string[];
  onAdded: (office: AipOfficeDetail) => void;
}) {
  const [open, setOpen]       = useState(false);
  const [sector, setSector]   = useState<string>(SECTORS[0]);
  const [groupName, setGroupName] = useState("");
  const [checked, setChecked] = useState<Set<number>>(new Set());

  const [group, setGroup]     = useState<AipAddablePrograms | null>(null);
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
      setGroup(null);
      setChecked(new Set());
      try {
        const addable = await getAipAddablePrograms(officeConfigId, sector);
        if (!cancelled) setGroup(addable);
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

  const targetsExisting = useMemo(
    () => existingGroupNames.some((n) => n.toUpperCase() === groupName.trim().toUpperCase()),
    [existingGroupNames, groupName]
  );

  async function add() {
    setSaving(true);
    setError(null);
    try {
      const office = await addAipProgramsWithGroup(aipRecordId, {
        officeConfigId,
        sector,
        groupName: groupName.trim() || null,
        ldipProgramIds: Array.from(checked),
      });
      onAdded(office);
      setChecked(new Set());
      setGroupName("");
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
          A sub-office group and the programs going into it. Programs come from this office&rsquo;s LDIP.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-4 p-4 sm:grid-cols-2">
        <div>
          <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
            Sector
          </label>
          <select value={sector} onChange={(e) => setSector(e.target.value)}
            className="w-full border border-slate-300 bg-white px-2 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600">
            {SECTORS.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>

        <div>
          <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
            (Preview) Office AIP ref code
          </label>
          <input readOnly value={group?.groupRefCode ?? "—"}
            className="w-full border border-slate-300 bg-slate-50 px-2 py-1.5 font-mono text-sm text-slate-600" />
        </div>

        <div className="sm:col-span-2">
          <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
            Office / sub-office name
            <span className="ml-1 font-normal normal-case text-slate-600">
              {/* The whole point of the datalist: an existing name re-targets that group, a new
                  one starts another. Saying which is happening removes the guesswork. */}
              {groupName.trim() === ""
                ? "— leave blank for this office's default block"
                : targetsExisting
                  ? "— adds to this existing group"
                  : "— starts a new group (a sector can hold several sub-offices)"}
            </span>
          </label>
          <input value={groupName} onChange={(e) => setGroupName(e.target.value.toUpperCase())}
            list="aip-group-names" maxLength={500}
            placeholder='e.g. "OFFICE OF THE GOVERNOR - WARDEN"'
            className="w-full border border-slate-300 bg-white px-2 py-1.5 text-sm uppercase text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600" />
          <datalist id="aip-group-names">
            {existingGroupNames.map((n) => <option key={n} value={n} />)}
          </datalist>
        </div>
      </div>

      {/* ── The closed list ──────────────────────────────────────────────── */}
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
        ) : group && group.programs.length > 0 ? (
          <>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-600">
              Programs in the {sector} LDIP
            </p>
            <ul className="max-h-64 space-y-1 overflow-y-auto">
              {group.programs.map((p) => (
                <li key={p.ldipProgramId}>
                  <label className="flex cursor-pointer items-start gap-2 py-1 text-sm text-slate-800">
                    {/* ⚠️ ldipProgramId, not any AIP id — this is what the add endpoint expects. */}
                    <input type="checkbox" checked={checked.has(p.ldipProgramId)} className="mt-1"
                      onChange={(e) => {
                        const next = new Set(checked);
                        if (e.target.checked) next.add(p.ldipProgramId); else next.delete(p.ldipProgramId);
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
          </>
        ) : (
          <p className="text-sm text-slate-600">This LDIP sector has no programs yet.</p>
        )}
      </div>

      {error && (
        <p className="mx-4 mb-3 border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">{error}</p>
      )}

      <div className="flex justify-end gap-2 border-t border-slate-200 px-4 py-3">
        <button type="button" onClick={() => setOpen(false)} disabled={saving}
          className="border border-slate-300 px-4 py-2 text-sm text-slate-600 hover:bg-slate-50 disabled:opacity-50">
          Cancel
        </button>
        <button type="button" onClick={add} disabled={saving || checked.size === 0}
          className="bg-green-700 px-4 py-2 text-sm font-medium text-white hover:bg-green-800 disabled:cursor-not-allowed disabled:bg-slate-300">
          {saving ? "Adding…" : `Add ${checked.size || ""} program${checked.size === 1 ? "" : "s"}`}
        </button>
      </div>
    </div>
  );
}
