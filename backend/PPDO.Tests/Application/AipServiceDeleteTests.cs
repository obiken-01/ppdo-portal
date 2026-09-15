using System.Text.Json;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// PPDO-88 — deleting a program, project or activity clears its allocation ledger rows and
/// comments, keeps a full audit snapshot, and on an entered year renumbers the later siblings
/// (<c>docs/v1.8/AIP_Entry_Layout_Spec.md</c> §2 decisions 8–12a, §4).
/// </summary>
public sealed partial class AipServiceTests
{
    private const string ProgramCode = "1000-000-1-01-010-001";

    /// <summary>One program; projects -001..-003; three activities under -001, one under -002, two under -003.</summary>
    private static (AipRecord rec, List<AipOffice> offices, List<AipProgram> programs,
        List<AipProject> projects, List<AipActivity> activities) SeedSiblingTree(int fiscalYear)
    {
        AipRecord rec = Rec(1, fiscalYear: fiscalYear);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = ProgramCode, Name = "Program" }];
        List<AipProject> projects =
        [
            new() { Id = 40, ProgramId = 30, RefCode = $"{ProgramCode}-001", Name = "Project 1" },
            new() { Id = 41, ProgramId = 30, RefCode = $"{ProgramCode}-002", Name = "Project 2" },
            new() { Id = 42, ProgramId = 30, RefCode = $"{ProgramCode}-003", Name = "Project 3" },
        ];
        List<AipActivity> activities =
        [
            new() { Id = 50, ProjectId = 40, RefCode = $"{ProgramCode}-001-001", Name = "A1" },
            new() { Id = 51, ProjectId = 40, RefCode = $"{ProgramCode}-001-002", Name = "A2" },
            new() { Id = 52, ProjectId = 40, RefCode = $"{ProgramCode}-001-003", Name = "A3" },
            new() { Id = 60, ProjectId = 41, RefCode = $"{ProgramCode}-002-001", Name = "B1" },
            new() { Id = 70, ProjectId = 42, RefCode = $"{ProgramCode}-003-001", Name = "C1" },
            new() { Id = 71, ProjectId = 42, RefCode = $"{ProgramCode}-003-002", Name = "C2" },
        ];
        return (rec, offices, programs, projects, activities);
    }

    private static AipReviewComment Comment(int id, AipCommentNodeType nodeType, int nodeId, string body = "Please revise") => new()
    {
        Id = id, AipOfficeId = 20, NodeType = nodeType, NodeId = nodeId,
        AuthorId = Guid.NewGuid(), Body = body, CreatedAt = DateTime.UtcNow,
    };

    private static bool IdsAre(IReadOnlyList<int> ids, params int[] expected)
        => ids.OrderBy(i => i).SequenceEqual(expected.OrderBy(i => i));

    // ── Renumbering ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_EnteredYearMiddleActivity_RenumbersLaterSiblings()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal($"{ProgramCode}-001-001", activities.Single(a => a.Id == 50).RefCode);
        Assert.Equal($"{ProgramCode}-001-002", activities.Single(a => a.Id == 52).RefCode);
        AipRenumberedNodeDto moved = Assert.Single(result.Value!.Renumbered);
        Assert.Equal(("Activity", 52, $"{ProgramCode}-001-002"), (moved.NodeType, moved.Id, moved.RefCode));
    }

    [Fact]
    public async Task DeleteActivity_EnteredYearLastActivity_RenumbersNothing()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(52, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Renumbered);
        Assert.Equal($"{ProgramCode}-001-002", activities.Single(a => a.Id == 51).RefCode);
    }

    [Fact]
    public async Task DeleteActivity_LegacyYearMiddleActivity_LeavesTheGap()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(LegacyFy);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Renumbered);
        Assert.Equal($"{ProgramCode}-001-003", activities.Single(a => a.Id == 52).RefCode);
    }

    [Fact]
    public async Task DeleteActivity_WhenRenumbering_ParksTheCodeBeforeSavingTheFinalOne()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);
        List<string> codeAtEachSave = [];
        activityRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => codeAtEachSave.Add(activities.Single(a => a.Id == 52).RefCode))
            .ReturnsAsync(1);

        await sut.DeleteActivityAsync(51, HostCaller());

        // Delete save, then the parked code, then the final one — never straight from -003 to -002,
        // which would let the unique index see two rows mid-shift.
        Assert.Equal(
            [$"{ProgramCode}-001-003", "~renumber-52", $"{ProgramCode}-001-002"],
            codeAtEachSave);
    }

    [Fact]
    public async Task DeleteProject_EnteredYearMiddleProject_RenumbersLaterProjectsAndTheirActivities()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProjectAsync(41, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal($"{ProgramCode}-001", projects.Single(j => j.Id == 40).RefCode);
        Assert.Equal($"{ProgramCode}-002", projects.Single(j => j.Id == 42).RefCode);
        Assert.Equal($"{ProgramCode}-002-001", activities.Single(a => a.Id == 70).RefCode);
        Assert.Equal($"{ProgramCode}-002-002", activities.Single(a => a.Id == 71).RefCode);
        Assert.Equal(1, result.Value!.RemovedActivityCount);
        Assert.Equal(
            ["Activity:70", "Activity:71", "Project:42"],
            result.Value.Renumbered.Select(r => $"{r.NodeType}:{r.Id}").OrderBy(s => s));
    }

    [Fact]
    public async Task DeleteProject_LegacyYearMiddleProject_LeavesTheGap()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(LegacyFy);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProjectAsync(41, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Renumbered);
        Assert.Equal($"{ProgramCode}-003", projects.Single(j => j.Id == 42).RefCode);
    }

    // ── Allocation ledger ─────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_ClearsTheAllocationLedgerForThatActivity()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        Mock<IAipAllocationLedgerRepository> ledger = new();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                ledgerRepo: ledger);

        await sut.DeleteActivityAsync(51, HostCaller());

        ledger.Verify(r => r.DeleteByActivityIdsAsync(
            It.Is<IReadOnlyList<int>>(ids => IdsAre(ids, 51)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteProject_ClearsTheLedgerForEveryActivityUnderIt()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        Mock<IAipAllocationLedgerRepository> ledger = new();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                ledgerRepo: ledger);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProjectAsync(40, HostCaller());

        Assert.Equal(3, result.Value!.RemovedActivityCount);
        ledger.Verify(r => r.DeleteByActivityIdsAsync(
            It.Is<IReadOnlyList<int>>(ids => IdsAre(ids, 50, 51, 52)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteProgram_ClearsTheLedgerForEveryActivityInTheSubtreeAndRenumbersNothing()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        Mock<IAipAllocationLedgerRepository> ledger = new();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                ledgerRepo: ledger);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProgramAsync(30, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Renumbered);
        ledger.Verify(r => r.DeleteByActivityIdsAsync(
            It.Is<IReadOnlyList<int>>(ids => IdsAre(ids, 50, 51, 52, 60, 70, 71)), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteOffice_ClearsTheLedgerForEveryActivityInTheOffice()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        Mock<IAipAllocationLedgerRepository> ledger = new();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                ledgerRepo: ledger);

        ServiceResult<bool> result = await sut.DeleteOfficeAsync(20, HostCaller());

        Assert.True(result.IsSuccess);
        ledger.Verify(r => r.DeleteByActivityIdsAsync(
            It.Is<IReadOnlyList<int>>(ids => IdsAre(ids, 50, 51, 52, 60, 70, 71)), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_RemovesItsCommentsAndLeavesOthers()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        List<AipReviewComment> comments =
        [
            Comment(1, AipCommentNodeType.Activity, 51),
            Comment(2, AipCommentNodeType.Activity, 51),
            Comment(3, AipCommentNodeType.Activity, 50),
        ];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                commentSeed: comments);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller());

        Assert.Equal(2, result.Value!.RemovedCommentCount);
        Assert.Equal(3, Assert.Single(comments).Id);
    }

    [Fact]
    public async Task DeleteProject_RemovesCommentsOnTheProjectAndItsActivities()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        List<AipReviewComment> comments =
        [
            Comment(1, AipCommentNodeType.Project, 40),
            Comment(2, AipCommentNodeType.Activity, 51),
            Comment(3, AipCommentNodeType.Project, 41),
        ];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                commentSeed: comments);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProjectAsync(40, HostCaller());

        Assert.Equal(2, result.Value!.RemovedCommentCount);
        Assert.Equal(3, Assert.Single(comments).Id);
    }

    // ── Audit snapshot ────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_AuditSnapshotKeepsTheRemovedLinesAndComments()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        List<AipExpenditure> lines = [new() { Id = 900, ActivityId = 51, Ps = 1234.5m }];
        List<AipReviewComment> comments = [Comment(1, AipCommentNodeType.Activity, 51, "Please re-cost this")];
        var (sut, _, _, _, _, audit, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                expSeed: lines, commentSeed: comments);

        await sut.DeleteActivityAsync(51, HostCaller());

        audit.Verify(a => a.LogAsync(
            "aip_activities", 51, AuditAction.Delete,
            It.Is<object?>(o => JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("Please re-cost this")
                             && JsonSerializer.Serialize(o, (JsonSerializerOptions?)null).Contains("1234.5")),
            null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Failure and workflow states ───────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_WhenTheRenumberLosesARace_ReturnsConflictAndLogsNoAuditRow()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        var (sut, _, _, _, _, audit, _, _, _, _, _, activityRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);
        activityRepo.SetupSequence(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1)
            .ThrowsAsync(new UniqueConstraintViolationException(
                "A unique constraint rejected this write.", "UX_aip_activities_project_id_ref_code", new Exception()));

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller());

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        audit.Verify(a => a.LogAsync(
            "aip_activities", It.IsAny<int>(), It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<object?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActivity_OfficeSubmittedToPpdo_ReturnsBadRequestAndClearsNothing()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        offices[0].WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;
        Mock<IAipAllocationLedgerRepository> ledger = new();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities,
                ledgerRepo: ledger);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(activities, a => a.Id == 51);
        ledger.Verify(r => r.DeleteByActivityIdsAsync(
            It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AipWorkflowStatus.DepartmentReview)]
    [InlineData(AipWorkflowStatus.ReturnedByPpdo)]
    public async Task DeleteActivity_OfficeStillWithTheOffice_IsAllowed(string workflowStatus)
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        offices[0].WorkflowStatus = workflowStatus;
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(activities, a => a.Id == 51);
    }
}
