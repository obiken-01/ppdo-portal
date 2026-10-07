/**
 * The machinery shared by AIP Entry's local drafts: the activity's details (PPDO-112,
 * `activity-drafts.ts`) and the open expenditure line (PPDO-192, `expenditure-drafts.ts`).
 *
 * Generalised out of PPDO-112 once it had a second caller (`AIP_Expenditure_Draft_Spec.md`
 * decision 7). Each feature keeps its own value shape, store, validation and decision; what is
 * shared is the part that must behave identically for both:
 *
 * - ⚠️ **A broken store never breaks the page.** IndexedDB missing, disabled, blocked or corrupt:
 *   every read resolves to nothing and every write or delete resolves and does nothing.
 * - ⚠️ **No key, no draft.** Keys carry the user (office PCs are shared); without a user there is
 *   no key, and nothing is read or written.
 * - The debounced session, whose `saved` cancels a pending write BEFORE deleting, so a stale draft
 *   cannot land after a real save.
 */

import { getAipCacheDb } from "./aip-cache-db";

/** Where drafts live. IndexedDB in the browser; an in-memory map in tests. */
export interface LocalDraftStore<V> {
  get(key: string): Promise<V | undefined>;
  put(key: string, value: V): Promise<void>;
  delete(key: string): Promise<void>;
}

/** `undefined` = the browser's IndexedDB; `null` = no store (IndexedDB unavailable, or tests). */
export type LocalStoreOption<V> = LocalDraftStore<V> | null | undefined;

/** `<userId>:<id>`, or null when there is no user — and then nothing is read or written. */
export function userScopedKey(userId: string | null | undefined, id: number): string | null {
  if (!userId) return null;
  return `${userId}:${id}`;
}

export interface LocalDrafts<V> {
  read(key: string | null, store?: LocalStoreOption<V>): Promise<V | undefined>;
  write(key: string | null, value: V, store?: LocalStoreOption<V>): Promise<void>;
  remove(key: string | null, store?: LocalStoreOption<V>): Promise<void>;
}

/**
 * Safe read / write / delete for one object store. `isValid` drops anything that is not this
 * feature's shape (a corrupt entry, or another store's value read by mistake).
 */
export function localDrafts<V>(storeName: string, isValid: (value: unknown) => value is V): LocalDrafts<V> {
  const idbStore: LocalDraftStore<V> = {
    async get(key) {
      const db = await getAipCacheDb();
      return db ? ((await db.get(storeName, key)) as V | undefined) : undefined;
    },
    async put(key, value) {
      const db = await getAipCacheDb();
      if (db) await db.put(storeName, value, key);
    },
    async delete(key) {
      const db = await getAipCacheDb();
      if (db) await db.delete(storeName, key);
    },
  };

  async function resolve(store: LocalStoreOption<V>): Promise<LocalDraftStore<V> | null> {
    if (store !== undefined) return store;
    return (await getAipCacheDb()) ? idbStore : null;
  }

  return {
    async read(key, store) {
      if (!key) return undefined;
      try {
        const s = await resolve(store);
        const value = s ? await s.get(key) : undefined;
        return isValid(value) ? value : undefined;
      } catch {
        return undefined;
      }
    },
    async write(key, value, store) {
      if (!key) return;
      try {
        const s = await resolve(store);
        if (s) await s.put(key, value);
      } catch {
        // A full quota or a closed database costs the safety net, never the page.
      }
    },
    async remove(key, store) {
      if (!key) return;
      try {
        const s = await resolve(store);
        if (s) await s.delete(key);
      } catch {
        // As above.
      }
    },
  };
}

// ── A session: one editor's typing, debounced ──────────────────────────────

export interface LocalDraftSession<P> {
  /** The form changed: write it after the debounce window (restarts on every call). */
  changed(payload: P): void;
  /** Write a pending change now — the tab is being hidden or the editor unmounted. */
  flush(): Promise<void>;
  /** ⚠️ A save succeeded: cancel any pending write and delete the draft. */
  saved(): Promise<void>;
  /** ⚠️ A save was refused with a conflict (409): KEEP the draft — the typing is what this protects. */
  conflicted(): Promise<void>;
  /** The user threw their edits away (Discard, Cancel): cancel any pending write and delete. */
  discarded(): Promise<void>;
}

export interface LocalDraftSessionOptions<V> {
  store?: LocalStoreOption<V>;
  /** Debounce window. Long enough not to write per keystroke; short enough to lose little. */
  delayMs?: number;
  now?: () => Date;
}

/**
 * @param build turns what the editor reports (`P`) into the stored value, stamped with `savedAt`.
 */
export function createLocalDraftSession<P, V>(
  key: string | null,
  io: LocalDrafts<V>,
  build: (payload: P, savedAt: string) => V,
  options: LocalDraftSessionOptions<V> = {},
): LocalDraftSession<P> {
  const { store, delayMs = 500, now = () => new Date() } = options;
  let timer: ReturnType<typeof setTimeout> | null = null;
  let pending: V | null = null;

  const cancel = () => {
    if (timer) clearTimeout(timer);
    timer = null;
    pending = null;
  };

  const flush = async () => {
    const value = pending;
    cancel();
    if (value) await io.write(key, value, store);
  };

  return {
    changed(payload) {
      if (!key) return;
      pending = build(payload, now().toISOString());
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => { void flush(); }, delayMs);
    },
    flush,
    async saved() {
      // ⚠️ Cancel BEFORE delete: a write still waiting on the timer would otherwise land after the
      // delete and resurrect the draft — the stale-draft-after-save failure.
      cancel();
      await io.remove(key, store);
    },
    conflicted: flush,
    async discarded() {
      cancel();
      await io.remove(key, store);
    },
  };
}
