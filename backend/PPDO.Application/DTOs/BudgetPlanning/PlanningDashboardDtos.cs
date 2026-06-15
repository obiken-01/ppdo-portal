namespace PPDO.Application.DTOs.BudgetPlanning;

public record StatusBreakdownDto(string Status, int Count);

public record LdipSummaryDto(int Total, IReadOnlyList<StatusBreakdownDto> Breakdown);

public record AipSummaryDto(int Total, IReadOnlyList<StatusBreakdownDto> Breakdown);

public record WfpSummaryDto(int FinalCount, int ActiveOfficeCount);

/// <summary>WFP status for one active office. WfpStatus = "Draft" | "Final" | "Not started".</summary>
public record WfpOfficeStatusDto(int OfficeId, string OfficeName, string WfpStatus, int? AipRecordId);

public record PlanningDashboardDto(
    int FiscalYear,
    IReadOnlyList<int> AvailableFiscalYears,
    LdipSummaryDto Ldip,
    AipSummaryDto Aip,
    WfpSummaryDto Wfp,
    IReadOnlyList<WfpOfficeStatusDto> WfpByOffice
);

public record RecentActivityDto(
    long Id,
    DateTime ChangedAt,
    string TableName,
    string Action,
    int RecordId,
    string ActorName
);
