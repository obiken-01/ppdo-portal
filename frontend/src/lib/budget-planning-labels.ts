/**
 * The allocation page wears two names, depending on who is reading it (v1.8.0).
 *
 * A **PPDO finance officer** (`canManagePpdoAllocation`) sets the ceiling *and* splits it
 * across PPDO's divisions. For them the page is "Allocation", and it says so throughout.
 *
 * A **PBO finance officer** (`canManagePboCeiling` alone) sets ceilings for any office and
 * need not be a PPDO employee at all. The division split is PPDO's own internal mechanic —
 * division is a scoping axis only for the host office, settled in `BudgetPlanningScope`
 * (RAL-250) — so for that reader the page is "Budget Ceilings" and the word "division"
 * does not appear on it anywhere. Hiding the controls but keeping the vocabulary would
 * still leave them asking what a division split is and whether they were meant to do one.
 *
 * The sidebar link, the breadcrumb, the page header and the tab strip all read their text
 * from here, so those four cannot drift apart the way three copies of `APP_VERSION` did.
 */

/** The slice of `MeResponse` these labels turn on. */
export interface AllocationLabelSubject {
  canManagePpdoAllocation?: boolean;
  /**
   * Read alongside the grant because `CanManagePpdoAllocation` is host-office-exclusive
   * (`docs/v1.8/Permission_Matrix.md` §4) — both endpoints refuse a guest-office caller
   * outright, for their own office as well as a foreign one. A guest office holding the
   * grant by mistake must therefore still read "Budget Ceilings": the division vocabulary
   * would describe work its own endpoints will refuse.
   */
  isHostOffice?: boolean;
}

export interface AllocationLabels {
  /** Sidebar link and breadcrumb leaf. */
  nav: string;
  /** `ConfigPageHeader` title. */
  title: string;
  /** `ConfigPageHeader` description. */
  description: string;
  /** Label of the first tab. */
  ceilingTab: string;
  /** Helper line above the fund-source list. */
  ceilingIntro: string;
  /**
   * Shown when no office is selected. A fifth surface as of PPDO-17: before the office
   * picker existed, a PBO-only caller was always pre-filled to their own office and could
   * never reach this state. Now it is the FIRST thing they see on every page load.
   */
  emptyOffice: string;
}

export function allocationLabels(me: AllocationLabelSubject | null): AllocationLabels {
  // Anyone reaching this page without the PPDO grant holds the PBO one — the page
  // redirects users with neither (see the useMe guard on the page itself).
  //
  // The host-office half is not redundant with the grant: a live PTO account held
  // `CanManagePpdoAllocation` by mistake and was duly shown "Allocation", a division
  // split tab, and the whole division vocabulary — for work its own endpoints refuse.
  // Keying on the grant alone means this page stays correct only while the grant is
  // administered correctly, which is exactly the assumption that failed.
  if (me?.canManagePpdoAllocation && me?.isHostOffice) {
    return {
      nav:         "Allocation",
      title:       "Allocation",
      description: "Set office budget ceilings and per-division allocation splits by fund source.",
      ceilingTab:  "Ceiling & Division Allocation",
      ceilingIntro:
        "One ceiling and division split per active fund source. General Fund is required; others are optional.",
      emptyOffice: "Select an office to configure allocation.",
    };
  }

  return {
    nav:         "Budget Ceilings",
    title:       "Budget Ceilings",
    description: "Set each office's budget ceiling by fund source.",
    ceilingTab:  "Ceilings",
    ceilingIntro:
      "One ceiling per active fund source. General Fund is required; others are optional.",
    emptyOffice: "Select an office to set its budget ceilings.",
  };
}
