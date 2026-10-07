import { describe, expect, it } from "vitest";
import { ceilingStaleCaption } from "./ceiling-staleness";

describe("ceilingStaleCaption (PPDO-113)", () => {
  it("says nothing while the figures are current", () => {
    expect(ceilingStaleCaption(null)).toBeNull();
  });

  it("names the time of the last good read, in Manila time", () => {
    // 01:14 UTC is 9:14 AM in Manila (UTC+8). A browser in another zone must still say 9:14.
    expect(ceilingStaleCaption("2026-10-07T01:14:00.000Z")).toBe(
      "As of 9:14 AM. These figures could not be refreshed and may be out of date."
    );
  });

  it("labels even a figure read seconds ago: a failed refresh is always said (Ralph, 2026-10-07)", () => {
    const justNow = new Date(Date.now() - 5_000).toISOString();
    expect(ceilingStaleCaption(justNow)).toMatch(/^As of /);
  });

  it("falls back to undated wording rather than 'Invalid Date' for a bad timestamp", () => {
    expect(ceilingStaleCaption("not a date")).toBe(
      "These figures could not be refreshed and may be out of date."
    );
  });
});
