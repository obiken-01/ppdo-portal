using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Expenditure lines under an AIP activity — the third stage of entry (V18-42 / PPDO-52, spec §4).
///
/// <para>
/// Its own service rather than more methods on <c>AipService</c>, which is already the largest in
/// the project. The write paths here are also the only ones that must fire two side effects in a
/// fixed order after every change, and keeping that sequence in one small class makes it possible
/// to see that all three methods do it.
/// </para>
///
/// <para>
/// <b>⚠️ Every write does three things, in this order, and skipping either side effect is silent:</b>
/// </para>
/// <list type="number">
/// <item>Write the line.</item>
/// <item><b>Recompute the parent activity's totals</b> (V18-34). Skip it and the activity keeps
/// yesterday's figures while its lines say otherwise — the printed form and the ceiling check then
/// read a number nobody entered.</item>
/// <item><b>Upsert the reservation ledger</b> (V18-45). Skip it and the office's remaining
/// allocation overstates what is left, so submit passes work the ceiling should have caught.</item>
/// </list>
///
/// <para>
/// ⚠️ <b>Delete uses a different recompute from add and edit</b>, and they do opposite things for
/// an activity that ends with no lines: <c>RecalculateAfterLineDeleteAsync</c> takes totals to
/// <b>0</b> (costed at nothing), while <c>RecalculateAsync</c> leaves a line-less activity
/// untouched (never costed). Both are 0 lines; only one is 0 pesos. Choosing by what is convenient
/// rather than by what just happened is how an imported FY≤2027 activity gets zeroed.
/// </para>
/// </summary>
public interface IAipExpenditureService
{
    /// <summary>Every line for one activity, oldest first. Scoped — a caller who may not read the owning office gets NotFound.</summary>
    Task<ServiceResult<IReadOnlyList<AipExpenditureDto>>> GetByActivityAsync(
        int activityId, User caller, CancellationToken ct = default);

    /// <summary>Adds a line, then recomputes and upserts the ledger.</summary>
    Task<ServiceResult<AipExpenditureWriteResultDto>> AddAsync(
        int activityId, CreateAipExpenditureDto dto, User caller, CancellationToken ct = default);

    /// <summary>Replaces a line's values, then recomputes and upserts the ledger.</summary>
    Task<ServiceResult<AipExpenditureWriteResultDto>> UpdateAsync(
        int expenditureId, UpdateAipExpenditureDto dto, User caller, CancellationToken ct = default);

    /// <summary>
    /// Deletes a line, then recomputes <b>with the delete-aware pass</b> and upserts the ledger.
    /// Removing the activity's last line leaves its total at 0, not null.
    /// </summary>
    Task<ServiceResult<AipExpenditureWriteResultDto>> DeleteAsync(
        int expenditureId, User caller, CancellationToken ct = default);
}
