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
    private readonly IAipFormExcelService            _excel;
    private readonly IPermissionService              _permissions;
    private readonly ILogger<AipConsolidatedService> _logger;

    public AipConsolidatedService(
        IAipRepository                  aipRepo,
        IAipExpenditureRepository       expRepo,
        IAipFormExcelService            excel,
        IPermissionService              permissions,
        ILogger<AipConsolidatedService> logger)
    {
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
        _excel       = excel;
        _permissions = permissions;
        _logger      = logger;
    }

    public async Task<ServiceResult<AipConsolidatedSheetDto>> GetSheetAsync(
        int fiscalYear, string? sector, User caller, CancellationToken ct = default)
    {
        string key = (sector ?? string.Empty).Trim().ToUpperInvariant();
        if (!AipSector.All.Contains(key))
            return ServiceResult<AipConsolidatedSheetDto>.BadRequest(
                $"'{sector}' is not an AIP sector. Expected one of: {string.Join(", ", AipSector.All)}.");

        if (!await _permissions.CanReviewAllOfficesAsync(caller, ct))
        {
            _logger.LogWarning(
                "Permission denied. UserId: {UserId}, Feature: {Feature}", caller.Id, "AipConsolidatedView");
            return ServiceResult<AipConsolidatedSheetDto>.Forbidden(
                "Only a cross-office reviewer can open the consolidated AIP.");
        }

        AipRecord? record = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, ct);

        // ⚠️ An unopened year is an EMPTY sheet, not a 404 — the reviewer has done nothing wrong, and
        // the page has a state for it.
        if (record is null)
            return ServiceResult<AipConsolidatedSheetDto>.Ok(new AipConsolidatedSheetDto(
                0, fiscalYear, key, Opened: false, 0, 0,
                AipSector.All.Select(s => new AipConsolidatedSectorCountDto(s, 0, 0)).ToList(),
                [], AipPrintedFigures.Zero));

        IReadOnlyList<AipOffice> groups = await _aipRepo.GetOfficesByAipIdAsync(record.Id, ct);

        List<AipOffice> onSheet = groups
            .Where(g => IsWithPpdo(g) && InSector(g, key))
            .ToList();

        AipTree tree = await LoadTreeAsync(record.Id, onSheet, ct);
        AipFormSheet sheet = BuildSheet(onSheet, tree, record.FiscalYear);

        return ServiceResult<AipConsolidatedSheetDto>.Ok(new AipConsolidatedSheetDto(
            record.Id,
            record.FiscalYear,
            key,
            Opened: true,
            CountOffices(groups.Where(IsWithPpdo)),
            CountOffices(groups),
            AipSector.All
                .Select(s =>
                {
                    List<AipOffice> inSector = groups.Where(g => InSector(g, s)).ToList();
                    return new AipConsolidatedSectorCountDto(
                        s, CountOffices(inSector.Where(IsWithPpdo)), CountOffices(inSector));
                })
                .ToList(),
            sheet.Rows,
            sheet.Total));
    }

    public async Task<ServiceResult<AipFormExportFileDto>> ExportWorkbookAsync(
        int fiscalYear, User caller, CancellationToken ct = default)
    {
        if (!await _permissions.CanReviewAllOfficesAsync(caller, ct))
        {
            _logger.LogWarning(
                "Permission denied. UserId: {UserId}, Feature: {Feature}", caller.Id, "AipConsolidatedExport");
            return ServiceResult<AipFormExportFileDto>.Forbidden(
                "Only a cross-office reviewer can download the consolidated AIP.");
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
        List<AipOffice> withPpdo = groups.Where(IsWithPpdo).ToList();

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
            CountOffices(groups),
            sheets);

        byte[] content = _excel.Export(workbook);

        _logger.LogInformation(
            "Consolidated AIP workbook built. FiscalYear: {FiscalYear}, SubmittedOffices: {SubmittedOffices}, " +
            "TotalOffices: {TotalOffices}, Rows: {Rows}, Bytes: {Bytes}, ElapsedMs: {ElapsedMs}, UserId: {UserId}",
            workbook.FiscalYear, workbook.SubmittedOffices, workbook.TotalOffices,
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
}
