using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AipService"/> (RAL-64).
/// Covers preview parsing, confirm import, status transitions.
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
        Mock<IRepository<AipRecord>>   aipRepo,
        Mock<IRepository<AipOffice>>   officeRepo,
        Mock<IRepository<AipProgram>>  programRepo,
        Mock<IRepository<AipProject>>  projectRepo,
        Mock<IRepository<AipActivity>> activityRepo,
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
        Mock<IRepository<AipRecord>>    aipRepo     = new();
        Mock<IRepository<AipOffice>>    officeRepo  = new();
        Mock<IRepository<AipProgram>>   programRepo = new();
        Mock<IRepository<AipProject>>   projectRepo = new();
        Mock<IRepository<AipActivity>>  actRepo     = new();
        Mock<IRepository<FundingSource>> fsRepo      = new();
        Mock<IRepository<User>>         userRepo    = new();
        Mock<IAipXlsmParser>  parser = new();
        Mock<IAuditService>   audit  = new();

        // AipRecord repo
        int nextAipId = 100;
        aipRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(aipSeed);
        aipRepo.Setup(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<AipRecord, CancellationToken>((e, _) => { e.Id = nextAipId++; aipSeed.Add(e); })
            .Returns(Task.CompletedTask);
        aipRepo.Setup(r => r.UpdateAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        aipRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // Hierarchy repos — seed GetAllAsync + capture adds
        List<AipOffice> officeList = officeSeed ?? [];
        int nextOfficeId = 200;
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(officeList);
        officeRepo.Setup(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()))
            .Callback<AipOffice, CancellationToken>((e, _) => e.Id = nextOfficeId++)
            .Returns(Task.CompletedTask);
        officeRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        List<AipProgram> programList = programSeed ?? [];
        int nextProgramId = 300;
        programRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(programList);
        programRepo.Setup(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .Callback<AipProgram, CancellationToken>((e, _) => e.Id = nextProgramId++)
            .Returns(Task.CompletedTask);
        programRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        List<AipProject> projectList = projectSeed ?? [];
        int nextProjectId = 400;
        projectRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(projectList);
        projectRepo.Setup(r => r.AddAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()))
            .Callback<AipProject, CancellationToken>((e, _) => e.Id = nextProjectId++)
            .Returns(Task.CompletedTask);
        projectRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        List<AipActivity> actList = actSeed ?? [];
        actRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(actList);
        actRepo.Setup(r => r.AddAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        actRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        // FundingSource and User repos
        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fsSeed);
        userRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userSeed ?? []);

        // Audit
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Parser — delegate to provided impl or use the mock directly
        if (parserImpl is not null)
            parser.Setup(p => p.Parse(It.IsAny<Stream>())).Returns(parserImpl.Parse);

        CallerContext ctx = new();
        ctx.SetUserId(UserId);

        AipService sut = new(
            aipRepo.Object, officeRepo.Object, programRepo.Object,
            projectRepo.Object, actRepo.Object, fsRepo.Object,
            userRepo.Object, parser.Object, audit.Object, ctx);

        return (sut, aipRepo, officeRepo, programRepo, projectRepo, actRepo, fsRepo, userRepo, parser, audit);
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

        var (sut, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

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

        var (sut, _, _, _, _, _, _, _, _, _) = Build([rec], [], userSeed: users);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null);

        Assert.Single(result);
        Assert.Equal("Ralph Alcaide", result[0].UploadedByName);
    }

    [Fact]
    public async Task GetAll_UnknownUploader_ReturnsNullUploadedByName()
    {
        AipRecord rec = new() { Id = 12, FiscalYear = 2027, EntrySource = "Upload",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Draft" };

        var (sut, _, _, _, _, _, _, _, _, _) = Build([rec], []);

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

        var (sut, _, _, _, _, _, _, _, _, _) = Build(seed, []);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(2027, null);

        Assert.Single(result);
        Assert.Equal(2027, result[0].FiscalYear);
    }

    // ── ParsePreviewAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ParsePreview_DetectsHierarchyLevels_ReturnsCounts()
    {
        // Arrange: parser returns 1 office > 1 program > 1 project > 2 activities
        ParsedAipActivity act1 = new("A-B-C-D-1-1-1-1", "Act 1", null, null, null, null, null, "GF",
            1000m, 2000m, null, 3000m, null, null, null);
        ParsedAipActivity act2 = new("A-B-C-D-1-1-1-2", "Act 2", null, null, null, null, null, null,
            null, null, null, null, null, null, null);
        ParsedAipProject proj  = new("A-B-C-D-1-1-1", "Project 1", [act1, act2]);
        ParsedAipProgram prog  = new("A-B-C-D-1-1", "Program 1", [proj]);
        ParsedAipOffice  off   = new("A-B-C-D-1", "Office 1", "GENERAL", [prog]);

        var (sut, _, _, _, _, _, _, _, parser, _) = Build([], []);
        parser.Setup(p => p.Parse(It.IsAny<Stream>()))
            .Returns(new Dictionary<string, List<ParsedAipOffice>>
                { ["GENERAL"] = [off] });

        // Act
        using MemoryStream ms = new();
        ServiceResult<AipImportPreviewDto> result =
            await sut.ParsePreviewAsync(ms, 2027, [], CancellationToken.None);

        // Assert
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

        var (sut, _, _, _, _, _, _, _, parser, _) = Build([], []);
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

        var (sut, _, _, _, _, _, _, _, parser, _) = Build([], []);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        // Graph insertion: only _aipRepo.AddAsync is called; child entities are
        // attached via navigation properties, NOT via their own repo.AddAsync.
        AipRecord? insertedGraph = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
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

        // Entire graph passed to a single AddAsync — no per-level repo calls.
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(insertedGraph);
        Assert.Single(insertedGraph!.Offices);
        AipOffice office = insertedGraph.Offices.First();
        Assert.Single(office.Programs);
        AipProgram program = office.Programs.First();
        Assert.Single(program.Projects);
        AipProject project = program.Projects.First();
        Assert.Single(project.Activities);
    }

    [Fact]
    public async Task ConfirmImport_SetsActivityFundingSourceSnapshot_WhenCodeMatches()
    {
        // Capture the root graph via aipRepo; navigate to the activity to assert snapshot.
        AipRecord? insertedGraph = null;
        var (sut, aipRepo, _, _, _, _, _, _, _, _) = Build([], [Fs(7, "GF")]);
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
        // Existing Draft for FY 2027 → new upload for same year should fail.
        var (sut, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);
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
        var (sut, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>());

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("FY 2027", result.Error);
    }

    [Fact]
    public async Task ConfirmImport_OnlyArchivedForYear_Succeeds()
    {
        // Archived record for same year should NOT block a new upload.
        AipRecord archived = Rec(1, PlanningStatus.Archived);
        var (sut, aipRepo, _, _, _, _, _, _, _, _) = Build([archived], []);
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
        var (sut, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Final, result.Value!.Status);
        Assert.Equal(PlanningStatus.Final, rec.Status);
    }

    [Fact]
    public async Task Finalize_AlreadyFinal_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Unlock_Final_TransitionsToDraft()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _) = Build([rec], []);

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
        var (sut, aipRepo, _, _, _, _, _, _, _, _) = Build(seed, []);

        int count = await sut.PurgeAllAsync(CancellationToken.None);

        Assert.Equal(2, count);
        aipRepo.Verify(r => r.DeleteAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── GetSummaryByIdAsync (RAL-89) ──────────────────────────────────────────

    [Fact]
    public async Task GetSummaryById_MissingId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(99, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetSummaryById_ExistingId_ReturnsOkWithCorrectFiscalYear()
    {
        AipRecord rec = Rec(5);
        var (sut, _, _, _, _, _, _, _, _, _) = Build([rec], []);

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

        var (sut, _, _, _, _, _, _, _, _, _) = Build(
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
        // AipActivitySummaryDto has no ProjectId, ProgramId, OfficeId, AipRecordId —
        // verify the dto only exposes the slim field set.
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

        var (sut, _, _, _, _, _, _, _, _, _) = Build(
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
}
