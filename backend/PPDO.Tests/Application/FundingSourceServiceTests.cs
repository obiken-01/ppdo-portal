using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="FundingSourceService"/> (RAL-70 + RAL-77): CSV upsert by code,
/// key uniqueness, soft delete, and audit log calls.
///
/// v1.8.0 (PPDO-109) adds the per-office half: the shared-plus-own-office read filter, the office
/// stamp on create, the usage guard on an office fund's delete, and the CSV round trip that must not
/// move a fund between offices.
/// </summary>
public sealed class FundingSourceServiceTests
{
    private static FundingSource Fs(int id, string code, string name, bool active = true, int? officeId = null) => new()
    {
        Id = id, Code = code, Name = name, IsActive = active, OfficeId = officeId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private const int GsoOfficeId  = 7;
    private const int PhoOfficeId  = 9;

    private static List<Office> DefaultOffices() =>
    [
        new() { Id = GsoOfficeId, OfficeCode = "GSO", OfficeName = "General Services Office", IsActive = true },
        new() { Id = PhoOfficeId, OfficeCode = "PHO", OfficeName = "Provincial Health Office", IsActive = true },
    ];

    private static (FundingSourceService sut, Mock<IRepository<FundingSource>> repo) Build(
        List<FundingSource> seed, IAuditService? audit = null,
        int wfpUsage = 0, int aipUsage = 0, List<Office>? offices = null)
    {
        Mock<IRepository<FundingSource>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<FundingSource>(), It.IsAny<CancellationToken>()))
            .Callback<FundingSource, CancellationToken>((f, _) => seed.Add(f))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<FundingSource>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IRepository<Office>> officeRepo = new();
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(offices ?? DefaultOffices());

        Mock<IWfpExpenditureRepository> wfpExp = new();
        wfpExp.Setup(r => r.CountByFundingSourceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wfpUsage);
        Mock<IAipExpenditureRepository> aipExp = new();
        aipExp.Setup(r => r.CountByFundingSourceAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aipUsage);

        return (new FundingSourceService(
            repo.Object, officeRepo.Object, wfpExp.Object, aipExp.Object,
            NullLogger<FundingSourceService>.Instance, audit ?? Mock.Of<IAuditService>()), repo);
    }

    private static (FundingSourceService sut, Mock<IRepository<FundingSource>> repo, Mock<IAuditService> audit)
        BuildWithAudit(List<FundingSource> seed)
    {
        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        (FundingSourceService sut, Mock<IRepository<FundingSource>> repo) = Build(seed, audit.Object);
        return (sut, repo, audit);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ReturnsConflict()
    {
        (FundingSourceService sut, _) = Build([Fs(1, "GF", "General Fund")]);
        ServiceResult<FundingSourceDto> result =
            await sut.CreateAsync(new UpsertFundingSourceDto("GF", "Duplicate", null));
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_New_ReturnsOk()
    {
        (FundingSourceService sut, _) = Build([Fs(1, "GF", "General Fund")]);
        ServiceResult<FundingSourceDto> result =
            await sut.CreateAsync(new UpsertFundingSourceDto("GAD", "Gender and Development", "desc"));
        Assert.True(result.IsSuccess);
        Assert.Equal("GAD", result.Value!.Code);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        FundingSource target = Fs(1, "GF", "General Fund");
        (FundingSourceService sut, _) = Build([target]);
        ServiceResult<FundingSourceDto> result = await sut.DeleteAsync(1);
        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
    }

    [Fact]
    public async Task ImportCsvAsync_UpsertByCode_CountsNewUpdatedSkipped()
    {
        List<FundingSource> seed = [Fs(1, "GF", "General Fund"), Fs(2, "GAD", "Old GAD Name")];
        (FundingSourceService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "code,name,description,color,is_active",
            "GF,General Fund,,,true",                          // unchanged → skipped
            "GAD,Gender and Development,Updated desc,,true",   // changed → updated
            "SEF,Special Education Fund,,,true");              // new → inserted

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(1, result.Value.Updated);
        Assert.Equal(1, result.Value.Skipped);
    }

    // ── audit logging (RAL-77) ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        (FundingSourceService sut, _, Mock<IAuditService> audit) = BuildWithAudit([]);

        await sut.CreateAsync(new UpsertFundingSourceDto("GF", "General Fund", null));

        audit.Verify(a => a.LogAsync(
            "funding_sources", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CallsAuditLog_CapturingOldAndNewValues()
    {
        List<FundingSource> seed = [Fs(1, "GF", "Old Name")];
        (FundingSourceService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.UpdateAsync(1, new UpsertFundingSourceDto("GF", "New Name", null));

        audit.Verify(a => a.LogAsync(
            "funding_sources", 1, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CallsAuditLog_WithDeleteAction()
    {
        List<FundingSource> seed = [Fs(1, "GF", "General Fund", active: true)];
        (FundingSourceService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "funding_sources", 1, AuditAction.Delete,
            It.IsNotNull<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Color field (new in migration AddFundingSourceColor) ──────────────────

    [Fact]
    public async Task CreateAsync_WithColor_PersistsColorOnEntity()
    {
        List<FundingSource> seed = [];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result =
            await sut.CreateAsync(new UpsertFundingSourceDto("GAD", "Gender and Development", null, "#7B61FF"));

        Assert.True(result.IsSuccess);
        Assert.Equal("#7B61FF", result.Value!.Color);
        Assert.Equal("#7B61FF", seed.Single().Color);
    }

    [Fact]
    public async Task CreateAsync_NullColor_PersistsNullOnEntity()
    {
        List<FundingSource> seed = [];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result =
            await sut.CreateAsync(new UpsertFundingSourceDto("GF", "General Fund", null, null));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Color);
        Assert.Null(seed.Single().Color);
    }

    [Fact]
    public async Task UpdateAsync_ChangesColor()
    {
        FundingSource target = Fs(1, "GAD", "GAD");
        target.Color = null;
        (FundingSourceService sut, _) = Build([target]);

        ServiceResult<FundingSourceDto> result =
            await sut.UpdateAsync(1, new UpsertFundingSourceDto("GAD", "GAD", null, "#FF6B6B"));

        Assert.True(result.IsSuccess);
        Assert.Equal("#FF6B6B", result.Value!.Color);
        Assert.Equal("#FF6B6B", target.Color);
    }

    [Fact]
    public async Task ExportCsvAsync_IncludesColorColumn()
    {
        FundingSource gad = Fs(1, "GAD", "Gender and Development");
        gad.Color = "#7B61FF";
        (FundingSourceService sut, _) = Build([gad]);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("color", csv);
        Assert.Contains("#7B61FF", csv);
    }

    [Fact]
    public async Task ImportCsvAsync_RoundtripsColorColumn()
    {
        List<FundingSource> seed = [];
        (FundingSourceService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "code,name,description,color,is_active",
            "GAD,Gender and Development,,#7B61FF,true");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal("#7B61FF", seed.Single().Color);
    }

    // ── Aliases field (new in migration AddFundingSourceAliases, RAL-157) ──────

    [Fact]
    public async Task CreateAsync_WithAliases_PersistsAliasesOnEntity()
    {
        List<FundingSource> seed = [];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result = await sut.CreateAsync(
            new UpsertFundingSourceDto("GAD", "5% GAD Fund", null, null, true,
                "Gender & Development Fund|GAD FUND|5% GAD"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Gender & Development Fund|GAD FUND|5% GAD", result.Value!.Aliases);
        Assert.Equal("Gender & Development Fund|GAD FUND|5% GAD", seed.Single().Aliases);
    }

    [Fact]
    public async Task UpdateAsync_ChangesAliases()
    {
        FundingSource target = Fs(1, "GAD", "5% GAD Fund");
        target.Aliases = null;
        (FundingSourceService sut, _) = Build([target]);

        ServiceResult<FundingSourceDto> result = await sut.UpdateAsync(1,
            new UpsertFundingSourceDto("GAD", "5% GAD Fund", null, null, true, "GAD FUND|5% GAD"));

        Assert.True(result.IsSuccess);
        Assert.Equal("GAD FUND|5% GAD", result.Value!.Aliases);
        Assert.Equal("GAD FUND|5% GAD", target.Aliases);
    }

    [Fact]
    public async Task ExportCsvAsync_IncludesAliasesColumn()
    {
        FundingSource gad = Fs(1, "GAD", "5% GAD Fund");
        gad.Aliases = "GAD FUND|5% GAD";
        (FundingSourceService sut, _) = Build([gad]);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("aliases", csv);
        Assert.Contains("GAD FUND|5% GAD", csv);
    }

    [Fact]
    public async Task ImportCsvAsync_RoundtripsAliasesColumn()
    {
        List<FundingSource> seed = [];
        (FundingSourceService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "code,name,description,color,is_active,aliases",
            "GAD,5% GAD Fund,,#7B61FF,true,Gender & Development Fund|GAD FUND|5% GAD");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal("Gender & Development Fund|GAD FUND|5% GAD", seed.Single().Aliases);
    }

    [Fact]
    public async Task ImportCsvAsync_ChangedAliasesOnly_CountsAsUpdated()
    {
        List<FundingSource> seed = [Fs(1, "GAD", "5% GAD Fund")];
        seed[0].Aliases = "GAD";
        (FundingSourceService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "code,name,description,color,is_active,aliases",
            "GAD,5% GAD Fund,,,true,GAD|GAD FUND|5% GAD");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal("GAD|GAD FUND|5% GAD", seed.Single().Aliases);
    }

    // ── PPDO-109 — per-office funds ───────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ForAnOffice_ReturnsSharedPlusOwnOnly()
    {
        List<FundingSource> seed =
        [
            Fs(1, "GF",   "General Fund"),
            Fs(2, "GAD",  "5% GAD Fund"),
            Fs(3, "GSOX", "GSO Motorpool Fund", officeId: GsoOfficeId),
            Fs(4, "PHOX", "PHO Trust Receipts", officeId: PhoOfficeId),
        ];
        (FundingSourceService sut, _) = Build(seed);

        IReadOnlyList<FundingSourceDto> rows =
            await sut.GetAllAsync(search: null, ActiveFilter.All, visibleToOfficeId: GsoOfficeId);

        Assert.Equal(["GAD", "GF", "GSOX"], rows.Select(r => r.Code).OrderBy(c => c));
        Assert.DoesNotContain("PHOX", rows.Select(r => r.Code));
    }

    [Fact]
    public async Task GetAllAsync_ForAnOffice_KeepsTheSharedFunds_NotJustItsOwn()
    {
        // The rule is shared PLUS own, never own INSTEAD OF shared — an encoder needs the General
        // Fund far more than their office's additions (D5). Stated separately from the test above
        // because getting this half wrong empties every picker in the app for a guest office.
        List<FundingSource> seed = [Fs(1, "GF", "General Fund"), Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)];
        (FundingSourceService sut, _) = Build(seed);

        IReadOnlyList<FundingSourceDto> rows =
            await sut.GetAllAsync(null, ActiveFilter.All, visibleToOfficeId: GsoOfficeId);

        Assert.Contains("GF", rows.Select(r => r.Code));
        Assert.Equal(2, rows.Count);
    }

    [Fact]
    public async Task GetAllAsync_WithNoOfficeId_ReturnsEveryOfficesFunds()
    {
        // The config manager's cross-office view — the only caller who gets it.
        List<FundingSource> seed =
        [
            Fs(1, "GF",   "General Fund"),
            Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId),
            Fs(4, "PHOX", "PHO Fund", officeId: PhoOfficeId),
        ];
        (FundingSourceService sut, _) = Build(seed);

        IReadOnlyList<FundingSourceDto> rows = await sut.GetAllAsync(null, ActiveFilter.All);

        Assert.Equal(3, rows.Count);
    }

    [Fact]
    public async Task GetAllAsync_ForAnOfficeThatOwnsNothing_ReturnsTheSharedFundsOnly()
    {
        // OfficeScope.NoOffice (0) lands here for a caller with no office: nothing owns office 0, so
        // they see the shared list and no more. The safe degradation, not full access (DECISION F).
        List<FundingSource> seed = [Fs(1, "GF", "General Fund"), Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)];
        (FundingSourceService sut, _) = Build(seed);

        IReadOnlyList<FundingSourceDto> rows = await sut.GetAllAsync(null, ActiveFilter.All, visibleToOfficeId: 0);

        Assert.Equal(["GF"], rows.Select(r => r.Code));
    }

    [Fact]
    public async Task GetAllAsync_SharedFundsSortFirst()
    {
        List<FundingSource> seed =
        [
            Fs(3, "AAA_OFFICE", "Office fund", officeId: GsoOfficeId),
            Fs(1, "ZZZ_SHARED", "Shared fund"),
        ];
        (FundingSourceService sut, _) = Build(seed);

        IReadOnlyList<FundingSourceDto> rows = await sut.GetAllAsync(null, ActiveFilter.All, GsoOfficeId);

        Assert.Equal("ZZZ_SHARED", rows[0].Code);   // shared first, despite sorting last by code
        Assert.Equal("AAA_OFFICE", rows[1].Code);
    }

    [Fact]
    public async Task GetAllAsync_LabelsTheOwningOffice_AndMarksSharedRows()
    {
        (FundingSourceService sut, _) = Build(
            [Fs(1, "GF", "General Fund"), Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)]);

        IReadOnlyList<FundingSourceDto> rows = await sut.GetAllAsync(null, ActiveFilter.All);

        FundingSourceDto shared = rows.Single(r => r.Code == "GF");
        Assert.True(shared.IsShared);
        Assert.Null(shared.OfficeCode);

        FundingSourceDto own = rows.Single(r => r.Code == "GSOX");
        Assert.False(own.IsShared);
        Assert.Equal("GSO", own.OfficeCode);
        Assert.Equal("General Services Office", own.OfficeName);
    }

    [Fact]
    public async Task GetAllAsync_CombinesTheOfficeFilterWithSearchAndStatus()
    {
        // The three filters compose — an office filter that quietly replaced the others would show a
        // department head inactive funds on an Active-only page.
        List<FundingSource> seed =
        [
            Fs(1, "GF",   "General Fund"),
            Fs(3, "GSOX", "GSO Motorpool", officeId: GsoOfficeId),
            Fs(5, "GSOY", "GSO Retired",   active: false, officeId: GsoOfficeId),
            Fs(4, "PHOX", "PHO Motorpool", officeId: PhoOfficeId),
        ];
        (FundingSourceService sut, _) = Build(seed);

        IReadOnlyList<FundingSourceDto> rows =
            await sut.GetAllAsync("motorpool", ActiveFilter.Active, visibleToOfficeId: GsoOfficeId);

        Assert.Equal(["GSOX"], rows.Select(r => r.Code));
    }

    [Fact]
    public async Task CreateAsync_WithAnOfficeId_StampsTheFundToThatOffice()
    {
        List<FundingSource> seed = [Fs(1, "GF", "General Fund")];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result = await sut.CreateAsync(
            new UpsertFundingSourceDto("GSOX", "GSO Motorpool Fund", null, OfficeId: GsoOfficeId));

        Assert.True(result.IsSuccess);
        Assert.Equal(GsoOfficeId, result.Value!.OfficeId);
        Assert.False(result.Value.IsShared);
        Assert.Equal("GSO", result.Value.OfficeCode);
        Assert.Equal(GsoOfficeId, seed.Single(f => f.Code == "GSOX").OfficeId);
    }

    [Fact]
    public async Task CreateAsync_WithNoOfficeId_CreatesASharedFund()
    {
        List<FundingSource> seed = [];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result =
            await sut.CreateAsync(new UpsertFundingSourceDto("SEF", "Special Education Fund", null));

        Assert.True(result.Value!.IsShared);
        Assert.Null(seed.Single().OfficeId);
    }

    [Fact]
    public async Task CreateAsync_WithAnUnknownOfficeId_ReturnsBadRequest()
    {
        (FundingSourceService sut, _) = Build([]);

        ServiceResult<FundingSourceDto> result = await sut.CreateAsync(
            new UpsertFundingSourceDto("GSOX", "GSO Fund", null, OfficeId: 4242));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_OfficeFundReusingASharedCode_ReturnsConflict()
    {
        // D6 — codes are globally unique, so an office cannot shadow GF with a fund of its own.
        // This is spec §3.4's "Adds a fund with the code GF → Rejected: the code is taken".
        (FundingSourceService sut, _) = Build([Fs(1, "GF", "General Fund")]);

        ServiceResult<FundingSourceDto> result = await sut.CreateAsync(
            new UpsertFundingSourceDto("gf", "GSO General Fund", null, OfficeId: GsoOfficeId));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    // ── Ownership changes (PPDO-128) ──────────────────────────────────────────
    // ↩️ These replace PPDO-109's "ownership is set once at creation" tests, which PPDO-128 reverses
    // on purpose: a config manager may now flip a fund between shared and office-owned. The handler
    // pins a department head's body to their own office, so these only ever see PPDO's choice.

    [Fact]
    public async Task UpdateAsync_SharedToOffice_NotInUse_MovesOwnership()
    {
        List<FundingSource> seed = [Fs(1, "LDRRMF", "Disaster Fund")];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result = await sut.UpdateAsync(1,
            new UpsertFundingSourceDto("LDRRMF", "Disaster Fund", null, OfficeId: GsoOfficeId));

        Assert.True(result.IsSuccess);
        Assert.Equal(GsoOfficeId, seed.Single().OfficeId);
        Assert.False(result.Value!.IsShared);
    }

    [Fact]
    public async Task UpdateAsync_SharedToOffice_InUse_ReturnsConflict_AndChangesNothing()
    {
        // ⚠️ The failure mode the guard exists for: other offices already have lines under a shared
        // fund, and limiting it to GSO would take it out of their pickers.
        List<FundingSource> seed = [Fs(1, "LDRRMF", "Disaster Fund")];
        (FundingSourceService sut, Mock<IRepository<FundingSource>> repo) = Build(seed, wfpUsage: 2, aipUsage: 1);

        ServiceResult<FundingSourceDto> result = await sut.UpdateAsync(1,
            new UpsertFundingSourceDto("LDRRMF", "Renamed", null, OfficeId: GsoOfficeId));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains("3 AIP/WFP lines", result.Error);
        Assert.Null(seed.Single().OfficeId);
        // The refused request must not half-apply — the rename in the same body is not saved either.
        Assert.Equal("Disaster Fund", seed.Single().Name);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_OfficeToAnotherOffice_InUse_ReturnsConflict()
    {
        List<FundingSource> seed = [Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)];
        (FundingSourceService sut, _) = Build(seed, aipUsage: 1);

        ServiceResult<FundingSourceDto> result = await sut.UpdateAsync(3,
            new UpsertFundingSourceDto("GSOX", "GSO Fund", null, OfficeId: PhoOfficeId));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains("1 AIP/WFP line,", result.Error);
        Assert.Equal(GsoOfficeId, seed.Single().OfficeId);
    }

    [Fact]
    public async Task UpdateAsync_OfficeToShared_InUse_Succeeds()
    {
        // Widening hides the fund from nobody, so usage never blocks it.
        List<FundingSource> seed = [Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)];
        (FundingSourceService sut, _) = Build(seed, wfpUsage: 40, aipUsage: 90);

        ServiceResult<FundingSourceDto> result = await sut.UpdateAsync(3,
            new UpsertFundingSourceDto("GSOX", "GSO Fund", null, OfficeId: null));

        Assert.True(result.IsSuccess);
        Assert.Null(seed.Single().OfficeId);
    }

    [Fact]
    public async Task UpdateAsync_UnchangedOwner_InUse_Succeeds()
    {
        // The common case — an ordinary rename of a fund in heavy use — must never meet the guard.
        List<FundingSource> seed = [Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)];
        (FundingSourceService sut, _) = Build(seed, wfpUsage: 40, aipUsage: 90);

        ServiceResult<FundingSourceDto> result = await sut.UpdateAsync(3,
            new UpsertFundingSourceDto("GSOX", "Renamed", null, OfficeId: GsoOfficeId));

        Assert.True(result.IsSuccess);
        Assert.Equal("Renamed", seed.Single().Name);
        Assert.Equal(GsoOfficeId, seed.Single().OfficeId);
    }

    [Fact]
    public async Task UpdateAsync_ToAnUnknownOffice_ReturnsBadRequest()
    {
        List<FundingSource> seed = [Fs(1, "LDRRMF", "Disaster Fund")];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result = await sut.UpdateAsync(1,
            new UpsertFundingSourceDto("LDRRMF", "Disaster Fund", null, OfficeId: 4242));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Null(seed.Single().OfficeId);
    }

    [Fact]
    public async Task DeleteAsync_WithTheGuardOn_AndTheFundInUse_ReturnsConflictNamingTheCount()
    {
        (FundingSourceService sut, _) = Build(
            [Fs(3, "GSOX", "GSO Motorpool Fund", officeId: GsoOfficeId)], wfpUsage: 2, aipUsage: 3);

        ServiceResult<FundingSourceDto> result = await sut.DeleteAsync(3, blockWhenInUse: true);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
        Assert.Contains("5 AIP/WFP lines", result.Error);   // 2 WFP + 3 AIP, reported as one count
    }

    [Fact]
    public async Task DeleteAsync_WithTheGuardOn_AndOneRowInUse_SaysLineNotLines()
    {
        (FundingSourceService sut, _) = Build(
            [Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)], wfpUsage: 1);

        ServiceResult<FundingSourceDto> result = await sut.DeleteAsync(3, blockWhenInUse: true);

        Assert.Contains("1 AIP/WFP line ", result.Error);
    }

    [Fact]
    public async Task DeleteAsync_WithTheGuardOn_AndTheFundUnused_SoftDeletes()
    {
        List<FundingSource> seed = [Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)];
        (FundingSourceService sut, _) = Build(seed);

        ServiceResult<FundingSourceDto> result = await sut.DeleteAsync(3, blockWhenInUse: true);

        Assert.True(result.IsSuccess);
        Assert.False(seed.Single().IsActive);
    }

    [Fact]
    public async Task DeleteAsync_WithTheGuardOff_StillSoftDeletesAFundInUse()
    {
        // ⚠️ A config manager keeps the unconditional soft delete. That is the whole point of soft
        // delete: retire a fund from the pickers while its history keeps resolving. Making the guard
        // unconditional would leave PPDO unable to retire any fund that was ever used — all of them.
        List<FundingSource> seed = [Fs(1, "GF", "General Fund")];
        (FundingSourceService sut, _) = Build(seed, wfpUsage: 400, aipUsage: 900);

        ServiceResult<FundingSourceDto> result = await sut.DeleteAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(seed.Single().IsActive);
    }

    [Fact]
    public async Task ExportCsvAsync_NamesTheOwningOffice()
    {
        (FundingSourceService sut, _) = Build(
            [Fs(1, "GF", "General Fund"), Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)]);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("office_code", csv);
        Assert.Contains("GSO", csv);
    }

    [Fact]
    public async Task ImportCsvAsync_DoesNotMoveAnExistingFundBetweenOffices()
    {
        // ⚠️ office_code is exported for a human to read and never imported. An export taken before a
        // department head added their fund, re-uploaded later, must not quietly make that fund
        // PPDO's — so an existing row KEEPS the office it has.
        List<FundingSource> seed = [Fs(3, "GSOX", "GSO Fund", officeId: GsoOfficeId)];
        (FundingSourceService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "code,name,description,color,is_active,aliases,office_code",
            "GSOX,Renamed By CSV,,,true,,PHO");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(GsoOfficeId, seed.Single().OfficeId);   // not PHO, and not null
        Assert.Equal("Renamed By CSV", seed.Single().Name);
    }

    [Fact]
    public async Task ImportCsvAsync_CreatesNewRowsAsShared()
    {
        List<FundingSource> seed = [];
        (FundingSourceService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "code,name,description,color,is_active,aliases,office_code",
            "SEF,Special Education Fund,,,true,,GSO");

        await sut.ImportCsvAsync(csv);

        Assert.Null(seed.Single().OfficeId);
    }
}
