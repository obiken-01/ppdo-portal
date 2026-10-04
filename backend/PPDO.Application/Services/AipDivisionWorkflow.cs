using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// One office's division-submit state for one AIP record (v1.8.0 — PPDO-149,
/// <c>Division_Submit_Spec.md</c> §5). Null from <see cref="IAipDivisionWorkflow.LoadAsync"/>
/// when the office is outside the division flow.
/// </summary>
/// <param name="Divisions">Every division of the office, inactive included, by id.</param>
/// <param name="Rows">
/// The office's submission rows, <b>tracked</b>, so a transition updates them in place. ⚠️ A
/// division with no row is in Draft — always ask <see cref="IsSubmitted"/>, never look for a row.
/// </param>
public sealed record AipDivisionOfficeState(
    int AipRecordId,
    int OfficeId,
    IReadOnlyDictionary<int, Division> Divisions,
    IReadOnlyList<AipDivisionSubmission> Rows)
{
    public bool IsSubmitted(int divisionId)
        => Rows.Any(r => r.DivisionId == divisionId && r.Status == AipDivisionStatus.Submitted);

    public AipDivisionSubmission? RowFor(int divisionId) => Rows.FirstOrDefault(r => r.DivisionId == divisionId);

    public string NameOf(int divisionId)
        => Divisions.TryGetValue(divisionId, out Division? d) ? d.Name : $"Division {divisionId}";

    /// <summary>
    /// The divisions that must submit before the office can go on: those with at least one activity
    /// (decision 10). ⚠️ Decided by the activities' tags, not by which users belong to a division,
    /// and a division with nothing tagged never blocks. An inactive division still holding tagged
    /// work counts — its department head submits it on its behalf or moves the work.
    /// </summary>
    public IReadOnlyList<int> RequiredDivisionIds(IEnumerable<AipActivity> officeActivities)
        => officeActivities
            .Where(a => a.DivisionId is not null)
            .Select(a => a.DivisionId!.Value)
            .Distinct()
            .OrderBy(id => id)
            .ToList();

    /// <summary>Required divisions still in Draft, as names in id order — for "Waiting on: …".</summary>
    public IReadOnlyList<string> WaitingNames(IEnumerable<AipActivity> officeActivities)
        => RequiredDivisionIds(officeActivities)
            .Where(id => !IsSubmitted(id))
            .Select(NameOf)
            .ToList();
}

/// <summary>
/// The pieces of the division flow that more than one service needs (v1.8.0 — PPDO-149): loading
/// an office's state, and handing every submitted division back. <c>AipSubmitService</c> uses both
/// for the department head's hops; <c>AipReviewService</c> uses the second for a PPDO return
/// (decision 11). Kept in one place so the "who is in the flow" rule cannot drift between them.
/// </summary>
public interface IAipDivisionWorkflow
{
    /// <summary>
    /// The office's state, or null when it is outside the division flow: an FY ≤ 2027 record
    /// (decision 12) or an office with no active division (decision 4). The same rule as
    /// <see cref="AipDivisionLock"/>.
    /// </summary>
    Task<AipDivisionOfficeState?> LoadAsync(AipRecord record, int officeId, CancellationToken ct = default);

    /// <summary>
    /// Stages every Submitted division back to Draft, stamping who returned it. ⚠️ <b>Stages
    /// only — it does not save.</b> The caller saves once, together with the office's own state
    /// change, so the two cannot land apart; then it calls <see cref="AuditReturnsAsync"/>.
    /// </summary>
    IReadOnlyList<AipDivisionSubmission> StageReturnAll(AipDivisionOfficeState state, Guid byUserId);

    /// <summary>One <c>RETURN_DIV</c> audit row per returned division, after the save.</summary>
    Task AuditReturnsAsync(IReadOnlyList<AipDivisionSubmission> returned, string cause, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AipDivisionWorkflow : IAipDivisionWorkflow
{
    private readonly IAipDivisionSubmissionRepository _repo;
    private readonly IAuditService                    _audit;
    private readonly ILogger<AipDivisionWorkflow>     _logger;

    public AipDivisionWorkflow(
        IAipDivisionSubmissionRepository repo, IAuditService audit, ILogger<AipDivisionWorkflow> logger)
    {
        _repo   = repo;
        _audit  = audit;
        _logger = logger;
    }

    public async Task<AipDivisionOfficeState?> LoadAsync(
        AipRecord record, int officeId, CancellationToken ct = default)
    {
        if (!AipFiscalYears.IsEntered(record.FiscalYear)) return null;

        // Sequential awaits — one DbContext (CLAUDE.md).
        IReadOnlyList<Division> divisions = await _repo.GetDivisionsByOfficeIdsAsync([officeId], ct);
        if (!divisions.Any(d => d.IsActive)) return null;

        IReadOnlyList<AipDivisionSubmission> rows = await _repo.GetForOfficeAsync(record.Id, officeId, ct);
        return new AipDivisionOfficeState(record.Id, officeId, divisions.ToDictionary(d => d.Id), rows);
    }

    public IReadOnlyList<AipDivisionSubmission> StageReturnAll(AipDivisionOfficeState state, Guid byUserId)
    {
        DateTime now = DateTime.UtcNow;
        List<AipDivisionSubmission> returned = [];
        foreach (AipDivisionSubmission row in state.Rows.Where(r => r.Status == AipDivisionStatus.Submitted))
        {
            row.Status       = AipDivisionStatus.Draft;
            row.ReturnedAt   = now;
            row.ReturnedById = byUserId;
            returned.Add(row);
        }
        return returned;
    }

    public async Task AuditReturnsAsync(
        IReadOnlyList<AipDivisionSubmission> returned, string cause, CancellationToken ct = default)
    {
        foreach (AipDivisionSubmission row in returned)
        {
            await _audit.LogAsync("aip_division_submissions", row.Id, AuditAction.ReturnDivision,
                new { Status = AipDivisionStatus.Submitted },
                new { Status = AipDivisionStatus.Draft, row.AipRecordId, row.OfficeId, row.DivisionId, Cause = cause },
                ct);
            _logger.LogInformation(
                "AIP division returned. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
                + "DivisionId: {DivisionId}, Cause: {Cause}, UserId: {UserId}",
                row.AipRecordId, row.OfficeId, row.DivisionId, cause, row.ReturnedById);
        }
    }
}
