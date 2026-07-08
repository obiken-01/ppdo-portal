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

    /// <summary>
    /// Creates a new expenditure (dto.Id is null) or replaces an existing one's periods and
    /// procurement items in place (dto.Id provided — delete-then-reinsert). Validates
    /// Nature/Frequency/period numbers and negative amounts before any write.
    /// </summary>
    Task<ServiceResult<WfpExpenditureDto>> SaveExpenditureAsync(
        SaveWfpExpenditureDto dto, CancellationToken ct = default);
}
