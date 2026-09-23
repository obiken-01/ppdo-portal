namespace PPDO.Application.DTOs.BudgetPlanning;

public record StatusBreakdownDto(string Status, int Count);

public record LdipSummaryDto(int Total, IReadOnlyList<StatusBreakdownDto> Breakdown);

public record AipSummaryDto(int Total, IReadOnlyList<StatusBreakdownDto> Breakdown);

public record WfpSummaryDto(int FinalCount, int ActiveOfficeCount);

/// <summary>WFP status for one active office. WfpStatus = "Draft" | "Final" | "Not started".</summary>
public record WfpOfficeStatusDto(int OfficeId, string OfficeCode, string OfficeName, string WfpStatus, int? AipRecordId);

public record PlanningDashboardDto(
    int FiscalYear,
    IReadOnlyList<int> AvailableFiscalYears,
    LdipSummaryDto Ldip,
    AipSummaryDto Aip,
    WfpSummaryDto Wfp,
    IReadOnlyList<WfpOfficeStatusDto> WfpByOffice,
    AllocationSetupOverviewDto Allocation
);

public record RecentActivityDto(
    long Id,
    DateTime ChangedAt,
    string TableName,
    string Action,
    int? RecordId,
    Guid? RecordGuid,
    string ActorName
);

// ── Office-scoped dashboard (RAL-60) ────────────────────────────────────────

/// <summary>Allocation "setup-complete" summary for one office+FY (Allocation_Requirements.md §4).</summary>
public record AllocationSetupSummaryDto(
    decimal? CeilingAmount,
    decimal Allocated,
    decimal? Remaining,
    bool IsOverAllocated,
    int AssignedProgramCount,
    int UnassignedProgramCount
);

/// <summary>
/// Office-scoped LDIP summary. ScopingSupported is false until RAL-61 adds
/// ldip_records.office_id — Total/Breakdown are meaningless placeholders until then.
/// </summary>
public record OfficeLdipSummaryDto(
    bool ScopingSupported,
    int Total,
    IReadOnlyList<StatusBreakdownDto> Breakdown
);

/// <summary>
/// Office-scoped AIP presence + PPA/activity counts + money, matched via office_ref_code.
///
/// <see cref="CostedInAip"/> was added by PPDO-20 and is the office's OWN total — the sum of every
/// activity under its AIP rows. It is deliberately not derivable from the per-division figures on
/// <see cref="PpdoDashboardDto.ByDivision"/>: a PPA assigned to two divisions counts in full
/// against both there, so summing that list overstates the office by the shared programs. The
/// dashboard's tiles read this field rather than that sum, which is what keeps the office total on
/// the page agreeing with the office table's row for the same office.
///
/// It is also the only costed figure a guest office can obtain: the cross-office endpoint that
/// computes the same number for every office correctly 403s a plain office user.
/// </summary>
public record OfficeAipSummaryDto(
    bool Exists,
    string? Status,
    int ProgramCount,
    int ProjectCount,
    int ActivityCount,
    decimal CostedInAip
);

/// <param name="ByDivision">
/// The office's own per-division breakdown (PPDO-126, PPDO-127) — empty for an office with no
/// divisions configured yet (render an empty state, not a blank panel), and for a division-scoped
/// caller narrowed to a division they no longer hold. A department head
/// (<c>CanManageOfficeSetup</c>) sees every division of their own office; anyone else sees only
/// their own division's row, same "whoever may set the split may see it" rule the Allocation page
/// applies. Reuses <see cref="DivisionSummaryDto"/> — the same shape <see cref="PpdoDashboardDto"/>
/// carries for PPDO's own dashboard — so a guest office renders on the same <c>DivisionTable</c>
/// component rather than a second one.
/// </param>
public record OfficeDashboardDto(
    int OfficeId,
    int FiscalYear,
    AllocationSetupSummaryDto Allocation,
    OfficeLdipSummaryDto Ldip,
    OfficeAipSummaryDto Aip,
    IReadOnlyList<DivisionSummaryDto> ByDivision
);
