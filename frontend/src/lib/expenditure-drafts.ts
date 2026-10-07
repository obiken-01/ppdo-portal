/**
 * PPDO-192 — an IndexedDB mirror of AIP Entry's open expenditure-line editor (new or existing line,
 * procurement items included), so a crashed or closed tab does not lose it
 * (`docs/v1.8/AIP_Expenditure_Draft_Spec.md`). Follows PPDO-112's activity draft
 * (`activity-drafts.ts`) and shares its machinery (`local-drafts.ts`).
 *
 * ⚠️ **A crash / accidental-close safety net, not offline editing.** Every save is still the normal
 * request with its rowVersion check (V18-71).
 *
 * ⚠️ **One slot per user per activity** (decision 1). The table has one open editor at a time, so
 * there is only ever one line's worth of unsaved typing; the slot records which line it is.
 *
 * ⚠️ **Prices are not re-read on restore** (decision 8). A stored unit price is the encoder's figure:
 * the server stores what it is sent and never re-reads the price index, and the price stays editable.
 */

import { EXPENDITURE_DRAFT_STORE } from "./aip-cache-db";
import {
  createLocalDraftSession, localDrafts, userScopedKey,
  type LocalDraftSession, type LocalDraftSessionOptions, type LocalDraftStore, type LocalStoreOption,
} from "./local-drafts";
import type { AipExpenditure, SaveAipProcurementItemRequest } from "@/types";

/**
 * The line editor's own form shape (`AipExpenditureTable`'s `Draft`): ids as strings ("" = none),
 * amounts as typed (null = blank), items in the SAVE shape (no id, no lineTotal).
 */
export interface ExpenditureDraftFields {
  accountId: string;
  fundingSourceId: string;
  ps: number | null;
  mooe: number | null;
  co: number | null;
  procurementItems: SaveAipProcurementItemRequest[];
}

export const EMPTY_EXPENDITURE_FIELDS: ExpenditureDraftFields = {
  accountId: "", fundingSourceId: "", ps: null, mooe: null, co: null, procurementItems: [],
};

/** Which editor the typing belongs to: the add row, or one saved line. */
export type ExpenditureDraftTarget = { kind: "new" } | { kind: "line"; lineId: number };

export interface ExpenditureDraft {
  target: ExpenditureDraftTarget;
  fields: ExpenditureDraftFields;
  /** The line's rowVersion when the edit began; null for a new line. */
  baseRowVersion: string | null;
  /** ISO time of the last write. Shown in the banner only; never compared with a server time. */
  savedAt: string;
}

export type ExpenditureDraftStore = LocalDraftStore<ExpenditureDraft>;

/** `<userId>:<activityId>`, or null without a user. */
export function expenditureDraftKey(userId: string | null | undefined, activityId: number): string | null {
  return userScopedKey(userId, activityId);
}

/** A saved line as the editor would open it. The one mapping both the editor and the decision use. */
export function lineToDraftFields(line: AipExpenditure): ExpenditureDraftFields {
  return {
    accountId: line.accountId != null ? String(line.accountId) : "",
    fundingSourceId: line.fundingSourceId != null ? String(line.fundingSourceId) : "",
    // Pesos in, pesos out — the draft holds exactly what the row stores.
    ps: line.ps,
    mooe: line.mooe,
    co: line.co,
    procurementItems: line.procurementItems.map((i) => ({
      priceIndexItemId: i.priceIndexItemId,
      name: i.name,
      unit: i.unit,
      unitPrice: i.unitPrice,
      qty: i.qty,
      numberOfDays: i.numberOfDays,
      periodNo: i.periodNo,
    })),
  };
}

// ── The decision ─────────────────────────────────────────────────────────────

function itemsKey(items: SaveAipProcurementItemRequest[]): string {
  return JSON.stringify(items.map((i) => [
    i.priceIndexItemId ?? null, i.name, i.unit, i.unitPrice, i.qty, i.numberOfDays, i.periodNo,
  ]));
}

/**
 * Whether a draft says what a saved line says — what a save of the draft would produce.
 *
 * - The fund is compared only when the draft names one. In single-fund mode the editor leaves the
 *   line's fund blank and the save takes the activity's; a blank is "whatever the activity has".
 * - A blank amount is the 0 the save sends.
 * - On an itemised draft the amounts are not compared: the server derives them from the items.
 */
export function sameExpenditureContent(draft: ExpenditureDraftFields, saved: ExpenditureDraftFields): boolean {
  if (draft.accountId !== saved.accountId) return false;
  if (draft.fundingSourceId !== "" && draft.fundingSourceId !== saved.fundingSourceId) return false;
  if (draft.procurementItems.length > 0 || saved.procurementItems.length > 0) {
    return itemsKey(draft.procurementItems) === itemsKey(saved.procurementItems);
  }
  return (draft.ps ?? 0) === (saved.ps ?? 0)
    && (draft.mooe ?? 0) === (saved.mooe ?? 0)
    && (draft.co ?? 0) === (saved.co ?? 0);
}

function isEmpty(f: ExpenditureDraftFields): boolean {
  return f.accountId === "" && f.fundingSourceId === ""
    && (f.ps ?? 0) === 0 && (f.mooe ?? 0) === 0 && (f.co ?? 0) === 0
    && f.procurementItems.length === 0;
}

/**
 * - `none` — nothing worth offering; the caller deletes the draft silently. No draft; an empty
 *   new-line draft; a draft equal to its saved line; ⚠️ or (decision 4a) a NEW-line draft equal to a
 *   saved line of the activity: it was saved and the delete never ran, and offering it would
 *   create a duplicate line.
 * - `restore` — the draft is newer than its line (or is a new line nobody saved).
 * - `restore-changed` — its line was saved since the draft began. Still offered; saving the
 *   restored draft checks against `baseRowVersion`, so the overlap reaches the conflict panel.
 * - `restore-as-new` — its line has been deleted since (decision 4). Offered as a NEW line: the
 *   typing is the encoder's work, and nothing exists again until they press Save.
 *
 * Clock-independent: decided by rowVersion and content, never by `savedAt`.
 */
export type ExpenditureDraftDecision = "none" | "restore" | "restore-changed" | "restore-as-new";

export function decideExpenditureDraft(
  draft: ExpenditureDraft | undefined, lines: AipExpenditure[],
): ExpenditureDraftDecision {
  if (!draft || isEmpty(draft.fields)) return "none";

  if (draft.target.kind === "new") {
    return lines.some((l) => sameExpenditureContent(draft.fields, lineToDraftFields(l))) ? "none" : "restore";
  }

  const lineId = draft.target.lineId;
  const line = lines.find((l) => l.id === lineId);
  if (!line) return "restore-as-new";
  if (sameExpenditureContent(draft.fields, lineToDraftFields(line))) return "none";
  return (draft.baseRowVersion ?? null) === (line.rowVersion ?? null) ? "restore" : "restore-changed";
}

// ── Safe read / write / delete, and the session ──────────────────────────────

function looksLikeExpenditureDraft(value: unknown): value is ExpenditureDraft {
  if (!value || typeof value !== "object") return false;
  const v = value as Partial<ExpenditureDraft>;
  return typeof v.savedAt === "string"
    && !!v.target && typeof v.target === "object"
    && (v.target.kind === "new" || (v.target.kind === "line" && typeof v.target.lineId === "number"))
    && !!v.fields && typeof v.fields === "object"
    && typeof v.fields.accountId === "string"
    && Array.isArray(v.fields.procurementItems);
}

const io = localDrafts<ExpenditureDraft>(EXPENDITURE_DRAFT_STORE, looksLikeExpenditureDraft);

type StoreOption = LocalStoreOption<ExpenditureDraft>;

export function readExpenditureDraft(key: string | null, store?: StoreOption): Promise<ExpenditureDraft | undefined> {
  return io.read(key, store);
}

export function deleteExpenditureDraft(key: string | null, store?: StoreOption): Promise<void> {
  return io.remove(key, store);
}

export interface ExpenditureDraftSession extends Omit<LocalDraftSession<unknown>, "changed"> {
  changed(target: ExpenditureDraftTarget, fields: ExpenditureDraftFields, baseRowVersion: string | null): void;
}

export function createExpenditureDraftSession(
  key: string | null, options: LocalDraftSessionOptions<ExpenditureDraft> = {},
): ExpenditureDraftSession {
  type Payload = { target: ExpenditureDraftTarget; fields: ExpenditureDraftFields; baseRowVersion: string | null };
  const session = createLocalDraftSession<Payload, ExpenditureDraft>(
    key, io, ({ target, fields, baseRowVersion }, savedAt) => ({ target, fields, baseRowVersion, savedAt }), options);
  return {
    changed: (target, fields, baseRowVersion) => session.changed({ target, fields, baseRowVersion }),
    flush: session.flush,
    saved: session.saved,
    conflicted: session.conflicted,
    discarded: session.discarded,
  };
}
