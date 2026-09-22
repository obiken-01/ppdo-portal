using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The concurrent-edit guard (V18-71 / PPDO-118) — two encoders in one office, second save loses.
///
/// <para>
/// ⚠️ <b>The conflict is injected, not provoked, and that is forced rather than lazy.</b> A real
/// <c>DbUpdateConcurrencyException</c> needs SQL Server to bump a <c>rowversion</c>. The
/// Infrastructure fixtures run on SQLite, which has no rowversion and populates nothing (PPDO-117),
/// so the exception can never fire there. What these tests own is everything <i>above</i> that
/// line: that the translated exception becomes a 409 rather than a 500, that the payload names the
/// right person, that a deleted row is told apart from an edited one, and that authorization still
/// wins. The database half — that the token bumps and the WHERE clause catches it — was verified
/// against real SQL Server in PPDO-117.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private const int ConflictActivityId = 40;

    private static readonly byte[] StaleVersion   = [0, 0, 0, 0, 0, 0, 0, 1];
    private static readonly byte[] CurrentVersion = [0, 0, 0, 0, 0, 0, 0, 9];

    /// <summary>
    /// The scoped tree from <c>AipWriteScopeTests</c>, with a repository whose save behaves as
    /// <paramref name="onSave"/> dictates and an activity already carrying a last-editor stamp.
    /// </summary>
    private static (AipService sut, Mock<IAipRepository> repo, AipActivity activity) BuildForConflict(
        bool conflictOnSave,
        Guid? lastEditedBy = null,
        string lastEditorName = "Ben Reyes")
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();

        Guid editorId = lastEditedBy ?? Guid.NewGuid();
        AipActivity activity = acts.Single(a => a.Id == ConflictActivityId);
        activity.RowVersion  = CurrentVersion;
        activity.UpdatedById = editorId;
        activity.UpdatedAt   = new DateTime(2026, 9, 22, 1, 14, 9, DateTimeKind.Utc);

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

        if (conflictOnSave)
        {
            repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ConcurrencyConflictException(
                    "The row changed since it was loaded.", new InvalidOperationException()));

            // ⚠️ The mock has to actually behave like a reload, and the reason is the whole point
            // of the reload existing. The service stamps the CURRENT caller onto the entity before
            // saving, so at the moment the conflict is caught the in-memory row says the refused
            // user changed it. Only re-reading the database restores the other editor. A no-op
            // mock here would have the test pass for the wrong reason — or, as it first did, fail
            // and look like a bug in the service.
            repo.Setup(r => r.ReloadAsync(It.IsAny<IRowVersioned>(), It.IsAny<CancellationToken>()))
                .Callback(() =>
                {
                    activity.UpdatedById = editorId;
                    activity.UpdatedAt   = new DateTime(2026, 9, 22, 1, 14, 9, DateTimeKind.Utc);
                    activity.RowVersion  = CurrentVersion;
                })
                .Returns(Task.CompletedTask);
        }

        return (sut, repo, activity);
    }

    // ── The conflict itself ───────────────────────────────────────────────────

    [Fact]
    public async Task UpdateActivity_WhenTheRowMovedUnderneath_ReturnsConflictNotAnUnhandledFailure()
    {
        // Red-test this one by deleting the catch in SaveActivityAsync: the exception escapes and
        // the test fails with ConcurrencyConflictException instead of asserting. Every other
        // UpdateActivity test saves against an unchanged row, so without this the catch could be
        // removed and the suite would stay green — the trap PPDO-115's inactive-target test hit.
        var (sut, _, _) = BuildForConflict(conflictOnSave: true);

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(),
            expectedRowVersion: StaleVersion);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_OnConflict_NamesWhoChangedIt()
    {
        var (sut, _, _) = BuildForConflict(conflictOnSave: true, lastEditorName: "Ben Reyes");

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(),
            expectedRowVersion: StaleVersion);

        Assert.Contains("Ben Reyes", result.Error);

        AipConflictDto<AipActivityDto> conflict =
            Assert.IsType<AipConflictDto<AipActivityDto>>(result.Details);
        Assert.Equal("Ben Reyes", conflict.ChangedByName);
    }

    [Fact]
    public async Task UpdateActivity_OnConflict_CarriesTheCurrentVersionSoOverwriteIsOneRequest()
    {
        // Without this the UI can only offer "discard and reload" — an Overwrite would need a
        // fetch first, and between the fetch and the save the row can move again.
        var (sut, _, _) = BuildForConflict(conflictOnSave: true);

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(),
            expectedRowVersion: StaleVersion);

        AipConflictDto<AipActivityDto> conflict =
            Assert.IsType<AipConflictDto<AipActivityDto>>(result.Details);
        Assert.Equal(Convert.ToBase64String(CurrentVersion), conflict.CurrentRowVersion);
    }

    [Fact]
    public async Task UpdateActivity_OnConflict_LeaksNothingInternal()
    {
        var (sut, _, _) = BuildForConflict(conflictOnSave: true);

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(),
            expectedRowVersion: StaleVersion);

        foreach (string forbidden in new[] { "Exception", "rowversion", "SaveChanges", "SQL" })
            Assert.DoesNotContain(forbidden, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdateActivity_OnConflict_WhenTheLastEditorCannotBeResolved_StillConflicts()
    {
        // A row last touched before PPDO-117 shipped has no updated_by_id, and a user can stop
        // resolving. A conflict that cannot be attributed is still a conflict — falling back to a
        // 500 here would turn "someone else edited this" into "the app broke".
        var (sut, repo, activity) = BuildForConflict(conflictOnSave: true);

        // Override the reload so the row comes back with no last-editor, which is what a row
        // untouched since PPDO-117 shipped actually looks like.
        repo.Setup(r => r.ReloadAsync(It.IsAny<IRowVersioned>(), It.IsAny<CancellationToken>()))
            .Callback(() => { activity.UpdatedById = null; activity.RowVersion = CurrentVersion; })
            .Returns(Task.CompletedTask);

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(),
            expectedRowVersion: StaleVersion);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        AipConflictDto<AipActivityDto> conflict =
            Assert.IsType<AipConflictDto<AipActivityDto>>(result.Details);
        Assert.Null(conflict.ChangedByName);
        Assert.Contains("someone else", result.Error);
    }

    // ── The actor stamp, which the message depends on ─────────────────────────

    [Fact]
    public async Task UpdateActivity_OnSuccess_StampsWhoChangedItAndWhen()
    {
        // The 409 is only as good as the last writer it can name. A write path that skips this
        // leaves the NEXT conflict reporting an anonymous editor.
        var (sut, _, activity) = BuildForConflict(conflictOnSave: false);
        User caller = WriteHostCaller();
        DateTime before = DateTime.UtcNow;

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), caller);

        Assert.True(result.IsSuccess);
        Assert.Equal(caller.Id, activity.UpdatedById);
        Assert.NotNull(activity.UpdatedAt);
        Assert.InRange(activity.UpdatedAt!.Value, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task UpdateActivityDetails_OnSuccess_StampsTheActorToo()
    {
        // Same guard on the OTHER activity write path. PPDO-92's three-place route gate is the
        // precedent: one call site missing the rule the others follow.
        var (sut, _, activity) = BuildForConflict(conflictOnSave: false);
        User caller = WriteHostCaller();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityDetailsAsync(
            ConflictActivityId,
            new UpdateAipActivityDetailsDto("Renamed", null, null, null, null, null, null, null, null),
            caller);

        Assert.True(result.IsSuccess);
        Assert.Equal(caller.Id, activity.UpdatedById);
    }

    [Fact]
    public async Task UpdateActivityIsCreation_OnSuccess_StampsTheActorToo()
    {
        var (sut, _, activity) = BuildForConflict(conflictOnSave: false);
        User caller = WriteHostCaller();

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(ConflictActivityId, true, caller);

        Assert.True(result.IsSuccess);
        Assert.Equal(caller.Id, activity.UpdatedById);
    }

    // ── The expected version reaches the database layer ───────────────────────

    [Fact]
    public async Task UpdateActivity_PassesTheCallersVersionToTheRepository()
    {
        // The guard is worthless if the browser's version never reaches EF's WHERE clause — the
        // save would compare against the copy read moments ago in this same request, which is
        // always current, and pass every time.
        var (sut, repo, activity) = BuildForConflict(conflictOnSave: false);

        await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(),
            expectedRowVersion: StaleVersion);

        repo.Verify(r => r.ExpectRowVersion(activity, StaleVersion), Times.Once);
    }

    [Fact]
    public async Task UpdateActivity_WithNoVersionSupplied_StillSaves()
    {
        // ⚠️ The PPDO-119 rollout state, asserted so it is a decision rather than an accident:
        // an omitted version saves UNGUARDED. PPDO-121 turns this into a 400, and until it ships
        // the guard is opt-out by omission.
        var (sut, repo, activity) = BuildForConflict(conflictOnSave: false);

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, UpdateActivity(), WriteHostCaller(),
            expectedRowVersion: null);

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.ExpectRowVersion(activity, null), Times.Once);
    }
}
