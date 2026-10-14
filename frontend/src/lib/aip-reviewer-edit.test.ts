import { describe, expect, it } from "vitest";
import type { AipReviewNotifications, MeResponse } from "@/types";
import {
  REVIEWER_REASON_MAX, canPpdoEditAmounts, reviewerEditNotice, reviewerReasonProblem,
} from "./aip-reviewer-edit";

function me(overrides: Partial<MeResponse> = {}): MeResponse {
  return { role: "Staff", canReviewAllOffices: true, ...overrides } as MeResponse;
}

describe("canPpdoEditAmounts (PPDO-214)", () => {
  it.each(["SubmittedToPpdo", "Consolidated"])("offers Edit amounts to a PPDO reviewer in %s", (status) => {
    expect(canPpdoEditAmounts(me(), status)).toBe(true);
  });

  it.each(["Draft", "DepartmentReview", "ReturnedByPpdo"])(
    "withholds it while the office still holds its AIP (%s)",
    (status) => {
      expect(canPpdoEditAmounts(me(), status)).toBe(false);
    }
  );

  it("withholds it from a reader without the cross-office grant", () => {
    expect(canPpdoEditAmounts(me({ canReviewAllOffices: false }), "SubmittedToPpdo")).toBe(false);
  });

  // The server never routes SuperAdmin through the reviewer path, so the button would only 400.
  it("withholds it from SuperAdmin", () => {
    expect(canPpdoEditAmounts(me({ role: "SuperAdmin" }), "SubmittedToPpdo")).toBe(false);
  });

  it("withholds it before the reader is known", () => {
    expect(canPpdoEditAmounts(null, "SubmittedToPpdo")).toBe(false);
  });
});

describe("reviewerReasonProblem", () => {
  it.each(["", "   ", "\n"])("asks for a reason when it is blank (%j)", (reason) => {
    expect(reviewerReasonProblem(reason)).toBe("Give a reason. The office will see it with your change.");
  });

  it("accepts a reason of up to 500 characters, counted after trimming", () => {
    expect(reviewerReasonProblem("  " + "x".repeat(REVIEWER_REASON_MAX) + "  ")).toBeNull();
  });

  it("refuses 501 characters, the same limit as the server", () => {
    expect(reviewerReasonProblem("x".repeat(REVIEWER_REASON_MAX + 1)))
      .toBe("Keep the reason to 500 characters.");
  });
});

describe("reviewerEditNotice", () => {
  const base = { reviewerEditCount: 3, reviewerEditFiscalYear: 2030 } as AipReviewNotifications;

  it("shows on the year the edits are in", () => {
    expect(reviewerEditNotice(base, 2030)).toEqual({ count: 3, fiscalYear: 2030 });
  });

  it("stays off another year's page", () => {
    expect(reviewerEditNotice(base, 2031)).toBeNull();
  });

  it("is hidden at zero", () => {
    expect(reviewerEditNotice({ ...base, reviewerEditCount: 0, reviewerEditFiscalYear: null }, 2030)).toBeNull();
  });

  it("is hidden while notifications are not loaded", () => {
    expect(reviewerEditNotice(null, 2030)).toBeNull();
  });

  // Without a page year (the sidebar), it shows wherever the edits are.
  it("ignores the year when none is given", () => {
    expect(reviewerEditNotice(base)).toEqual({ count: 3, fiscalYear: 2030 });
  });
});
