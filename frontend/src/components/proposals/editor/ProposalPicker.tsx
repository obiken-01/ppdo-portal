"use client";

/**
 * The editor's selector row (PPDO-160, decision 29, wireframe 5): Fiscal year · Office (callers who
 * see more than one) · Program · Project, each project showing its proposal status, plus
 * **Open in AIP Entry ↗** and the **One section at a time** switch.
 *
 * Data: one `GET /proposals/projects` for the year and office. Picking a project is the page's to
 * act on (open its proposal, or the Create panel), through the unsaved-changes guard.
 *
 * ⚠️ **Open in AIP Entry is shown only for the reader's own office.** AIP Entry edits the reader's own
 * office and nothing else, so for a reviewer looking at another office's proposal the link would open
 * their own office's tree at a project id that is not in it.
 */

import { useEffect, useMemo, useState } from "react";
import Link from "next/link";
import Lookup from "@/components/ui/Lookup";
import OfficeSelect from "@/components/ui/OfficeSelect";
import ProposalStatusPill from "@/components/proposals/ProposalStatusPill";
import { ENTERED_FISCAL_YEAR_OPTIONS } from "@/lib/aip-fiscal-years";
import { getProposalProjects } from "@/lib/investment-proposals";
import type { OfficeResponse, ProposalProjectOption } from "@/types";

const label = "mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600";

export default function ProposalPicker({
  fiscalYear, officeId, programId, projectId, multiOffice, offices, aipEntryHref, oneAtATime,
  onFiscalYear, onOffice, onProgram, onProject, onOneAtATime, reloadKey,
}: {
  fiscalYear: number;
  officeId: number | null;
  programId: number | null;
  projectId: number | null;
  multiOffice: boolean;
  offices: OfficeResponse[];
  /** Null hides the link (not the reader's own office, or no project). */
  aipEntryHref: string | null;
  oneAtATime: boolean;
  onFiscalYear: (fy: number) => void;
  onOffice: (id: number | null) => void;
  onProgram: (id: number | null) => void;
  onProject: (option: ProposalProjectOption) => void;
  onOneAtATime: (on: boolean) => void;
  /** Bumped after a create/finalize/delete, so the statuses beside each project refresh. */
  reloadKey: number;
}) {
  const [options, setOptions] = useState<ProposalProjectOption[]>([]);
  const [error, setError] = useState(false);

  useEffect(() => {
    // A multi-office reader must name the office; the server answers 400 "Choose an office." otherwise.
    if (multiOffice && officeId == null) { setOptions([]); return; }
    let live = true;
    setError(false);
    getProposalProjects(fiscalYear, officeId)
      .then((list) => { if (live) setOptions(list); })
      .catch(() => { if (live) { setOptions([]); setError(true); } });
    return () => { live = false; };
  }, [fiscalYear, officeId, multiOffice, reloadKey]);

  const programs = useMemo(() => {
    const seen = new Map<number, { id: number; refCode: string; name: string }>();
    for (const o of options) if (!seen.has(o.programId)) seen.set(o.programId, { id: o.programId, refCode: o.programRefCode, name: o.programName });
    return Array.from(seen.values());
  }, [options]);
  const projects = options.filter((o) => programId == null || o.programId === programId);

  return (
    <div className="flex flex-wrap items-end gap-3 border border-slate-200 bg-white px-4 py-3">
      <div>
        <label htmlFor="proposal-fy" className={label}>Fiscal year</label>
        <select id="proposal-fy" value={fiscalYear} onChange={(e) => onFiscalYear(Number(e.target.value))}
          className="border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600">
          {ENTERED_FISCAL_YEAR_OPTIONS.map((y) => <option key={y} value={y}>FY {y}</option>)}
        </select>
      </div>
      {multiOffice && (
        <div className="w-64 max-w-full">
          <span className={label}>Office</span>
          <OfficeSelect offices={offices} value={officeId} onChange={onOffice} placeholder="Choose an office…" />
        </div>
      )}
      <div className="w-72 max-w-full">
        <span className={label}>Program</span>
        <Lookup
          items={programs}
          value={programId}
          onChange={onProgram}
          getId={(p) => p.id}
          getLabel={(p) => `${p.refCode} — ${p.name}`}
          allOptionLabel="All programs"
          placeholder="Search programs…"
          disabled={programs.length === 0}
        />
      </div>
      <div className="w-80 max-w-full">
        <span className={label}>Project</span>
        <Lookup
          items={projects}
          value={projectId}
          onChange={(id) => {
            const o = options.find((x) => x.aipProjectId === id);
            if (o) onProject(o);
          }}
          getId={(p) => p.aipProjectId}
          getLabel={(p) => `${p.projectRefCode} — ${p.projectName}`}
          renderOption={(p) => (
            <span className="flex items-center justify-between gap-2">
              <span className="min-w-0 truncate">{p.projectRefCode} — {p.projectName}</span>
              <ProposalStatusPill status={p.status} />
            </span>
          )}
          placeholder={multiOffice && officeId == null ? "Choose an office first" : "Search projects…"}
          disabled={projects.length === 0}
        />
      </div>
      {aipEntryHref && (
        <Link href={aipEntryHref} className="pb-1.5 text-sm font-medium text-green-700 hover:underline">
          Open in AIP Entry ↗
        </Link>
      )}
      <label className="ml-auto inline-flex items-center gap-2 pb-1.5 text-sm text-slate-800">
        <input type="checkbox" role="switch" checked={oneAtATime} onChange={(e) => onOneAtATime(e.target.checked)}
          className="accent-green-700" />
        One section at a time
      </label>
      {error && <p className="w-full text-xs text-danger-500">Couldn&rsquo;t load this office&rsquo;s projects.</p>}
    </div>
  );
}
