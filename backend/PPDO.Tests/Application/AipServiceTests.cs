using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AipService"/> (RAL-64, RAL-93).
/// Covers preview parsing, confirm import, status transitions, and
/// — after RAL-93 — server-side scoped reads via <see cref="IAipRepository"/>.
/// All repositories and IAipXlsmParser are mocked.
/// </summary>
public sealed class AipServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static AipRecord Rec(int id, string status = "Draft") => new()
    {
        Id = id, FiscalYear = 2027, EntrySource = "Upload",
        UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = status,
    };

    private static FundingSource Fs(int id, string code) => new()
    {
        Id = id, Code = code, Name = $"Fund {code}", IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User MakeUser(Guid id, string fullName) => new()
    {
        Id = id, FullName = fullName, Username = "user",
        PasswordHash = "x", Role = UserRole.Staff,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (
        AipService sut,
        Mock<IAipRepository>           aipRepo,
        Mock<IRepository<FundingSource>> fsRepo,
        Mock<IUserRepository>           userRepo,
        Mock<IAipXlsmParser> parser,
        Mock<IAuditService>  audit,
        Mock<IRepository<AipOffice>> officeRepo,
        Mock<IWfpRepository> wfpRepo,
        Mock<IOfficeRepository> officeConfigRepo,
        Mock<IRepository<AipProgram>> programRepo,
        Mock<IRepository<AipProject>> projectRepo,
        Mock<IRepository<AipActivity>> activityRepo)
        Build(
            List<AipRecord>    aipSeed,
            List<FundingSource> fsSeed,
            List<User>?        userSeed    = null,
            List<AipOffice>?   officeSeed  = null,
            List<AipProgram>?  programSeed = null,
            List<AipProject>?  projectSeed = null,
            List<AipActivity>? actSeed     = null,
            IAipXlsmParser?    parserImpl  = null,
            IReadOnlyCollection<int>? aipIdsWithWfp = null,
            List<Office>? officeConfigSeed = null)
    {
        Mock<IAipRepository>            aipRepo  = new();
        Mock<IRepository<FundingSource>> fsRepo   = new();
        Mock<IUserRepository>            userRepo = new();
        Mock<IAipXlsmParser>  parser = new();
        Mock<IAuditService>   audit  = new();
        Mock<IRepository<AipOffice>> officeRepo = new();
        Mock<IWfpRepository> wfpRepo = new();
        HashSet<int> wfpUsage = aipIdsWithWfp is null ? [] : new HashSet<int>(aipIdsWithWfp);
        wfpRepo.Setup(r => r.AnyForAipRecordAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => wfpUsage.Contains(id));

        List<AipOffice>  officeList  = officeSeed  ?? [];
        List<AipProgram> programList = programSeed ?? [];
        List<AipProject> projectList = projectSeed ?? [];
        List<AipActivity> actList    = actSeed     ?? [];
        int nextChildId = 500;

        List<Office> officeConfigList = officeConfigSeed ?? [];
        Mock<IOfficeRepository> officeConfigRepo = new();
        officeConfigRepo.Setup(r => r.GetByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => officeConfigList.FirstOrDefault(o => o.Id == id));

        Mock<IRepository<AipProgram>> programRepo = new();
        programRepo.Setup(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .Callback<AipProgram, CancellationToken>((p, _) => { if (p.Id == 0) p.Id = nextChildId++; programList.Add(p); })
            .Returns(Task.CompletedTask);
        programRepo.Setup(r => r.DeleteAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .Callback<AipProgram, CancellationToken>((p, _) => programList.Remove(p))
            .Returns(Task.CompletedTask);
        programRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IRepository<AipProject>> projectRepo = new();
        projectRepo.Setup(r => r.AddAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()))
            .Callback<AipProject, CancellationToken>((p, _) => { if (p.Id == 0) p.Id = nextChildId++; projectList.Add(p); })
            .Returns(Task.CompletedTask);
        projectRepo.Setup(r => r.DeleteAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()))
            .Callback<AipProject, CancellationToken>((p, _) => projectList.Remove(p))
            .Returns(Task.CompletedTask);
        projectRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IRepository<AipActivity>> activityRepo = new();
        activityRepo.Setup(r => r.AddAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()))
            .Callback<AipActivity, CancellationToken>((a, _) => { if (a.Id == 0) a.Id = nextChildId++; actList.Add(a); })
            .Returns(Task.CompletedTask);
        activityRepo.Setup(r => r.DeleteAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()))
            .Callback<AipActivity, CancellationToken>((a, _) => actList.Remove(a))
            .Returns(Task.CompletedTask);
        activityRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ── AipOffice repo (RAL-178 replace-import path — delete/add top-level offices;
        // DB-level cascade handles Program/Project/Activity, so no in-memory graph mirrors it) ──
        officeRepo.Setup(r => r.DeleteAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()))
            .Callback<AipOffice, CancellationToken>((o, _) => officeList.Remove(o))
            .Returns(Task.CompletedTask);
        officeRepo.Setup(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()))
            .Callback<AipOffice, CancellationToken>((o, _) => officeList.Add(o))
            .Returns(Task.CompletedTask);
        officeRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ── AipRecord repo (IRepository<AipRecord> base + IAipRepository) ────────

        int nextAipId = 100;
        aipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(aipSeed);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = nextAipId++; aipSeed.Add(e); })
            .Returns(Task.CompletedTask);
        aipRepo.Setup(r => r.UpdateAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        aipRepo.Setup(r => r.DeleteAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        aipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // ── Scoped read methods (RAL-93) ─────────────────────────────────────────

        aipRepo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => aipSeed.FirstOrDefault(r => r.Id == id));

        // GetOfficesByAipIdAsync — scoped to one AIP record id (used by GetByIdAsync / GetSummaryByIdAsync)
        aipRepo.Setup(r => r.GetOfficesByAipIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                (IReadOnlyList<AipOffice>)officeList.Where(o => o.AipRecordId == id).ToList());

        // GetOfficesByAipIdsAsync — scoped to a set of AIP ids (used by GetAllAsync for office counts)
        aipRepo.Setup(r => r.GetOfficesByAipIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipOffice>)officeList.Where(o => ids.Contains(o.AipRecordId)).ToList());

        aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipProgram>)programList.Where(p => ids.Contains(p.OfficeId)).ToList());

        aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipProject>)projectList.Where(j => ids.Contains(j.ProgramId)).ToList());

        aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<AipActivity>)actList.Where(a => ids.Contains(a.ProjectId)).ToList());

        aipRepo.Setup(r => r.GetOfficeByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => officeList.FirstOrDefault(o => o.Id == id));

        aipRepo.Setup(r => r.GetProgramByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => programList.FirstOrDefault(p => p.Id == id));

        aipRepo.Setup(r => r.GetProjectByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => projectList.FirstOrDefault(j => j.Id == id));

        aipRepo.Setup(r => r.GetActivityByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => actList.FirstOrDefault(a => a.Id == id));

        // GetLatestByFiscalYearAsync (RAL-165) — mirrors AipRepository's real implementation:
        // the single non-Archived record for the year, ordered by Id ascending.
        aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int fy, CancellationToken _) => aipSeed
                .Where(r => r.FiscalYear == fy && r.Status != PlanningStatus.Archived)
                .OrderBy(r => r.Id)
                .FirstOrDefault());

        // ── Config repos ──────────────────────────────────────────────────────────

        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fsSeed);
        List<User> userList = userSeed ?? [];
        userRepo.Setup(r => r.GetNamesByIdsAsync(It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Guid> ids, CancellationToken _) =>
                (IReadOnlyDictionary<Guid, string>)userList
                    .Where(u => ids.Contains(u.Id))
                    .ToDictionary(u => u.Id, u => u.FullName));

        // ── Audit ─────────────────────────────────────────────────────────────────

        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        if (parserImpl is not null)
            parser.Setup(p => p.Parse(It.IsAny<Stream>())).Returns(parserImpl.Parse);

        CallerContext ctx = new();
        ctx.SetUserId(UserId);

        AipService sut = new(
            aipRepo.Object, fsRepo.Object, userRepo.Object,
            parser.Object, audit.Object, ctx, officeRepo.Object, wfpRepo.Object,
            officeConfigRepo.Object, programRepo.Object, projectRepo.Object, activityRepo.Object);

        return (sut, aipRepo, fsRepo, userRepo, parser, audit, officeRepo, wfpRepo,
            officeConfigRepo, programRepo, projectRepo, activityRepo);
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_PopulatesOfficeCount_FromAipOfficesTable()
    {
        Guid uploaderId = Guid.NewGuid();
        AipRecord rec = new() { Id = 10, FiscalYear = 2027, EntrySource = "Upload",
            UploadedById = uploaderId, UploadedAt = DateTime.UtcNow, Status = "Draft" };

        List<AipOffice> offices =
        [
            new() { Id = 1, AipRecordId = 10, RefCode = "A", Name = "Off1", Sector = "GENERAL" },
            new() { Id = 2, AipRecordId = 10, RefCode = "B", Name = "Off2", Sector = "SOCIAL" },
        ];

        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null);

        Assert.Single(result);
        Assert.Equal(2, result[0].OfficeCount);
    }

    [Fact]
    public async Task GetAll_PopulatesUploadedByName_FromUsersTable()
    {
        Guid uploaderId = Guid.NewGuid();
        AipRecord rec = new() { Id = 11, FiscalYear = 2027, EntrySource = "Upload",
            UploadedById = uploaderId, UploadedAt = DateTime.UtcNow, Status = "Draft" };

        List<User> users = [ MakeUser(uploaderId, "Ralph Alcaide") ];

        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], userSeed: users);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null);

        Assert.Single(result);
        Assert.Equal("Ralph Alcaide", result[0].UploadedByName);
    }

    [Fact]
    public async Task GetAll_UnknownUploader_ReturnsNullUploadedByName()
    {
        AipRecord rec = new() { Id = 12, FiscalYear = 2027, EntrySource = "Upload",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Draft" };

        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null);

        Assert.Single(result);
        Assert.Null(result[0].UploadedByName);
    }

    [Fact]
    public async Task GetAll_FiltersByFiscalYear()
    {
        List<AipRecord> seed =
        [
            new() { Id = 1, FiscalYear = 2027, EntrySource = "Upload",
                UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Draft" },
            new() { Id = 2, FiscalYear = 2026, EntrySource = "Upload",
                UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Final" },
        ];

        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build(seed, []);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(2027, null);

        Assert.Single(result);
        Assert.Equal(2027, result[0].FiscalYear);
    }

    // ── RAL-93: scoped query verification ────────────────────────────────────

    [Fact]
    public async Task GetById_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(5);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        await sut.GetByIdAsync(5, CancellationToken.None);

        // Scoped lookup must be called; full-table scan must NOT.
        aipRepo.Verify(r => r.GetByIntIdAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_UsesGetOfficesByAipIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(7);
        List<AipOffice> offices = [new() { Id = 1, AipRecordId = 7, RefCode = "X", Name = "O", Sector = "GENERAL" }];
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        await sut.GetByIdAsync(7, CancellationToken.None);

        aipRepo.Verify(r => r.GetOfficesByAipIdAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetById_WfpBuiltFromRecord_HasWfpUsageIsTrue()
    {
        AipRecord rec = Rec(8);
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], aipIdsWithWfp: [8]);

        ServiceResult<AipRecordDetailDto> result = await sut.GetByIdAsync(8, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.HasWfpUsage);
    }

    [Fact]
    public async Task GetById_NoWfpBuiltFromRecord_HasWfpUsageIsFalse()
    {
        AipRecord rec = Rec(9);
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordDetailDto> result = await sut.GetByIdAsync(9, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasWfpUsage);
    }

    [Fact]
    public async Task GetSummaryById_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(9);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        await sut.GetSummaryByIdAsync(9, CancellationToken.None);

        aipRepo.Verify(r => r.GetByIntIdAsync(9, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Finalize_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(3, PlanningStatus.Draft);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        await sut.FinalizeAsync(3, CancellationToken.None);

        aipRepo.Verify(r => r.GetByIntIdAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unlock_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(4, PlanningStatus.Final);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        await sut.UnlockAsync(4, CancellationToken.None);

        aipRepo.Verify(r => r.GetByIntIdAsync(4, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetAll_OfficeCountsUseScopedQuery_NotFullTableScan()
    {
        // Two AIP records: office count should be scoped to these two ids only.
        List<AipRecord> recs = [Rec(10), Rec(11)];
        List<AipOffice> allOffices =
        [
            new() { Id = 1, AipRecordId = 10, RefCode = "A", Name = "O1", Sector = "G" },
            new() { Id = 2, AipRecordId = 11, RefCode = "B", Name = "O2", Sector = "G" },
        ];
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build(recs, [], officeSeed: allOffices);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null);

        // GetOfficesByAipIdsAsync must be called; GetAllAsync on offices (old pattern) must NOT.
        aipRepo.Verify(r => r.GetOfficesByAipIdsAsync(
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(1, result[0].OfficeCount);
        Assert.Equal(1, result[1].OfficeCount);
    }

    // ── ParsePreviewAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ParsePreview_DetectsHierarchyLevels_ReturnsCounts()
    {
        ParsedAipActivity act1 = new("A-B-C-D-1-1-1-1", "Act 1", null, null, null, null, null, "GF",
            1000m, 2000m, null, 3000m, null, null, null);
        ParsedAipActivity act2 = new("A-B-C-D-1-1-1-2", "Act 2", null, null, null, null, null, null,
            null, null, null, null, null, null, null);
        ParsedAipProject proj  = new("A-B-C-D-1-1-1", "Project 1", [act1, act2]);
        ParsedAipProgram prog  = new("A-B-C-D-1-1", "Program 1", [proj]);
        ParsedAipOffice  off   = new("A-B-C-D-1", "Office 1", "GENERAL", [prog]);

        var (sut, _, _, _, parser, _, _, _, _, _, _, _) = Build([], []);
        parser.Setup(p => p.Parse(It.IsAny<Stream>()))
            .Returns(new Dictionary<string, List<ParsedAipOffice>>
                { ["GENERAL"] = [off] });

        using MemoryStream ms = new();
        ServiceResult<AipImportPreviewDto> result =
            await sut.ParsePreviewAsync(ms, 2027, [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Counts.Offices);
        Assert.Equal(1, result.Value.Counts.Programs);
        Assert.Equal(1, result.Value.Counts.Projects);
        Assert.Equal(2, result.Value.Counts.Activities);
    }

    [Fact]
    public async Task ParsePreview_MatchedFundingSource_AddsNoWarning()
    {
        ParsedAipActivity act = new("A-B-C-D-1-1-1-1", "Act 1", null, null, null, null, null, "GF",
            null, null, null, null, null, null, null);
        ParsedAipProject proj = new("A-B-C-D-1-1-1", "P", [act]);
        ParsedAipProgram prog = new("A-B-C-D-1-1", "Prog", [proj]);
        ParsedAipOffice  off  = new("A-B-C-D-1", "Office", "GENERAL", [prog]);

        var (sut, _, _, _, parser, _, _, _, _, _, _, _) = Build([], []);
        parser.Setup(p => p.Parse(It.IsAny<Stream>()))
            .Returns(new Dictionary<string, List<ParsedAipOffice>> { ["GENERAL"] = [off] });

        using MemoryStream ms = new();
        ServiceResult<AipImportPreviewDto> result =
            await sut.ParsePreviewAsync(ms, 2027, [Fs(1, "GF")], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Warnings);
    }

    [Fact]
    public async Task ParsePreview_UnmatchedFundingSource_AddsWarning()
    {
        ParsedAipActivity act = new("A-B-C-D-1-1-1-1", "Act 1", null, null, null, null, null, "UNKNOWN",
            null, null, null, null, null, null, null);
        ParsedAipProject proj = new("A-B-C-D-1-1-1", "P", [act]);
        ParsedAipProgram prog = new("A-B-C-D-1-1", "Prog", [proj]);
        ParsedAipOffice  off  = new("A-B-C-D-1", "Office", "GENERAL", [prog]);

        var (sut, _, _, _, parser, _, _, _, _, _, _, _) = Build([], []);
        parser.Setup(p => p.Parse(It.IsAny<Stream>()))
            .Returns(new Dictionary<string, List<ParsedAipOffice>> { ["GENERAL"] = [off] });

        using MemoryStream ms = new();
        ServiceResult<AipImportPreviewDto> result =
            await sut.ParsePreviewAsync(ms, 2027, [Fs(1, "GF")], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.Warnings);
    }

    [Fact]
    public async Task ParsePreview_ProgramLineItem_EchoedInDto_AndCountedAsProjectAndActivity()
    {
        ParsedAipActivity lineItem = new("A-B-C-D-1-1", "Program 1", null, null, null, null, null, "GF",
            50000m, null, null, 50000m, null, null, null);
        ParsedAipProgram prog = new("A-B-C-D-1-1", "Program 1", [], lineItem);
        ParsedAipOffice  off  = new("A-B-C-D-1", "Office 1", "GENERAL", [prog]);

        var (sut, _, _, _, parser, _, _, _, _, _, _, _) = Build([], []);
        parser.Setup(p => p.Parse(It.IsAny<Stream>()))
            .Returns(new Dictionary<string, List<ParsedAipOffice>> { ["GENERAL"] = [off] });

        using MemoryStream ms = new();
        ServiceResult<AipImportPreviewDto> result =
            await sut.ParsePreviewAsync(ms, 2027, [Fs(1, "GF")], CancellationToken.None);

        Assert.True(result.IsSuccess);
        ParsedAipProgramDto progDto = result.Value!.SectorOffices["GENERAL"][0].Programs[0];
        Assert.NotNull(progDto.LineItem);
        Assert.Equal(50000m, progDto.LineItem!.Total);
        // The synthetic project + activity it will become at confirm time count toward the totals.
        Assert.Equal(1, result.Value.Counts.Projects);
        Assert.Equal(1, result.Value.Counts.Activities);
    }

    // ── ConfirmImportAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmImport_SetsEntrySourceUpload()
    {
        AipRecord? created = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = 1; created = e; })
            .Returns(Task.CompletedTask);

        AipImportConfirmDto dto = new(2027, "test.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>());

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(created);
        Assert.Equal("Upload", created!.EntrySource);
        Assert.Equal("test.xlsm", created.OriginalFilename);
    }

    [Fact]
    public async Task ConfirmImport_PersistsAllFourHierarchyLevels()
    {
        AipRecord? insertedGraph = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = 100; insertedGraph = e; })
            .Returns(Task.CompletedTask);

        var dto = new AipImportConfirmDto(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>
            {
                ["GENERAL"] =
                [
                    new ParsedAipOfficeDto("A-B-C-D-1", "Office 1", "GENERAL",
                    [
                        new ParsedAipProgramDto("A-B-C-D-1-1", "Program 1",
                        [
                            new ParsedAipProjectDto("A-B-C-D-1-1-1", "Project 1",
                            [
                                new ParsedAipActivityDto("A-B-C-D-1-1-1-1", "Activity 1",
                                    null, null, null, null, null, "GF",
                                    1000m, 2000m, null, 3000m, null, null, null),
                            ]),
                        ]),
                    ]),
                ],
            });

        await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(insertedGraph);
        Assert.Single(insertedGraph!.Offices);
        AipOffice office = insertedGraph.Offices.First();
        Assert.Single(office.Programs);
        AipProgram program = office.Programs.First();
        // Function band is required going forward — new programs default to Core at import
        // time rather than being left unset (AipService.ConfirmImportAsync).
        Assert.Equal("CORE", program.FunctionBand);
        Assert.Single(program.Projects);
        AipProject project = program.Projects.First();
        Assert.Single(project.Activities);
    }

    [Fact]
    public async Task ConfirmImport_SetsActivityFundingSourceSnapshot_WhenCodeMatches()
    {
        AipRecord? insertedGraph = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(7, "GF")]);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = 100; insertedGraph = e; })
            .Returns(Task.CompletedTask);

        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>
            {
                ["GENERAL"] =
                [
                    new ParsedAipOfficeDto("A-B-C-D-1", "O", "GENERAL",
                    [
                        new ParsedAipProgramDto("A-B-C-D-1-1", "Prog",
                        [
                            new ParsedAipProjectDto("A-B-C-D-1-1-1", "Proj",
                            [
                                new ParsedAipActivityDto("A-B-C-D-1-1-1-1", "Act",
                                    null, null, null, null, null, "GF",
                                    null, null, null, null, null, null, null),
                            ]),
                        ]),
                    ]),
                ],
            });

        await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        AipActivity act = insertedGraph!.Offices.First().Programs.First().Projects.First().Activities.First();
        Assert.Equal(7, act.FundingSourceId);
        Assert.Equal("GF", act.FundingSourceSnapshot);
    }

    // ── Program/project-level line items (RAL-108) ───────────────────────────

    [Fact]
    public async Task ConfirmImport_ProgramLineItem_MaterializesSyntheticProjectAndActivity()
    {
        AipRecord? insertedGraph = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = 100; insertedGraph = e; })
            .Returns(Task.CompletedTask);

        ParsedAipActivityDto lineItem = new(
            "1000-000-1-01-011-004", "DISASTER RESILIENT HUMAN RIGHTS AND JUSTICE PROGRAM",
            "ID", "PLO", "January", "December", "Human rights protected", "GF",
            50000m, null, null, 50000m, null, null, null);

        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>
            {
                ["GENERAL"] =
                [
                    new ParsedAipOfficeDto("1000-000-1-01-011", "Provincial Legal Office", "GENERAL",
                    [
                        new ParsedAipProgramDto("1000-000-1-01-011-004",
                            "DISASTER RESILIENT HUMAN RIGHTS AND JUSTICE PROGRAM",
                            [], lineItem),
                    ]),
                ],
            });

        await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        AipProgram program = insertedGraph!.Offices.First().Programs.First();
        AipProject syntheticProject = Assert.Single(program.Projects);
        Assert.True(syntheticProject.IsSynthetic);
        Assert.Equal(program.RefCode, syntheticProject.RefCode);

        AipActivity syntheticActivity = Assert.Single(syntheticProject.Activities);
        Assert.True(syntheticActivity.IsSynthetic);
        Assert.Equal("1000-000-1-01-011-004", syntheticActivity.RefCode);
        Assert.Equal(50000m, syntheticActivity.Ps);
        Assert.Equal(50000m, syntheticActivity.Total);
        Assert.Equal(1, syntheticActivity.FundingSourceId);
        Assert.Equal("GF", syntheticActivity.FundingSourceSnapshot);
    }

    [Fact]
    public async Task ConfirmImport_ProjectLineItem_MaterializesSyntheticActivity_AlongsideRealActivities()
    {
        AipRecord? insertedGraph = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = 100; insertedGraph = e; })
            .Returns(Task.CompletedTask);

        ParsedAipActivityDto realActivity = new(
            "A-B-C-D-1-1-1-1", "Real activity", null, null, null, null, null, null,
            null, null, null, null, null, null, null);
        ParsedAipActivityDto lineItem = new(
            "A-B-C-D-1-1-1", "Project with its own line item", null, null, null, null, null, "GF",
            null, 25000m, null, 25000m, null, null, null);

        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>
            {
                ["SOCIAL"] =
                [
                    new ParsedAipOfficeDto("A-B-C-D-1", "Office", "SOCIAL",
                    [
                        new ParsedAipProgramDto("A-B-C-D-1-1", "Program",
                        [
                            new ParsedAipProjectDto("A-B-C-D-1-1-1", "Project with its own line item",
                                [realActivity], lineItem),
                        ]),
                    ]),
                ],
            });

        await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        AipProject project = insertedGraph!.Offices.First().Programs.First().Projects.First();
        Assert.False(project.IsSynthetic);
        Assert.Equal(2, project.Activities.Count);

        AipActivity real = project.Activities.Single(a => a.RefCode == "A-B-C-D-1-1-1-1");
        Assert.False(real.IsSynthetic);

        AipActivity synthetic = project.Activities.Single(a => a.RefCode == "A-B-C-D-1-1-1");
        Assert.True(synthetic.IsSynthetic);
        Assert.Equal(25000m, synthetic.Mooe);
        Assert.Equal(25000m, synthetic.Total);
    }

    [Fact]
    public async Task ConfirmImport_NoLineItem_NoSyntheticNodesCreated()
    {
        AipRecord? insertedGraph = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = 100; insertedGraph = e; })
            .Returns(Task.CompletedTask);

        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>
            {
                ["GENERAL"] =
                [
                    new ParsedAipOfficeDto("A-B-C-D-1", "Office 1", "GENERAL",
                    [
                        new ParsedAipProgramDto("A-B-C-D-1-1", "Program 1",
                        [
                            new ParsedAipProjectDto("A-B-C-D-1-1-1", "Project 1",
                            [
                                new ParsedAipActivityDto("A-B-C-D-1-1-1-1", "Activity 1",
                                    null, null, null, null, null, "GF",
                                    1000m, 2000m, null, 3000m, null, null, null),
                            ]),
                        ]),
                    ]),
                ],
            });

        await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        AipProgram program = insertedGraph!.Offices.First().Programs.First();
        Assert.Single(program.Projects);
        Assert.All(program.Projects, p => Assert.False(p.IsSynthetic));
        Assert.All(program.Projects.SelectMany(p => p.Activities), a => Assert.False(a.IsSynthetic));
    }

    // ── Re-upload into an existing record (RAL-178) ──────────────────────────

    [Fact]
    public async Task ConfirmImport_TargetRecordId_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>(), TargetRecordId: 999);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task ConfirmImport_TargetRecordId_NotDraft_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>(), TargetRecordId: 1);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Final", result.Error);
    }

    [Fact]
    public async Task ConfirmImport_TargetRecordId_ManualEntrySource_ReturnsBadRequest()
    {
        AipRecord manual = new()
        {
            Id = 1, FiscalYear = 2027, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([manual], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>(), TargetRecordId: 1);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("manual entry", result.Error);
    }

    [Fact]
    public async Task ConfirmImport_TargetRecordId_WfpBuiltFromRecord_ReturnsBadRequest()
    {
        AipRecord target = Rec(1, PlanningStatus.Draft);
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _) =
            Build([target], [Fs(1, "GF")], aipIdsWithWfp: [1]);
        AipImportConfirmDto dto = new(2027, "aip-corrected.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>(), TargetRecordId: 1);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Work Financial Plan", result.Error);
        // Must reject before touching the hierarchy — no offices deleted.
        officeRepo.Verify(r => r.DeleteAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmImport_TargetRecordId_DoesNotTriggerDuplicateYearGuard()
    {
        // The target record itself is the "conflict" GetLatestByFiscalYearAsync would find —
        // the replace path must bypass that guard entirely, not reject itself.
        AipRecord target = Rec(1, PlanningStatus.Draft);
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([target], [Fs(1, "GF")]);
        AipImportConfirmDto dto = new(2027, "aip-corrected.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>(), TargetRecordId: 1);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ConfirmImport_TargetRecordId_ReplacesHierarchy_PreservesIdentity()
    {
        AipRecord target = new()
        {
            Id = 1, FiscalYear = 2027, EntrySource = "Upload",
            OriginalFilename = "original.xlsm",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow.AddDays(-5),
            Status = PlanningStatus.Draft,
        };
        List<AipOffice> existingOffices =
        [
            new() { Id = 50, AipRecordId = 1, RefCode = "OLD-1", Name = "Old Office", Sector = "GENERAL" },
        ];
        var (sut, aipRepo, _, _, _, _, officeRepo, _, _, _, _, _) =
            Build([target], [Fs(1, "GF")], officeSeed: existingOffices);

        ParsedAipActivityDto act = new("A-B-C-D-1-1-1-1", "Activity", null, null, null, null, null, "GF",
            1000m, null, null, 1000m, null, null, null);
        AipImportConfirmDto dto = new(2027, "aip-corrected.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>
            {
                ["GENERAL"] =
                [
                    new ParsedAipOfficeDto("A-B-C-D-1", "New Office", "GENERAL",
                    [
                        new ParsedAipProgramDto("A-B-C-D-1-1", "Program",
                        [
                            new ParsedAipProjectDto("A-B-C-D-1-1-1", "Project", [act]),
                        ]),
                    ]),
                ],
            }, TargetRecordId: 1);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Id);
        Assert.Equal("aip-corrected.xlsm", result.Value.OriginalFilename);

        // The old office was deleted (DB cascade would remove its Program/Project/Activity
        // children — not re-verifiable against a mock, but the top-level delete is).
        officeRepo.Verify(r => r.DeleteAsync(
            It.Is<AipOffice>(o => o.Id == 50), It.IsAny<CancellationToken>()), Times.Once);

        // No new AipRecord was created — same Id, same target list.
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(1, target.Id);
        Assert.Equal("New Office", target.Offices.Single().Name);
    }

    [Fact]
    public async Task ConfirmImport_DuplicateDraftYear_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>());

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("FY 2027", result.Error);
    }

    [Fact]
    public async Task ConfirmImport_DuplicateFinalYear_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>());

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("FY 2027", result.Error);
    }

    [Fact]
    public async Task ConfirmImport_OnlyArchivedForYear_Succeeds()
    {
        AipRecord archived = Rec(1, PlanningStatus.Archived);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([archived], []);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>());

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── Status transitions ────────────────────────────────────────────────────

    [Fact]
    public async Task Finalize_Draft_TransitionsToFinal()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Final, result.Value!.Status);
        Assert.Equal(PlanningStatus.Final, rec.Status);
    }

    [Fact]
    public async Task Finalize_AlreadyFinal_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Unlock_Final_TransitionsToDraft()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordDto> result = await sut.UnlockAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Draft, result.Value!.Status);
        Assert.Equal(PlanningStatus.Draft, rec.Status);
    }

    // ── PurgeAllAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeAll_DeletesAllAipRecords_ReturnsCount()
    {
        List<AipRecord> seed = [Rec(1, PlanningStatus.Draft), Rec(2, PlanningStatus.Final)];
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build(seed, []);

        int count = await sut.PurgeAllAsync(CancellationToken.None);

        Assert.Equal(2, count);
        aipRepo.Verify(r => r.DeleteAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── GetSummaryByIdAsync (RAL-89) ──────────────────────────────────────────

    [Fact]
    public async Task GetSummaryById_MissingId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(99, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetSummaryById_ExistingId_ReturnsOkWithCorrectFiscalYear()
    {
        AipRecord rec = Rec(5);
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(5, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value!.Id);
        Assert.Equal(2027, result.Value.FiscalYear);
    }

    [Fact]
    public async Task GetSummaryById_MapsHierarchy_OfficeProgramProjectActivity()
    {
        AipRecord rec = Rec(10);

        AipOffice office = new() { Id = 201, AipRecordId = 10, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        AipProgram prog  = new() { Id = 301, OfficeId = 201, RefCode = "1000-000-1-01-010-001", Name = "Program A" };
        AipProject proj  = new() { Id = 401, ProgramId = 301, RefCode = "1000-000-1-01-010-001-001", Name = "Project X" };
        AipActivity act  = new()
        {
            Id = 501, ProjectId = 401, RefCode = "1000-000-1-01-010-001-001-001",
            Name = "Activity Z", Total = 500m, FundingSourceId = 3,
            FundingSourceSnapshot = "GF",
            EsreCode = "E01", ImplementingOffice = "PPDO",
            StartDate = "Jan", EndDate = "Dec",
            ExpectedOutputs = "Some output",
        };

        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [rec], [],
            officeSeed:  [office],
            programSeed: [prog],
            projectSeed: [proj],
            actSeed:     [act]);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(10, CancellationToken.None);

        Assert.True(result.IsSuccess);
        AipOfficeSummaryDto  offDto  = Assert.Single(result.Value!.Offices);
        AipProgramSummaryDto progDto = Assert.Single(offDto.Programs);
        AipProjectSummaryDto projDto = Assert.Single(progDto.Projects);
        AipActivitySummaryDto actDto = Assert.Single(projDto.Activities);

        Assert.Equal("1000-000-1-01-010", offDto.RefCode);
        Assert.Equal("GENERAL", offDto.Sector);
        Assert.Equal("Program A", progDto.Name);
        Assert.Equal("Project X", projDto.Name);
        Assert.Equal(501, actDto.Id);
        Assert.Equal("Activity Z", actDto.Name);
        Assert.Equal(500m, actDto.Total);
        Assert.Equal("GF", actDto.FundingSourceSnapshot);
        Assert.Equal(3, actDto.FundingSourceId);
    }

    [Fact]
    public async Task GetSummaryById_ActivitySummary_OmitsHierarchyForeignKeys()
    {
        AipRecord rec    = Rec(20);
        AipOffice office = new() { Id = 202, AipRecordId = 20, RefCode = "A", Name = "Office", Sector = "SOCIAL" };
        AipProgram prog  = new() { Id = 302, OfficeId = 202, RefCode = "B", Name = "Prog" };
        AipProject proj  = new() { Id = 402, ProgramId = 302, RefCode = "C", Name = "Proj" };
        AipActivity act  = new()
        {
            Id = 502, ProjectId = 402, RefCode = "D", Name = "Act",
            Ps = 100m, Mooe = 200m, Co = 50m, Total = 350m,
            FundingSourceId = 2, FundingSourceSnapshot = "20DF",
        };

        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [rec], [],
            officeSeed:  [office],
            programSeed: [prog],
            projectSeed: [proj],
            actSeed:     [act]);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(20, CancellationToken.None);

        Assert.True(result.IsSuccess);
        AipActivitySummaryDto dto = result.Value!.Offices[0].Programs[0].Projects[0].Activities[0];
        Assert.Equal(100m, dto.Ps);
        Assert.Equal(200m, dto.Mooe);
        Assert.Equal(50m,  dto.Co);
        Assert.Equal(350m, dto.Total);
        Assert.Equal(2,    dto.FundingSourceId);
    }

    // ── UpdateProgramFunctionBandAsync (v1.4 Q1) ─────────────────────────────

    [Fact]
    public async Task UpdateProgramFunctionBand_ValidValue_PersistsCanonicalizedValue()
    {
        AipProgram prog = new() { Id = 301, OfficeId = 201, RefCode = "P", Name = "Prog" };
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], programSeed: [prog]);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(301, "core", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("CORE", result.Value!.FunctionBand);
        Assert.Equal("CORE", prog.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgramFunctionBand_NullOrEmpty_ReturnsBadRequest()
    {
        // Function band is required (v1.4 follow-up) — clearing it back to null/empty is no
        // longer a valid operation; the existing value is left untouched.
        AipProgram prog = new() { Id = 302, OfficeId = 201, RefCode = "P", Name = "Prog", FunctionBand = "SUPPORT" };
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], programSeed: [prog]);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(302, "", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("SUPPORT", prog.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgramFunctionBand_InvalidValue_ReturnsBadRequest()
    {
        AipProgram prog = new() { Id = 303, OfficeId = 201, RefCode = "P", Name = "Prog" };
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], programSeed: [prog]);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(303, "BOGUS", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Null(prog.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgramFunctionBand_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(999, "CORE", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── UpdateActivityIsCreationAsync (v1.4 Q2) ──────────────────────────────

    [Fact]
    public async Task UpdateActivityIsCreation_True_Persists()
    {
        AipActivity act = new() { Id = 501, ProjectId = 401, RefCode = "A", Name = "Act" };
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], actSeed: [act]);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(501, true, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsCreation);
        Assert.True(act.IsCreation);
    }

    [Fact]
    public async Task UpdateActivityIsCreation_False_Persists()
    {
        AipActivity act = new() { Id = 502, ProjectId = 401, RefCode = "A", Name = "Act", IsCreation = true };
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], actSeed: [act]);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(502, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsCreation);
        Assert.False(act.IsCreation);
    }

    [Fact]
    public async Task UpdateActivityIsCreation_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(999, true, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Scoped-query regression guards (RAL-165 — perf audit Tier 1) ──────────

    [Fact]
    public async Task GetAll_UsesScopedUserNameLookup_NeverFullUsersTableLoad()
    {
        Guid uploaderId = Guid.NewGuid();
        AipRecord rec = Rec(10);
        rec.UploadedById = uploaderId;
        User uploader = MakeUser(uploaderId, "Jane Uploader");

        var (sut, _, _, userRepo, _, _, _, _, _, _, _, _) = Build([rec], [], userSeed: [uploader]);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null);

        Assert.Equal("Jane Uploader", result[0].UploadedByName);
        userRepo.Verify(r => r.GetNamesByIdsAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids.Contains(uploaderId)),
            It.IsAny<CancellationToken>()), Times.Once);
        userRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmImport_UsesScopedFiscalYearLookup_NeverFullTableLoad()
    {
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>());

        await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        aipRepo.Verify(r => r.GetLatestByFiscalYearAsync(2027, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Manual entry (RAL-62) ─────────────────────────────────────────────────

    private static Office MakeOffice(int id, string name, string? officeRefCode, bool isActive = true) => new()
    {
        Id = id, OfficeCode = name[..Math.Min(4, name.Length)].ToUpperInvariant(), OfficeName = name,
        OfficeRefCode = officeRefCode, IsActive = isActive,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task CreateManualRecord_NoConflict_CreatesDraftManualRecord()
    {
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(new CreateAipRecordDto(2028), UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Manual", result.Value!.EntrySource);
        Assert.Equal("Draft", result.Value.Status);
        Assert.Equal(2028, result.Value.FiscalYear);
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateManualRecord_ActiveRecordExistsForYear_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(new CreateAipRecordDto(2027), UserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddOffice_ValidSectorAndOffice_ComputesRefCodeFromSectorPrefix()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _) =
            Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result =
            await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"));

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010", result.Value!.RefCode);
        Assert.Equal("PPDO", result.Value.Name);
        Assert.Equal("GENERAL", result.Value.Sector);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("SOCIAL",   "3000")]
    [InlineData("ECONOMIC", "8000")]
    [InlineData("OTHERS",   "9000")]
    public async Task AddOffice_EachSector_UsesItsOwnPrefix(string sector, string expectedPrefix)
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result =
            await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, sector));

        Assert.True(result.IsSuccess);
        Assert.Equal($"{expectedPrefix}-000-1-01-010", result.Value!.RefCode);
    }

    [Fact]
    public async Task AddOffice_SameOfficeDifferentSector_BothAllowed()
    {
        // A physical office can legitimately run programs under more than one sector.
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> first  = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"));
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "SOCIAL"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.RefCode, second.Value!.RefCode);
    }

    [Fact]
    public async Task AddOffice_SameOfficeSameSectorTwice_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"));
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"));

        Assert.False(second.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, second.Code);
    }

    [Fact]
    public async Task AddOffice_WithNameOverride_UsesProvidedNameNotConfigName()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "ECONOMIC", "Provincial Planning and Development Office - Special Projects"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Provincial Planning and Development Office - Special Projects", result.Value!.Name);
        Assert.Equal("8000-000-1-01-010", result.Value.RefCode);
    }

    [Fact]
    public async Task AddOffice_BlankNameOverride_FallsBackToConfigOfficeName()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "GENERAL", "   "));

        Assert.True(result.IsSuccess);
        Assert.Equal("PPDO", result.Value!.Name);
    }

    [Fact]
    public async Task AddOffice_SameRefCodeDifferentName_BothAllowed_SubOfficePattern()
    {
        // Real AIP data: "OFFICE OF THE GOVERNOR - WARDEN" and "OFFICE OF THE GOVERNOR - AKAP-HUB"
        // both appear under the SAME ref code (sub-office / program-cluster rows sharing one
        // physical office) — the guard must key off RefCode+Name, not RefCode alone.
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "Office of the Governor", "01-001")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> first  = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "SOCIAL", "Office of the Governor - Warden"));
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "SOCIAL", "Office of the Governor - AKAP-HUB"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.RefCode, second.Value!.RefCode);
        Assert.NotEqual(first.Value.Name, second.Value.Name);
    }

    [Fact]
    public async Task AddOffice_SameRefCodeSameNameTwice_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "Office of the Governor", "01-001")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "SOCIAL", "Office of the Governor - Warden"));
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "SOCIAL", "office of the governor - warden"));

        Assert.False(second.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, second.Code);
    }

    [Fact]
    public async Task AddOffice_InvalidSector_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "MADEUP"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddOffice_OfficeMissingRefCodeConfig_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", null)];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddOffice_InactiveOffice_ReturnsNotFound()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010", isActive: false)];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AddOffice_RecordNotDraft_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddProgram_FirstUnderOffice_RefCodeAppends001()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _) = Build([rec], [], officeSeed: offices);

        ServiceResult<AipProgramDto> result =
            await sut.AddProgramAsync(20, new CreateAipProgramDto("Program One", null));

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010-001", result.Value!.RefCode);
        Assert.Equal("CORE", result.Value.FunctionBand);
        programRepo.Verify(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddProgram_SecondUnderSameOffice_RefCodeIncrementsPastExisting()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-003", Name = "Existing" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices, programSeed: programs);

        ServiceResult<AipProgramDto> result =
            await sut.AddProgramAsync(20, new CreateAipProgramDto("Program Two", "STRATEGIC"));

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010-004", result.Value!.RefCode);
        Assert.Equal("STRATEGIC", result.Value.FunctionBand);
    }

    [Fact]
    public async Task AddProgram_EmptyName_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(20, new CreateAipProgramDto("  ", null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddProgram_OfficeParentRecordNotDraft_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(20, new CreateAipProgramDto("X", null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddProgram_OfficeNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(999, new CreateAipProgramDto("X", null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AddProject_FirstUnderProgram_RefCodeAppends001()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        var (sut, _, _, _, _, _, _, _, _, _, projectRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs);

        ServiceResult<AipProjectDto> result =
            await sut.AddProjectAsync(30, new CreateAipProjectDto("Project One"));

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010-001-001", result.Value!.RefCode);
        projectRepo.Verify(r => r.AddAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddProject_ProgramNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProjectDto> result = await sut.AddProjectAsync(999, new CreateAipProjectDto("X"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AddProject_AncestorRecordNotDraft_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices, programSeed: programs);

        ServiceResult<AipProjectDto> result = await sut.AddProjectAsync(30, new CreateAipProjectDto("X"));

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddActivity_ValidFields_ComputesRefCodeAndTotal()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        List<AipProject> projects = [new() { Id = 40, ProgramId = 30, RefCode = "1000-000-1-01-010-001-001", Name = "Project" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo) =
            Build([rec], [Fs(1, "GF")], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", "SS", "PPDO", "January", "December", "Outputs", "GF",
            1000m, 500m, 250m, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010-001-001-001", result.Value!.RefCode);
        Assert.Equal(1750m, result.Value.Total);
        Assert.Equal("GF", result.Value.FundingSourceSnapshot);
        Assert.False(result.Value.IsSynthetic);
        activityRepo.Verify(r => r.AddAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddActivity_AllAmountsBlank_TotalIsNull()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        List<AipProject> projects = [new() { Id = 40, ProgramId = 30, RefCode = "1000-000-1-01-010-001-001", Name = "Project" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", null, null, null, null, null, null, null, null, null, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Total);
    }

    [Fact]
    public async Task AddActivity_InvalidEsreCode_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        List<AipProject> projects = [new() { Id = 40, ProgramId = 30, RefCode = "1000-000-1-01-010-001-001", Name = "Project" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", "XX", null, null, null, null, null, null, null, null, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddActivity_UnmatchedFundingSource_StillSavesSnapshotRaw()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        List<AipProject> projects = [new() { Id = 40, ProgramId = 30, RefCode = "1000-000-1-01-010-001-001", Name = "Project" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", null, null, null, null, null, "UNKNOWN-CODE", null, null, null, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.FundingSourceId);
        Assert.Equal("UNKNOWN-CODE", result.Value.FundingSourceSnapshot);
    }

    [Fact]
    public async Task AddActivity_ProjectNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        CreateAipActivityDto dto = new("X", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(999, dto);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AddActivity_AncestorRecordNotDraft_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        List<AipProject> projects = [new() { Id = 40, ProgramId = 30, RefCode = "1000-000-1-01-010-001-001", Name = "Project" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new("X", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Delete Program / Project / Activity ───────────────────────────────────

    private static (AipRecord rec, List<AipOffice> offices, List<AipProgram> programs,
        List<AipProject> projects, List<AipActivity> activities) SeedDeleteTree(
        string recordStatus = PlanningStatus.Draft)
    {
        AipRecord rec = Rec(1, recordStatus);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        List<AipProject> projects = [new() { Id = 40, ProgramId = 30, RefCode = "1000-000-1-01-010-001-001", Name = "Project" }];
        List<AipActivity> activities = [new() { Id = 50, ProjectId = 40, RefCode = "1000-000-1-01-010-001-001-001", Name = "Activity" }];
        return (rec, offices, programs, projects, activities);
    }

    [Fact]
    public async Task DeleteProgram_ExistingDraftProgram_RemovesIt()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProgramAsync(30);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        programRepo.Verify(r => r.DeleteAsync(
            It.Is<AipProgram>(p => p.Id == 30), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(programs, p => p.Id == 30);
    }

    [Fact]
    public async Task DeleteProgram_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<bool> result = await sut.DeleteProgramAsync(999);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteProgram_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProgramAsync(30);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        programRepo.Verify(r => r.DeleteAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProject_ExistingDraftProject_RemovesIt()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, projectRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProjectAsync(40);

        Assert.True(result.IsSuccess);
        projectRepo.Verify(r => r.DeleteAsync(
            It.Is<AipProject>(p => p.Id == 40), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(projects, p => p.Id == 40);
    }

    [Fact]
    public async Task DeleteProject_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<bool> result = await sut.DeleteProjectAsync(999);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteProject_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, projectRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProjectAsync(40);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        projectRepo.Verify(r => r.DeleteAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActivity_ExistingDraftActivity_RemovesIt()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteActivityAsync(50);

        Assert.True(result.IsSuccess);
        activityRepo.Verify(r => r.DeleteAsync(
            It.Is<AipActivity>(a => a.Id == 50), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(activities, a => a.Id == 50);
    }

    [Fact]
    public async Task DeleteActivity_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<bool> result = await sut.DeleteActivityAsync(999);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteActivity_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteActivityAsync(50);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        activityRepo.Verify(r => r.DeleteAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
