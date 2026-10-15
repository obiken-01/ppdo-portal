import { describe, expect, it } from "vitest";
import { badgeLabel, relativeTime, safeLink, shouldPoll, sourceLabel } from "./notifications";

describe("badgeLabel (PPDO-222)", () => {
  it("shows nothing for zero, the count up to 99, and 99+ above", () => {
    expect(badgeLabel(0)).toBeNull();
    expect(badgeLabel(-3)).toBeNull();
    expect(badgeLabel(Number.NaN)).toBeNull();
    expect(badgeLabel(3)).toBe("3");
    expect(badgeLabel(99)).toBe("99");
    expect(badgeLabel(100)).toBe("99+");
    expect(badgeLabel(120)).toBe("99+");
  });
});

describe("shouldPoll", () => {
  it("polls only while the tab is visible", () => {
    expect(shouldPoll("visible")).toBe(true);
    expect(shouldPoll("hidden")).toBe(false);
    expect(shouldPoll("prerender")).toBe(false);
  });
});

describe("relativeTime", () => {
  const now = new Date("2026-10-15T12:00:00Z");
  const ago = (ms: number) => new Date(now.getTime() - ms).toISOString();

  it("reads minutes, hours and days", () => {
    expect(relativeTime(ago(10_000), now)).toBe("just now");
    expect(relativeTime(ago(5 * 60_000), now)).toBe("5 min ago");
    expect(relativeTime(ago(3 * 3_600_000), now)).toBe("3 h ago");
    expect(relativeTime(ago(2 * 86_400_000), now)).toBe("2 d ago");
  });

  it("falls back to a date after a week, and tolerates a future or bad timestamp", () => {
    expect(relativeTime(ago(30 * 86_400_000), now)).toMatch(/2026/);
    expect(relativeTime(ago(-60_000), now)).toBe("just now");
    expect(relativeTime("not a date", now)).toBe("just now");
  });
});

describe("sourceLabel", () => {
  it("names proposals, and AIP with its year from the link", () => {
    expect(sourceLabel({ type: "ProposalReply", link: "/budget-planning/proposals/edit?id=9&comments=open" })).toBe("Investment proposal");
    expect(sourceLabel({ type: "AipSentToPpdo", link: "/budget-planning/aip/review?officeId=3&fiscalYear=2031" })).toBe("AIP FY 2031");
    expect(sourceLabel({ type: "AipReviewerEdit", link: "/budget-planning/aip/entry?fiscalYear=2031&activityId=4" })).toBe("AIP FY 2031");
    expect(sourceLabel({ type: "AipReturnedByPpdo", link: "/somewhere" })).toBe("AIP");
    expect(sourceLabel({ type: "Other", link: "/x" })).toBe("Notification");
  });
});

describe("safeLink", () => {
  it("follows portal paths only", () => {
    expect(safeLink("/budget-planning/aip/entry?fiscalYear=2031")).toBe("/budget-planning/aip/entry?fiscalYear=2031");
    expect(safeLink("https://evil.example/x")).toBe("/notifications");
    expect(safeLink("//evil.example/x")).toBe("/notifications");
    expect(safeLink("javascript:alert(1)")).toBe("/notifications");
  });
});
