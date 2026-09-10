using Microsoft.Extensions.Logging;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Implementation of <see cref="IAipActivityTotalsService"/> (v1.8.0 Phase 2 — V18-34).
///
/// <para>
/// Both methods are the same two steps: sum the activity's lines in SQL, then stage the result onto
/// the parent. They differ only in what "no lines" is taken to mean — see the interface.
/// </para>
///
/// <para>
/// ⚠️ <b>The two awaits are sequential and must stay that way.</b> Both repositories share one
/// <c>AppDbContext</c>, which is not thread-safe; a <c>Task.WhenAll</c> over two repository calls
/// sharing it is exactly what produced the <c>GetStatsAsync</c> production 500 (see
/// <c>CLAUDE.md</c>). There is nothing to gain here anyway — the second call needs the first's
/// result.
/// </para>
/// </summary>
public sealed class AipActivityTotalsService : IAipActivityTotalsService
{
    private readonly IAipRepository            _aipRepo;
    private readonly IAipExpenditureRepository _expenditureRepo;
    private readonly ILogger<AipActivityTotalsService> _logger;

    public AipActivityTotalsService(
        IAipRepository            aipRepo,
        IAipExpenditureRepository expenditureRepo,
        ILogger<AipActivityTotalsService> logger)
    {
        _aipRepo         = aipRepo;
        _expenditureRepo = expenditureRepo;
        _logger          = logger;
    }

    /// <inheritdoc />
    public Task<bool> RecalculateAsync(int activityId, CancellationToken ct = default)
        => RecalculateCoreAsync(activityId, zeroWhenNoLines: false, ct);

    /// <inheritdoc />
    public Task<bool> RecalculateAfterLineDeleteAsync(int activityId, CancellationToken ct = default)
        => RecalculateCoreAsync(activityId, zeroWhenNoLines: true, ct);

    private async Task<bool> RecalculateCoreAsync(
        int activityId, bool zeroWhenNoLines, CancellationToken ct)
    {
        AipExpenditureTotalsDto totals = await _expenditureRepo.SumByActivityIdAsync(activityId, ct);

        bool changed = await _aipRepo.ApplyActivityTotalsAsync(activityId, totals, zeroWhenNoLines, ct);

        if (!changed)
        {
            // Not an error. Either the activity has no lines and none are expected — every FY≤2027
            // activity is in that state permanently — or the id does not resolve.
            _logger.LogInformation(
                "AIP activity totals left unchanged. ActivityId: {ActivityId}, LineCount: {LineCount}",
                activityId, totals.LineCount);
            return false;
        }

        await _aipRepo.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AIP activity totals recomputed. ActivityId: {ActivityId}, LineCount: {LineCount}, Total: {Total}",
            activityId, totals.LineCount, totals.Total);

        return true;
    }
}
