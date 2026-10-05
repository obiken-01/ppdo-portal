/**
 * PPDO-111 (V18-65) — a read-through IndexedDB cache for AIP Entry's reference lists, with
 * stale-while-revalidate (`docs/v1.8/AIP_Offline_Caching_Spec.md` decisions 1, 2, 5, 8; §4, §5).
 *
 * A repeat visit renders the pickers from the cache with no loading state, then re-fetches in the
 * background and re-renders only if the list changed. Offline, the background fetch fails silently
 * and the cache keeps serving. A first visit behaves exactly as before: a live fetch, then the cache
 * is filled.
 *
 * ⚠️ **A broken cache must never break the page.** Private browsing, disabled storage, a full quota
 * or a corrupt database all fall back to today's live-fetch-only behaviour: every open, read and
 * write is wrapped, and a failure is treated as "nothing cached".
 *
 * ⚠️ **The key must carry every axis the response varies by** (decision 8). Funding sources vary by
 * the office asked for AND by who asks (the server clamps an office-scoped caller to their own
 * office), so their key carries both. A key missing an axis does not look like a bug in testing: it
 * serves office A's private funds to office B on a shared machine. Use `cacheKey`, never a string
 * built by hand.
 *
 * Not cached here, on purpose: the AIP record tree (V18-68), ceilings (V18-66), and anything that is
 * live AIP state wearing a list shape (decision 9). Config divisions are not cached either: AIP Entry
 * does not read them (it reads live division status), and the endpoint is clamped per caller.
 *
 * Nothing is evicted: the largest list is the price index (~6,400 rows).
 */

import { openDB, type IDBPDatabase } from "idb";

export const CACHE_DB = "ppdo-aip-cache";
export const REFERENCE_STORE = "reference-data";
const DB_VERSION = 1;

/** The lists this cache holds. A new one must have its scope axes checked before it is added. */
export type ReferenceKind = "accounts" | "offices" | "funding-sources" | "price-index" | "esre-codes" | "cc-typologies";

export interface CacheEntry<T> {
  data: T;
  /** ISO time the data was fetched from the server. */
  fetchedAt: string;
}

/** Where entries live. IndexedDB in the browser; an in-memory map in tests. */
export interface CacheStore {
  get<T>(key: string): Promise<CacheEntry<T> | undefined>;
  set<T>(key: string, entry: CacheEntry<T>): Promise<void>;
}

// ── Keys ─────────────────────────────────────────────────────────────────────

/**
 * `kind` alone for a global list, or `kind:axis=value:…` for a scoped one. Axes are sorted so the
 * same scope always gives the same key, and a missing value becomes `none`: its own key, never a
 * wildcard that could match a real office.
 */
export function cacheKey(
  kind: ReferenceKind,
  scope?: Record<string, string | number | null | undefined>
): string {
  if (!scope) return kind;
  const parts = Object.keys(scope)
    .sort()
    .map((axis) => {
      const v = scope[axis];
      return `${axis}=${v == null || v === "" ? "none" : String(v)}`;
    });
  return parts.length > 0 ? `${kind}:${parts.join(":")}` : kind;
}

// ── Stale-while-revalidate ──────────────────────────────────────────────────

/** Whether a fresh response should replace what is cached: only when there is none, or it differs. */
export function shouldReplace<T>(cached: CacheEntry<T> | undefined, fresh: T): boolean {
  if (!cached) return true;
  return JSON.stringify(cached.data) !== JSON.stringify(fresh);
}

async function safeGet<T>(store: CacheStore | null, key: string): Promise<CacheEntry<T> | undefined> {
  if (!store) return undefined;
  try {
    return await store.get<T>(key);
  } catch {
    return undefined;
  }
}

async function safeSet<T>(store: CacheStore | null, key: string, data: T, now: () => Date): Promise<void> {
  if (!store) return;
  try {
    await store.set(key, { data, fetchedAt: now().toISOString() });
  } catch {
    // A full quota or a closed database costs the cache, never the page.
  }
}

export interface LoadOptions {
  /** The store to use; defaults to the browser's IndexedDB. Null forces live-only. */
  store?: CacheStore | null;
  now?: () => Date;
}

/**
 * Loads one reference list through the cache. `onData` is called with the cached list at once (if
 * there is one), and again with the fresh list only if it differs. With nothing cached it waits for
 * the live fetch, as before; if that fails, `onError` is called, exactly as the live path did.
 * A failed background refresh while cached data is showing is silent (spec §6).
 *
 * Returns a promise that settles when the live fetch has been handled, for tests and callers that
 * want to know.
 */
export async function loadThroughCache<T>(
  key: string,
  fetcher: () => Promise<T>,
  onData: (data: T) => void,
  onError?: (error: unknown) => void,
  options: LoadOptions = {}
): Promise<void> {
  const store = options.store !== undefined ? options.store : await defaultStore();
  const now = options.now ?? (() => new Date());

  const cached = await safeGet<T>(store, key);
  if (cached) onData(cached.data);

  let fresh: T;
  try {
    fresh = await fetcher();
  } catch (e) {
    if (!cached) onError?.(e);
    return;
  }

  if (shouldReplace(cached, fresh)) {
    onData(fresh);
    await safeSet(store, key, fresh, now);
  }
}

// ── The IndexedDB store ─────────────────────────────────────────────────────

let dbPromise: Promise<IDBPDatabase | null> | null = null;

function openDatabase(): Promise<IDBPDatabase | null> {
  if (typeof window === "undefined" || typeof indexedDB === "undefined") return Promise.resolve(null);
  dbPromise ??= openDB(CACHE_DB, DB_VERSION, {
    upgrade(db) {
      if (!db.objectStoreNames.contains(REFERENCE_STORE)) db.createObjectStore(REFERENCE_STORE);
    },
  }).catch(() => null); // private browsing, storage disabled, a blocked upgrade: no cache
  return dbPromise;
}

const idbStore: CacheStore = {
  async get<T>(key: string) {
    const db = await openDatabase();
    return db ? ((await db.get(REFERENCE_STORE, key)) as CacheEntry<T> | undefined) : undefined;
  },
  async set<T>(key: string, entry: CacheEntry<T>) {
    const db = await openDatabase();
    if (db) await db.put(REFERENCE_STORE, entry, key);
  },
};

async function defaultStore(): Promise<CacheStore | null> {
  return (await openDatabase()) ? idbStore : null;
}
