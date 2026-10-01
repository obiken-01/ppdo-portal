"use client";

/**
 * The investment proposal editor for one loaded proposal (Demo 2.15 — PPDO-160, spec §6.2,
 * decisions 23, 24, 28; wireframes 1–3).
 *
 * ── How saving works (decision 24) ──
 * Two copies of the content: `saved` (what the server last returned) and `working` (what is on
 * screen). A section is unsaved when its fields differ between the two (`isSectionDirty`).
 * **Save section** sends ONE PUT: `saved` with that section's fields taken from `working`, so unsaved
 * work in other sections is never saved by accident. **Save all** sends every unsaved section. One
 * row version covers the whole proposal, so a save anywhere moves it on.
 *
 * After a save, the response becomes `saved`, and `working` is the response plus the edits of every
 * section still unsaved. The server rebuilds child rows on every save (new ids, new "g{id}" group
 * keys); the dirty check ignores ids and maps group keys to positions, so that does not make an
 * untouched section look unsaved.
 *
 * ⚠️ **Never autosave** (ticket "Do NOT"). Leaving with unsaved edits asks first — in focus mode
 * when changing section, and for every page exit through the shared `UnsavedChanges` guard.
 */

import { useCallback, useEffect, useMemo, useRef, useState, type MutableRefObject, type ReactNode } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { useToast } from "@/components/ui/Toast";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { useUnsavedChange } from "@/components/ui/UnsavedChanges";
import ProposalStatusPill from "@/components/proposals/ProposalStatusPill";
import {
  deleteProposal, exportProposal, finalizeProposal, getProposal, proposalFileName, reopenProposal, updateProposal,
  type ProposalWriteOutcome,
} from "@/lib/investment-proposals";
import {
  SECTIONS, SECTION_BY_KEY, arrange, isSectionDirty, isSectionStarted, money, sectionOfErrorPath, withSection,
  type SectionKey,
} from "@/lib/proposal-editor";
import type { Proposal, ProposalContent } from "@/types";
import ProposalRail from "./ProposalRail";
import ProposalSectionCard, { SectionSkeleton, primaryBtn, secondaryBtn, type SectionStatus } from "./ProposalSectionCard";
import ProposalWorkPlanEditor from "./ProposalWorkPlanEditor";
import AnnexH1Preview from "./AnnexH1Preview";
import {
  SectionA, SectionB, SectionC, SectionD, SectionE, SectionF, SectionI, SectionJ, SectionK, SectionL, SectionM, SectionS,
  type SectionProps,
} from "./sections";

const BODY: Partial<Record<SectionKey, (p: SectionProps) => ReactNode>> = {
  A: SectionA, B: SectionB, C: SectionC, D: SectionD, E: SectionE, F: SectionF, I: SectionI,
  J: SectionJ, K: SectionK, L: SectionL, M: SectionM, S: SectionS,
};

function manila(iso: string): string {
  return new Date(iso).toLocaleString("en-PH", {
    timeZone: "Asia/Manila", day: "numeric", month: "short", year: "numeric", hour: "numeric", minute: "2-digit",
  });
}

function plain(html: string | null): string {
  return (html ?? "").replace(/<[^>]+>/g, " ").replace(/&nbsp;/g, " ").replace(/\s+/g, " ").trim();
}

/** The one-line summary a folded card shows (spec §6.2). */
function summarise(key: SectionKey, c: ProposalContent, p: Proposal): string {
  const filledText = (h: string | null) => {
    const t = plain(h);
    return t ? (t.length > 90 ? `${t.slice(0, 90)}…` : t) : "Empty";
  };
  switch (key) {
    case "A": return [c.projectLocation ?? "No location", c.hgdgScore != null ? `HGDG score ${c.hgdgScore}` : "No HGDG score",
      `${c.beneficiariesSummary.length} beneficiary row${c.beneficiariesSummary.length === 1 ? "" : "s"}`].join(" · ");
    case "B": return filledText(c.description);
    case "C": return filledText(c.rationale);
    case "D": return `${c.benefits.filter((b) => plain(b.benefit) || plain(b.cost)).length} of 5 sectors filled`;
    case "E": return `${c.logframe.filter((l) => l.target?.trim() || l.verification?.trim()).length} of 4 rows filled`;
    case "F": return `Direct: ${c.directSameAsSummary ? "same as Section A" : `${c.targetBeneficiaries.filter((t) => t.kind === "Direct").length} rows`} · ${c.targetBeneficiaries.filter((t) => t.kind === "Indirect").length} indirect rows`;
    case "G": {
      const rows = arrange(c, p.aipRows).filter((x) => x.kind === "row").length;
      return `${rows} row${rows === 1 ? "" : "s"} in ${c.groups.length} group${c.groups.length === 1 ? "" : "s"}`;
    }
    case "H": return `${p.aipRows.length} activit${p.aipRows.length === 1 ? "y" : "ies"} · ₱${money(p.header.projectCost)}`;
    case "I": return `${c.teamMembers.length} member${c.teamMembers.length === 1 ? "" : "s"}`;
    case "J": return filledText(c.partnershipSustainability);
    case "K": return `${c.monitoring.length} M&E row${c.monitoring.length === 1 ? "" : "s"}`;
    case "L": return `${c.risks.length} risk${c.risks.length === 1 ? "" : "s"}`;
    case "M": return p.header.climateTypology || "N/A";
    case "S": return `${c.signatories.filter((s) => s.name?.trim()).length} of 4 slots printed`;
  }
}

type Dialog =
  | { kind: "leaveSection"; to: SectionKey }
  | { kind: "finalize" } | { kind: "reopen" } | { kind: "delete" } | { kind: "reload" }
  | { kind: "export"; message: string };

export default function ProposalEditor({
  initial, picker, oneAtATime, saveAllRef, onLifecycle,
}: {
  initial: Proposal;
  /** The selector row, rendered under the sticky header. */
  picker: ReactNode;
  oneAtATime: boolean;
  /** The page's leave guard calls this for "Save and continue". */
  saveAllRef: MutableRefObject<(() => Promise<boolean>) | null>;
  /** After create-adjacent changes (finalize, reopen) so the picker's statuses refresh. */
  onLifecycle: () => void;
}) {
  const router = useRouter();
  const { toast } = useToast();

  const [proposal, setProposal] = useState<Proposal>(initial);
  const [working, setWorking] = useState<ProposalContent>(initial.content);
  const saved = proposal.content;
  const [errors, setErrors] = useState<Record<string, string[]>>({});
  const [conflict, setConflict] = useState<{ keys: SectionKey[]; message: string } | null>(null);
  const [saving, setSaving] = useState<SectionKey[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [current, setCurrent] = useState<SectionKey>("A");
  const [collapsed, setCollapsed] = useState<Set<SectionKey>>(() => new Set());
  const [dialog, setDialog] = useState<Dialog | null>(null);
  const [menuOpen, setMenuOpen] = useState(false);
  // The section the reader last worked in — what Ctrl+S saves in the all-cards view.
  const lastTouched = useRef<SectionKey>("A");

  // Mirrors for async work: a save started now must read the content as it is when it finishes.
  const workingRef = useRef(working);
  workingRef.current = working;
  const proposalRef = useRef(proposal);
  proposalRef.current = proposal;

  const isFinal = proposal.status === "Final";
  const readOnly = !proposal.canEdit || isFinal;

  const dirtyKeys = useMemo(
    () => SECTIONS.filter((s) => isSectionDirty(working, saved, s.key)).map((s) => s.key),
    [working, saved],
  );
  const errorCount = useMemo(() => {
    const counts: Partial<Record<SectionKey, number>> = {};
    for (const [path, msgs] of Object.entries(errors)) {
      const k = sectionOfErrorPath(path);
      if (k) counts[k] = (counts[k] ?? 0) + msgs.length;
    }
    return counts;
  }, [errors]);

  const statusOf = useCallback((k: SectionKey): SectionStatus => {
    if (SECTION_BY_KEY[k].fromAip) return "fromAip";
    if (errorCount[k]) return "error";
    if (dirtyKeys.includes(k)) return "unsaved";
    return isSectionStarted(saved, k) ? "saved" : "notStarted";
  }, [dirtyKeys, errorCount, saved]);

  const update = useCallback((key: SectionKey) => (patch: Partial<ProposalContent>) => {
    lastTouched.current = key;
    setWorking((w) => ({ ...w, ...patch }));
  }, []);

  function clearErrors(keys: SectionKey[]) {
    setErrors((prev) => Object.fromEntries(Object.entries(prev).filter(([p]) => !keys.includes(sectionOfErrorPath(p) as SectionKey))));
  }

  /** Applies a write's new proposal, keeping the on-screen edits of every section not just saved. */
  function adopt(next: Proposal, keepDirty: SectionKey[]) {
    let w = next.content;
    for (const k of keepDirty) w = withSection(w, workingRef.current, k);
    setProposal(next);
    setWorking(w);
  }

  /** Saves `keys` in one PUT. Resolves true when it did. */
  const save = useCallback(async (keys: SectionKey[]): Promise<boolean> => {
    const p = proposalRef.current;
    const w = workingRef.current;
    const dirtyNow = SECTIONS.filter((s) => isSectionDirty(w, p.content, s.key)).map((s) => s.key);
    const toSave = keys.filter((k) => dirtyNow.includes(k));
    if (toSave.length === 0) return true;

    let body = p.content;
    for (const k of toSave) body = withSection(body, w, k);

    setSaving(toSave);
    setConflict(null);
    let outcome: ProposalWriteOutcome;
    try {
      outcome = await updateProposal(p.id, p.rowVersion, body);
    } finally {
      setSaving(null);
    }

    if (outcome.kind === "ok") {
      adopt(outcome.proposal, dirtyNow.filter((k) => !toSave.includes(k)));
      clearErrors(toSave);
      toast.success(toSave.length === 1 ? `Section ${toSave[0] === "S" ? "Signatories" : toSave[0]} saved` : `${toSave.length} sections saved`);
      return true;
    }
    if (outcome.kind === "stale") {
      setConflict({ keys: toSave, message: outcome.message });
      return false;
    }
    if (outcome.kind === "invalid") {
      clearErrors(toSave);
      setErrors((prev) => ({ ...prev, ...outcome.errors }));
      // Scroll to the first error in what was saved (spec §6.2 "Validation").
      requestAnimationFrame(() => document.querySelector("[data-field-error]")?.scrollIntoView({ block: "center", behavior: "smooth" }));
      return false;
    }
    toast.error("Couldn't save. Your changes are still here.", outcome.message);
    return false;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [toast]);

  const saveAll = useCallback(() => save(SECTIONS.map((s) => s.key)), [save]);
  useEffect(() => {
    saveAllRef.current = saveAll;
    return () => { saveAllRef.current = null; };
  }, [saveAll, saveAllRef]);

  function discard(keys: SectionKey[]) {
    let w = workingRef.current;
    for (const k of keys) w = withSection(w, proposalRef.current.content, k);
    setWorking(w);
    clearErrors(keys);
    if (conflict && keys.some((k) => conflict.keys.includes(k))) setConflict(null);
  }

  // Page exits (links, the picker, reload/close) through the shared guard.
  const dirtyLabel = dirtyKeys.length === 1 ? `Section ${dirtyKeys[0] === "S" ? "Signatories" : dirtyKeys[0]}` : `${dirtyKeys.length} sections`;
  useUnsavedChange(dirtyKeys.length > 0, dirtyLabel, () => discard(SECTIONS.map((s) => s.key)));

  // Ctrl+S saves the section in view (spec §6.2).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (!(e.ctrlKey || e.metaKey) || e.key.toLowerCase() !== "s") return;
      e.preventDefault();
      if (readOnly) return;
      void save([oneAtATime ? current : lastTouched.current]);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [save, oneAtATime, current, readOnly]);

  function goTo(key: SectionKey) {
    if (oneAtATime) {
      if (key === current) return;
      if (dirtyKeys.includes(current) && !readOnly) { setDialog({ kind: "leaveSection", to: key }); return; }
      setCurrent(key);
      window.scrollTo({ top: 0, behavior: "smooth" });
    } else {
      setCollapsed((c) => { const n = new Set(c); n.delete(key); return n; });
      requestAnimationFrame(() => document.querySelector(`[data-section="${key}"]`)?.scrollIntoView({ behavior: "smooth", block: "start" }));
    }
  }

  async function lifecycle(run: () => Promise<ProposalWriteOutcome>, done: string) {
    setBusy(true);
    try {
      const outcome = await run();
      if (outcome.kind === "ok") {
        setProposal(outcome.proposal);
        setWorking(outcome.proposal.content);
        setErrors({});
        setConflict(null);
        toast.success(done);
        onLifecycle();
      } else {
        toast.error(outcome.message);
      }
    } finally {
      setBusy(false);
    }
  }

  async function reload() {
    setBusy(true);
    try {
      const fresh = await getProposal(proposal.id);
      setProposal(fresh);
      setWorking(fresh.content);
      setErrors({});
      setConflict(null);
    } catch {
      toast.error("Couldn't reload this proposal.");
    } finally {
      setBusy(false);
    }
  }

  async function doExport() {
    setBusy(true);
    try {
      await exportProposal(proposal.id, proposalFileName(proposal.header.projectRefCode, proposal.header.projectTitle));
    } catch {
      toast.error("Couldn't export the proposal.");
    } finally {
      setBusy(false);
    }
  }

  function startExport() {
    const notes: string[] = [];
    const missing = proposal.warnings.activitiesWithoutLines;
    if (missing.length > 0)
      notes.push(`${missing.length} activit${missing.length === 1 ? "y has" : "ies have"} no expenditure detail. Their totals are printed without a breakdown.`);
    if (dirtyKeys.length > 0) notes.push("The export prints the saved proposal, not your unsaved changes.");
    if (notes.length === 0) void doExport();
    else setDialog({ kind: "export", message: notes.join(" ") });
  }

  const sectionProps = (key: SectionKey): SectionProps => ({
    proposal, content: working, update: update(key), readOnly, errors,
  });

  function body(key: SectionKey): ReactNode {
    if (key === "G") return <ProposalWorkPlanEditor content={working} aipRows={proposal.aipRows} update={update("G")} readOnly={readOnly} errors={errors} />;
    if (key === "H") return <AnnexH1Preview content={working} aipRows={proposal.aipRows} />;
    const Body = BODY[key]!;
    return <Body {...sectionProps(key)} />;
  }

  function card(key: SectionKey) {
    const def = SECTION_BY_KEY[key];
    const idx = SECTIONS.findIndex((s) => s.key === key);
    const prev = SECTIONS[idx - 1];
    const next = SECTIONS[idx + 1];
    const label = (k: SectionKey) => `${k === "S" ? "" : `${k} · `}${SECTION_BY_KEY[k].short}`;
    return (
      <div key={key} onFocusCapture={() => { lastTouched.current = key; }}>
        <ProposalSectionCard
          def={def}
          status={statusOf(key)}
          errorCount={errorCount[key] ?? 0}
          readOnly={readOnly}
          dirty={dirtyKeys.includes(key)}
          saving={saving?.includes(key) ?? false}
          conflict={conflict?.keys.includes(key) ? conflict.message : null}
          collapsible={!oneAtATime}
          collapsed={!oneAtATime && collapsed.has(key)}
          summary={summarise(key, working, proposal)}
          onToggle={() => setCollapsed((c) => { const n = new Set(c); if (n.has(key)) n.delete(key); else n.add(key); return n; })}
          onSave={() => void save([key])}
          onDiscard={() => discard([key])}
          onReload={() => setDialog({ kind: "reload" })}
          onDismissConflict={() => setConflict(null)}
          footerStart={oneAtATime && prev ? (
            <button type="button" onClick={() => goTo(prev.key)} className="text-sm font-medium text-green-700 hover:underline">← {label(prev.key)}</button>
          ) : undefined}
          footerEnd={oneAtATime && next ? (
            <button type="button" onClick={() => goTo(next.key)} className="ml-2 text-sm font-medium text-green-700 hover:underline">{label(next.key)} →</button>
          ) : undefined}
        >
          {body(key)}
        </ProposalSectionCard>
      </div>
    );
  }

  const statuses = Object.fromEntries(SECTIONS.map((s) => [s.key, statusOf(s.key)])) as Record<SectionKey, SectionStatus>;
  const h = proposal.header;
  const missing = proposal.warnings.activitiesWithoutLines;

  return (
    <div>
      {/* ── Sticky header ── */}
      <div className="sticky top-0 z-20 -mx-4 mb-3 border-b border-slate-200 bg-slate-100/95 px-4 py-3 backdrop-blur sm:-mx-6 sm:px-6">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="min-w-0">
            <Link href="/budget-planning/proposals" className="text-xs font-medium text-green-700 hover:underline">← Investment Proposals</Link>
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-lg font-semibold text-slate-800">PGOM Investment Proposal</h1>
              <ProposalStatusPill status={proposal.status} />
              {dirtyKeys.length > 0 && (
                <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-500">
                  {dirtyKeys.length} section{dirtyKeys.length === 1 ? "" : "s"} unsaved
                </span>
              )}
            </div>
            <p className="truncate text-sm text-slate-600">{h.projectRefCode} — {h.projectTitle}</p>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {!readOnly && dirtyKeys.length > 0 && (
              <button type="button" onClick={() => void saveAll()} disabled={saving != null} className={primaryBtn}>
                Save all ({dirtyKeys.length})
              </button>
            )}
            <button type="button" onClick={startExport} disabled={busy} className={secondaryBtn}>Export Word</button>
            {proposal.canEdit && !isFinal && (
              <button type="button" onClick={() => setDialog({ kind: "finalize" })} disabled={busy || dirtyKeys.length > 0}
                title={dirtyKeys.length > 0 ? "Save or discard every section first." : undefined} className={primaryBtn}>
                Finalize
              </button>
            )}
            {isFinal && proposal.canReopen && (
              <button type="button" onClick={() => setDialog({ kind: "reopen" })} disabled={busy} className={secondaryBtn}>Reopen</button>
            )}
            {proposal.canEdit && !isFinal && (
              <div className="relative">
                <button type="button" onClick={() => setMenuOpen((o) => !o)} aria-haspopup="menu" aria-expanded={menuOpen}
                  aria-label="More actions" className={secondaryBtn}>⋯</button>
                {menuOpen && (
                  <div role="menu" className="absolute right-0 z-30 mt-1 w-44 border border-slate-200 bg-white shadow-lg">
                    <button type="button" role="menuitem" onClick={() => { setMenuOpen(false); setDialog({ kind: "delete" }); }}
                      className="block w-full px-3 py-2 text-left text-sm text-danger-500 hover:bg-danger-100">
                      Delete proposal
                    </button>
                  </div>
                )}
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="mb-3">{picker}</div>

      {/* ── Banners ── */}
      {isFinal && (
        <p className="mb-3 border border-green-200 bg-green-50 px-4 py-2 text-sm text-slate-800">
          Final{proposal.finalizedAt ? ` — ${manila(proposal.finalizedAt)}` : ""}{proposal.finalizedByName ? ` by ${proposal.finalizedByName}` : ""}.
          {" "}{proposal.canReopen ? "Reopen it to make changes." : "Only the office's department head or a PPDO administrator can reopen it."}
        </p>
      )}
      {isFinal && proposal.aipChangedSinceFinal && (
        <p className="mb-3 border border-amber-300 bg-amber-50 px-4 py-2 text-sm text-amber-900">
          The AIP has changed since this proposal was finalized. It still prints the figures it was finalized with.
        </p>
      )}
      {!proposal.canEdit && !isFinal && (
        <p className="mb-3 border border-slate-200 bg-white px-4 py-2 text-sm text-slate-600">
          You can read and export this proposal. Changes are made by the office.
        </p>
      )}
      {missing.length > 0 && (
        <p className="mb-3 border border-amber-300 bg-amber-50 px-4 py-2 text-sm text-amber-900">
          {missing.length} activit{missing.length === 1 ? "y has" : "ies have"} no expenditure detail. Their totals are printed
          without a breakdown: {missing.map((a) => a.refCode).join(", ")}.
        </p>
      )}

      {/* ── Rail + sections ── */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[14rem_1fr]">
        <div className="lg:sticky lg:top-28 lg:self-start">
          <ProposalRail current={oneAtATime ? current : null} statuses={statuses} onSelect={goTo} loading={false} />
        </div>
        <div className="min-w-0 space-y-4">
          {!oneAtATime && (
            <div className="flex justify-end gap-3 text-sm">
              <button type="button" className="font-medium text-green-700 hover:underline" onClick={() => setCollapsed(new Set())}>Expand all</button>
              <button type="button" className="font-medium text-green-700 hover:underline" onClick={() => setCollapsed(new Set(SECTIONS.map((s) => s.key)))}>Collapse all</button>
            </div>
          )}
          {oneAtATime ? card(current) : SECTIONS.map((s) => card(s.key))}
        </div>
      </div>

      {/* ── Dialogs ── */}
      {dialog?.kind === "leaveSection" && (
        <ConfirmDialog
          title="Save this section first?"
          message={`Section ${current === "S" ? "Signatories" : current} has unsaved changes.`}
          secondaryLabel="Save and continue"
          busy={saving != null}
          onSecondary={() => {
            const to = dialog.to;
            void save([current]).then((ok) => { setDialog(null); if (ok) setCurrent(to); });
          }}
          confirmLabel="Discard"
          cancelLabel="Keep editing"
          variant="warning"
          onConfirm={() => { discard([current]); setCurrent(dialog.to); }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === "finalize" && (
        <ConfirmDialog
          title="Finalize this proposal?"
          message="Finalizing locks the proposal and freezes the AIP figures it prints. Only the office's department head or a PPDO administrator can reopen it."
          confirmLabel="Finalize"
          onConfirm={() => void lifecycle(() => finalizeProposal(proposal.id, proposal.rowVersion), "Proposal finalized")}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === "reopen" && (
        <ConfirmDialog
          title="Reopen this proposal?"
          message="It goes back to Draft and reads the AIP live again. The frozen figures are discarded."
          confirmLabel="Reopen"
          variant="warning"
          onConfirm={() => void lifecycle(() => reopenProposal(proposal.id, proposal.rowVersion), "Proposal reopened")}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === "delete" && (
        <ConfirmDialog
          title="Delete this proposal?"
          message="Every section is deleted. The AIP project stays, and a new proposal can be created for it."
          confirmLabel="Delete"
          variant="danger"
          onConfirm={() => {
            void (async () => {
              setBusy(true);
              const refusal = await deleteProposal(proposal.id, proposal.rowVersion);
              setBusy(false);
              if (refusal) { toast.error(refusal); return; }
              // Nothing unsaved survives a delete, so the guard must not ask on the way out.
              discard(SECTIONS.map((s) => s.key));
              toast.success("Proposal deleted");
              router.push("/budget-planning/proposals");
            })();
          }}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === "reload" && (
        <ConfirmDialog
          title="Reload the latest version?"
          message="Your unsaved changes in every section will be lost. Copy anything you want to keep first."
          confirmLabel="Reload"
          variant="danger"
          onConfirm={() => void reload()}
          onClose={() => setDialog(null)}
        />
      )}
      {dialog?.kind === "export" && (
        <ConfirmDialog
          title="Export to Word?"
          message={dialog.message}
          confirmLabel="Export"
          onConfirm={() => void doExport()}
          onClose={() => setDialog(null)}
        />
      )}
    </div>
  );
}

/** The editor's loading shape: header and rail at once, then section-shaped skeletons (spec §6.2). */
export function ProposalEditorSkeleton({ picker }: { picker: ReactNode }) {
  return (
    <div>
      <div className="mb-3 border-b border-slate-200 py-3">
        <h1 className="text-lg font-semibold text-slate-800">PGOM Investment Proposal</h1>
        <div className="mt-1 h-4 w-64 animate-pulse bg-slate-100" />
      </div>
      <div className="mb-3">{picker}</div>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[14rem_1fr]">
        <ProposalRail current={null} statuses={{}} onSelect={() => undefined} loading />
        <div className="space-y-4">
          <SectionSkeleton shape="summary" />
          <SectionSkeleton shape="text" />
          <SectionSkeleton shape="table" />
        </div>
      </div>
    </div>
  );
}
