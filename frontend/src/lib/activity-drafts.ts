/**
 * PPDO-112 (V18-64) — an IndexedDB mirror of an AIP activity's descriptive fields while they are
 * being typed, so a crashed or closed tab does not lose them (`docs/v1.8/AIP_Offline_Caching_Spec.md`
 * decision 7; §3 "Draft recovery", "Draft cleared", "Edge: two tabs"; §5; §6).
 *
 * ⚠️ **A crash / accidental-close safety net, not offline editing.** Nothing here saves anything to
 * the server; every write still goes through the normal `PUT` and its rowVersion check (V18-71).
 *
 * ↩️ Ported from WFP's `localStorage` draft (`wfp/page.tsx`), not copied: WFP compares a browser
 * `savedAt` against the server's `updatedAt`. AIP activity DTOs carry no `updatedAt`, and comparing
 * a browser clock to a server clock is wrong whenever the two disagree. So "is the draft newer than
 * the last save" is answered with the row's **rowVersion** instead — see `decideDraft`.
 *
 * ⚠️ **The key carries the user** (decision 8, applied to drafts). Office PCs are shared; a key of the
 * activity alone offers user B the text user A typed and never saved. No user → no key → no draft.
 *
 * ⚠️ **A broken store must never break the page.** IndexedDB missing, disabled, blocked or corrupt:
 * every call resolves and does nothing, and the page behaves exactly as it did before this existed.
 */

import { DRAFT_STORE } from "./aip-cache-db";
import {
  createLocalDraftSession, localDrafts, userScopedKey,
  type LocalDraftStore, type LocalDraftSessionOptions, type LocalStoreOption,
} from "./local-drafts";

/**
 * Every field `AipActivityFields` edits, in the FORM's shape (what its inputs hold), not the DTO's:
 * blank strings rather than nulls, and the implementing offices as the picked list without the
 * proponent office. Comparing form shape to form shape is what lets "the draft says what the server
 * says" be exact. Expenditure lines are not here — out of scope.
 */
export interface ActivityDraftFields {
  name: string;
  esreCode: string;
  implementingOffices: string[];
  startDate: string;
  endDate: string;
  expectedOutputs: string;
  ccAdaptation: number | null;
  ccMitigation: number | null;
  ccTypologyCode: string;
}

export interface ActivityDraft {
  fields: ActivityDraftFields;
  /** The activity's rowVersion when this edit began — what the draft was typed against. */
  baseRowVersion: string | null;
  /** ISO time of the last write. Shown in the banner only; never compared with a server time. */
  savedAt: string;
}

/** Where drafts live. IndexedDB in the browser; an in-memory map in tests. */
export type DraftStore = LocalDraftStore<ActivityDraft>;

/** `undefined` = the browser's IndexedDB; `null` = no store (IndexedDB unavailable, or tests). */
type StoreOption = LocalStoreOption<ActivityDraft>;

// ── Keys ─────────────────────────────────────────────────────────────────────

/** `<userId>:<activityId>`, or null when there is no user — and then nothing is read or written. */
export function draftKey(userId: string | null | undefined, activityId: number): string | null {
  return userScopedKey(userId, activityId);
}

// ── The decision ─────────────────────────────────────────────────────────────

export function sameDraftFields(a: ActivityDraftFields, b: ActivityDraftFields): boolean {
  return a.name === b.name
    && a.esreCode === b.esreCode
    && a.implementingOffices.join("/") === b.implementingOffices.join("/")
    && a.startDate === b.startDate
    && a.endDate === b.endDate
    && a.expectedOutputs === b.expectedOutputs
    && a.ccAdaptation === b.ccAdaptation
    && a.ccMitigation === b.ccMitigation
    && a.ccTypologyCode === b.ccTypologyCode;
}

/**
 * - `none` — no draft, or the draft says exactly what the server says (the caller deletes it).
 * - `restore` — the row has not been saved since the draft began: the draft is the newer copy.
 * - `restore-changed` — the row WAS saved since (by someone else, or by this user elsewhere). Still
 *   offered, because the typed text is the user's and may be worth keeping, but worded so they know
 *   the activity moved on. Saving the restored text is then checked against `baseRowVersion`, so the
 *   overlap reaches the conflict panel instead of silently overwriting.
 *
 * Clock-independent on purpose: no browser time is compared with a server time.
 */
export type DraftDecision = "none" | "restore" | "restore-changed";

export function decideDraft(
  draft: ActivityDraft | undefined,
  server: ActivityDraftFields,
  serverRowVersion: string | null
): DraftDecision {
  if (!draft) return "none";
  if (sameDraftFields(draft.fields, server)) return "none";
  return (draft.baseRowVersion ?? null) === (serverRowVersion ?? null) ? "restore" : "restore-changed";
}

// ── Safe read / write / delete ──────────────────────────────────────────────
//
// ↩️ PPDO-192 — the machinery moved to `local-drafts.ts`, shared with the expenditure-line draft.
// This file keeps its API and its behaviour exactly; its tests are the proof.

function looksLikeDraft(value: unknown): value is ActivityDraft {
  if (!value || typeof value !== "object") return false;
  const v = value as Partial<ActivityDraft>;
  return typeof v.savedAt === "string"
    && !!v.fields && typeof v.fields === "object"
    && typeof v.fields.name === "string"
    && Array.isArray(v.fields.implementingOffices);
}

const io = localDrafts<ActivityDraft>(DRAFT_STORE, looksLikeDraft);

export async function readDraft(key: string | null, store?: StoreOption): Promise<ActivityDraft | undefined> {
  return io.read(key, store);
}

export async function writeDraft(key: string | null, draft: ActivityDraft, store?: StoreOption): Promise<void> {
  return io.write(key, draft, store);
}

export async function deleteDraft(key: string | null, store?: StoreOption): Promise<void> {
  return io.remove(key, store);
}

// ── A session: one activity's edit, debounced ───────────────────────────────

export interface DraftSession {
  /** The form changed: write it after the debounce window (restarts on every call). */
  changed(fields: ActivityDraftFields, baseRowVersion: string | null): void;
  /** Write a pending change now — the tab is being hidden or the panel unmounted. */
  flush(): Promise<void>;
  /** ⚠️ A save succeeded: cancel any pending write and delete the draft. */
  saved(): Promise<void>;
  /** ⚠️ A save was refused with a conflict (409): KEEP the draft — the text is what this protects. */
  conflicted(): Promise<void>;
  /** The user threw their edits away (Discard, Cancel): cancel any pending write and delete. */
  discarded(): Promise<void>;
}

export type DraftSessionOptions = LocalDraftSessionOptions<ActivityDraft>;

export function createDraftSession(key: string | null, options: DraftSessionOptions = {}): DraftSession {
  const session = createLocalDraftSession<
    { fields: ActivityDraftFields; baseRowVersion: string | null }, ActivityDraft
  >(key, io, ({ fields, baseRowVersion }, savedAt) => ({ fields, baseRowVersion, savedAt }), options);
  return {
    changed: (fields, baseRowVersion) => session.changed({ fields, baseRowVersion }),
    flush: session.flush,
    saved: session.saved,
    conflicted: session.conflicted,
    discarded: session.discarded,
  };
}
