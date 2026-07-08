using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// WFP (Work and Financial Plan) save + status lifecycle (RAL-64).
/// One WFP per (office, division) per AIP record. SaveAsync creates or replaces (upsert).
/// Status workflow: Draft → Final (finalize) → Draft (unlock, admin only).
/// Business rules: quarterly_total ≤ net_appropriation per line;
///   Σ gross_total_appropriation ≤ division_allocation (RAL-102).
/// </summary>
public interface IWfpService
{
    Task<IReadOnlyList<WfpRecordDto>> GetAllAsync(
        int? aipRecordId, int? officeId, int? divisionId = null, CancellationToken ct = default);

    Task<ServiceResult<WfpRecordDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Upsert: creates a new WFP or replaces all activities/lines of an existing Draft WFP
    /// for the given (aipRecordId, officeId, divisionId) triplet.
    /// When divisionId is provided: enforces the setup gate (ceiling + allocation + program assignment)
    /// and validates Σ gross total ≤ division allocation before any write.
    /// Returns Forbidden if the existing WFP is Final.
    /// </summary>
    Task<ServiceResult<WfpRecordDto>> SaveAsync(SaveWfpDto dto, Guid createdById, CancellationToken ct = default);

    Task<ServiceResult<WfpRecordDto>> FinalizeAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<WfpRecordDto>> UnlockAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Finds or creates the WFP record for (aipRecordId, officeId, divisionId) and, within it,
    /// the WFP activity for aipActivityId — WITHOUT touching any other existing activity (unlike
    /// <see cref="SaveAsync"/>'s destructive delete-then-reinsert-all). This is the enabler the
    /// v1.4 entry wizard (RAL-123) needs to obtain a wfp_activity_id before calling RAL-120's
    /// <c>WfpExpenditureService.SaveExpenditureAsync</c> — the old SaveAsync's replace-all
    /// semantics would silently orphan/cascade-delete any wfp_expenditures already attached to
    /// an activity if used for this purpose. Returns Forbidden if the record is already Final.
    /// </summary>
    Task<ServiceResult<WfpActivityRefDto>> EnsureActivityAsync(
        int aipRecordId, int officeId, int? divisionId, int fiscalYear, int aipActivityId,
        Guid createdById, CancellationToken ct = default);

    /// <summary>
    /// Generates an A4-landscape Excel report for the given WFP record.
    /// Loads the parent AIP hierarchy and config-office name internally.
    /// Returns <c>NotFound</c> if the WFP or its AIP record does not exist.
    /// </summary>
    Task<ServiceResult<byte[]>> ExportReportAsync(int id, CancellationToken ct = default);

    /// <summary>Wipes all WFP records (cascade removes activities/lines). Returns deleted count.</summary>
    Task<int> PurgeAllAsync(CancellationToken ct = default);
}
