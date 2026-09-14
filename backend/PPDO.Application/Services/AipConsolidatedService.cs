using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IAipConsolidatedService"/> (V18-55 / PPDO-73).</summary>
public sealed class AipConsolidatedService : IAipConsolidatedService
{
    private readonly IAipRepository                  _aipRepo;
    private readonly IAipExpenditureRepository       _expRepo;
    private readonly IPermissionService              _permissions;
    private readonly ILogger<AipConsolidatedService> _logger;

    public AipConsolidatedService(
        IAipRepository                  aipRepo,
        IAipExpenditureRepository       expRepo,
        IPermissionService              permissions,
        ILogger<AipConsolidatedService> logger)
    {
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
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
            .Where(g => IsWithPpdo(g) && string.Equals(g.Sector, key, StringComparison.OrdinalIgnoreCase))
            .ToList();

        AipFormSheet sheet = AipFormSheet.Empty;
        if (onSheet.Count > 0)
        {
            // ⚠️ Only the groups on this sheet are expanded — a sent-back or drafting office's tree is
            // never loaded, let alone shown. Sequential awaits: one DbContext, not thread-safe.
            IReadOnlyList<AipProgram> programs =
                await _aipRepo.GetProgramsByOfficeIdsAsync(onSheet.Select(g => g.Id).ToList(), ct);
            IReadOnlyList<AipProject> projects =
                await _aipRepo.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList(), ct);
            IReadOnlyList<AipActivity> activities =
                await _aipRepo.GetActivitiesByProjectIdsAsync(projects.Select(j => j.Id).ToList(), ct);
            IReadOnlyList<AipActivityFundCodeDto> fundRows =
                await _expRepo.GetFundCodesByAipRecordAsync(record.Id, ct);

            sheet = AipFormRowBuilder.Build(
                onSheet, programs, projects, activities,
                AipTreeMapper.GroupFundCodes(fundRows), record.FiscalYear);
        }

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
                    List<AipOffice> inSector = groups
                        .Where(g => string.Equals(g.Sector, s, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    return new AipConsolidatedSectorCountDto(
                        s, CountOffices(inSector.Where(IsWithPpdo)), CountOffices(inSector));
                })
                .ToList(),
            sheet.Rows,
            sheet.Total));
    }

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
}
