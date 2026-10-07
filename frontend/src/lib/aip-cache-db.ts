/**
 * The one place AIP Entry's IndexedDB database is opened (`docs/v1.8/AIP_Offline_Caching_Spec.md` §5).
 *
 * PPDO-111 opened `ppdo-aip-cache` at version 1 with a single store. PPDO-112 adds the draft store
 * at version 2. PPDO-192 adds the expenditure-line draft store at version 3 (it could not be folded
 * into 2: a browser already at 2 would never run the upgrade and never get the store). Both modules open it through here, because two modules opening one database at two
 * versions block each other on `versionchange` — the older connection never lets the newer one in.
 *
 * ⚠️ **The upgrade creates only what is missing.** Every user who opened AIP Entry since PPDO-111
 * already has a version-1 database holding `reference-data`; the upgrade must add `activity-drafts`
 * beside it, never recreate the whole thing. A future store follows the same rule: bump the version,
 * add a `contains` guard.
 *
 * ⚠️ **A database that will not open is "no database", never a hung page.** Private browsing,
 * disabled storage and a corrupt file reject, and are caught. A BLOCKED upgrade does not reject — it
 * waits, possibly forever, for another tab still holding the old version (a tab opened before this
 * deploy, whose code never closes its connection). So the open is raced against a timeout, and a
 * blocked upgrade costs this page its cache and drafts, not its pickers. Connections opened here do
 * close themselves when a newer version asks (`blocking`), so the next upgrade is not blocked by us.
 */

import { openDB, type IDBPDatabase } from "idb";

export const CACHE_DB = "ppdo-aip-cache";
export const REFERENCE_STORE = "reference-data";
export const DRAFT_STORE = "activity-drafts";
export const EXPENDITURE_DRAFT_STORE = "expenditure-drafts";
export const DB_VERSION = 3;

/** Long enough for a cold open on a slow office PC; short enough that a blocked one is not noticed. */
const OPEN_TIMEOUT_MS = 3000;

/**
 * Opens (and if needed upgrades) the database. Not memoised — `getAipCacheDb` is what the app
 * uses; this is exported for the upgrade test. Rejects on failure.
 */
export function openAipCacheDb(name: string = CACHE_DB, onClosed?: () => void): Promise<IDBPDatabase> {
  return openDB(name, DB_VERSION, {
    upgrade(db) {
      // v0 → v1 (PPDO-111), v1 → v2 (PPDO-112), v2 → v3 (PPDO-192). Guarded, so each step runs once
      // whatever the start — a v1 browser goes straight to 3.
      if (!db.objectStoreNames.contains(REFERENCE_STORE)) db.createObjectStore(REFERENCE_STORE);
      if (!db.objectStoreNames.contains(DRAFT_STORE)) db.createObjectStore(DRAFT_STORE);
      if (!db.objectStoreNames.contains(EXPENDITURE_DRAFT_STORE)) db.createObjectStore(EXPENDITURE_DRAFT_STORE);
    },
    blocking(_current, _blocked, event) {
      // A newer version is waiting on this connection: let it in, and reopen at that version next time.
      (event.target as IDBDatabase).close();
      onClosed?.();
    },
    terminated() {
      onClosed?.();
    },
  });
}

let dbPromise: Promise<IDBPDatabase | null> | null = null;

/** The shared connection, or null when IndexedDB is unavailable, broken or blocked. Never rejects. */
export function getAipCacheDb(): Promise<IDBPDatabase | null> {
  if (typeof indexedDB === "undefined") return Promise.resolve(null);
  if (!dbPromise) {
    const reset = () => { dbPromise = null; };
    const timeout = new Promise<null>((resolve) => setTimeout(() => resolve(null), OPEN_TIMEOUT_MS));
    dbPromise = Promise.race([openAipCacheDb(CACHE_DB, reset), timeout]).catch(() => null);
  }
  return dbPromise;
}
