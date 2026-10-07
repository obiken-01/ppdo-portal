import "fake-indexeddb/auto";
import { openDB } from "idb";
import { describe, expect, it } from "vitest";
import {
  CACHE_DB, DB_VERSION, DRAFT_STORE, EXPENDITURE_DRAFT_STORE, REFERENCE_STORE, getAipCacheDb, openAipCacheDb,
} from "./aip-cache-db";

// fake-indexeddb is an in-memory IndexedDB for Node. Each test uses its own database name.

describe("openAipCacheDb", () => {
  it("creates all three stores on a fresh database", async () => {
    const db = await openAipCacheDb("fresh");
    expect(db.version).toBe(DB_VERSION);
    expect(Array.from(db.objectStoreNames).sort())
      .toEqual([DRAFT_STORE, EXPENDITURE_DRAFT_STORE, REFERENCE_STORE].sort());
    db.close();
  });

  it("is at version 3 (PPDO-192): version 2 could not carry the new store to browsers already on it", () => {
    expect(DB_VERSION).toBe(3);
  });

  it("⚠️ upgrades a PPDO-112 version-2 database without losing either existing store's entries", async () => {
    // Exactly what PPDO-112 left: version 2, two stores, an entry in each.
    const v2 = await openDB("from-v2", 2, {
      upgrade(db) {
        db.createObjectStore(REFERENCE_STORE);
        db.createObjectStore(DRAFT_STORE);
      },
    });
    await v2.put(REFERENCE_STORE, { data: ["A"], fetchedAt: "2026-10-04T00:00:00.000Z" }, "accounts");
    await v2.put(DRAFT_STORE, { fields: { name: "x" }, baseRowVersion: null, savedAt: "s" }, "u1:42");
    v2.close();

    const db = await openAipCacheDb("from-v2");
    expect(db.version).toBe(3);
    expect(db.objectStoreNames.contains(EXPENDITURE_DRAFT_STORE)).toBe(true);
    expect(await db.get(REFERENCE_STORE, "accounts")).toEqual({ data: ["A"], fetchedAt: "2026-10-04T00:00:00.000Z" });
    expect(await db.get(DRAFT_STORE, "u1:42")).toEqual({ fields: { name: "x" }, baseRowVersion: null, savedAt: "s" });
    db.close();
  });

  it("⚠️ upgrades a PPDO-111 version-1 database without losing its reference data", async () => {
    // Exactly what PPDO-111 created: version 1, one store, an entry in it.
    const v1 = await openDB("from-v1", 1, {
      upgrade(db) { db.createObjectStore(REFERENCE_STORE); },
    });
    await v1.put(REFERENCE_STORE, { data: ["A"], fetchedAt: "2026-10-04T00:00:00.000Z" }, "accounts");
    v1.close();

    // ↩️ PPDO-192: straight from 1 to 3 in one open (a browser that skipped PPDO-112's build).
    const db = await openAipCacheDb("from-v1");
    expect(db.version).toBe(3);
    expect(db.objectStoreNames.contains(DRAFT_STORE)).toBe(true);
    expect(db.objectStoreNames.contains(EXPENDITURE_DRAFT_STORE)).toBe(true);
    expect(await db.get(REFERENCE_STORE, "accounts")).toEqual({
      data: ["A"], fetchedAt: "2026-10-04T00:00:00.000Z",
    });
    db.close();
  });

  it("is a no-op upgrade when the database is already at the current version", async () => {
    (await openAipCacheDb("twice")).close();
    const db = await openAipCacheDb("twice");
    expect(db.version).toBe(DB_VERSION);
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
