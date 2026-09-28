using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Builds the <see cref="AipDivisionContext"/> the division lock reads (v1.8.0 — PPDO-148).
///
/// <para>
/// Separate from the rules so the rules stay pure and the database reads stay in one place. It is
/// used by <c>AipService</c> and <c>AipExpenditureService</c> for writes, and by the tree read for
/// each activity's <c>CanEdit</c>.
/// </para>
/// </summary>
public interface IAipDivisionLock
{
    /// <summary>The context for one office — the write paths. Two queries at most.</summary>
    Task<AipDivisionContext> LoadAsync(AipOffice office, User caller, CancellationToken ct = default);

    /// <summary>
    /// The context for every office of one record, keyed by <see cref="AipOffice.Id"/> — the tree
    /// read. ⚠️ Two queries for the whole tree, never two per office: a host-office caller's
    /// tree holds every office in the province.
    /// </summary>
    Task<IReadOnlyDictionary<int, AipDivisionContext>> LoadForOfficesAsync(
        AipRecord record, IReadOnlyList<AipOffice> offices, User caller, CancellationToken ct = default);

    /// <summary>
    /// <see cref="ReviewerWriteGuard.DeniesWriteAsync"/> for this caller. The endpoints apply it
    /// before the service is reached; the tree read needs the same answer for <c>CanEdit</c>, or a
    /// comment-only reviewer would be shown rows that answer 403 when saved.
    /// </summary>
    Task<bool> DeniesWriteAsync(User caller, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AipDivisionLock : IAipDivisionLock
{
    private readonly IAipRepository                   _aipRepo;
    private readonly IAipDivisionSubmissionRepository _submissions;
    private readonly IPermissionService               _permissions;

    public AipDivisionLock(
        IAipRepository aipRepo,
        IAipDivisionSubmissionRepository submissions,
        IPermissionService permissions)
    {
        _aipRepo     = aipRepo;
        _submissions = submissions;
        _permissions = permissions;
    }

    public Task<bool> DeniesWriteAsync(User caller, CancellationToken ct = default)
        => ReviewerWriteGuard.DeniesWriteAsync(caller, _permissions, ct);

    public async Task<AipDivisionContext> LoadAsync(
        AipOffice office, User caller, CancellationToken ct = default)
    {
        AipRecord? record = await _aipRepo.GetByIntIdAsync(office.AipRecordId, ct);
        if (record is null) return AipDivisionContext.None;

        IReadOnlyDictionary<int, AipDivisionContext> one =
            await LoadForOfficesAsync(record, [office], caller, ct);
        return one.GetValueOrDefault(office.Id, AipDivisionContext.None);
    }

    public async Task<IReadOnlyDictionary<int, AipDivisionContext>> LoadForOfficesAsync(
        AipRecord record, IReadOnlyList<AipOffice> offices, User caller, CancellationToken ct = default)
    {
        // Decision 12 — FY ≤ 2027 has no entry flow to change. No query at all for those years.
        if (!AipFiscalYears.IsEntered(record.FiscalYear) || offices.Count == 0)
            return offices.ToDictionary(o => o.Id, _ => AipDivisionContext.None);

        // ⚠️ A group with no owning office is a legacy row the V18-32 backfill could not match. It
        // has no divisions to consult, so it keeps today's behaviour.
        List<int> officeIds = offices
            .Where(o => o.OfficeId is not null)
            .Select(o => o.OfficeId!.Value)
            .Distinct()
            .ToList();

        // Sequential awaits — one DbContext, which is not thread-safe (CLAUDE.md).
        IReadOnlyList<Division> divisions = await _submissions.GetDivisionsByOfficeIdsAsync(officeIds, ct);
        List<int> divisioned = divisions.Where(d => d.IsActive).Select(d => d.OfficeId).Distinct().ToList();
        IReadOnlyList<AipDivisionSubmission> rows = divisioned.Count == 0
            ? []
            : await _submissions.GetForOfficesAsync(record.Id, divisioned, ct);

        bool adminRole = caller.Role is UserRole.SuperAdmin or UserRole.Admin;
        // Resolved once, not per office: the grant is per user. "Of THIS office" is the office
        // comparison below, never OfficeScope — which would make a PPDO department head the head
        // of every office in the province (same rule as AipReviewService).
        bool holdsReviewGrant = !adminRole && await _permissions.CanReviewBudgetPlanningAsync(caller, ct);

        Dictionary<int, AipDivisionContext> result = [];
        foreach (AipOffice office in offices)
        {
            if (office.OfficeId is not int officeId || !divisioned.Contains(officeId))
            {
                result[office.Id] = AipDivisionContext.None;
                continue;
            }

            Dictionary<int, Division> own = divisions
                .Where(d => d.OfficeId == officeId)
                .ToDictionary(d => d.Id);
            HashSet<int> submitted = rows
                .Where(r => r.OfficeId == officeId && r.Status == AipDivisionStatus.Submitted)
                .Select(r => r.DivisionId)
                .ToHashSet();

            int? callerDivision = caller.DivisionId is int d
                                  && own.TryGetValue(d, out Division? mine) && mine.IsActive
                ? d
                : null;

            bool departmentHead = adminRole || (holdsReviewGrant && caller.OfficeId == officeId);

            result[office.Id] = new AipDivisionContext(
                hasDivisions: true, departmentHead, callerDivision, own, submitted);
        }

        return result;
    }
}
