import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  createDraftSession, decideDraft, deleteDraft, draftKey, readDraft, sameDraftFields, writeDraft,
  type ActivityDraft, type ActivityDraftFields, type DraftStore,
} from "./activity-drafts";

/** An in-memory store standing in for IndexedDB. */
function memoryStore(seed: Record<string, ActivityDraft> = {}) {
  const map = new Map<string, ActivityDraft>(Object.entries(seed));
  const store: DraftStore = {
    get: async (key) => map.get(key),
    put: async (key, draft) => { map.set(key, draft); },
    delete: async (key) => { map.delete(key); },
  };
  return { store, map };
}

/** A store whose every call throws, as a closed or corrupt database would. */
const brokenStore: DraftStore = {
  get: async () => { throw new Error("broken"); },
  put: async () => { throw new Error("broken"); },
  delete: async () => { throw new Error("broken"); },
};

const SERVER: ActivityDraftFields = {
  name: "Conduct of LDC meetings",
  esreCode: "1000-1",
  implementingOffices: ["OPV"],
  startDate: "January",
  endDate: "December",
  expectedOutputs: "4 meetings",
  ccAdaptation: null,
  ccMitigation: 50000,
  ccTypologyCode: "",
};

const draft = (fields: Partial<ActivityDraftFields>, baseRowVersion: string | null = "AAAA="): ActivityDraft => ({
  fields: { ...SERVER, ...fields },
  baseRowVersion,
  savedAt: "2026-10-06T01:00:00.000Z",
});

const NOW = () => new Date("2026-10-06T02:00:00Z");

describe("draftKey", () => {
  it("carries the user as well as the activity, so two users of one browser never share a draft", () => {
    expect(draftKey("u1", 42)).toBe("u1:42");
    expect(draftKey("u1", 42)).not.toBe(draftKey("u2", 42));
  });

  it("is null with no user: no draft is ever written under a key missing its user axis", () => {
    expect(draftKey(null, 42)).toBeNull();
    expect(draftKey(undefined, 42)).toBeNull();
    expect(draftKey("", 42)).toBeNull();
  });
});

describe("sameDraftFields", () => {
  it("is true for the same values", () => {
    expect(sameDraftFields(SERVER, { ...SERVER, implementingOffices: ["OPV"] })).toBe(true);
  });

  it("notices a change in any field, including the office list and the money fields", () => {
    expect(sameDraftFields(SERVER, { ...SERVER, name: "x" })).toBe(false);
    expect(sameDraftFields(SERVER, { ...SERVER, implementingOffices: ["OPV", "LFC"] })).toBe(false);
    expect(sameDraftFields(SERVER, { ...SERVER, ccAdaptation: 0 })).toBe(false);
    expect(sameDraftFields(SERVER, { ...SERVER, ccTypologyCode: "A1" })).toBe(false);
  });
});

describe("decideDraft", () => {
  it("is none when there is no draft", () => {
    expect(decideDraft(undefined, SERVER, "AAAA=")).toBe("none");
  });

  it("is none when the draft says exactly what the server says (the caller deletes it)", () => {
    expect(decideDraft(draft({}), SERVER, "AAAA=")).toBe("none");
    // …even when the row moved on: there is nothing in the draft worth offering.
    expect(decideDraft(draft({}), SERVER, "BBBB=")).toBe("none");
  });

  it("offers a restore when the draft differs and nobody saved since it began", () => {
    expect(decideDraft(draft({ name: "typed" }), SERVER, "AAAA=")).toBe("restore");
  });

  it("offers a restore, flagged as changed, when the row was saved since the draft began", () => {
    expect(decideDraft(draft({ name: "typed" }), SERVER, "BBBB=")).toBe("restore-changed");
  });

  it("treats a missing base version against a real one as changed, never as the same", () => {
    expect(decideDraft(draft({ name: "typed" }, null), SERVER, "AAAA=")).toBe("restore-changed");
    expect(decideDraft(draft({ name: "typed" }, null), SERVER, null)).toBe("restore");
  });

  it("does not depend on any clock: an old savedAt is still offered if the row has not moved", () => {
    const old = { ...draft({ name: "typed" }), savedAt: "2001-01-01T00:00:00.000Z" };
    expect(decideDraft(old, SERVER, "AAAA=")).toBe("restore");
  });
});

describe("read / write / delete", () => {
  it("round-trips a draft", async () => {
    const { store } = memoryStore();
    await writeDraft("u1:42", draft({ name: "typed" }), store);
    expect((await readDraft("u1:42", store))?.fields.name).toBe("typed");
    await deleteDraft("u1:42", store);
    expect(await readDraft("u1:42", store)).toBeUndefined();
  });

  it("does nothing with a null key", async () => {
    const { store, map } = memoryStore();
    await writeDraft(null, draft({ name: "typed" }), store);
    expect(map.size).toBe(0);
    expect(await readDraft(null, store)).toBeUndefined();
  });

  it("IndexedDB unavailable (null store): every call resolves and does nothing", async () => {
    await expect(writeDraft("u1:42", draft({}), null)).resolves.toBeUndefined();
    await expect(readDraft("u1:42", null)).resolves.toBeUndefined();
    await expect(deleteDraft("u1:42", null)).resolves.toBeUndefined();
  });

  it("a broken store never throws: a failure is treated as no draft", async () => {
    await expect(writeDraft("u1:42", draft({}), brokenStore)).resolves.toBeUndefined();
    await expect(readDraft("u1:42", brokenStore)).resolves.toBeUndefined();
    await expect(deleteDraft("u1:42", brokenStore)).resolves.toBeUndefined();
  });

  it("drops a stored value that is not a draft shape (a corrupt or foreign entry)", async () => {
    const { store, map } = memoryStore();
    map.set("u1:42", { nonsense: true } as unknown as ActivityDraft);
    expect(await readDraft("u1:42", store)).toBeUndefined();
  });
});

describe("createDraftSession", () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  const settle = async () => { await vi.runAllTimersAsync(); };

  it("debounces: many edits inside the window write once, with the last value", async () => {
    const { store, map } = memoryStore();
    const put = vi.spyOn(store, "put");
    const s = createDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "a" }, "AAAA=");
    await vi.advanceTimersByTimeAsync(200);
    s.changed({ ...SERVER, name: "ab" }, "AAAA=");
    await vi.advanceTimersByTimeAsync(200);
    s.changed({ ...SERVER, name: "abc" }, "AAAA=");
    expect(put).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(500);
    expect(put).toHaveBeenCalledTimes(1);
    expect(map.get("u1:42")).toEqual({
      fields: { ...SERVER, name: "abc" }, baseRowVersion: "AAAA=", savedAt: "2026-10-06T02:00:00.000Z",
    });
  });

  it("flush writes the pending edit at once (tab hidden, panel unmounted)", async () => {
    const { store, map } = memoryStore();
    const s = createDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "typed" }, "AAAA=");
    await s.flush();
    expect(map.get("u1:42")?.fields.name).toBe("typed");
  });

  it("⚠️ saved: deletes the draft AND cancels a pending write, so it cannot come back", async () => {
    const { store, map } = memoryStore({ "u1:42": draft({ name: "older" }) });
    const s = createDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "typed" }, "AAAA=");
    await s.saved();
    await settle();
    expect(map.has("u1:42")).toBe(false);
  });

  it("⚠️ conflict: KEEPS the draft, with the latest text written through", async () => {
    const { store, map } = memoryStore();
    const s = createDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "typed" }, "AAAA=");
    await s.conflicted();
    await settle();
    expect(map.get("u1:42")?.fields.name).toBe("typed");
  });

  it("discarded: deletes the draft and cancels a pending write", async () => {
    const { store, map } = memoryStore({ "u1:42": draft({ name: "older" }) });
    const s = createDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "typed" }, "AAAA=");
    await s.discarded();
    await settle();
    expect(map.has("u1:42")).toBe(false);
  });

  it("no user (null key): never writes anything", async () => {
    const { store, map } = memoryStore();
    const s = createDraftSession(null, { store, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "typed" }, "AAAA=");
    await s.flush();
    await settle();
    expect(map.size).toBe(0);
  });

  it("IndexedDB unavailable: every session call resolves, never throws", async () => {
    const s = createDraftSession("u1:42", { store: null, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "typed" }, "AAAA=");
    await expect(s.flush()).resolves.toBeUndefined();
    await expect(s.conflicted()).resolves.toBeUndefined();
    await expect(s.saved()).resolves.toBeUndefined();
    await expect(s.discarded()).resolves.toBeUndefined();
    await settle();
  });

  it("a broken store: the debounced write fails silently", async () => {
    const s = createDraftSession("u1:42", { store: brokenStore, delayMs: 500, now: NOW });
    s.changed({ ...SERVER, name: "typed" }, "AAAA=");
    await settle();
    await expect(s.flush()).resolves.toBeUndefined();
  });
});
