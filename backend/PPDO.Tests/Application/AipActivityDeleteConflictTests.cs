using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// PPDO-193 (V18-71) — deleting an activity checks the version the caller loaded, like every other
/// activity write. Before this, spec §4 listed the activity DELETE as guarded but nothing checked
/// it: a stale copy could delete an activity someone had just edited, with no conflict panel.
///
/// <para>
/// As in <c>AipConcurrentEditTests</c>, the conflict is injected rather than provoked: SQLite has
/// no rowversion, so a real <c>DbUpdateConcurrencyException</c> cannot fire in a unit test. These
/// tests own everything above that line — the version reaching the delete's save, the 409 and its
/// payload, the deleted-first case, and authorization still winning.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    /// <summary>
    /// The scoped tree with an activity carrying a last-editor stamp. When
    /// <paramref name="conflictOnSave"/>, the activity repository's save throws as SQL Server does
    /// when the delete's WHERE matches zero rows, and the reload restores the other editor's stamp.
    /// The delete itself is a no-op in the conflict case: the transaction rolls back, so the row is
    /// still there.
    /// </summary>
    private static (AipService sut, Mock<IAipRepository> repo, Mock<IRepository<AipActivity>> activityRepo,
        AipActivity activity, List<AipActivity> acts, List<string> calls) BuildForDeleteConflict(
        bool conflictOnSave, string lastEditorName = "Ben Reyes")
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();

        Guid editorId = Guid.NewGuid();
        AipActivity activity = acts.Single(a => a.Id == ConflictActivityId);
        activity.RowVersion  = CurrentVersion;
        activity.UpdatedById = editorId;
        activity.UpdatedAt   = new DateTime(2026, 10, 7, 1, 14, 9, DateTimeKind.Utc);

        List<User> users =
        [
            new()
            {
                Id = editorId, Username = "ben", PasswordHash = "h", FullName = lastEditorName,
                Role = Domain.Enums.UserRole.Staff, OfficeId = HostOfficeId, IsActive = true,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            },
        ];

        var built = Build(recs, [], userSeed: users, officeSeed: offices,
            programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: [ConfigOffice(HostOfficeId, true), ConfigOffice(GuestOfficeId, false)]);

        AipService sut = built.Item1;
        Mock<IAipRepository> repo = built.Item2;
        Mock<IRepository<AipActivity>> activityRepo = built.Item12;

        // The order of the three calls that make the check real (see the ordering test).
        List<string> calls = [];
        activityRepo.Setup(r => r.ExpectRowVersion(It.IsAny<IRowVersioned>(), It.IsAny<byte[]?>()))
            .Callback(() => calls.Add("expect"));

        if (conflictOnSave)
        {
            activityRepo.Setup(r => r.DeleteAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()))
                .Callback(() => calls.Add("delete"))
                .Returns(Task.CompletedTask);
            activityRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .Callback(() => calls.Add("save"))
                .ThrowsAsync(new ConcurrencyConflictException(
                    "The row changed since it was loaded.", new InvalidOperationException()));

            repo.Setup(r => r.ReloadAsync(It.IsAny<IRowVersioned>(), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    activity.UpdatedById = editorId;
                    activity.UpdatedAt   = new DateTime(2026, 10, 7, 1, 14, 9, DateTimeKind.Utc);
                    activity.RowVersion  = CurrentVersion;
                })
                .Returns(Task.CompletedTask);
        }

        return (sut, repo, activityRepo, activity, acts, calls);
    }

    // ── The conflict ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_WhenTheRowMovedUnderneath_ReturnsConflictNotAnUnhandledFailure()
    {
        // Red-test by removing the catch in DeleteActivityAsync: the exception escapes instead.
        var (sut, _, _, _, acts, _) = BuildForDeleteConflict(conflictOnSave: true);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(
            ConflictActivityId, WriteHostCaller(), expectedRowVersion: StaleVersion);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains(acts, a => a.Id == ConflictActivityId);
    }

    [Fact]
    public async Task DeleteActivity_OnConflict_CarriesTheStandardActivityPayload()
    {
        // The same shape as an edit conflict, so AipConflictPanel can render it unchanged and
        // "Delete it" (Overwrite) is one request with the current version.
        var (sut, _, _, _, _, _) = BuildForDeleteConflict(conflictOnSave: true, lastEditorName: "Ben Reyes");

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(
            ConflictActivityId, WriteHostCaller(), expectedRowVersion: StaleVersion);

        Assert.Contains("Ben Reyes", result.Error);
        AipConflictDto<AipActivityDto> conflict =
            Assert.IsType<AipConflictDto<AipActivityDto>>(result.ErrorDetails);
        Assert.Equal("Ben Reyes", conflict.ChangedByName);
        Assert.Equal(Convert.ToBase64String(CurrentVersion), conflict.CurrentRowVersion);
        Assert.Equal(ConflictActivityId, conflict.Current.Id);
    }

    [Fact]
    public async Task DeleteActivity_WhenSomeoneElseDeletedItFirst_ReturnsNotFoundNotConflict()
    {
        // The other user deleted rather than edited: a different answer and a different recovery.
        var (sut, repo, _, activity, acts, _) = BuildForDeleteConflict(conflictOnSave: true);
        repo.Setup(r => r.ReloadAsync(It.IsAny<IRowVersioned>(), It.IsAny<CancellationToken>()))
            .Callback(() => acts.Remove(activity))
            .Returns(Task.CompletedTask);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(
            ConflictActivityId, WriteHostCaller(), expectedRowVersion: StaleVersion);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
        Assert.Contains("deleted", result.Error);
    }

    // ── The version reaches the delete's save ─────────────────────────────────

    [Fact]
    public async Task DeleteActivity_PassesTheCallersVersionToTheRepository()
    {
        // Without this the delete compares against the copy read moments ago in this same request,
        // which is always current, and passes every time.
        var (sut, _, activityRepo, activity, _, _) = BuildForDeleteConflict(conflictOnSave: false);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(
            ConflictActivityId, WriteHostCaller(), expectedRowVersion: StaleVersion);

        Assert.True(result.IsSuccess);
        activityRepo.Verify(r => r.ExpectRowVersion(activity, StaleVersion), Times.Once);
    }

    [Fact]
    public async Task DeleteActivity_DeclaresTheVersionAfterTheDeleteAndBeforeItsSave()
    {
        // ⚠️ The ledger, comment and audit writes earlier in the same transaction save too, and a
        // save accepts all changes — which would reset the declared OriginalValue to the copy read
        // in this request and make the check pass. So the version must be declared after
        // everything else and immediately before the delete's own save.
        var (sut, _, _, _, _, calls) = BuildForDeleteConflict(conflictOnSave: true);

        await sut.DeleteActivityAsync(ConflictActivityId, WriteHostCaller(), expectedRowVersion: StaleVersion);

        Assert.Equal(["delete", "expect", "save"], calls);
    }

    [Fact]
    public async Task DeleteActivity_WithNoVersionSupplied_StillDeletes()
    {
        // ⚠️ The staged-rollout state (spec §8), asserted so it is a decision: an omitted version
        // deletes UNGUARDED until PPDO-121 turns it into a 400.
        var (sut, _, activityRepo, activity, acts, _) = BuildForDeleteConflict(conflictOnSave: false);

        ServiceResult<AipDeleteResultDto> result =
            await sut.DeleteActivityAsync(ConflictActivityId, WriteHostCaller());

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(acts, a => a.Id == ConflictActivityId);
        activityRepo.Verify(r => r.ExpectRowVersion(activity, null), Times.Once);
    }

    // ── The unguarded-write warning PPDO-121 waits on ─────────────────────────

    /// <summary>The scoped tree with a logger the test can inspect.</summary>
    private static (AipService sut, Mock<Microsoft.Extensions.Logging.ILogger<AipService>> logger) BuildWithLogger()
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();
        acts.Single(a => a.Id == ConflictActivityId).RowVersion = CurrentVersion;
        Mock<Microsoft.Extensions.Logging.ILogger<AipService>> logger = new();

        var built = Build(recs, [], officeSeed: offices,
            programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: [ConfigOffice(HostOfficeId, true), ConfigOffice(GuestOfficeId, false)],
            logger: logger.Object);
        return (built.Item1, logger);
    }

    [Fact]
    public async Task DeleteActivity_WithNoVersion_LogsTheUnguardedWarning()
    {
        // Red-test by removing the WarnIfMissing call in DeleteActivityAsync.
        var (sut, logger) = BuildWithLogger();

        await sut.DeleteActivityAsync(ConflictActivityId, WriteHostCaller());

        UnguardedWriteWarning.Verify(logger, Times.Once());
    }

    [Fact]
    public async Task DeleteActivity_WithAVersion_LogsNoUnguardedWarning()
    {
        var (sut, logger) = BuildWithLogger();

        await sut.DeleteActivityAsync(ConflictActivityId, WriteHostCaller(), expectedRowVersion: CurrentVersion);

        UnguardedWriteWarning.Verify(logger, Times.Never());
    }

    [Fact]
    public async Task UpdateActivity_WithNoVersion_LogsTheUnguardedWarning()
    {
        // Covers SaveActivityAsync, the one save behind all three activity edit paths.
        var (sut, logger) = BuildWithLogger();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(), expectedRowVersion: null);

        Assert.True(result.IsSuccess);
        UnguardedWriteWarning.Verify(logger, Times.Once());
    }

    // ── Authorization still wins ──────────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_CallerOutsideTheOfficeWithAStaleVersion_IsRefusedBeforeTheVersionIsChecked()
    {
        // A 409 here would tell a caller who may not touch this office that the row exists and
        // has moved on. The scope refusal must come first, and the version must never be looked at.
        // Red-test by moving the version check above the guard.
        var (sut, _, activityRepo, _, acts, _) = BuildForDeleteConflict(conflictOnSave: true);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(
            ConflictActivityId, WriteGuestCaller(), expectedRowVersion: StaleVersion);

        Assert.False(result.IsSuccess);
        Assert.NotEqual(ServiceErrorCode.Conflict, result.Code);
        Assert.Null(result.ErrorDetails);
        activityRepo.Verify(r => r.ExpectRowVersion(It.IsAny<IRowVersioned>(), It.IsAny<byte[]?>()), Times.Never);
        Assert.Contains(acts, a => a.Id == ConflictActivityId);
    }
}
