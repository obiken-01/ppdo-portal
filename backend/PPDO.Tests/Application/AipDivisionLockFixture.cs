using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// A real <see cref="AipDivisionLock"/> over an in-memory division and submission list (PPDO-148).
///
/// <para>
/// ⚠️ The real lock, not a mocked <see cref="IAipDivisionLock"/>. The service tests are meant to
/// prove the write paths CONSULT the lock, and a mock that answers whatever the test set up would
/// pass whether or not the service asked the right question. Only the two repositories are
/// faked, and empty lists mean "no divisions", which is today's behaviour — so every test written
/// before PPDO-148 keeps asserting what it always did.
/// </para>
/// </summary>
internal sealed class AipDivisionLockFixture
{
    public List<Division>              Divisions   { get; } = [];
    public List<AipDivisionSubmission> Submissions { get; } = [];
    public Mock<IAipDivisionSubmissionRepository> Repo { get; } = new();

    public AipDivisionLockFixture()
    {
        Repo.Setup(r => r.GetDivisionsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<Division>)Divisions.Where(d => ids.Contains(d.OfficeId)).ToList());
        Repo.Setup(r => r.GetForOfficesAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int recordId, IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipDivisionSubmission>)Submissions
                    .Where(s => s.AipRecordId == recordId && ids.Contains(s.OfficeId)).ToList());
    }

    public IAipDivisionLock Build(IAipRepository aipRepo, IPermissionService permissions)
        => new AipDivisionLock(aipRepo, Repo.Object, permissions);

    /// <summary>The lock with no divisions anywhere — the pre-PPDO-148 world.</summary>
    public static IAipDivisionLock None(IAipRepository aipRepo, IPermissionService? permissions = null)
        => new AipDivisionLockFixture().Build(aipRepo, permissions ?? new PermissionService());

    public Division AddDivision(int id, int officeId, string name, bool active = true)
    {
        Division d = new() { Id = id, OfficeId = officeId, Name = name, IsActive = active };
        Divisions.Add(d);
        return d;
    }

    public void Submit(int aipRecordId, int officeId, int divisionId)
        => Submissions.Add(new AipDivisionSubmission
        {
            AipRecordId = aipRecordId, OfficeId = officeId, DivisionId = divisionId,
            Status = AipDivisionStatus.Submitted, SubmittedAt = DateTime.UtcNow,
        });
}
