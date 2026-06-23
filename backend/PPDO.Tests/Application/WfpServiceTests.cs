using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="WfpService"/> (RAL-64, RAL-93).
/// Covers: create vs replace, WFP status guard, quarterly-total validation,
/// snapshot population, reserve computation, finalize/unlock transitions,
/// and — after RAL-93 — server-side scoped reads via <see cref="IWfpRepository"/>.
/// All repositories and IAuditService are mocked.
/// </summary>
public sealed class WfpServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static WfpRecord WfpRec(int id, string status, int aipId = 2, int officeId = 3) => new()
    {
        Id = id, AipRecordId = aipId, OfficeId = officeId, FiscalYear = 2027,
        Status = status, CreatedById = UserId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static WfpActivity WfpAct(int id, int wfpId) => new()
    {
        Id = id, WfpId = wfpId, AipActivityId = 99,
    };

    private static Account Acct(int id, string number, string title) => new()
    {
        Id = id, AccountNumber = number, AccountTitle = title, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static FundingSource Fs(int id, string code) => new()
    {
        Id = id, Code = code, Name = $"Fund {code}", IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static SaveWfpDto SimpleDto(
        int aipId, int officeId, int aipActivityId,
        decimal totalAppropriation = 1000m, bool applyReserve = false,
        decimal q1 = 250m, decimal q2 = 250m, decimal q3 = 250m, decimal q4 = 250m,
        int? accountId = null, int? fsId = null) => new(
        aipId, officeId, 2027,
        [new SaveWfpActivityDto(aipActivityId, [
            new SaveWfpExpenditureLineDto(
                "PS", null, null, null, null,
                accountId, totalAppropriation, applyReserve,
                q1, q2, q3, q4, fsId, 0),
        ])]);

    // ── Build ─────────────────────────────────────────────────────────────────

    private static (
        WfpService sut,
        Mock<IWfpRepository>                  wfpRepo,
        Mock<IRepository<WfpActivity>>        actRepo,
        Mock<IRepository<WfpExpenditureLine>> lineRepo,
        Mock<IAuditService>                   audit)
        Build(
            List<WfpRecord>     wfpSeed,
            List<WfpActivity>   actSeed,
            List<Account>       accountSeed,
            List<FundingSource> fsSeed)
    {
        Mock<IWfpRepository>                  wfpRepo     = new();
        Mock<IRepository<WfpActivity>>        actRepo     = new();
        Mock<IRepository<WfpExpenditureLine>> lineRepo    = new();
        Mock<IRepository<Account>>            accountRepo = new();
        Mock<IRepository<FundingSource>>      fsRepo      = new();
        Mock<IAuditService>                   audit       = new();

        // ── WfpRecord base repo (GetAllAsync / Add / Update / Delete / Save) ──

        int nextWfpId = 50;
        wfpRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(wfpSeed);
        wfpRepo.Setup(r => r.AddAsync(It.IsAny<WfpRecord>(), It.IsAny<CancellationToken>()))
            .Callback<WfpRecord, CancellationToken>((e, _) => { e.Id = nextWfpId++; wfpSeed.Add(e); })
            .Returns(Task.CompletedTask);
        wfpRepo.Setup(r => r.UpdateAsync(It.IsAny<WfpRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        wfpRepo.Setup(r => r.DeleteAsync(It.IsAny<WfpRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        wfpRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ── Scoped read methods (RAL-93) ──────────────────────────────────────

        wfpRepo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => wfpSeed.FirstOrDefault(r => r.Id == id));

        wfpRepo.Setup(r => r.GetFilteredAsync(
                It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? aipId, int? offId, CancellationToken _) =>
                (IReadOnlyList<WfpRecord>)wfpSeed
                    .Where(r => !aipId.HasValue || r.AipRecordId == aipId.Value)
                    .Where(r => !offId.HasValue  || r.OfficeId    == offId.Value)
                    .OrderByDescending(r => r.UpdatedAt)
                    .ToList());

        wfpRepo.Setup(r => r.FindByAipAndOfficeAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int aipId, int offId, CancellationToken _) =>
                wfpSeed.FirstOrDefault(r => r.AipRecordId == aipId && r.OfficeId == offId));

        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int wfpId, CancellationToken _) =>
                (IReadOnlyList<WfpActivity>)actSeed.Where(a => a.WfpId == wfpId).ToList());

        wfpRepo.Setup(r => r.GetLinesByActivityIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WfpExpenditureLine>());

        // ── WfpActivity repo (write-only: Add / Delete / Save) ───────────────

        int nextActId = 200;
        actRepo.Setup(r => r.AddAsync(It.IsAny<WfpActivity>(), It.IsAny<CancellationToken>()))
            .Callback<WfpActivity, CancellationToken>((e, _) => { e.Id = nextActId++; actSeed.Add(e); })
            .Returns(Task.CompletedTask);
        actRepo.Setup(r => r.DeleteAsync(It.IsAny<WfpActivity>(), It.IsAny<CancellationToken>()))
            .Callback<WfpActivity, CancellationToken>((e, _) => actSeed.Remove(e))
            .Returns(Task.CompletedTask);
        actRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ── WfpExpenditureLine repo (write-only: Add / Save) ─────────────────

        lineRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditureLine>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lineRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ── Config repos ──────────────────────────────────────────────────────

        accountRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(accountSeed);
        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fsSeed);

        // ── Audit ─────────────────────────────────────────────────────────────

        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        CallerContext ctx = new();
        ctx.SetUserId(UserId);

        Mock<IAipService>      aipSvc    = new();
        Mock<IOfficeService>   officeSvc = new();
        Mock<IWfpExcelService> excelSvc  = new();

        aipSvc.Setup(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipRecordDetailDto>.NotFound("AIP not found."));
        officeSvc.Setup(s => s.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<OfficeDto>.NotFound("Office not found."));
        excelSvc.Setup(s => s.GenerateWfpReport(It.IsAny<WfpExcelReportData>()))
            .Returns([1, 2, 3]);

        WfpService sut = new(wfpRepo.Object, actRepo.Object, lineRepo.Object,
            accountRepo.Object, fsRepo.Object, audit.Object, ctx,
            aipSvc.Object, officeSvc.Object, excelSvc.Object);

        return (sut, wfpRepo, actRepo, lineRepo, audit);
    }

    // ── Save — create vs replace ──────────────────────────────────────────────

    [Fact]
    public async Task Save_NewOfficeAip_CreatesWfpRecord()
    {
        var (sut, wfpRepo, _, _, _) = Build([], [], [], []);

        ServiceResult<WfpRecordDto> result = await sut.SaveAsync(
            SimpleDto(2, 3, 10), UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Draft, result.Value!.Status);
        wfpRepo.Verify(r => r.AddAsync(It.IsAny<WfpRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_ExistingDraftWfp_DeletesOldActivitiesAndCreatesNew()
    {
        WfpRecord existing = WfpRec(1, PlanningStatus.Draft, 2, 3);
        WfpActivity oldAct = WfpAct(10, 1);
        var (sut, wfpRepo, actRepo, _, _) = Build([existing], [oldAct], [], []);

        ServiceResult<WfpRecordDto> result = await sut.SaveAsync(
            SimpleDto(2, 3, 99), UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        actRepo.Verify(r => r.DeleteAsync(It.Is<WfpActivity>(a => a.Id == 10), It.IsAny<CancellationToken>()), Times.Once);
        wfpRepo.Verify(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()), Times.Once);
        actRepo.Verify(r => r.AddAsync(It.IsAny<WfpActivity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_FinalWfp_ReturnsForbidden()
    {
        WfpRecord finalRec = WfpRec(1, PlanningStatus.Final, 2, 3);
        var (sut, _, _, _, _) = Build([finalRec], [], [], []);

        ServiceResult<WfpRecordDto> result = await sut.SaveAsync(
            SimpleDto(2, 3, 10), UserId, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    // ── Save — snapshot population ────────────────────────────────────────────

    [Fact]
    public async Task Save_PopulatesAccountNumberAndTitleSnapshot()
    {
        Account acct = Acct(5, "5-01-01-010", "Salaries");
        WfpExpenditureLine? captured = null;
        var (sut, _, _, lineRepo, _) = Build([], [], [acct], []);
        lineRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditureLine>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditureLine, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await sut.SaveAsync(SimpleDto(2, 3, 10, accountId: 5), UserId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("5-01-01-010", captured!.AccountNumberSnapshot);
        Assert.Equal("Salaries", captured.AccountTitleSnapshot);
    }

    [Fact]
    public async Task Save_PopulatesFundingSourceSnapshot()
    {
        FundingSource fs = Fs(7, "GF");
        WfpExpenditureLine? captured = null;
        var (sut, _, _, lineRepo, _) = Build([], [], [], [fs]);
        lineRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditureLine>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditureLine, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await sut.SaveAsync(SimpleDto(2, 3, 10, fsId: 7), UserId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(7, captured!.FundingSourceId);
        Assert.Equal("GF", captured.FundingSourceSnapshot);
    }

    [Fact]
    public async Task Save_PopulatesFundingSourceNameSnapshot()
    {
        FundingSource fs = Fs(7, "GF");
        WfpExpenditureLine? captured = null;
        var (sut, _, _, lineRepo, _) = Build([], [], [], [fs]);
        lineRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditureLine>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditureLine, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await sut.SaveAsync(SimpleDto(2, 3, 10, fsId: 7), UserId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("Fund GF", captured!.FundingSourceNameSnapshot);
    }

    // ── Save — computed fields ────────────────────────────────────────────────

    [Fact]
    public async Task Save_ApplyReserveTrue_ComputesReserveAndNet()
    {
        WfpExpenditureLine? captured = null;
        var (sut, _, _, lineRepo, _) = Build([], [], [], []);
        lineRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditureLine>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditureLine, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        // TotalAppropriation=1000, ApplyReserve=true → reserve=100, net=900
        await sut.SaveAsync(SimpleDto(2, 3, 10,
            totalAppropriation: 1000m, applyReserve: true,
            q1: 225m, q2: 225m, q3: 225m, q4: 225m),
            UserId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(100m, captured!.ReserveAmount);
        Assert.Equal(900m, captured.NetAppropriation);
    }

    [Fact]
    public async Task Save_ApplyReserveFalse_ZeroReserve_NetEqualsTotal()
    {
        WfpExpenditureLine? captured = null;
        var (sut, _, _, lineRepo, _) = Build([], [], [], []);
        lineRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditureLine>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditureLine, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await sut.SaveAsync(SimpleDto(2, 3, 10,
            totalAppropriation: 500m, applyReserve: false,
            q1: 125m, q2: 125m, q3: 125m, q4: 125m),
            UserId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(0m, captured!.ReserveAmount);
        Assert.Equal(500m, captured.NetAppropriation);
    }

    [Fact]
    public async Task Save_ComputesQuarterlyTotal()
    {
        WfpExpenditureLine? captured = null;
        var (sut, _, _, lineRepo, _) = Build([], [], [], []);
        lineRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditureLine>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditureLine, CancellationToken>((e, _) => captured = e)
            .Returns(Task.CompletedTask);

        await sut.SaveAsync(SimpleDto(2, 3, 10,
            totalAppropriation: 1000m, applyReserve: false,
            q1: 100m, q2: 200m, q3: 300m, q4: 400m),
            UserId, CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(1000m, captured!.QuarterlyTotal);
    }

    // ── Save — quarterly-total validation ─────────────────────────────────────

    [Fact]
    public async Task Save_QuarterlyTotalExceedsNetAppropriation_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], [], [], []);

        // NetAppropriation=1000, QuarterlyTotal=1001
        ServiceResult<WfpRecordDto> result = await sut.SaveAsync(
            SimpleDto(2, 3, 10,
                totalAppropriation: 1000m, applyReserve: false,
                q1: 300m, q2: 300m, q3: 300m, q4: 101m),
            UserId, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Save_QuarterlyTotalEqualsNetAppropriation_Succeeds()
    {
        var (sut, _, _, _, _) = Build([], [], [], []);

        // NetAppropriation=1000, QuarterlyTotal=1000 → valid
        ServiceResult<WfpRecordDto> result = await sut.SaveAsync(
            SimpleDto(2, 3, 10,
                totalAppropriation: 1000m, applyReserve: false,
                q1: 250m, q2: 250m, q3: 250m, q4: 250m),
            UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── Finalize ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Finalize_Draft_SetsFinalizedAt_AndTransitionsToFinal()
    {
        WfpRecord rec = WfpRec(1, PlanningStatus.Draft);
        var (sut, _, _, _, _) = Build([rec], [], [], []);

        ServiceResult<WfpRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Final, result.Value!.Status);
        Assert.Equal(PlanningStatus.Final, rec.Status);
        Assert.NotNull(rec.FinalizedAt);
    }

    [Fact]
    public async Task Finalize_AlreadyFinal_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([WfpRec(1, PlanningStatus.Final)], [], [], []);

        ServiceResult<WfpRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Unlock ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unlock_Final_ClearsFinalizedAt_TransitionsToDraft()
    {
        WfpRecord rec = WfpRec(1, PlanningStatus.Final);
        rec.FinalizedAt = DateTime.UtcNow.AddDays(-1);
        var (sut, _, _, _, _) = Build([rec], [], [], []);

        ServiceResult<WfpRecordDto> result = await sut.UnlockAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Draft, result.Value!.Status);
        Assert.Equal(PlanningStatus.Draft, rec.Status);
        Assert.Null(rec.FinalizedAt);
    }

    [Fact]
    public async Task Unlock_Draft_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([WfpRec(1, PlanningStatus.Draft)], [], [], []);

        ServiceResult<WfpRecordDto> result = await sut.UnlockAsync(1, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── PurgeAllAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeAll_DeletesAllWfpRecords_ReturnsCount()
    {
        List<WfpRecord> seed = [WfpRec(1, PlanningStatus.Draft), WfpRec(2, PlanningStatus.Final)];
        var (sut, wfpRepo, _, _, _) = Build(seed, [], [], []);

        int count = await sut.PurgeAllAsync(CancellationToken.None);

        Assert.Equal(2, count);
        wfpRepo.Verify(r => r.DeleteAsync(It.IsAny<WfpRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── RAL-93: scoped query verification ────────────────────────────────────

    [Fact]
    public async Task GetAll_UsesGetFilteredAsync_NotGetAllAsync()
    {
        List<WfpRecord> seed = [WfpRec(1, PlanningStatus.Draft, 2, 3)];
        var (sut, wfpRepo, _, _, _) = Build(seed, [], [], []);

        IReadOnlyList<WfpRecordDto> result = await sut.GetAllAsync(2, 3, CancellationToken.None);

        Assert.Single(result);
        wfpRepo.Verify(r => r.GetFilteredAsync(2, 3, It.IsAny<CancellationToken>()), Times.Once);
        wfpRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        WfpRecord rec = WfpRec(5, PlanningStatus.Draft);
        var (sut, wfpRepo, _, _, _) = Build([rec], [], [], []);

        await sut.GetByIdAsync(5, CancellationToken.None);

        wfpRepo.Verify(r => r.GetByIntIdAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        wfpRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_UsesGetActivitiesByWfpIdAsync_NotActRepoGetAllAsync()
    {
        WfpRecord rec = WfpRec(7, PlanningStatus.Draft);
        var (sut, wfpRepo, _, _, _) = Build([rec], [], [], []);

        await sut.GetByIdAsync(7, CancellationToken.None);

        wfpRepo.Verify(r => r.GetActivitiesByWfpIdAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_ExistingCheck_UsesFindByAipAndOfficeAsync()
    {
        var (sut, wfpRepo, _, _, _) = Build([], [], [], []);

        await sut.SaveAsync(SimpleDto(2, 3, 10), UserId, CancellationToken.None);

        wfpRepo.Verify(r => r.FindByAipAndOfficeAsync(2, 3, It.IsAny<CancellationToken>()), Times.Once);
        wfpRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Finalize_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        WfpRecord rec = WfpRec(3, PlanningStatus.Draft);
        var (sut, wfpRepo, _, _, _) = Build([rec], [], [], []);

        await sut.FinalizeAsync(3, CancellationToken.None);

        wfpRepo.Verify(r => r.GetByIntIdAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        wfpRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unlock_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        WfpRecord rec = WfpRec(4, PlanningStatus.Final);
        var (sut, wfpRepo, _, _, _) = Build([rec], [], [], []);

        await sut.UnlockAsync(4, CancellationToken.None);

        wfpRepo.Verify(r => r.GetByIntIdAsync(4, It.IsAny<CancellationToken>()), Times.Once);
        wfpRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ExportReportAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExportReportAsync_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _) = Build([], [], [], []);

        ServiceResult<byte[]> result = await sut.ExportReportAsync(999, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task ExportReportAsync_AipNotFound_ReturnsNotFound()
    {
        // AIP service returns NotFound (default stub in Build()) → export propagates it
        WfpRecord rec = WfpRec(1, PlanningStatus.Draft, aipId: 2, officeId: 3);
        var (sut, _, _, _, _) = Build([rec], [], [], []);

        ServiceResult<byte[]> result = await sut.ExportReportAsync(1, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task ExportReportAsync_KnownId_DelegatesAndReturnsBytes()
    {
        WfpRecord rec = WfpRec(42, PlanningStatus.Draft, aipId: 2, officeId: 3);

        // Build a self-contained set of mocks for this inline test.
        Mock<IWfpRepository>                  wfpRepo     = new();
        Mock<IRepository<WfpActivity>>        actRepo     = new();
        Mock<IRepository<WfpExpenditureLine>> lineRepo    = new();
        Mock<IRepository<Account>>            accountRepo = new();
        Mock<IRepository<FundingSource>>      fsRepo      = new();
        Mock<IAuditService>                   audit       = new();

        wfpRepo.Setup(r => r.GetByIntIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rec);
        wfpRepo.Setup(r => r.GetActivitiesByWfpIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WfpActivity>());
        wfpRepo.Setup(r => r.GetLinesByActivityIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<WfpExpenditureLine>());
        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FundingSource>());
        accountRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Account>());

        CallerContext ctx = new(); ctx.SetUserId(UserId);

        AipRecordDetailDto aipDetail = new(2, 2027, "upload", null, Guid.NewGuid(),
            DateTime.UtcNow, "Final", null, null, []);

        Mock<IAipService> aipSvc = new();
        aipSvc.Setup(s => s.GetByIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipRecordDetailDto>.Ok(aipDetail));

        OfficeDto officeDto = new(3, "PPDO", "Provincial Planning and Development Office", null, true);
        Mock<IOfficeService> officeSvc = new();
        officeSvc.Setup(s => s.GetByIdAsync(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<OfficeDto>.Ok(officeDto));

        byte[] expectedBytes = [10, 20, 30];
        Mock<IWfpExcelService> excelSvc = new();
        excelSvc.Setup(s => s.GenerateWfpReport(It.IsAny<WfpExcelReportData>()))
            .Returns(expectedBytes);

        WfpService sut = new(wfpRepo.Object, actRepo.Object, lineRepo.Object,
            accountRepo.Object, fsRepo.Object, audit.Object, ctx,
            aipSvc.Object, officeSvc.Object, excelSvc.Object);

        ServiceResult<byte[]> result = await sut.ExportReportAsync(42, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedBytes, result.Value!);
        excelSvc.Verify(s => s.GenerateWfpReport(It.IsAny<WfpExcelReportData>()), Times.Once);
    }
}
