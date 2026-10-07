import "fake-indexeddb/auto";
import { openDB } from "idb";
import { describe, expect, it } from "vitest";
import { CACHE_DB, DB_VERSION, DRAFT_STORE, REFERENCE_STORE, getAipCacheDb, openAipCacheDb } from "./aip-cache-db";

// fake-indexeddb is an in-memory IndexedDB for Node. Each test uses its own database name.

describe("openAipCacheDb", () => {
  it("creates both stores on a fresh database", async () => {
    const db = await openAipCacheDb("fresh");
    expect(db.version).toBe(DB_VERSION);
    expect(Array.from(db.objectStoreNames).sort()).toEqual([DRAFT_STORE, REFERENCE_STORE].sort());
    db.close();
  });

  it("⚠️ upgrades a PPDO-111 version-1 database without losing its reference data", async () => {
    // Exactly what PPDO-111 created: version 1, one store, an entry in it.
    const v1 = await openDB("from-v1", 1, {
      upgrade(db) { db.createObjectStore(REFERENCE_STORE); },
    });
    await v1.put(REFERENCE_STORE, { data: ["A"], fetchedAt: "2026-10-04T00:00:00.000Z" }, "accounts");
    v1.close();

    const db = await openAipCacheDb("from-v1");
    expect(db.version).toBe(2);
    expect(db.objectStoreNames.contains(DRAFT_STORE)).toBe(true);
    expect(await db.get(REFERENCE_STORE, "accounts")).toEqual({
      data: ["A"], fetchedAt: "2026-10-04T00:00:00.000Z",
    });
    db.close();
  });

  it("is a no-op upgrade when the database is already at version 2", async () => {
    (await openAipCacheDb("twice")).close();
    const db = await openAipCacheDb("twice");
    expect(db.version).toBe(2);
    db.close();
  });

  it("closes itself when a newer version asks, so the next upgrade is not blocked by it", async () => {
    let closed = false;
    await openAipCacheDb("blocking", () => { closed = true; });
    const newer = await openDB("blocking", DB_VERSION + 1);
    expect(closed).toBe(true);
    newer.close();
  });

  it("⚠️ a tab still on the old version blocks the upgrade: no database, never a hung page", async () => {
    // A tab opened before this deploy: version 1, and PPDO-111's code never closes its connection.
    const oldTab = await openDB(CACHE_DB, 1, {
      upgrade(db) { db.createObjectStore(REFERENCE_STORE); },
    });
    const started = Date.now();
    expect(await getAipCacheDb()).toBeNull();
    expect(Date.now() - started).toBeLessThan(5000);
    oldTab.close();
  }, 10000);
});
