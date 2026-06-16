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
            List<User>?        userSeed   = null,
            List<AipOffice>?   officeSeed = null,
            IAipXlsmParser?    parserImpl = null)
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

        // Hierarchy repos — capture adds and seed GetAllAsync
        List<AipOffice> officeList = officeSeed ?? [];
        int nextOfficeId = 200;
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(officeList);
        officeRepo.Setup(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()))
            .Callback<AipOffice, CancellationToken>((e, _) => e.Id = nextOfficeId++)
            .Returns(Task.CompletedTask);
        officeRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        int nextProgramId = 300;
        programRepo.Setup(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()))
            .Callback<AipProgram, CancellationToken>((e, _) => e.Id = nextProgramId++)
            .Returns(Task.CompletedTask);
        programRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        int nextProjectId = 400;
        projectRepo.Setup(r => r.AddAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()))
            .Callback<AipProject, CancellationToken>((e, _) => e.Id = nextProjectId++)
            .Returns(Task.CompletedTask);
        projectRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

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
        var (sut, aipRepo, officeRepo, programRepo, projectRepo, actRepo, _, _, _, _) =
            Build([], [Fs(1, "GF")]);

        // One office > 1 program > 1 project > 1 activity
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

        aipRepo.Verify(r     => r.AddAsync(It.IsAny<AipRecord>(),    It.IsAny<CancellationToken>()), Times.Once);
        officeRepo.Verify(r  => r.AddAsync(It.IsAny<AipOffice>(),   It.IsAny<CancellationToken>()), Times.Once);
        programRepo.Verify(r => r.AddAsync(It.IsAny<AipProgram>(),  It.IsAny<CancellationToken>()), Times.Once);
        projectRepo.Verify(r => r.AddAsync(It.IsAny<AipProject>(),  It.IsAny<CancellationToken>()), Times.Once);
        actRepo.Verify(r     => r.AddAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmImport_SetsActivityFundingSourceSnapshot_WhenCodeMatches()
    {
        AipActivity? createdActivity = null;
        var (sut, _, _, _, _, actRepo, _, _, _, _) = Build([], [Fs(7, "GF")]);
        actRepo.Setup(r => r.AddAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()))
            .Callback<AipActivity, CancellationToken>((e, _) => createdActivity = e)
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

        Assert.NotNull(createdActivity);
        Assert.Equal(7, createdActivity!.FundingSourceId);
        Assert.Equal("GF", createdActivity.FundingSourceSnapshot);
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
}
