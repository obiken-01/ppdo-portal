using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="LdipService"/> (RAL-64).
/// Covers: ref-code generation, status transitions, edit guards, audit log calls.
/// IRepository&lt;LdipRecord&gt; and IAuditService are mocked; no database access.
/// </summary>
public sealed class LdipServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static LdipRecord Rec(int id, string status, int fyStart = 2027) => new()
    {
        Id = id, RefCode = $"LDIP-{fyStart}-001", Title = "Test LDIP",
        FiscalYearStart = fyStart, FiscalYearEnd = fyStart + 2,
        EntryMode = "New", Status = status,
        CreatedById = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (LdipService sut, Mock<IRepository<LdipRecord>> repo, Mock<IAuditService> audit)
        Build(List<LdipRecord> seed)
    {
        Mock<IRepository<LdipRecord>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<LdipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<LdipRecord, CancellationToken>((e, _) => seed.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<LdipRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        CallerContext ctx = new();
        ctx.SetUserId(UserId);

        return (new LdipService(repo.Object, audit.Object, ctx), repo, audit);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_GeneratesRefCode_InDraftStatus()
    {
        (LdipService sut, _, _) = Build([]);
        CreateLdipDto dto = new("My LDIP", 2027, 2029, "New");

        ServiceResult<LdipRecordDto> result = await sut.CreateAsync(dto, UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Draft, result.Value!.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.RefCode));
        Assert.StartsWith("LDIP-", result.Value.RefCode);
    }

    [Fact]
    public async Task Create_CallsAuditLog_CreateAction()
    {
        (LdipService sut, _, Mock<IAuditService> audit) = Build([]);

        await sut.CreateAsync(new CreateLdipDto("T", 2027, 2029, "New"), UserId);

        audit.Verify(a => a.LogAsync(
            "ldip_records", It.IsAny<int>(), AuditAction.Create,
            null, It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_DraftRecord_Succeeds()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        (LdipService sut, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.UpdateAsync(1, new UpdateLdipDto("New Title", 2028, 2030, "Amendment"));

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
        Assert.Equal("Amendment", result.Value.EntryMode);
    }

    [Fact]
    public async Task Update_FinalRecord_ReturnsBadRequest()
    {
        (LdipService sut, _, _) = Build([Rec(1, PlanningStatus.Final)]);

        ServiceResult<LdipRecordDto> result = await sut.UpdateAsync(1, new UpdateLdipDto("X", 2027, 2029, "New"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Update_ArchivedRecord_ReturnsBadRequest()
    {
        (LdipService sut, _, _) = Build([Rec(1, PlanningStatus.Archived)]);

        ServiceResult<LdipRecordDto> result = await sut.UpdateAsync(1, new UpdateLdipDto("X", 2027, 2029, "New"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Update_MissingId_ReturnsNotFound()
    {
        (LdipService sut, _, _) = Build([]);

        ServiceResult<LdipRecordDto> result = await sut.UpdateAsync(999, new UpdateLdipDto("X", 2027, 2029, "New"));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Finalize ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Finalize_Draft_TransitionsToFinal()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        (LdipService sut, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.FinalizeAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Final, result.Value!.Status);
        Assert.Equal(PlanningStatus.Final, rec.Status);
    }

    [Fact]
    public async Task Finalize_AlreadyFinal_ReturnsBadRequest()
    {
        (LdipService sut, _, _) = Build([Rec(1, PlanningStatus.Final)]);

        ServiceResult<LdipRecordDto> result = await sut.FinalizeAsync(1);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Unlock ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unlock_Final_TransitionsToDraft()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Final);
        (LdipService sut, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.UnlockAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Draft, result.Value!.Status);
        Assert.Equal(PlanningStatus.Draft, rec.Status);
    }

    [Fact]
    public async Task Unlock_Draft_ReturnsBadRequest()
    {
        (LdipService sut, _, _) = Build([Rec(1, PlanningStatus.Draft)]);

        ServiceResult<LdipRecordDto> result = await sut.UnlockAsync(1);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Archive ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Archive_Draft_TransitionsToArchived()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        (LdipService sut, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.ArchiveAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Archived, result.Value!.Status);
    }

    [Fact]
    public async Task Archive_Final_TransitionsToArchived()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Final);
        (LdipService sut, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.ArchiveAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Archived, result.Value!.Status);
    }

    // ── PurgeAll ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeAll_DeletesAllRecords_ReturnsCount()
    {
        List<LdipRecord> seed = [Rec(1, PlanningStatus.Draft), Rec(2, PlanningStatus.Final, 2028)];
        (LdipService sut, Mock<IRepository<LdipRecord>> repo, _) = Build(seed);

        int count = await sut.PurgeAllAsync();

        Assert.Equal(2, count);
        repo.Verify(r => r.DeleteAsync(It.IsAny<LdipRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
