using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IAipConsolidatedService"/> (V18-55 / PPDO-73, V18-60 / PPDO-84).</summary>
public sealed class AipConsolidatedService : IAipConsolidatedService
{
    private readonly IAipRepository                  _aipRepo;
    private readonly IAipExpenditureRepository       _expRepo;
    private readonly IOfficeRepository               _officeRepo;
    private readonly IAipFormExcelService            _excel;
    private readonly IPermissionService              _permissions;
    private readonly ILogger<AipConsolidatedService> _logger;

    public AipConsolidatedService(
        IAipRepository                  aipRepo,
        IAipExpenditureRepository       expRepo,
        IOfficeRepository               officeRepo,
        IAipFormExcelService            excel,
        IPermissionService              permissions,
        ILogger<AipConsolidatedService> logger)
    {
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
        _officeRepo  = officeRepo;
        _excel       = excel;
        _permissions = permissions;
        _logger      = logger;
    }

    public async Task<ServiceResult<AipConsolidatedSheetDto>> GetSheetAsync(
        int fiscalYear, string? sector, int? officeId, User caller, CancellationToken ct = default)
    {
        string key = (sector ?? string.Empty).Trim().ToUpperInvariant();
        if (!AipSector.All.Contains(key))
            return ServiceResult<AipConsolidatedSheetDto>.BadRequest(
                $"'{sector}' is not an AIP sector. Expected one of: {string.Join(", ", AipSector.All)}.");

        ReportScope? resolved = await ResolveScopeAsync(officeId, caller, ct);
        if (resolved is not ReportScope scope)
        {
            _logger.LogWarning(
                "Permission denied. UserId: {UserId}, Feature: {Feature}", caller.Id, "AipConsolidatedView");
            return ServiceResult<AipConsolidatedSheetDto>.Forbidden(
                "Only an AIP reviewer can open this report.");
        }

        AipRecord? record = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, ct);

        // ⚠️ An unopened year is an EMPTY sheet, not a 404 — the reviewer has done nothing wrong, and
        // the page has a state for it.
        if (record is null)
            return ServiceResult<AipConsolidatedSheetDto>.Ok(new AipConsolidatedSheetDto(
                0, fiscalYear, key, Opened: false, 0, 0,
                AipSector.All.Select(s => new AipConsolidatedSectorCountDto(s, 0, 0)).ToList(),
                [], AipPrintedFigures.Zero, scope.Name));

        IReadOnlyList<AipOffice> groups = await _aipRepo.GetOfficesByAipIdAsync(record.Id, ct);

        // ⚠️ Every count below reads through the SAME two predicates the rows do, so a scoped report's
        // header cannot disagree with its own grid. For one office that yields 1/1 or 0/1 naturally,
        // rather than a second rule written out by hand (spec §4).
        List<AipOffice> mine = groups.Where(g => InScopeOffice(g, scope)).ToList();
        List<AipOffice> printed = mine.Where(g => StateCounts(g, scope)).ToList();

        List<AipOffice> onSheet = printed.Where(g => InSector(g, key)).ToList();

        AipTree tree = await LoadTreeAsync(record.Id, onSheet, ct);
        AipFormSheet sheet = BuildSheet(onSheet, tree, record.FiscalYear);

        return ServiceResult<AipConsolidatedSheetDto>.Ok(new AipConsolidatedSheetDto(
            record.Id,
            record.FiscalYear,
            key,
            Opened: true,
            CountOffices(printed),
            CountOffices(mine),
            AipSector.All
                .Select(s => new AipConsolidatedSectorCountDto(
                    s,
                    CountOffices(printed.Where(g => InSector(g, s))),
                    CountOffices(mine.Where(g => InSector(g, s)))))
                .ToList(),
            sheet.Rows,
            sheet.Total,
            scope.Name,
            await DescribeOfficeAsync(scope, groups, ct)));
    }

    public async Task<ServiceResult<AipFormExportFileDto>> ExportWorkbookAsync(
        int fiscalYear, int? officeId, User caller, CancellationToken ct = default)
    {
        ReportScope? resolved = await ResolveScopeAsync(officeId, caller, ct);
        if (resolved is not ReportScope scope)
        {
            _logger.LogWarning(
                "Permission denied. UserId: {UserId}, Feature: {Feature}", caller.Id, "AipConsolidatedExport");
            return ServiceResult<AipFormExportFileDto>.Forbidden(
                "Only an AIP reviewer can open this report.");
        }

        // ⚠️ FY≤2027 keeps its old shape and is not re-rendered under the FY2028+ rules
        // (AIP_Form_Spec.md §8). The page only offers FY2028 onward; this is for a direct call.
        if (!AipPrintedFigures.AppliesTo(fiscalYear))
            return ServiceResult<AipFormExportFileDto>.BadRequest(
                $"FY {AipFiscalYears.FirstEnteredFiscalYear - 1} and earlier are not rendered as the Annex B export.");

        Stopwatch watch = Stopwatch.StartNew();

        AipRecord? record = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, ct);

        // ⚠️ Unlike the grid, an unopened year IS a 404 — there is no document to hand over.
        if (record is null)
            return ServiceResult<AipFormExportFileDto>.NotFound($"FY {fiscalYear} has not been opened.");

        IReadOnlyList<AipOffice> groups = await _aipRepo.GetOfficesByAipIdAsync(record.Id, ct);

        // The same two predicates the grid uses — a one-office workbook is the consolidated one with
        // a narrower set of groups, not a second way of building a sheet.
        List<AipOffice> mine = groups.Where(g => InScopeOffice(g, scope)).ToList();
        List<AipOffice> withPpdo = mine.Where(g => StateCounts(g, scope)).ToList();

        // ⚠️ One tree load for all four sheets (§12), then the builder once per sector — never four
        // round trips. The builder groups by office id, so the whole tree can be handed to each call.
        AipTree tree = await LoadTreeAsync(record.Id, withPpdo, ct);

        List<AipFormWorkbookSheetDto> sheets = AipSector.All
            .Select(s =>
            {
                AipFormSheet sheet = BuildSheet(withPpdo.Where(g => InSector(g, s)).ToList(), tree, record.FiscalYear);
                return new AipFormWorkbookSheetDto(s, sheet.Rows, sheet.Total);
            })
            .ToList();

        AipFormWorkbookDto workbook = new(
            record.FiscalYear,
            ManilaToday(),
            CountOffices(withPpdo),
            CountOffices(mine),
            sheets,
            await DescribeOfficeAsync(scope, groups, ct));

        byte[] content = _excel.Export(workbook);

        _logger.LogInformation(
            "Consolidated AIP workbook built. FiscalYear: {FiscalYear}, Scope: {Scope}, OfficeId: {OfficeId}, " +
            "SubmittedOffices: {SubmittedOffices}, TotalOffices: {TotalOffices}, Rows: {Rows}, Bytes: {Bytes}, " +
            "ElapsedMs: {ElapsedMs}, UserId: {UserId}",
            workbook.FiscalYear, scope.Name, scope.OfficeId, workbook.SubmittedOffices, workbook.TotalOffices,
            sheets.Sum(s => s.Rows.Count), content.Length, watch.ElapsedMilliseconds, caller.Id);

        return ServiceResult<AipFormExportFileDto>.Ok(new AipFormExportFileDto(workbook.FileName, content));
    }

    /// <summary>The trees of <paramref name="groups"/>, and the fund codes the form's column (7) prints.</summary>
    private sealed record AipTree(
        IReadOnlyList<AipProgram>  Programs,
        IReadOnlyList<AipProject>  Projects,
        IReadOnlyList<AipActivity> Activities,
        IReadOnlyDictionary<int, IReadOnlyList<string>> FundCodes)
    {
        public static readonly AipTree Empty = new([], [], [], new Dictionary<int, IReadOnlyList<string>>());
    }

    /// <summary>
    /// ⚠️ Only <paramref name="groups"/> are expanded — a sent-back or drafting office's tree is never
    /// loaded, let alone shown. Nothing is loaded at all when there is nothing to expand. Sequential
    /// awaits: one DbContext, not thread-safe.
    /// </summary>
    private async Task<AipTree> LoadTreeAsync(int aipRecordId, IReadOnlyList<AipOffice> groups, CancellationToken ct)
    {
        if (groups.Count == 0) return AipTree.Empty;

        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsAsync(groups.Select(g => g.Id).ToList(), ct);
        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList(), ct);
        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(projects.Select(j => j.Id).ToList(), ct);
        IReadOnlyList<AipActivityFundCodeDto> fundRows =
            await _expRepo.GetFundCodesByAipRecordAsync(aipRecordId, ct);

        return new AipTree(programs, projects, activities, AipTreeMapper.GroupFundCodes(fundRows));
    }

    private static AipFormSheet BuildSheet(IReadOnlyList<AipOffice> groups, AipTree tree, int fiscalYear)
        => groups.Count == 0
            ? AipFormSheet.Empty
            : AipFormRowBuilder.Build(groups, tree.Programs, tree.Projects, tree.Activities, tree.FundCodes, fiscalYear);

    private static bool InSector(AipOffice group, string sector)
        => string.Equals(group.Sector, sector, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether an office's work is in the consolidation — with PPDO, or already accepted.
    ///
    /// ⚠️ <b>A sent-back office is out</b> (decided 2026-09-14): it is back in the office's hands and
    /// its figures may be about to change. It returns when it is re-submitted.
    /// </summary>
    private static bool IsWithPpdo(AipOffice group)
        => group.WorkflowStatus is AipWorkflowStatus.SubmittedToPpdo or AipWorkflowStatus.Consolidated;

    /// <summary>
    /// Distinct <b>offices</b>, not group rows — one office can head several groups, even across
    /// sectors (decision 21). A legacy group with no owning office counts as its own office rather
    /// than being folded into every other unowned group.
    /// </summary>
    private static int CountOffices(IEnumerable<AipOffice> groups)
        => groups.Select(g => g.OfficeId ?? -g.Id).Distinct().Count();

    /// <summary>The "As of" date and the file name's date — Manila, never the server's clock zone.</summary>
    private static DateOnly ManilaToday()
    {
        TimeZoneInfo manila;
        try   { manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch { manila = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, manila));
    }

    // ── Scope (PPDO-90) ───────────────────────────────────────────────────────

    /// <summary>
    /// Which offices a caller's report covers, and which workflow states count.
    /// </summary>
    /// <param name="OfficeId">Null only for the consolidated scope.</param>
    /// <param name="AnyState">
    /// True for a department head reading their OWN office: the point of the report is seeing their
    /// work as it will print before sending it on, so a draft is exactly what they need
    /// (<c>AIP_Report_Spec.md</c> §2 decision 3, confirmed by Ralph 2026-09-16).
    /// </param>
    private sealed record ReportScope(int? OfficeId, bool AnyState)
    {
        public bool IsConsolidated => OfficeId is null;
        public string Name => IsConsolidated ? AipReportScope.Consolidated : AipReportScope.Office;
    }

    /// <summary>
    /// Resolves the caller's scope, or null when neither reviewer flag is held (403).
    ///
    /// <para>
    /// ⚠️ <b>A department head is pinned to <c>users.office_id</c>, never resolved through
    /// <c>OfficeScope.Resolve</c>.</b> A department head who sits in the HOST office (PPDO) resolves to
    /// <c>SeeAll</c> there — which would quietly hand them every office in the province through a
    /// read that is supposed to be their own office only. The pin is the whole guard, and it is
    /// tested by name.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Cross-office wins when both flags are held, so a PPDO reviewer who also heads a division
    /// keeps the consolidated view rather than being narrowed to one office.
    /// </para>
    /// </summary>
    private async Task<ReportScope?> ResolveScopeAsync(int? requestedOfficeId, User caller, CancellationToken ct)
    {
        if (await _permissions.CanReviewAllOfficesAsync(caller, ct))
            return new ReportScope(requestedOfficeId, AnyState: false);

        if (await _permissions.CanReviewBudgetPlanningAsync(caller, ct))
            // ⚠️ The requested id is IGNORED, not refused — clamping matches every other AIP read and
            // avoids turning the parameter into an existence oracle for other offices.
            return new ReportScope(caller.OfficeId, AnyState: true);

        return null;
    }

    /// <summary>The groups a scope covers, before the sector filter — office match only.</summary>
    private static bool InScopeOffice(AipOffice group, ReportScope scope)
        => scope.IsConsolidated || group.OfficeId == scope.OfficeId;

    /// <summary>Whether a group's workflow state is printed under this scope.</summary>
    private static bool StateCounts(AipOffice group, ReportScope scope)
        => scope.AnyState || IsWithPpdo(group);

    /// <summary>
    /// The <c>office</c> block a scoped report carries. Null when consolidated, and also when the
    /// scope names an office with no group on this record — there is nothing to describe.
    /// </summary>
    private async Task<AipReportOfficeDto?> DescribeOfficeAsync(
        ReportScope scope, IReadOnlyList<AipOffice> groups, CancellationToken ct)
    {
        if (scope.OfficeId is not int officeId) return null;

        Office? office = await _officeRepo.GetByIdAsync(officeId, ct);
        if (office is null) return null;

        // ⚠️ An office can head SEVERAL groups, even across sectors (decision 21). The furthest-along
        // status is the office's position: a head who has sent one group on has not finished until the
        // last one follows, and showing "Draft" beside work already with PPDO would read as a loss.
        List<AipOffice> mine = groups.Where(g => g.OfficeId == officeId).ToList();
        string status = mine.Count == 0
            ? AipWorkflowStatus.Draft
            : mine.Select(g => g.WorkflowStatus).OrderBy(RankOf).Last();

        return new AipReportOfficeDto(officeId, office.OfficeCode, office.OfficeName, status);
    }

    /// <summary>How far along a status is, for picking an office's furthest-along group.</summary>
    private static int RankOf(string status) => status switch
    {
        AipWorkflowStatus.Draft            => 0,
        AipWorkflowStatus.ReturnedByPpdo   => 1,
        AipWorkflowStatus.DepartmentReview => 2,
        AipWorkflowStatus.SubmittedToPpdo  => 3,
        AipWorkflowStatus.Consolidated     => 4,
        _                                  => 0,
    };

}
