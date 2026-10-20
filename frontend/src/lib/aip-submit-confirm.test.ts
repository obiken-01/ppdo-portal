import { describe, expect, it } from "vitest";
import type { AipUnresolvedCounts, DivisionFundAmount, DivisionSummary, ProposalProjectOption } from "@/types";
import { buildAipSubmitConfirmation, divisionAllocationOverages } from "./aip-submit-confirm";

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

// ── PPDO-237: division over its allocation, per fund ────────────────────────

function fund(code: string, amount: number, used: number): DivisionFundAmount {
  return { fundingSourceId: code.length, fundCode: code, fundName: code, amount, used, remaining: amount - used };
}

function division(id: number, code: string | null, name: string, funds: DivisionFundAmount[]): DivisionSummary {
  const allocated = funds.reduce((s, f) => s + f.amount, 0);
  const costedInAip = funds.reduce((s, f) => s + f.used, 0);
  return {
    divisionId: id, divisionCode: code, divisionName: name, allocated, costedInAip,
    remaining: allocated - costedInAip, costedActivityCount: 1, totalActivities: 1,
    aipStatus: "In progress", submissionStatus: "Todo", allocationByFund: funds,
  };
}

// The UAT case: REV ₱600,000 GF allocated, ₱1,151,000 costed.
const REV_OVER = division(1, "REV", "Revenue Division (test)", [fund("GF", 600_000, 1_151_000)]);
const CASH_WITHIN = division(2, "CASH", "Cash Division", [fund("GF", 900_000, 250_000)]);

describe("divisionAllocationOverages (PPDO-237)", () => {
  it("finds one division over in one fund", () => {
    expect(divisionAllocationOverages([REV_OVER, CASH_WITHIN])).toEqual([
      { divisionLabel: "REV", fundCode: "GF", over: 551_000, unset: false },
    ]);
  });

  it("lists each fund that is over, one entry per division and fund", () => {
    const rev = division(1, "REV", "Revenue", [fund("GF", 600_000, 1_151_000), fund("TF", 100_000, 150_000)]);
    expect(divisionAllocationOverages([rev]).map((o) => [o.fundCode, o.over])).toEqual([
      ["GF", 551_000], ["TF", 50_000],
    ]);
  });

  it("treats an unset allocation as ₱0 — over by the whole costed amount", () => {
    const rev = division(1, "REV", "Revenue", [fund("GAD", 0, 75_000)]);
    expect(divisionAllocationOverages([rev])).toEqual([
      { divisionLabel: "REV", fundCode: "GAD", over: 75_000, unset: true },
    ]);
  });

  it("is empty when every division is within (exactly at the allocation included)", () => {
    const exact = division(3, "EX", "Exact", [fund("GF", 500_000, 500_000)]);
    expect(divisionAllocationOverages([CASH_WITHIN, exact])).toEqual([]);
  });

  it("is empty for an office with no divisions", () => {
    expect(divisionAllocationOverages([])).toEqual([]);
  });

  it("scopes to one division when given its id", () => {
    const tf = division(2, "CASH", "Cash", [fund("GF", 100, 200)]);
    expect(divisionAllocationOverages([REV_OVER, tf], 2).map((o) => o.divisionLabel)).toEqual(["CASH"]);
  });

  it("names a division with no code by its name, and never counts the untagged row", () => {
    const noCode = division(4, null, "Budget Unit", [fund("GF", 0, 10)]);
    const untagged = division(0, null, "No division", [fund("GF", 0, 999)]);
    expect(divisionAllocationOverages([noCode, untagged]).map((o) => o.divisionLabel)).toEqual(["Budget Unit"]);
  });
});

describe("buildAipSubmitConfirmation — allocation warnings (PPDO-237)", () => {
  it("warns on the division submit and offers Submit anyway", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "division", divisionName: "Revenue Division (test)" },
      ceilingWarning: null, unresolved: NONE, proposalGaps: [],
      allocationOverages: divisionAllocationOverages([REV_OVER], 1),
    });

    expect(c.warnings).toHaveLength(1);
    expect(c.warnings[0]).toContain("REV is over its GF allocation by ₱551,000.00.");
    expect(c.warnings[0]).toContain("Your department head can rebalance division allocations");
    expect(c.confirmLabel).toBe("Submit anyway");
    expect(c.variant).toBe("warning");
  });

  it("gives a within-allocation division a plain confirm", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "division", divisionName: "Cash Division" },
      ceilingWarning: null, unresolved: NONE, proposalGaps: [],
      allocationOverages: divisionAllocationOverages([REV_OVER, CASH_WITHIN], 2),
    });

    expect(c.warnings).toEqual([]);
    expect(c.confirmLabel).toBe("Submit Cash Division");
  });

  it("says an unset allocation is treated as ₱0", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "division", divisionName: "Revenue" },
      ceilingWarning: null, unresolved: NONE, proposalGaps: [],
      allocationOverages: divisionAllocationOverages([division(1, "REV", "Revenue", [fund("GAD", 0, 75_000)])]),
    });

    expect(c.warnings[0]).toContain("REV’s GAD allocation is not set (treated as ₱0); over by ₱75,000.00.");
  });

  it("lists every over-allocated division on the PPDO hop, with Send anyway", () => {
    const pto = division(5, "PTO", "Treasury", [fund("GF", 0, 20_000)]);
    const c = buildAipSubmitConfirmation({
      hop: { kind: "toPpdo", resubmit: false },
      ceilingWarning: null, unresolved: NONE, proposalGaps: null,
      allocationOverages: divisionAllocationOverages([REV_OVER, CASH_WITHIN, pto]),
    });

    expect(c.warnings).toHaveLength(2);
    expect(c.warnings[0]).toContain("REV is over its GF allocation by ₱551,000.00.");
    expect(c.warnings[1]).toContain("PTO’s GF allocation is not set");
    expect(c.warnings.join(" ")).not.toContain("CASH");
    expect(c.warnings.join(" ")).not.toContain("your department head");
    expect(c.confirmLabel).toBe("Send anyway");
  });

  it("adds no allocation warning for an office with no divisions, or when the read failed", () => {
    for (const allocationOverages of [divisionAllocationOverages([]), null]) {
      const c = buildAipSubmitConfirmation({
        hop: { kind: "encoder" }, ceilingWarning: null, unresolved: NONE, proposalGaps: null, allocationOverages,
      });
      expect(c.warnings).toEqual([]);
    }
  });

  it("keeps the office ceiling and the allocation as separate warnings, ceiling first", () => {
    const c = buildAipSubmitConfirmation({
      hop: { kind: "division", divisionName: "Revenue" },
      ceilingWarning: CEILING, unresolved: NONE, proposalGaps: [],
      allocationOverages: divisionAllocationOverages([REV_OVER]),
    });

    expect(c.warnings).toHaveLength(2);
    expect(c.warnings[0]).toContain(CEILING);
    expect(c.warnings[1]).toContain("REV is over its GF allocation");
  });
});
