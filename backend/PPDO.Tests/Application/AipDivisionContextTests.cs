using Moq;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The division lock's rules as pure functions (<see cref="AipDivisionContext"/>), and how
/// <see cref="AipDivisionLock"/> builds the context from the database (v1.8.0 — PPDO-148).
/// The role × state grid for edits lives in <see cref="PermissionMatrixTests"/>; this file covers
/// the create tag, the re-tag target and container deletes, plus the loader's inputs.
/// </summary>
public sealed class AipDivisionContextTests
{
    private const int Office = 7;
    private const int A = 1, B = 2, Inactive = 3;

    private static AipDivisionContext Ctx(
        bool head = false, int? caller = A, int[]? submitted = null, bool hasDivisions = true)
    {
        Dictionary<int, Division> divisions = new()
        {
            [A]        = new Division { Id = A,        OfficeId = Office, Name = "Planning",    IsActive = true },
            [B]        = new Division { Id = B,        OfficeId = Office, Name = "Engineering", IsActive = true },
            [Inactive] = new Division { Id = Inactive, OfficeId = Office, Name = "Old",         IsActive = false },
        };
        return new AipDivisionContext(hasDivisions, head, caller, divisions, (submitted ?? []).ToHashSet());
    }

    // ── New activity tag ──────────────────────────────────────────────────────

    [Fact]
    public void NewTag_Encoder_AlwaysOwnDivision_WhateverWasRequested()
    {
        Assert.Equal((A, (string?)null), Ctx().ResolveNewActivityTag(B));
        Assert.Equal((A, (string?)null), Ctx().ResolveNewActivityTag(null));
    }

    [Fact]
    public void NewTag_Encoder_OwnDivisionSubmitted_IsRefused()
        => Assert.NotNull(Ctx(submitted: [A]).ResolveNewActivityTag(null).Refusal);

    [Fact]
    public void NewTag_EncoderWithNoDivision_IsRefused()
        => Assert.Equal(AipDivisionContext.NotAssignedMessage, Ctx(caller: null).ResolveNewActivityTag(A).Refusal);

    [Fact]
    public void NewTag_Head_MustChoose_AndMayChooseASubmittedDivision()
    {
        Assert.Equal(AipDivisionContext.ChooseDivisionMessage, Ctx(head: true, caller: null).ResolveNewActivityTag(null).Refusal);
        Assert.Equal((B, (string?)null), Ctx(head: true, submitted: [B]).ResolveNewActivityTag(B));
    }

    [Fact]
    public void NewTag_Head_InactiveOrForeignDivision_IsRefused()
    {
        Assert.Equal(AipDivisionContext.DivisionInactiveMessage, Ctx(head: true).ResolveNewActivityTag(Inactive).Refusal);
        Assert.Equal(AipDivisionContext.DivisionNotInOfficeMessage, Ctx(head: true).ResolveNewActivityTag(99).Refusal);
    }

    [Fact]
    public void NewTag_NoDivisionFlow_IsUntagged_AndIgnoresTheRequest()
        => Assert.Equal(((int?)null, (string?)null), Ctx(hasDivisions: false, head: true).ResolveNewActivityTag(B));

    [Fact]
    public void None_AllowsEverything()
    {
        AipDivisionContext none = AipDivisionContext.None;
        Assert.Null(none.RefuseActivityWrite(null));
        Assert.Null(none.RefuseContainerWrite());
        Assert.Null(none.RefuseContainerDelete([1, 2, null], "program"));
        Assert.Equal(((int?)null, (string?)null), none.ResolveNewActivityTag(5));
    }

    // ── Container delete ──────────────────────────────────────────────────────

    [Fact]
    public void ContainerDelete_Encoder_OnlyOwnOpenWork_IsAllowed()
        => Assert.Null(Ctx().RefuseContainerDelete([A, A], "program"));

    [Fact]
    public void ContainerDelete_Encoder_EmptyContainer_IsAllowed_EvenAfterSubmit()
        => Assert.Null(Ctx(submitted: [A]).RefuseContainerDelete([], "project"));

    [Theory]
    [InlineData(new[] { A, B }, "another division's")]
    [InlineData(new[] { A, 0 }, "no division")]
    public void ContainerDelete_Encoder_ForeignOrUntaggedWork_IsRefused(int[] tags, string reason)
    {
        IEnumerable<int?> divisionIds = tags.Select(t => t == 0 ? (int?)null : t);
        Assert.Contains(reason, Ctx().RefuseContainerDelete(divisionIds, "program"));
    }

    [Fact]
    public void ContainerDelete_Encoder_OwnSubmittedWork_IsRefused()
        => Assert.Equal("This program contains Planning's submitted activities.",
            Ctx(submitted: [A]).RefuseContainerDelete([A], "program"));

    [Fact]
    public void ContainerDelete_Head_Anything_IsAllowed()
        => Assert.Null(Ctx(head: true, submitted: [A, B]).RefuseContainerDelete([A, B, null], "office"));

    // ── The loader ────────────────────────────────────────────────────────────

    private sealed class LoaderWorld
    {
        public readonly Mock<IAipRepository> AipRepo = new();
        public readonly AipDivisionLockFixture Divisions = new();
        public readonly AipOffice GroupA = new() { Id = 100, AipRecordId = 9, OfficeId = Office };
        public readonly AipOffice GroupB = new() { Id = 101, AipRecordId = 9, OfficeId = 8 };
        public AipRecord Record = new() { Id = 9, FiscalYear = 2028, Status = PlanningStatus.Draft };

        public LoaderWorld()
        {
            AipRepo.Setup(r => r.GetByIntIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(() => Record);
            Divisions.AddDivision(A, Office, "Planning");
            Divisions.AddDivision(B, Office, "Engineering");
        }

        public IAipDivisionLock Lock => Divisions.Build(AipRepo.Object, new PermissionService());
    }

    private static User StaffIn(int officeId, int? division, bool reviewGrant = false, UserRole role = UserRole.Staff) => new()
    {
        Id = Guid.NewGuid(), Role = role, OfficeId = officeId, DivisionId = division,
        Office = new Office { Id = officeId, OfficeCode = "X" },
        OverrideCanReviewBudgetPlanning = reviewGrant ? true : null,
    };

    [Fact]
    public async Task Loader_Fy2027_IsNone_AndQueriesNothing()
    {
        LoaderWorld w = new() { Record = new AipRecord { Id = 9, FiscalYear = 2027 } };

        AipDivisionContext ctx = await w.Lock.LoadAsync(w.GroupA, StaffIn(Office, A));

        Assert.False(ctx.HasDivisions);
        w.Divisions.Repo.Verify(r => r.GetDivisionsByOfficeIdsAsync(
            It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Loader_OfficeWithOnlyInactiveDivisions_IsNone()
    {
        LoaderWorld w = new();
        w.Divisions.Divisions.Clear();
        w.Divisions.AddDivision(Inactive, Office, "Old", active: false);

        Assert.False((await w.Lock.LoadAsync(w.GroupA, StaffIn(Office, Inactive))).HasDivisions);
    }

    [Fact]
    public async Task Loader_ReadsTheCallersDivision_AndTheSubmittedOnes()
    {
        LoaderWorld w = new();
        w.Divisions.Submit(9, Office, B);

        AipDivisionContext ctx = await w.Lock.LoadAsync(w.GroupA, StaffIn(Office, A));

        Assert.True(ctx.HasDivisions);
        Assert.Equal(A, ctx.CallerDivisionId);
        Assert.False(ctx.IsDepartmentHead);
        Assert.True(ctx.IsSubmitted(B));
        Assert.False(ctx.IsSubmitted(A));
    }

    /// <summary>A submission of the same division in ANOTHER year's record must not lock this one.</summary>
    [Fact]
    public async Task Loader_IgnoresSubmissionsOfOtherRecords()
    {
        LoaderWorld w = new();
        w.Divisions.Submit(aipRecordId: 8, Office, A);

        Assert.False((await w.Lock.LoadAsync(w.GroupA, StaffIn(Office, A))).IsSubmitted(A));
    }

    [Fact]
    public async Task Loader_CallersDivisionFromAnotherOffice_CountsAsNone()
    {
        LoaderWorld w = new();
        w.Divisions.AddDivision(77, 8, "Elsewhere");

        Assert.Null((await w.Lock.LoadAsync(w.GroupA, StaffIn(8, 77))).CallerDivisionId);
    }

    [Theory]
    [InlineData(UserRole.Staff,      true,  true)]   // own office's review grant
    [InlineData(UserRole.Staff,      false, false)]
    [InlineData(UserRole.Admin,      false, true)]
    [InlineData(UserRole.SuperAdmin, false, true)]
    public async Task Loader_DepartmentHead_IsTheOwnOfficeReviewerOrAnAdmin(UserRole role, bool grant, bool expected)
    {
        LoaderWorld w = new();

        AipDivisionContext ctx = await w.Lock.LoadAsync(w.GroupA, StaffIn(Office, null, grant, role));

        Assert.Equal(expected, ctx.IsDepartmentHead);
    }

    [Fact]
    public async Task Loader_ReviewGrantInAnotherOffice_IsNotThisOfficesHead()
    {
        LoaderWorld w = new();

        Assert.False((await w.Lock.LoadAsync(w.GroupA, StaffIn(8, null, reviewGrant: true))).IsDepartmentHead);
    }

    /// <summary>⚠️ The tree read: two queries for any number of offices, never two per office.</summary>
    [Fact]
    public async Task Loader_ForManyOffices_QueriesOnce_AndLeavesOfficesWithoutDivisionsAlone()
    {
        LoaderWorld w = new();

        IReadOnlyDictionary<int, AipDivisionContext> all =
            await w.Lock.LoadForOfficesAsync(w.Record, [w.GroupA, w.GroupB], StaffIn(Office, A));

        Assert.True(all[w.GroupA.Id].HasDivisions);
        Assert.False(all[w.GroupB.Id].HasDivisions);
        w.Divisions.Repo.Verify(r => r.GetDivisionsByOfficeIdsAsync(
            It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Once);
        w.Divisions.Repo.Verify(r => r.GetForOfficesAsync(
            It.IsAny<int>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
