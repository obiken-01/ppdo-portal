import { describe, expect, it } from "vitest";
import type { AipUnresolvedCounts, ProposalProjectOption } from "@/types";
import { buildAipSubmitConfirmation } from "./aip-submit-confirm";

const NONE: AipUnresolvedCounts = { fromPpdo: 0, fromDepartmentHead: 0, total: 0 };
const ONE_PPDO: AipUnresolvedCounts = { fromPpdo: 1, fromDepartmentHead: 0, total: 1 };
const GAP = { projectName: "Road study", status: "None" } as ProposalProjectOption;
const CEILING = "Over the ceiling by ₱100,000.";

describe("buildAipSubmitConfirmation (PPDO-206)", () => {
  it("lists all three warnings in ONE dialog for a division submit", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "division", divisionName: "Planning" },
      ceilingWarning: CEILING, unresolved: ONE_PPDO, proposalGaps: [GAP],
    });

    expect(c.warnings).toHaveLength(3);
    expect(c.warnings[0]).toContain("Road study");
    expect(c.warnings[1]).toContain(CEILING);
    expect(c.warnings[2]).toContain("1 unresolved comment from PPDO");
    expect(c.confirmLabel).toBe("Submit anyway");
    expect(c.variant).toBe("warning");
  });

  it("is a plain confirm when nothing applies", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "division", divisionName: "Planning" },
      ceilingWarning: null, unresolved: NONE, proposalGaps: [],
    });

    expect(c.warnings).toEqual([]);
    expect(c.confirmLabel).toBe("Submit Planning");
    expect(c.variant).toBe("primary");
  });

  it("confirms an office-level encoder submit even with no warnings", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "encoder" }, ceilingWarning: null, unresolved: NONE, proposalGaps: null,
    });

    expect(c.title).toBe("Submit for department review?");
    expect(c.confirmLabel).toBe("Submit");
  });

  it("warns about an open PPDO comment on the FIRST send to PPDO", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "toPpdo", resubmit: false }, ceilingWarning: null, unresolved: ONE_PPDO, proposalGaps: null,
    });

    expect(c.title).toBe("Send to PPDO?");
    expect(c.warnings).toHaveLength(1);
    expect(c.warnings[0]).toContain("1 unresolved comment from PPDO");
    expect(c.confirmLabel).toBe("Send anyway");
  });

  it("always asks before sending to PPDO, even with no warnings", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "toPpdo", resubmit: true }, ceilingWarning: null, unresolved: NONE, proposalGaps: null,
    });

    expect(c.title).toBe("Re-submit to PPDO?");
    expect(c.warnings).toEqual([]);
    expect(c.confirmLabel).toBe("Re-submit to PPDO");
  });

  it("never lists the ceiling on the PPDO hop, and names the head in the third person there", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "toPpdo", resubmit: false },
      ceilingWarning: CEILING,
      unresolved: { fromPpdo: 0, fromDepartmentHead: 2, total: 2 },
      proposalGaps: null,
    });

    expect(c.warnings).toHaveLength(1);
    expect(c.warnings[0]).toContain("2 unresolved comments from the department head");
    expect(c.warnings[0]).not.toContain("your department head");
  });

  it("gives no comment warning when the comment read failed", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "encoder" }, ceilingWarning: null, unresolved: null, proposalGaps: null,
    });

    expect(c.warnings).toEqual([]);
  });
});
