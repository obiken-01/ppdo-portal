using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// WFP expenditure save + read (v1.4 WFP Rework — RAL-120). Single entry point for the
/// schema/computation pipeline everything else in the epic builds on: period amounts (or
/// Σ procurement items per period) roll up to Q1–Q4 -> Net -> Total, always computed
/// server-side, never trusted from the client.
///
/// <see cref="SaveExpenditureAsync"/> is intentionally named so RAL-122's ceiling-check
/// rejection has one obvious place to hook in.
/// </summary>
public interface IWfpExpenditureService
{
    Task<ServiceResult<WfpExpenditureDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Expenditures already saved under this WFP activity — the entry wizard's "added so far" list (RAL-123).</summary>
    Task<IReadOnlyList<WfpExpenditureDto>> GetByActivityIdAsync(int wfpActivityId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new expenditure (dto.Id is null) or replaces an existing one's periods and
    /// procurement items in place (dto.Id provided — delete-then-reinsert). Validates
    /// Nature/Frequency/period numbers and negative amounts before any write.
    ///
    /// Reserve rule (RAL-121): when ApplyReserve is true, ReserveAmount defaults to
    /// <c>WfpReserveRule.Rate</c> × Net if not supplied, and is rejected (BadRequest) if it
    /// exceeds that cap — regardless of the account's default_apply_reserve, which is a
    /// pre-fill only, never an eligibility gate.
    /// </summary>
    Task<ServiceResult<WfpExpenditureDto>> SaveExpenditureAsync(
        SaveWfpExpenditureDto dto, CancellationToken ct = default);

    /// <summary>
    /// Deletes an expenditure and its child periods/procurement items, then recomputes the
    /// division-allocation ledger (RAL-129). Forbidden when the parent WFP record is Final.
    /// </summary>
    Task<ServiceResult<bool>> DeleteExpenditureAsync(int id, CancellationToken ct = default);

    /// <summary>The current reserve rate, for the frontend to display without hard-coding it.</summary>
    WfpReserveRateDto GetReserveRate();
}
