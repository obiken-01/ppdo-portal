import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  EMPTY_EXPENDITURE_FIELDS, createExpenditureDraftSession, decideExpenditureDraft, expenditureDraftKey,
  lineToDraftFields, readExpenditureDraft, sameExpenditureContent,
  type ExpenditureDraft, type ExpenditureDraftFields, type ExpenditureDraftStore,
} from "./expenditure-drafts";
import type { AipExpenditure, AipProcurementItem } from "@/types";

/** An in-memory store standing in for IndexedDB. */
function memoryStore(seed: Record<string, ExpenditureDraft> = {}) {
  const map = new Map<string, ExpenditureDraft>(Object.entries(seed));
  const store: ExpenditureDraftStore = {
    get: async (key) => map.get(key),
    put: async (key, draft) => { map.set(key, draft); },
    delete: async (key) => { map.delete(key); },
  };
  return { store, map };
}

const item = (over: Partial<AipProcurementItem> = {}): AipProcurementItem => ({
  id: 1, priceIndexItemId: 77, name: "Bond paper, A4", unit: "ream", unitPrice: 250,
  qty: 4, numberOfDays: 1, periodNo: 2, lineTotal: 1000, ...over,
} as AipProcurementItem);

const line = (over: Partial<AipExpenditure> = {}): AipExpenditure => ({
  id: 11, activityId: 42, accountId: 93, accountTitle: "Office Supplies Expenses",
  fundingSourceId: 1, fundingSourceCode: "GF", fundingSourceName: "General Fund",
  ps: 0, mooe: 5000, co: 0, total: 5000, procurementItems: [], rowVersion: "LINE-V1",
  ...over,
} as AipExpenditure);

const fields = (over: Partial<ExpenditureDraftFields> = {}): ExpenditureDraftFields => ({
  ...lineToDraftFields(line()), ...over,
});

const draftOf = (
  target: ExpenditureDraft["target"], f: ExpenditureDraftFields, baseRowVersion: string | null = null,
): ExpenditureDraft => ({ target, fields: f, baseRowVersion, savedAt: "2026-10-07T01:00:00.000Z" });

const NEW = { kind: "new" } as const;
const LINE_11 = { kind: "line", lineId: 11 } as const;

describe("expenditureDraftKey", () => {
  it("carries the user as well as the activity, so two users of one browser never share a draft", () => {
    expect(expenditureDraftKey("u1", 42)).toBe("u1:42");
    expect(expenditureDraftKey("u2", 42)).not.toBe(expenditureDraftKey("u1", 42));
  });

  it("is null with no user: nothing is read or written", () => {
    expect(expenditureDraftKey(null, 42)).toBeNull();
    expect(expenditureDraftKey(undefined, 42)).toBeNull();
    expect(expenditureDraftKey("", 42)).toBeNull();
  });
});

describe("lineToDraftFields", () => {
  it("is the editor's form shape: ids as strings, items in the SAVE shape (no id, no lineTotal)", () => {
    const f = lineToDraftFields(line({ procurementItems: [item()] }));
    expect(f.accountId).toBe("93");
    expect(f.fundingSourceId).toBe("1");
    expect(f.procurementItems).toEqual([{
      priceIndexItemId: 77, name: "Bond paper, A4", unit: "ream", unitPrice: 250,
      qty: 4, numberOfDays: 1, periodNo: 2,
    }]);
  });
});

describe("sameExpenditureContent", () => {
  it("ignores the fund when the draft has none: single-fund mode takes the activity's fund at save", () => {
    expect(sameExpenditureContent(fields({ fundingSourceId: "" }), lineToDraftFields(line()))).toBe(true);
  });

  it("treats a blank amount as the 0 the save would send", () => {
    expect(sameExpenditureContent(fields({ ps: null, co: null }), lineToDraftFields(line()))).toBe(true);
  });

  it("compares items, not amounts, on an itemised line (the server derives the amounts)", () => {
    const saved = lineToDraftFields(line({ procurementItems: [item()], mooe: 1000, total: 1000 }));
    const typed = { ...saved, ps: null, mooe: null, co: null };
    expect(sameExpenditureContent(typed, saved)).toBe(true);
    expect(sameExpenditureContent({ ...typed, procurementItems: [{ ...saved.procurementItems[0], qty: 5 }] }, saved))
      .toBe(false);
  });

  it("notices a different account or amount", () => {
    expect(sameExpenditureContent(fields({ accountId: "94" }), lineToDraftFields(line()))).toBe(false);
    expect(sameExpenditureContent(fields({ mooe: 5001 }), lineToDraftFields(line()))).toBe(false);
  });
});

describe("decideExpenditureDraft", () => {
  it("is none when there is no draft", () => {
    expect(decideExpenditureDraft(undefined, [line()])).toBe("none");
  });

  it("is none for an empty new-line draft (+ Add Account opened, nothing typed)", () => {
    expect(decideExpenditureDraft(draftOf(NEW, EMPTY_EXPENDITURE_FIELDS), [])).toBe("none");
  });

  it("⚠️ 4a: is none for a new-line draft that matches a saved line — it was saved, offering it would duplicate", () => {
    // Saved, then the tab died before the delete ran. Single-fund mode: the draft carries no fund.
    expect(decideExpenditureDraft(draftOf(NEW, fields({ fundingSourceId: "" })), [line()])).toBe("none");
  });

  it("offers a new-line draft that matches no saved line", () => {
    expect(decideExpenditureDraft(draftOf(NEW, fields({ mooe: 7500 })), [line()])).toBe("restore");
  });

  it("is none for an edited-line draft equal to the saved line (the caller deletes it)", () => {
    expect(decideExpenditureDraft(draftOf(LINE_11, lineToDraftFields(line()), "LINE-V1"), [line()])).toBe("none");
  });

  it("offers an edited-line draft when the line has not been saved since", () => {
    expect(decideExpenditureDraft(draftOf(LINE_11, fields({ mooe: 6000 }), "LINE-V1"), [line()])).toBe("restore");
  });

  it("offers it flagged as changed when the line was saved since the draft began", () => {
    expect(decideExpenditureDraft(draftOf(LINE_11, fields({ mooe: 6000 }), "LINE-V0"), [line()]))
      .toBe("restore-changed");
  });

  it("offers a draft for a line deleted since as a NEW line (decision 4)", () => {
    expect(decideExpenditureDraft(draftOf(LINE_11, fields({ mooe: 6000 }), "LINE-V1"), [])).toBe("restore-as-new");
  });

  it("does not depend on any clock: an old savedAt is still offered if the line has not moved", () => {
    const old = { ...draftOf(LINE_11, fields({ mooe: 6000 }), "LINE-V1"), savedAt: "2020-01-01T00:00:00.000Z" };
    expect(decideExpenditureDraft(old, [line()])).toBe("restore");
  });
});

describe("read", () => {
  it("drops a stored value that is not an expenditure draft (an activity draft, a corrupt entry)", async () => {
    const { store, map } = memoryStore();
    map.set("u1:42", { fields: { name: "x" }, baseRowVersion: null, savedAt: "x" } as unknown as ExpenditureDraft);
    expect(await readExpenditureDraft("u1:42", store)).toBeUndefined();
  });

  it("IndexedDB unavailable (null store) or no key: resolves to nothing", async () => {
    expect(await readExpenditureDraft("u1:42", null)).toBeUndefined();
    expect(await readExpenditureDraft(null)).toBeUndefined();
  });
});

describe("createExpenditureDraftSession", () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  const NOW = () => new Date("2026-10-07T02:00:00Z");

  it("writes the target, the fields and the base version after the debounce", async () => {
    const { store, map } = memoryStore();
    const s = createExpenditureDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed(LINE_11, fields({ mooe: 6000 }), "LINE-V1");
    await vi.advanceTimersByTimeAsync(500);
    expect(map.get("u1:42")).toEqual({
      target: LINE_11, fields: fields({ mooe: 6000 }), baseRowVersion: "LINE-V1",
      savedAt: "2026-10-07T02:00:00.000Z",
    });
  });

  it("⚠️ saved: deletes the draft AND cancels a pending write, so it cannot come back", async () => {
    const { store, map } = memoryStore({ "u1:42": draftOf(NEW, fields()) });
    const s = createExpenditureDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed(NEW, fields({ mooe: 1 }), null);
    await s.saved();
    await vi.runAllTimersAsync();
    expect(map.has("u1:42")).toBe(false);
  });

  it("⚠️ conflict: KEEPS the draft, with the latest typing written through", async () => {
    const { store, map } = memoryStore();
    const s = createExpenditureDraftSession("u1:42", { store, delayMs: 500, now: NOW });
    s.changed(LINE_11, fields({ mooe: 6000 }), "LINE-V1");
    await s.conflicted();
    expect(map.get("u1:42")?.fields.mooe).toBe(6000);
  });

  it("no user (null key): never writes anything", async () => {
    const { store, map } = memoryStore();
    const s = createExpenditureDraftSession(null, { store, delayMs: 500, now: NOW });
    s.changed(NEW, fields(), null);
    await vi.runAllTimersAsync();
    expect(map.size).toBe(0);
  });
});
