using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Application.Services;

// The initial PPMP, read off the consolidated AIP's procurement items for an entered year (v1.9.2 —
// PPDO-263, docs/v1.9/Initial_PPMP_Report_Spec.md). Same shape as the initial WFP (PPDO-224): nothing is
// stored or copied (decision 4), so a PPDO edit after consolidation shows at once.
public sealed partial class PpmpReportService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PpmpReportDto>> GetInitialReportAsync(
        int officeId, int fiscalYear, int? requestedDivisionId, User caller, CancellationToken cancellationToken = default)
    {
        // ── Decision 8: PPDO only ──
        // ⚠️ Not InitialWfpReadRule. That rule lets a guest office's department head read their own
        // office's initial WFP; the initial PPMP is GSO's working document's draft, read by PPDO alone.
        // The endpoint asks the same question; it is asked again so the service is safe on its own.
        if (!await _permissions.CanAccessWfpAsync(caller, cancellationToken))
            return ServiceResult<PpmpReportDto>.Forbidden("The PPMP report is read by PPDO only.");

        Office? office = await _officeRepo.GetByIdAsync(officeId, cancellationToken);
        if (office is null)
            return ServiceResult<PpmpReportDto>.NotFound(WfpReportService.ReportNotFoundMessage);

        AipRecord? record = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, cancellationToken);
        if (record is null)
            return ServiceResult<PpmpReportDto>.NotFound($"No AIP found for fiscal year {fiscalYear}.");

        List<AipOffice> groups = (await _aipRepo.GetOfficesByAipIdNoTrackingAsync(record.Id, cancellationToken))
            .Where(g => g.OfficeId == officeId)
            .ToList();
        if (groups.Count == 0)
            return ServiceResult<PpmpReportDto>.NotFound(
                $"No AIP hierarchy found for {office.OfficeName} under FY {fiscalYear}.");

        // ── The gate (decision 5), the initial WFP's words ──
        if (groups.Any(g => g.WorkflowStatus != AipWorkflowStatus.Consolidated))
            return ServiceResult<PpmpReportDto>.Conflict(caller.OfficeId == officeId
                ? WfpReportService.OwnNotConsolidatedMessage(fiscalYear)
                : WfpReportService.NotConsolidatedMessage(office.OfficeCode, fiscalYear));

        // ── The division (decision 11) ──
        // PPDO's own office: the WFP report's rule (RAL-136), so Staff are forced to their division.
        // Another office is never narrowed by the reader's own PPDO division, as on the initial WFP.
        int? divisionId = requestedDivisionId;
        if (caller.OfficeId == officeId
            && !WfpDivisionScope.TryFilter(
                await WfpDivisionScope.ResolveAsync(caller, _permissions), requestedDivisionId, out divisionId))
            return ServiceResult<PpmpReportDto>.NotFound(WfpReportService.ReportNotFoundMessage);

        // Sequential reads: one DbContext. A fixed number of queries, never one per activity or line.
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(
            groups.Select(g => g.Id).ToList(), cancellationToken);
        IReadOnlyList<AipProject> projects = await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(
            programs.Select(p => p.Id).ToList(), cancellationToken);
        List<AipActivity> activities = (await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(
                projects.Select(p => p.Id).ToList(), cancellationToken))
            // By the activity's division tag (PPDO-148); untagged work is whole-office only.
            .Where(a => divisionId is not int d || a.DivisionId == d)
            .ToList();

        IReadOnlyList<AipExpenditure> lines = await _aipExpenditures.GetByActivityIdsAsync(
            activities.Select(a => a.Id).ToList(), cancellationToken);
        ILookup<int, AipProcurementItem> itemsByLine = (await _aipExpenditures.GetProcurementItemsByExpenditureIdsAsync(
                lines.Select(l => l.Id).ToList(), cancellationToken))
            .OrderBy(i => i.Id)
            .ToLookup(i => i.ExpenditureId);

        // Decision 7: a line with no items is left out, and a partly itemised line shows its items only.
        Dictionary<int, List<SourceLine>> linesByAipActivityId = lines
            .OrderBy(l => l.Id)
            .Select(l => (l.ActivityId, Line: FromAip(l, itemsByLine[l.Id])))
            .Where(x => x.Line.Items.Count > 0)
            .GroupBy(x => x.ActivityId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Line).ToList());

        return ServiceResult<PpmpReportDto>.Ok(await AssembleAsync(
            fiscalYear, office, divisionId, programs, projects, activities, linesByAipActivityId,
            WfpReportSources.Aip, cancellationToken));
    }

    /// <summary>
    /// An AIP line as a source line (decisions 6, 10, 12). Each item already carries its quarter
    /// (<see cref="AipProcurementItem.PeriodNo"/>, 1–4), so nothing is spread over months. Amounts are
    /// the items' <see cref="AipProcurementItem.LineTotal"/> (qty × price × days): no +30% uplift, no reserve.
    /// </summary>
    private static SourceLine FromAip(AipExpenditure line, IEnumerable<AipProcurementItem> items) => new(
        FundName(line.FundingSourceNameSnapshot), line.AccountNumberSnapshot, line.AccountTitleSnapshot,
        items
            .Select(i => new SourceItem(
                Math.Clamp(i.PeriodNo, 1, 4), i.PriceIndexItemId, i.Name, i.Unit, i.UnitPrice, i.Qty, i.LineTotal))
            .ToList());
}
