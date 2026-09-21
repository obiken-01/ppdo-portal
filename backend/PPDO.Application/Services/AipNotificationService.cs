using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IAipNotificationService"/> (V18-58 / PPDO-75).</summary>
public sealed class AipNotificationService : IAipNotificationService
{
    /// <summary>Who handed an office back — the same side names the History read uses.</summary>
    public const string ReturnedByPpdo           = "Ppdo";
    public const string ReturnedByDepartmentHead = "DepartmentHead";

    private readonly IAipRepository     _aipRepo;
    private readonly IAuditRepository   _auditRepo;
    private readonly IPermissionService _permissions;

    public AipNotificationService(
        IAipRepository     aipRepo,
        IAuditRepository   auditRepo,
        IPermissionService permissions)
    {
        _aipRepo     = aipRepo;
        _auditRepo   = auditRepo;
        _permissions = permissions;
    }

    public async Task<ServiceResult<AipReviewNotificationsDto>> GetForCallerAsync(
        User caller, CancellationToken ct = default)
    {
        int firstYear = AipFiscalYears.FirstEnteredFiscalYear;

        // Sequential throughout — every read shares one DbContext, which is not thread-safe.
        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        bool deptHead    = await _permissions.CanReviewBudgetPlanningAsync(caller, ct);

        int  pendingForPpdo = 0;
        int? ppdoYear       = null;
        if (crossOffice)
        {
            IReadOnlyDictionary<int, int> byYear = await _aipRepo.CountOfficesAtStatusByFiscalYearAsync(
                AipWorkflowStatus.SubmittedToPpdo, PlanningStatus.Draft, firstYear, ct);
            pendingForPpdo = byYear.Values.Sum();
            ppdoYear       = byYear.Count > 0 ? byYear.Keys.Min() : null;
        }

        int  pendingForDeptHead = 0;
        int? deptHeadYear       = null;
        List<AipReturnedNoticeDto> returned = [];

        // ⚠️ Own office by caller.OfficeId, never OfficeScope.Resolve — which hands every host-office
        // user the whole province. Only ever this one office's rows.
        if (caller.OfficeId is int own && own != OfficeScope.NoOffice)
        {
            IReadOnlyList<AipOfficeStatusRow> rows = await _aipRepo.GetOfficeStatusesAsync(
                own, PlanningStatus.Draft, firstYear, ct);

            foreach (IGrouping<(int AipRecordId, int FiscalYear), AipOfficeStatusRow> year in rows
                         .GroupBy(r => (r.AipRecordId, r.FiscalYear))
                         .OrderBy(g => g.Key.FiscalYear))
            {
                // Groups move together; if they ever disagree the office is as far as the one behind.
                string status = AipReadinessColumn.OfficeStatus(year.Select(r => r.WorkflowStatus))!;
                int fiscalYear = year.Key.FiscalYear;

                if (deptHead && status == AipWorkflowStatus.DepartmentReview)
                {
                    pendingForDeptHead++;
                    deptHeadYear ??= fiscalYear;
                }

                if (status == AipWorkflowStatus.ReturnedByPpdo)
                {
                    returned.Add(new AipReturnedNoticeDto(fiscalYear, ReturnedByPpdo));
                }
                else if (status == AipWorkflowStatus.Draft && !deptHead)
                {
                    // A Draft is only "returned" when the last thing that happened to it was a return.
                    // Not asked for a department head: the return to the encoders is their own act.
                    List<int> groupIds = year.Select(r => r.GroupId).ToList();
                    string? latest = await _auditRepo.GetLatestActionAsync(
                        "aip_offices", groupIds, AuditAction.AipHandOffs, ct);
                    if (latest == AuditAction.ReturnToEncoder)
                        returned.Add(new AipReturnedNoticeDto(fiscalYear, ReturnedByDepartmentHead));
                }
            }
        }

        return ServiceResult<AipReviewNotificationsDto>.Ok(new AipReviewNotificationsDto(
            pendingForPpdo, ppdoYear, pendingForDeptHead, deptHeadYear, returned));
    }
}
