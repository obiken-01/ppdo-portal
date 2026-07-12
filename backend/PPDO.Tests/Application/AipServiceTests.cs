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
        Mock<IRepository<User>>        userRepo,
        Mock<IAipXlsmParser> parser,
        Mock<IAuditService>  audit)
        Build(
            List<AipRecord>    aipSeed,
            List<FundingSource> fsSeed,
            List<User>?        userSeed    = null,
            List<AipOffice>?   officeSeed  = null,
            List<AipProgram>?  programSeed = null,
            List<AipProject>?  projectSeed = null,
            List<AipActivity>? actSeed     = null,
            IAipXlsmParser?    parserImpl  = null)
    {
        Mock<IAipRepository>            aipRepo  = new();
        Mock<IRepository<FundingSource>> fsRepo   = new();
        Mock<IRepository<User>>          userRepo = new();
        Mock<IAipXlsmParser>  parser = new();
        Mock<IAuditService>   audit  = new();

        List<AipOffice>  officeList  = officeSeed  ?? [];
        List<AipProgram> programList = programSeed ?? [];
        List<AipProject> projectList = projectSeed ?? [];
        List<AipActivity> actList    = actSeed     ?? [];

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

        aipRepo.Setup(r => r.GetProgramByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => programList.FirstOrDefault(p => p.Id == id));

        aipRepo.Setup(r => r.GetActivityByIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => actList.FirstOrDefault(a => a.Id == id));

        // ── Config repos ──────────────────────────────────────────────────────────

        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fsSeed);
        userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(userSeed ?? []);

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
            parser.Object, audit.Object, ctx);

        return (sut, aipRepo, fsRepo, userRepo, parser, audit);
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

        var (sut, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

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

        var (sut, _, _, _, _, _) = Build([rec], [], userSeed: users);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null);

        Assert.Single(result);
        Assert.Equal("Ralph Alcaide", result[0].UploadedByName);
    }

    [Fact]
    public async Task GetAll_UnknownUploader_ReturnsNullUploadedByName()
    {
        AipRecord rec = new() { Id = 12, FiscalYear = 2027, EntrySource = "Upload",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Draft" };

        var (sut, _, _, _, _, _) = Build([rec], []);

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

        var (sut, _, _, _, _, _) = Build(seed, []);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(2027, null);

        Assert.Single(result);
        Assert.Equal(2027, result[0].FiscalYear);
    }

    // ── RAL-93: scoped query verification ────────────────────────────────────

    [Fact]
    public async Task GetById_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(5);
        var (sut, aipRepo, _, _, _, _) = Build([rec], []);

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
        var (sut, aipRepo, _, _, _, _) = Build([rec], [], officeSeed: offices);

        await sut.GetByIdAsync(7, CancellationToken.None);

        aipRepo.Verify(r => r.GetOfficesByAipIdAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetSummaryById_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(9);
        var (sut, aipRepo, _, _, _, _) = Build([rec], []);

        await sut.GetSummaryByIdAsync(9, CancellationToken.None);

        aipRepo.Verify(r => r.GetByIntIdAsync(9, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Finalize_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(3, PlanningStatus.Draft);
        var (sut, aipRepo, _, _, _, _) = Build([rec], []);

        await sut.FinalizeAsync(3, CancellationToken.None);

        aipRepo.Verify(r => r.GetByIntIdAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unlock_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(4, PlanningStatus.Final);
        var (sut, aipRepo, _, _, _, _) = Build([rec], []);

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
        var (sut, aipRepo, _, _, _, _) = Build(recs, [], officeSeed: allOffices);

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

        var (sut, _, _, _, parser, _) = Build([], []);
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

        var (sut, _, _, _, parser, _) = Build([], []);
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

        var (sut, _, _, _, parser, _) = Build([], []);
        parser.Setup(p => p.Parse(It.IsAny<Stream>()))
            .Returns(new Dictionary<string, List<ParsedAipOffice>> { ["GENERAL"] = [off] });

        using MemoryStream ms = new();
        ServiceResult<AipImportPreviewDto> result =
            await sut.ParsePreviewAsync(ms, 2027, [Fs(1, "GF")], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value!.Warnings);
    }

    // ── ConfirmImportAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ConfirmImport_SetsEntrySourceUpload()
    {
        AipRecord? created = null;
        var (sut, aipRepo, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        var (sut, aipRepo, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        var (sut, aipRepo, _, _, _, _) = Build([], [Fs(7, "GF")]);
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

    [Fact]
    public async Task ConfirmImport_DuplicateDraftYear_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);
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
        var (sut, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);
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
        var (sut, aipRepo, _, _, _, _) = Build([archived], []);
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
        var (sut, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Final, result.Value!.Status);
        Assert.Equal(PlanningStatus.Final, rec.Status);
    }

    [Fact]
    public async Task Finalize_AlreadyFinal_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Unlock_Final_TransitionsToDraft()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        var (sut, _, _, _, _, _) = Build([rec], []);

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
        var (sut, aipRepo, _, _, _, _) = Build(seed, []);

        int count = await sut.PurgeAllAsync(CancellationToken.None);

        Assert.Equal(2, count);
        aipRepo.Verify(r => r.DeleteAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── GetSummaryByIdAsync (RAL-89) ──────────────────────────────────────────

    [Fact]
    public async Task GetSummaryById_MissingId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(99, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetSummaryById_ExistingId_ReturnsOkWithCorrectFiscalYear()
    {
        AipRecord rec = Rec(5);
        var (sut, _, _, _, _, _) = Build([rec], []);

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

        var (sut, _, _, _, _, _) = Build(
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

        var (sut, _, _, _, _, _) = Build(
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
        var (sut, _, _, _, _, _) = Build([], [], programSeed: [prog]);

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
        var (sut, _, _, _, _, _) = Build([], [], programSeed: [prog]);

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
        var (sut, _, _, _, _, _) = Build([], [], programSeed: [prog]);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(303, "BOGUS", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Null(prog.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgramFunctionBand_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _) = Build([], []);

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
        var (sut, _, _, _, _, _) = Build([], [], actSeed: [act]);

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
        var (sut, _, _, _, _, _) = Build([], [], actSeed: [act]);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(502, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsCreation);
        Assert.False(act.IsCreation);
    }

    [Fact]
    public async Task UpdateActivityIsCreation_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _) = Build([], []);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(999, true, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }
}
