"use client";

/**
 * The unsaved-changes guard for AIP Entry (v1.8.0 — PPDO-166).
 *
 * An expenditure line, its procurement items, an activity's details and a project's fields all hold
 * their edits in component state until that editor's own Save. Before this, every way off an editor
 * — Done, Change activity/project, the pickers, a checklist or comment jump, a sidebar link, a
 * reload — dropped those edits without a word. Ralph lost a re-costed price-index line that way.
 *
 * - An editor reports itself with `useAipUnsavedChange(dirty, label, discard)`. Dirty means "differs
 *   from what was loaded", so opening an editor and changing nothing never asks.
 * - The page wraps its exits in `useAipLeaveGuard()`. Nothing unsaved → the exit runs; otherwise the
 *   "Discard unsaved changes?" dialog asks first (Keep editing / Discard — the WFP Entry dialog).
 * - While anything is unsaved, the provider also asks before an in-app link navigates away (the
 *   sidebar, "Back to search"), and arms the browser's own "Leave site?" prompt for reload/close.
 *
 * ⚠️ **The browser Back button is not caught.** The App Router has no navigation-blocking hook, and
 * faking one with history tricks breaks Back/Forward in ways worse than the loss it prevents.
 *
 * ⚠️ **No provider, no guard.** Both hooks are inert outside `AipUnsavedChangesProvider`, so an
 * editor reused elsewhere (the review modal renders `AipActivityFields`) behaves exactly as before.
 *
 * Keeping the edits and restoring them later is a separate, deferred question (PPDO-112 / PPDO-131).
 */

import {
  createContext, useCallback, useContext, useEffect, useId, useRef, useState, type ReactNode,
} from "react";
import { useRouter } from "next/navigation";
import ConfirmDialog from "@/components/ui/ConfirmDialog";

interface DirtyEntry {
  /** What is unsaved, as the dialog names it — "an expenditure line", "the activity details". */
  label: string;
  /** Drops the edit — closes the editor, as its own Cancel would. */
  discard: () => void;
}

interface UnsavedChangesContext {
  report: (id: string, entry: DirtyEntry | null) => void;
  confirmLeave: (proceed: () => void) => void;
}

const Ctx = createContext<UnsavedChangesContext | null>(null);

export function AipUnsavedChangesProvider({ children }: { children: ReactNode }) {
  const router = useRouter();
  // A ref, not state, so `confirmLeave` always sees the latest set — an exit can run in the same
  // tick as the edit that made something dirty. `dirtyCount` mirrors its size for the listeners.
  const entries = useRef(new Map<string, DirtyEntry>());
  const [dirtyCount, setDirtyCount] = useState(0);
  const [pending, setPending] = useState<{ labels: string[]; proceed: () => void } | null>(null);

  const report = useCallback((id: string, entry: DirtyEntry | null) => {
    if (entry) entries.current.set(id, entry);
    else entries.current.delete(id);
    setDirtyCount(entries.current.size);
  }, []);

  const confirmLeave = useCallback((proceed: () => void) => {
    if (entries.current.size === 0) { proceed(); return; }
    const labels = Array.from(new Set(Array.from(entries.current.values(), (e) => e.label)));
    setPending({ labels, proceed });
  }, []);

  function discardAndProceed() {
    if (!pending) return;
    // Every editor is closed, not just the ones the exit happens to unmount: a checklist jump to the
    // activity already open keeps its panel mounted, and would otherwise keep the edit on screen.
    const dirty = Array.from(entries.current.values());
    entries.current.clear();
    setDirtyCount(0);
    for (const entry of dirty) entry.discard();
    pending.proceed();
  }

  // Reload / close / typing a URL — the browser shows its own prompt; the wording is not ours to set.
  useEffect(() => {
    if (dirtyCount === 0) return;
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = "";
    };
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [dirtyCount]);

  // In-app links. A capture-phase listener on the document runs before React's own handlers (which
  // sit on the root), so it can stop a `next/link` before it navigates. External links, new-tab
  // clicks and downloads are left alone — `beforeunload` covers a same-tab external link.
  useEffect(() => {
    if (dirtyCount === 0) return;
    const onClick = (e: MouseEvent) => {
      if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
      const anchor = (e.target as Element | null)?.closest?.("a[href]") as HTMLAnchorElement | null;
      if (!anchor || (anchor.target && anchor.target !== "_self") || anchor.hasAttribute("download")) return;
      const url = new URL(anchor.href, window.location.href);
      if (url.origin !== window.location.origin) return;
      if (url.href === window.location.href) return;
      e.preventDefault();
      e.stopPropagation();
      confirmLeave(() => router.push(url.pathname + url.search + url.hash));
    };
    document.addEventListener("click", onClick, true);
    return () => document.removeEventListener("click", onClick, true);
  }, [dirtyCount, confirmLeave, router]);

  return (
    <Ctx.Provider value={{ report, confirmLeave }}>
      {children}
      {pending && (
        <ConfirmDialog
          title="Discard unsaved changes?"
          message={`You have unsaved changes to ${joinLabels(pending.labels)}. Leaving now will lose them.`}
          confirmLabel="Discard"
          cancelLabel="Keep editing"
          variant="warning"
          onConfirm={discardAndProceed}
          onClose={() => setPending(null)}
        />
      )}
    </Ctx.Provider>
  );
}

/**
 * Reports this editor's unsaved edit while `dirty` is true. `discard` closes the editor — it runs
 * when the user picks Discard, before the exit they asked for.
 */
export function useAipUnsavedChange(dirty: boolean, label: string, discard: () => void): void {
  const ctx = useContext(Ctx);
  const id = useId();
  // The latest `discard` without re-reporting on every render — it closes over the editor's state.
  const discardRef = useRef(discard);
  discardRef.current = discard;

  useEffect(() => {
    if (!ctx) return;
    ctx.report(id, dirty ? { label, discard: () => discardRef.current() } : null);
  }, [ctx, id, dirty, label]);

  // Unmounting (the panel switched away, the row saved) always clears the report.
  useEffect(() => () => ctx?.report(id, null), [ctx, id]);
}

/** Wraps an exit: runs it at once when nothing is unsaved, otherwise asks first. */
export function useAipLeaveGuard(): (proceed: () => void) => void {
  const ctx = useContext(Ctx);
  return ctx?.confirmLeave ?? runNow;
}

function runNow(proceed: () => void) {
  proceed();
}

function joinLabels(labels: string[]): string {
  if (labels.length <= 1) return labels[0] ?? "this page";
  return `${labels.slice(0, -1).join(", ")} and ${labels[labels.length - 1]}`;
}
