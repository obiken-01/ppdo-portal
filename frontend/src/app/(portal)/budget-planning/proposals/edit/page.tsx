"use client";

/**
 * Investment proposal editor (v1.8.0 Demo 2.15 — PPDO-160, `Investment_Proposal_Spec.md` §6.2).
 * Routes (static export, so query strings like the AIP pages):
 *   ?id={proposalId}                                   → the proposal
 *   ?projectId={aipProjectId}&fiscalYear=&officeId=    → the selector on that project: the Create
 *                                                        panel when it has no proposal, else ?id=
 *
 * This file is the shell: the route, the picker's selection, loading/error, and the Create panel.
 * The editing itself — sections, saving, the leave guard's "Save and continue" — is
 * `components/proposals/editor/ProposalEditor`.
 *
 * ⚠️ **Every route change goes through the leave guard** (`useLeaveGuard`): picking another project in
 * the selector with unsaved sections asks Save and continue · Discard · Keep editing first.
 */

import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import Link from "next/link";
import { useMe } from "@/lib/me-cache";
import { budgetPlanningFallback, canOpenInvestmentProposals, isCommentOnlyReviewer } from "@/lib/budget-planning-access";
import { resolveEnteredFiscalYear } from "@/lib/aip-fiscal-years";
import { useDefaultFiscalYear } from "@/lib/default-fiscal-year";
import { listOffices } from "@/lib/config";
import {
  createProposal, getProposal, getProposalProjects, proposalEditorHref, proposalErrorMessage,
} from "@/lib/investment-proposals";
import { UnsavedChangesProvider, useLeaveGuard } from "@/components/ui/UnsavedChanges";
import ProposalPicker from "@/components/proposals/editor/ProposalPicker";
import ProposalEditor, { ProposalEditorSkeleton } from "@/components/proposals/editor/ProposalEditor";
import { primaryBtn } from "@/components/proposals/editor/ProposalSectionCard";
import type { MeResponse, OfficeResponse, Proposal, ProposalProjectOption } from "@/types";

function positive(v: string | null): number | null {
  const n = Number(v);
  return Number.isInteger(n) && n > 0 ? n : null;
}

export default function ProposalEditorPage() {
  // The editor saves through this ref; the provider's "Save and continue" calls it.
  const saveAllRef = useRef<(() => Promise<boolean>) | null>(null);
  return (
    <UnsavedChangesProvider onSaveAll={() => saveAllRef.current?.() ?? Promise.resolve(true)}>
      <Suspense fallback={null}>
        <EditorPage saveAllRef={saveAllRef} />
      </Suspense>
    </UnsavedChangesProvider>
  );
}

function EditorPage({ saveAllRef }: { saveAllRef: React.MutableRefObject<(() => Promise<boolean>) | null> }) {
  const me = useMe(canOpenInvestmentProposals, budgetPlanningFallback);
  const router = useRouter();
  const params = useSearchParams();
  const leave = useLeaveGuard();

  const id = positive(params.get("id"));
  const routeProjectId = positive(params.get("projectId"));
  const routeYear = positive(params.get("fiscalYear"));
  const routeOffice = positive(params.get("officeId"));

  const { ready: defaultReady, defaultFiscalYear } = useDefaultFiscalYear();
  const [fiscalYear, setFiscalYear] = useState<number | null>(null);
  const [officeId, setOfficeId] = useState<number | null>(routeOffice);
  const [programId, setProgramId] = useState<number | null>(null);
  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [oneAtATime, setOneAtATime] = useState(true);
  const [reloadKey, setReloadKey] = useState(0);

  const [proposal, setProposal] = useState<Proposal | null>(null);
  const [loadError, setLoadError] = useState<{ notFound: boolean } | null>(null);
  const [loading, setLoading] = useState(id != null);

  const multiOffice = me?.isHostOffice === true;

  // The year: the proposal's own when one is open; else the URL's; else the default (PPDO-145).
  useEffect(() => {
    if (fiscalYear != null || id != null) return;
    if (routeYear != null) { setFiscalYear(resolveEnteredFiscalYear(routeYear, null)); return; }
    if (defaultReady) setFiscalYear(resolveEnteredFiscalYear(null, defaultFiscalYear));
  }, [fiscalYear, id, routeYear, defaultReady, defaultFiscalYear]);

  // A guest office reads its own office only; the server pins it, so default to it for the picker.
  useEffect(() => {
    if (me && !multiOffice && officeId == null && me.officeId != null) setOfficeId(me.officeId);
  }, [me, multiOffice, officeId]);

  useEffect(() => {
    if (!multiOffice) return;
    void listOffices({ active: "true" }).then(setOffices).catch(() => setOffices([]));
  }, [multiOffice]);

  const load = useCallback(async (proposalId: number) => {
    setLoading(true);
    setLoadError(null);
    try {
      const p = await getProposal(proposalId);
      setProposal(p);
      setFiscalYear(p.header.fiscalYear);
      setOfficeId(p.header.officeId);
      setProgramId(p.header.programId);
    } catch (e) {
      setProposal(null);
      const status = (e as { response?: { status?: number } })?.response?.status;
      setLoadError({ notFound: status === 404 });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (id != null) void load(id);
    else { setProposal(null); setLoading(false); }
  }, [id, load]);

  // ?projectId= with an existing proposal goes straight to it (spec §6.2 "Routes").
  const [routeOption, setRouteOption] = useState<ProposalProjectOption | null>(null);
  const [routeOptionMissing, setRouteOptionMissing] = useState(false);
  useEffect(() => {
    if (id != null || routeProjectId == null || fiscalYear == null) return;
    if (multiOffice && officeId == null) return;
    let live = true;
    setRouteOptionMissing(false);
    getProposalProjects(fiscalYear, officeId)
      .then((list) => {
        if (!live) return;
        const o = list.find((x) => x.aipProjectId === routeProjectId) ?? null;
        if (o?.proposalId != null) { router.replace(proposalEditorHref(o.proposalId)); return; }
        setRouteOption(o);
        setRouteOptionMissing(o == null);
        if (o) setProgramId(o.programId);
      })
      .catch(() => { if (live) setRouteOptionMissing(true); });
    return () => { live = false; };
  }, [id, routeProjectId, fiscalYear, officeId, multiOffice, router, reloadKey]);

  function pickProject(o: ProposalProjectOption) {
    leave(() => {
      if (o.proposalId != null) router.push(proposalEditorHref(o.proposalId));
      else {
        const q = new URLSearchParams({ projectId: String(o.aipProjectId), fiscalYear: String(fiscalYear) });
        if (officeId != null) q.set("officeId", String(officeId));
        router.push(`/budget-planning/proposals/edit?${q.toString()}`);
      }
    });
  }

  const selectedProjectId = proposal?.header.aipProjectId ?? routeProjectId;
  const ownOffice = me?.officeId != null && proposal?.header.officeId === me.officeId;
  const aipEntryHref = proposal && ownOffice
    ? `/budget-planning/aip/entry?fiscalYear=${proposal.header.fiscalYear}&programId=${proposal.header.programId}&projectId=${proposal.header.aipProjectId}`
    : null;

  const picker = fiscalYear == null ? null : (
    <ProposalPicker
      fiscalYear={fiscalYear}
      officeId={officeId}
      programId={programId}
      projectId={selectedProjectId}
      multiOffice={multiOffice}
      offices={offices}
      aipEntryHref={aipEntryHref}
      oneAtATime={oneAtATime}
      onFiscalYear={(fy) => { setFiscalYear(fy); setProgramId(null); }}
      onOffice={(o) => { setOfficeId(o); setProgramId(null); }}
      onProgram={setProgramId}
      onProject={pickProject}
      onOneAtATime={setOneAtATime}
      reloadKey={reloadKey}
    />
  );

  return (
    <div className="p-4 sm:p-6">
      {loading || me == null ? (
        <ProposalEditorSkeleton picker={picker} />
      ) : loadError ? (
        <LoadError notFound={loadError.notFound} onRetry={() => id != null && void load(id)} />
      ) : proposal ? (
        <ProposalEditor
          key={proposal.id}
          initial={proposal}
          picker={picker}
          oneAtATime={oneAtATime}
          saveAllRef={saveAllRef}
          onLifecycle={() => setReloadKey((k) => k + 1)}
        />
      ) : (
        <NoProposal
          me={me}
          picker={picker}
          option={routeOption}
          missing={routeProjectId != null && routeOptionMissing}
          onCreated={(proposalId) => router.push(proposalEditorHref(proposalId))}
        />
      )}
    </div>
  );
}

function LoadError({ notFound, onRetry }: { notFound: boolean; onRetry: () => void }) {
  return (
    <div className="border border-slate-200 bg-white px-4 py-12 text-center">
      <h1 className="mb-2 text-lg font-semibold text-slate-800">PGOM Investment Proposal</h1>
      {notFound ? (
        <>
          <p className="text-sm text-slate-600">Proposal not found. It may have been deleted or is outside your office.</p>
          <Link href="/budget-planning/proposals" className="mt-3 inline-block text-sm font-medium text-green-700 hover:underline">
            ← Back to the list
          </Link>
        </>
      ) : (
        <>
          <p className="text-sm text-danger-500">Couldn&rsquo;t load this proposal.</p>
          <button type="button" onClick={onRetry} className="mt-3 text-sm font-medium text-green-700 hover:underline">Retry</button>
        </>
      )}
    </div>
  );
}

/** The selector with no proposal open: choose a project, or create the chosen one's proposal. */
function NoProposal({
  me, picker, option, missing, onCreated,
}: {
  me: MeResponse;
  picker: React.ReactNode;
  option: ProposalProjectOption | null;
  missing: boolean;
  onCreated: (proposalId: number) => void;
}) {
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const readOnly = isCommentOnlyReviewer(me);

  async function create(projectId: number) {
    setCreating(true);
    setError(null);
    try {
      const outcome = await createProposal(projectId);
      onCreated(outcome.kind === "created" ? outcome.proposal.id : outcome.proposalId);
    } catch (e) {
      setError(proposalErrorMessage(e, "Couldn't create the proposal."));
      setCreating(false);
    }
  }

  return (
    <div>
      <div className="mb-3 border-b border-slate-200 py-3">
        <Link href="/budget-planning/proposals" className="text-xs font-medium text-green-700 hover:underline">← Investment Proposals</Link>
        <h1 className="text-lg font-semibold text-slate-800">PGOM Investment Proposal</h1>
      </div>
      <div className="mb-3">{picker}</div>
      <div className="border border-slate-200 bg-white px-4 py-10 text-center">
        {option ? (
          <>
            <p className="text-sm font-medium text-slate-800">{option.projectRefCode} — {option.projectName}</p>
            <p className="mt-1 text-sm text-slate-600">This project has no investment proposal yet.</p>
            {!readOnly && (
              <>
                <p className="mt-1 text-xs text-slate-600">
                  Creates it with the description, objective and activity names already filled in from the AIP.
                </p>
                <button type="button" onClick={() => void create(option.aipProjectId)} disabled={creating} className={`${primaryBtn} mt-4`}>
                  {creating ? "Creating…" : "Create proposal"}
                </button>
              </>
            )}
            {error && <p role="alert" className="mt-3 text-sm text-danger-500">{error}</p>}
          </>
        ) : missing ? (
          <p className="text-sm text-slate-600">That project isn&rsquo;t in this office&rsquo;s AIP for the year shown. Choose one above.</p>
        ) : (
          <p className="text-sm text-slate-600">Choose a program and project above to open or create its proposal.</p>
        )}
      </div>
    </div>
  );
}
