import { describe, expect, it, vi } from "vitest";

// lib/aip imports the shared Axios instance; aipConflict never calls it.
vi.mock("./api", () => ({ default: {} }));

import { aipConflict } from "./aip";

/** A rejected Axios call, as the catch block receives it. */
function rejected(status: number, data?: unknown) {
  return { isAxiosError: true, response: { status, data } };
}

const CONFLICT = {
  changedByName: "Maria Cruz",
  changedAtUtc: "2026-11-03T01:00:00Z",
  currentRowVersion: "AAAAAAAAB9E=",
  current: { id: 7, name: "Road study" },
};

describe("aipConflict (PPDO-294)", () => {
  it("reads the conflict out of a 409 that carries one", () => {
    const err = rejected(409, { data: CONFLICT, error: "Maria Cruz changed this activity while you were editing it." });
    expect(aipConflict(err)).toEqual(CONFLICT);
  });

  it("is null for a 409 whose body is only a message", () => {
    // ⚠️ The API sends these: a reload that failed after a version clash (AipService
    // ActivityConflictAsync), a delete that lost a renumber race, "another project was added at
    // the same moment". None has a row to compare or a version to overwrite with, so the caller
    // shows the message instead (aipErrorMessage) rather than an empty conflict panel.
    expect(aipConflict(rejected(409, { data: null, error: "This list changed while you were saving — reload and try again." }))).toBeNull();
  });

  it("is null for a 409 with no body at all", () => {
    expect(aipConflict(rejected(409))).toBeNull();
    expect(aipConflict(rejected(409, ""))).toBeNull();
  });

  it("is null for a 409 whose data is not a conflict", () => {
    expect(aipConflict(rejected(409, { data: { renumbered: [] }, error: "x" }))).toBeNull();
    // Half a conflict is not one either: no row to compare, or no version to Overwrite with.
    expect(aipConflict(rejected(409, { data: { ...CONFLICT, current: null }, error: "x" }))).toBeNull();
    expect(aipConflict(rejected(409, { data: { ...CONFLICT, currentRowVersion: null }, error: "x" }))).toBeNull();
  });

  it("is null for any other status, even with a conflict-shaped body", () => {
    expect(aipConflict(rejected(400, { data: CONFLICT, error: "Bad request." }))).toBeNull();
    expect(aipConflict(rejected(500))).toBeNull();
  });

  it("is null for a failure with no response (network down)", () => {
    expect(aipConflict(new Error("Network Error"))).toBeNull();
    expect(aipConflict(undefined)).toBeNull();
  });
});
