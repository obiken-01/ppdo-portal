import { describe, expect, it, vi } from "vitest";
import { cacheKey, loadThroughCache, shouldReplace, type CacheEntry, type CacheStore } from "./reference-cache";

/** An in-memory store standing in for IndexedDB. */
function memoryStore(seed: Record<string, CacheEntry<unknown>> = {}) {
  const map = new Map<string, CacheEntry<unknown>>(Object.entries(seed));
  const store: CacheStore = {
    get: async <T>(key: string) => map.get(key) as CacheEntry<T> | undefined,
    set: async <T>(key: string, entry: CacheEntry<T>) => {
      map.set(key, entry as CacheEntry<unknown>);
    },
  };
  return { store, map };
}

const NOW = () => new Date("2026-10-05T01:00:00Z");
const entry = <T>(data: T): CacheEntry<T> => ({ data, fetchedAt: "2026-10-04T00:00:00.000Z" });

describe("cacheKey", () => {
  it("is the kind alone for a global list", () => {
    expect(cacheKey("accounts")).toBe("accounts");
  });

  it("carries every scope axis, so two offices never share a key", () => {
    const a = cacheKey("funding-sources", { office: 15, user: "u1" });
    const b = cacheKey("funding-sources", { office: 3, user: "u1" });
    expect(a).toBe("funding-sources:office=15:user=u1");
    expect(a).not.toBe(b);
  });

  it("gives two users of the same office different keys", () => {
    expect(cacheKey("funding-sources", { office: 15, user: "u1" })).not.toBe(
      cacheKey("funding-sources", { office: 15, user: "u2" })
    );
  });

  it("makes a missing office its own key, never a wildcard or the bare kind", () => {
    const none = cacheKey("funding-sources", { office: null, user: "u1" });
    expect(none).toBe("funding-sources:office=none:user=u1");
    expect(none).not.toBe(cacheKey("funding-sources", { office: 15, user: "u1" }));
    expect(none).not.toBe("funding-sources");
    expect(cacheKey("funding-sources", { office: undefined, user: "u1" })).toBe(none);
  });

  it("does not depend on the order the axes are written in", () => {
    expect(cacheKey("funding-sources", { user: "u1", office: 15 })).toBe(
      cacheKey("funding-sources", { office: 15, user: "u1" })
    );
  });
});

describe("shouldReplace", () => {
  it("replaces when nothing is cached", () => {
    expect(shouldReplace(undefined, [1])).toBe(true);
  });

  it("keeps the cache when the fresh list is the same", () => {
    expect(shouldReplace(entry([{ id: 1, name: "Rice" }]), [{ id: 1, name: "Rice" }])).toBe(false);
  });

  it("replaces when anything in the list changed", () => {
    expect(shouldReplace(entry([{ id: 1, name: "Rice" }]), [{ id: 1, name: "Corn" }])).toBe(true);
  });
});

describe("loadThroughCache", () => {
  it("cold cache: serves the live fetch once and fills the cache", async () => {
    const { store, map } = memoryStore();
    const onData = vi.fn();
    await loadThroughCache("accounts", async () => ["A"], onData, undefined, { store, now: NOW });
    expect(onData.mock.calls).toEqual([[["A"]]]);
    expect(map.get("accounts")).toEqual({ data: ["A"], fetchedAt: "2026-10-05T01:00:00.000Z" });
  });

  it("warm cache, unchanged: serves the cache at once and does not re-render or rewrite", async () => {
    const seeded = entry(["A"]);
    const { store, map } = memoryStore({ accounts: seeded });
    const onData = vi.fn();
    await loadThroughCache("accounts", async () => ["A"], onData, undefined, { store, now: NOW });
    expect(onData.mock.calls).toEqual([[["A"]]]);
    expect(map.get("accounts")).toBe(seeded);
  });

  it("warm cache, changed: serves the cache, then the fresh list, and stores it", async () => {
    const { store, map } = memoryStore({ accounts: entry(["A"]) });
    const onData = vi.fn();
    await loadThroughCache("accounts", async () => ["A renamed"], onData, undefined, { store, now: NOW });
    expect(onData.mock.calls).toEqual([[["A"]], [["A renamed"]]]);
    expect(map.get("accounts")?.data).toEqual(["A renamed"]);
  });

  it("warm cache, offline: the failed refresh is silent and leaves the cache alone", async () => {
    const seeded = entry(["A"]);
    const { store, map } = memoryStore({ accounts: seeded });
    const onData = vi.fn();
    const onError = vi.fn();
    await loadThroughCache("accounts", () => Promise.reject(new Error("offline")), onData, onError, { store });
    expect(onData.mock.calls).toEqual([[["A"]]]);
    expect(onError).not.toHaveBeenCalled();
    expect(map.get("accounts")).toBe(seeded);
  });

  it("cold cache, offline: reports the error exactly as the live path did", async () => {
    const { store } = memoryStore();
    const onData = vi.fn();
    const onError = vi.fn();
    await loadThroughCache("accounts", () => Promise.reject(new Error("offline")), onData, onError, { store });
    expect(onData).not.toHaveBeenCalled();
    expect(onError).toHaveBeenCalledOnce();
  });

  it("no IndexedDB (store null): behaves exactly like a live fetch", async () => {
    const onData = vi.fn();
    await loadThroughCache("accounts", async () => ["A"], onData, undefined, { store: null });
    expect(onData.mock.calls).toEqual([[["A"]]]);
  });

  it("a store that throws on every call still serves the live data", async () => {
    const broken: CacheStore = {
      get: () => Promise.reject(new Error("corrupt")),
      set: () => Promise.reject(new Error("quota")),
    };
    const onData = vi.fn();
    await loadThroughCache("accounts", async () => ["A"], onData, undefined, { store: broken });
    expect(onData.mock.calls).toEqual([[["A"]]]);
  });

  it("never serves one key's data under another", async () => {
    const { store } = memoryStore({
      [cacheKey("funding-sources", { office: 15, user: "u1" })]: entry(["OPA own fund"]),
    });
    const onData = vi.fn();
    await loadThroughCache(
      cacheKey("funding-sources", { office: 3, user: "u2" }),
      async () => ["PTO own fund"],
      onData,
      undefined,
      { store }
    );
    expect(onData.mock.calls).toEqual([[["PTO own fund"]]]);
  });
});
