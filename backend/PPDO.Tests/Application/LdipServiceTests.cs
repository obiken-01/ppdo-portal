using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="LdipService"/> (RAL-64, RAL-61).
/// Covers: document ref-code generation, office resolution + AIP ref-code computation,
/// hierarchy full-replace with renumbering, sector validation, status transitions with
/// finalize-time completeness checks, and audit log calls.
/// ILdipRepository, IRepository&lt;Office&gt;, and IAuditService are mocked; no database access.
/// </summary>
public sealed class LdipServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static Office Off(int id = 1, string? refCode = "01-010", string code = "PPDO") => new()
    {
        Id = id, OfficeCode = code, OfficeName = "Provincial Planning and Development Office",
        OfficeRefCode = refCode, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static LdipRecord Rec(int id, string status, int fyStart = 2027, int? officeId = 1) => new()
    {
        Id = id, RefCode = $"LDIP-{fyStart}-001", Title = "Test LDIP",
        FiscalYearStart = fyStart, FiscalYearEnd = fyStart + 2,
        EntryMode = "New", Status = status, OfficeId = officeId,
        CreatedById = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static LdipOffice Group(int id, int recId, string refCode, string sector, params string[] programNames)
    {
        LdipOffice g = new() { Id = id, LdipRecordId = recId, RefCode = refCode, Name = "PPDO", Sector = sector };
        for (int i = 0; i < programNames.Length; i++)
            g.Programs.Add(new LdipProgram
            {
                Id = id * 100 + i, LdipOfficeId = id,
                RefCode = $"{refCode}-{i + 1:D3}", Name = programNames[i], Budget = 100m,
            });
        return g;
    }

    private static SaveLdipGroupDto SaveGroup(string sector, string name, params (string Name, decimal Budget)[] programs)
        => new(sector, name, programs.Select(p => new SaveLdipProgramDto(p.Name, p.Budget)).ToList());

    private static FundingSource Fs(int id, string code) => new()
    {
        Id = id, Code = code, Name = code, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (LdipService sut, Mock<ILdipRepository> repo, Mock<IAuditService> audit,
        Mock<ILdipXlsmParser> parser, Mock<IRepository<FundingSource>> fsRepo)
        Build(List<LdipRecord> seed, List<Office>? offices = null, Dictionary<int, List<LdipOffice>>? groups = null,
              List<FundingSource>? fundingSources = null)
    {
        Dictionary<int, List<LdipOffice>> groupStore = groups ?? [];

        Mock<ILdipRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => seed.FirstOrDefault(r => r.Id == id));
        repo.Setup(r => r.GetListAsync(It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? officeId, string? status, CancellationToken _) =>
                (IReadOnlyList<LdipRecord>)seed
                    .Where(r => officeId == null || r.OfficeId == officeId)
                    .Where(r => string.IsNullOrWhiteSpace(status) || r.Status == status.Trim())
                    .OrderByDescending(r => r.CreatedAt)
                    .ToList());
        // Return a snapshot copy — the real repo materialises via ToListAsync(), so
        // the service can iterate while DeleteOfficeGroupAsync mutates the store.
        repo.Setup(r => r.GetOfficeGroupsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int recId, CancellationToken _) =>
                (IReadOnlyList<LdipOffice>)(groupStore.GetValueOrDefault(recId) ?? []).ToList());
        repo.Setup(r => r.AddAsync(It.IsAny<LdipRecord>(), It.IsAny<CancellationToken>()))
            .Callback<LdipRecord, CancellationToken>((e, _) =>
            {
                e.Id = seed.Count == 0 ? 1 : seed.Max(r => r.Id) + 1;
                seed.Add(e);
                groupStore[e.Id] = [.. e.Offices];
            })
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.AddOfficeGroupAsync(It.IsAny<LdipOffice>(), It.IsAny<CancellationToken>()))
            .Callback<LdipOffice, CancellationToken>((g, _) =>
            {
                if (!groupStore.TryGetValue(g.LdipRecordId, out List<LdipOffice>? list))
                    groupStore[g.LdipRecordId] = list = [];
                list.Add(g);
            })
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteOfficeGroupAsync(It.IsAny<LdipOffice>(), It.IsAny<CancellationToken>()))
            .Callback<LdipOffice, CancellationToken>((g, _) => groupStore.GetValueOrDefault(g.LdipRecordId)?.Remove(g))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<LdipRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.DeleteAsync(It.IsAny<LdipRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IRepository<Office>> officeRepo = new();
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(offices ?? [Off()]);

        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Mock<ILdipXlsmParser> parser = new();

        Mock<IRepository<FundingSource>> fsRepo = new();
        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(fundingSources ?? []);

        CallerContext ctx = new();
        ctx.SetUserId(UserId);

        return (new LdipService(repo.Object, officeRepo.Object, audit.Object, ctx, parser.Object, fsRepo.Object),
            repo, audit, parser, fsRepo);
    }

    // ── Create — document ref code + status ───────────────────────────────────

    [Fact]
    public async Task Create_GeneratesRefCode_InDraftStatus()
    {
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("My LDIP", 2027, 2029, "New", OfficeId: 1);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Draft, result.Value!.Status);
        Assert.StartsWith("LDIP-", result.Value.RefCode);
        Assert.Equal(1, result.Value.OfficeId);
    }

    [Fact]
    public async Task Create_MissingOfficeId_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.CreateAsync(new CreateLdipDto("T", 2027, 2029, "New"), UserId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("office", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_UnknownOffice_ReturnsNotFound()
    {
        (LdipService sut, _, _, _, _) = Build([]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.CreateAsync(new CreateLdipDto("T", 2027, 2029, "New", OfficeId: 99), UserId);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task Create_OfficeWithoutRefCode_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([], offices: [Off(refCode: null)]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.CreateAsync(new CreateLdipDto("T", 2027, 2029, "New", OfficeId: 1), UserId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("ref code", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_BlankTitle_AutoGeneratesFromOfficeCodeAndYears()
    {
        (LdipService sut, _, _, _, _) = Build([]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.CreateAsync(new CreateLdipDto("", 2027, 2029, "New", OfficeId: 1), UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("LDIP 2027-2029 — PPDO", result.Value!.Title);
    }

    [Fact]
    public async Task Create_GroupAndProgramNames_StoredUppercase()
    {
        // Names normalise to UPPERCASE server-side — matching the source AIP files.
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1,
            Groups: [SaveGroup("General", "Office of the Provincial Governor",
                ("Office Functionality and Operations Program", 100m))]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("OFFICE OF THE PROVINCIAL GOVERNOR", result.Value!.Groups[0].Name);
        Assert.Equal("OFFICE FUNCTIONALITY AND OPERATIONS PROGRAM", result.Value.Groups[0].Programs[0].Name);
    }

    // ── Create — AIP ref-code computation (the RAL-61 core) ──────────────────

    [Fact]
    public async Task Create_ComputesGroupRefCode_FromSectorPrefixAndOfficeRefCode()
    {
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1,
            Groups: [SaveGroup("General", "PPDO", ("Program A", 500m))]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        Assert.True(result.IsSuccess);
        LdipOfficeGroupDto group = Assert.Single(result.Value!.Groups);
        Assert.Equal("1000-000-1-01-010", group.RefCode);
        Assert.Equal("1000-000-1-01-010-001", group.Programs[0].RefCode);
    }

    [Fact]
    public async Task Create_ProgramRefCodes_AreContiguousInSubmittedOrder()
    {
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1,
            Groups: [SaveGroup("General", "PPDO", ("P1", 1m), ("P2", 2m), ("P3", 3m))]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        IReadOnlyList<LdipProgramDto> programs = result.Value!.Groups[0].Programs;
        Assert.Equal(
            ["1000-000-1-01-010-001", "1000-000-1-01-010-002", "1000-000-1-01-010-003"],
            programs.Select(p => p.RefCode).ToList());
        Assert.Equal(["P1", "P2", "P3"], programs.Select(p => p.Name).ToList());
    }

    [Fact]
    public async Task Create_MultipleSectors_GetDistinctRefCodes_AndMayRenameSubOffice()
    {
        // Same office, two sectors — the Economic group carries a different
        // display name while sharing the office ref-code suffix (the real AIP quirk).
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1, Groups:
        [
            SaveGroup("General",  "PPDO",                    ("P1", 1m)),
            SaveGroup("Economic", "PPDO - SPECIAL PROJECTS", ("P2", 2m)),
        ]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        Assert.Equal(2, result.Value!.Groups.Count);
        LdipOfficeGroupDto general  = result.Value.Groups.First(g => g.Sector == "General");
        LdipOfficeGroupDto economic = result.Value.Groups.First(g => g.Sector == "Economic");
        Assert.Equal("1000-000-1-01-010", general.RefCode);
        Assert.Equal("8000-000-1-01-010", economic.RefCode);
        Assert.Equal("PPDO - SPECIAL PROJECTS", economic.Name);
        Assert.Equal("8000-000-1-01-010-001", economic.Programs[0].RefCode);
    }

    [Fact]
    public async Task Create_MultipleGroupsInOneSector_NumbersProgramsContinuouslyAcrossGroups()
    {
        // The real AIP-file shape: PGO - WARDEN / - AKAP-HUB / - HOUSING all share
        // 3000-000-1-01-… under Social; program numbering continues across them
        // (never restarts at -001 per group, which would collide).
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1, Groups:
        [
            SaveGroup("Social", "PGO - WARDEN",   ("Jail Operations Program", 100m)),
            SaveGroup("Social", "PGO - AKAP-HUB", ("AKAP Hub Health Assistance Program", 200m)),
            SaveGroup("Social", "PGO - HOUSING",  ("Housing Program A", 300m), ("Housing Program B", 400m)),
        ]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value!.Groups.Count);
        Assert.All(result.Value.Groups, g => Assert.Equal("3000-000-1-01-010", g.RefCode));
        List<string> allProgramRefs = result.Value.Groups
            .SelectMany(g => g.Programs)
            .Select(p => p.RefCode)
            .ToList();
        Assert.Equal(
            ["3000-000-1-01-010-001", "3000-000-1-01-010-002",
             "3000-000-1-01-010-003", "3000-000-1-01-010-004"],
            allProgramRefs);
    }

    [Fact]
    public async Task Create_DuplicateSectorAndName_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1, Groups:
        [
            SaveGroup("General", "PPDO", ("P1", 1m)),
            SaveGroup("general", "ppdo", ("P2", 2m)),
        ]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Duplicate group", result.Error);
    }

    [Fact]
    public async Task Create_UnknownSector_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1,
            Groups: [SaveGroup("Industrial", "A", ("P1", 1m))]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Unknown sector", result.Error);
    }

    [Fact]
    public async Task Create_CallsAuditLog_CreateAction()
    {
        (LdipService sut, _, Mock<IAuditService> audit, _, _) = Build([]);

        await sut.CreateAsync(new CreateLdipDto("T", 2027, 2029, "New", OfficeId: 1), UserId);

        audit.Verify(a => a.LogAsync(
            "ldip_records", It.IsAny<int>(), AuditAction.Create,
            null, It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Update — full replace + renumbering ──────────────────────────────────

    [Fact]
    public async Task Update_DraftRecord_Succeeds()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        (LdipService sut, _, _, _, _) = Build([rec]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.UpdateAsync(1, new UpdateLdipDto("New Title", 2028, 2030, "Amendment", OfficeId: 1));

        Assert.True(result.IsSuccess);
        Assert.Equal("New Title", result.Value!.Title);
        Assert.Equal("Amendment", result.Value.EntryMode);
    }

    [Fact]
    public async Task Update_RemovingMiddleProgram_RenumbersRemaining()
    {
        // Seed: 3 programs 001/002/003. Resubmit without the middle one —
        // the survivors must renumber to 001/002 with no gap.
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        Dictionary<int, List<LdipOffice>> groups = new()
        {
            [1] = [Group(10, 1, "1000-000-1-01-010", "General", "P1", "P2", "P3")],
        };
        (LdipService sut, _, _, _, _) = Build([rec], groups: groups);

        ServiceResult<LdipRecordDetailDto> result = await sut.UpdateAsync(1,
            new UpdateLdipDto("T", 2027, 2029, "New", OfficeId: 1,
                Groups: [SaveGroup("General", "PPDO", ("P1", 1m), ("P3", 3m))]));

        Assert.True(result.IsSuccess);
        IReadOnlyList<LdipProgramDto> programs = result.Value!.Groups[0].Programs;
        Assert.Equal(2, programs.Count);
        Assert.Equal("1000-000-1-01-010-001", programs[0].RefCode);
        Assert.Equal("P1", programs[0].Name);
        Assert.Equal("1000-000-1-01-010-002", programs[1].RefCode);
        Assert.Equal("P3", programs[1].Name);
    }

    [Fact]
    public async Task Update_ReplacesHierarchy_DeletesOldGroupsFirst()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        LdipOffice oldGroup = Group(10, 1, "1000-000-1-01-010", "General", "Old");
        Dictionary<int, List<LdipOffice>> groups = new() { [1] = [oldGroup] };
        (LdipService sut, Mock<ILdipRepository> repo, _, _, _) = Build([rec], groups: groups);

        await sut.UpdateAsync(1, new UpdateLdipDto("T", 2027, 2029, "New", OfficeId: 1,
            Groups: [SaveGroup("Social", "PPDO", ("New program", 5m))]));

        repo.Verify(r => r.DeleteOfficeGroupAsync(oldGroup, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.AddOfficeGroupAsync(
            It.Is<LdipOffice>(g => g.RefCode == "3000-000-1-01-010"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_FinalRecord_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.UpdateAsync(1, new UpdateLdipDto("X", 2027, 2029, "New", OfficeId: 1));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Update_ArchivedRecord_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([Rec(1, PlanningStatus.Archived)]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.UpdateAsync(1, new UpdateLdipDto("X", 2027, 2029, "New", OfficeId: 1));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Update_MissingId_ReturnsNotFound()
    {
        (LdipService sut, _, _, _, _) = Build([]);

        ServiceResult<LdipRecordDetailDto> result =
            await sut.UpdateAsync(999, new UpdateLdipDto("X", 2027, 2029, "New", OfficeId: 1));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── GetAll — office scoping ───────────────────────────────────────────────

    [Fact]
    public async Task GetAll_WithOfficeId_ReturnsOnlyThatOfficesRecords()
    {
        List<LdipRecord> seed = [Rec(1, PlanningStatus.Draft, officeId: 1), Rec(2, PlanningStatus.Draft, officeId: 2)];
        (LdipService sut, _, _, _, _) = Build(seed);

        IReadOnlyList<LdipRecordDto> result = await sut.GetAllAsync(null, officeId: 2);

        Assert.Single(result);
        Assert.Equal(2, result[0].Id);
    }

    // ── Finalize — completeness checks ────────────────────────────────────────

    [Fact]
    public async Task Finalize_DraftWithPrograms_TransitionsToFinal()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        Dictionary<int, List<LdipOffice>> groups = new()
        {
            [1] = [Group(10, 1, "1000-000-1-01-010", "General", "P1")],
        };
        (LdipService sut, _, _, _, _) = Build([rec], groups: groups);

        ServiceResult<LdipRecordDto> result = await sut.FinalizeAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Final, rec.Status);
    }

    [Fact]
    public async Task Finalize_NoPrograms_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)]);

        ServiceResult<LdipRecordDto> result = await sut.FinalizeAsync(1);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("at least one program", result.Error);
    }

    [Fact]
    public async Task Finalize_NoOffice_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft, officeId: null)]);

        ServiceResult<LdipRecordDto> result = await sut.FinalizeAsync(1);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("office", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finalize_YearStartAfterYearEnd_ReturnsBadRequest()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        rec.FiscalYearStart = 2030;
        rec.FiscalYearEnd   = 2027;
        (LdipService sut, _, _, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.FinalizeAsync(1);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("year start", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Finalize_AlreadyFinal_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)]);

        ServiceResult<LdipRecordDto> result = await sut.FinalizeAsync(1);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Unlock / Archive ──────────────────────────────────────────────────────

    [Fact]
    public async Task Unlock_Final_TransitionsToDraft()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Final);
        (LdipService sut, _, _, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.UnlockAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Draft, rec.Status);
    }

    [Fact]
    public async Task Unlock_Draft_ReturnsBadRequest()
    {
        (LdipService sut, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)]);

        ServiceResult<LdipRecordDto> result = await sut.UnlockAsync(1);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Archive_Draft_TransitionsToArchived()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Draft);
        (LdipService sut, _, _, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.ArchiveAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Archived, result.Value!.Status);
    }

    [Fact]
    public async Task Archive_Final_TransitionsToArchived()
    {
        LdipRecord rec = Rec(1, PlanningStatus.Final);
        (LdipService sut, _, _, _, _) = Build([rec]);

        ServiceResult<LdipRecordDto> result = await sut.ArchiveAsync(1);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Archived, result.Value!.Status);
    }

    // ── PurgeAll ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeAll_DeletesAllRecords_ReturnsCount()
    {
        List<LdipRecord> seed = [Rec(1, PlanningStatus.Draft), Rec(2, PlanningStatus.Final, 2028)];
        (LdipService sut, Mock<ILdipRepository> repo, _, _, _) = Build(seed);

        int count = await sut.PurgeAllAsync();

        Assert.Equal(2, count);
        repo.Verify(r => r.DeleteAsync(It.IsAny<LdipRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── File upload — ParsePreview (RAL-113) ──────────────────────────────────

    private static ParsedLdipProgram Prog(
        string refCode, string name, decimal? ps = null, decimal? mooe = null, decimal? co = null,
        decimal? total = null, string? fundingSourceRaw = null) => new(
        refCode, name, ImplementingOffice: null, StartDate: "2027", EndDate: "2029",
        ExpectedOutputs: null, FundingSourceRaw: fundingSourceRaw, Ps: ps, Mooe: mooe, Co: co, Total: total,
        CcAdaptation: null, CcMitigation: null, CcTypologyCode: null,
        PdpRdp: null, Sdgs: null, SendaiFramework: null, NdrrmPlan: null, Nsp: null, Pdpdfp: null);

    private static Office Off2() => new()
    {
        Id = 2, OfficeCode = "GSO", OfficeName = "General Services Office",
        OfficeRefCode = "01-020", IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task ParsePreview_MatchesMultipleOffices_AcrossSectors_NoPickerNeeded()
    {
        (LdipService sut, _, _, Mock<ILdipXlsmParser> parser, _) =
            Build([], offices: [Off(), Off2()]);
        parser.Setup(p => p.Parse(It.IsAny<Stream>())).Returns(new Dictionary<string, List<ParsedLdipOffice>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GENERAL"] = [
                new("1000-000-1-01-010", "PPDO", "GENERAL", [Prog("1000-000-1-01-010-001", "P1", total: 100m)]),
                new("1000-000-1-01-020", "GSO", "GENERAL", [Prog("1000-000-1-01-020-001", "P2", total: 200m)]),
            ],
            ["SOCIAL"] = [
                new("3000-000-1-01-010", "PPDO", "SOCIAL", [Prog("3000-000-1-01-010-001", "P3", total: 300m)]),
            ],
        });

        ServiceResult<LdipImportPreviewDto> result =
            await sut.ParsePreviewAsync(new MemoryStream(), 2027, 2029, knownFundingSources: []);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Counts.Offices);
        Assert.Equal(3, result.Value.Counts.Groups);
        Assert.Equal(3, result.Value.Counts.Programs);
        Assert.Empty(result.Value.Warnings);

        LdipImportOfficeResultDto ppdo = result.Value.Offices.Single(o => o.OfficeCode == "PPDO");
        Assert.Equal(2, ppdo.Groups.Count); // General + Social
        LdipImportOfficeResultDto gso = result.Value.Offices.Single(o => o.OfficeCode == "GSO");
        Assert.Single(gso.Groups);
    }

    [Fact]
    public async Task ParsePreview_UnmatchedOfficeRefCode_AddsWarning_AndIsExcluded()
    {
        (LdipService sut, _, _, Mock<ILdipXlsmParser> parser, _) = Build([], offices: [Off()]);
        parser.Setup(p => p.Parse(It.IsAny<Stream>())).Returns(new Dictionary<string, List<ParsedLdipOffice>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GENERAL"] = [
                new("1000-000-1-01-010", "PPDO", "GENERAL", [Prog("1000-000-1-01-010-001", "P1", total: 100m)]),
                new("1000-000-1-99-999", "UNKNOWN OFFICE", "GENERAL", [Prog("1000-000-1-99-999-001", "Not mine", total: 999m)]),
            ],
        });

        ServiceResult<LdipImportPreviewDto> result =
            await sut.ParsePreviewAsync(new MemoryStream(), 2027, 2029, knownFundingSources: []);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Offices);
        Assert.DoesNotContain(result.Value.Offices, o => o.Groups.Any(g => g.Programs.Any(p => p.Name == "Not mine")));
        Assert.Contains(result.Value.Warnings, w => w.Contains("UNKNOWN OFFICE") && w.Contains("did not match"));
    }

    [Fact]
    public async Task ParsePreview_UnmatchedFundingSource_AddsWarning()
    {
        (LdipService sut, _, _, Mock<ILdipXlsmParser> parser, _) = Build([], offices: [Off()]);
        parser.Setup(p => p.Parse(It.IsAny<Stream>())).Returns(new Dictionary<string, List<ParsedLdipOffice>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GENERAL"] = [
                new("1000-000-1-01-010", "PPDO", "GENERAL",
                    [Prog("1000-000-1-01-010-001", "P1", total: 100m, fundingSourceRaw: "Unknown Fund")]),
            ],
        });

        ServiceResult<LdipImportPreviewDto> result =
            await sut.ParsePreviewAsync(new MemoryStream(), 2027, 2029,
                knownFundingSources: [Fs(1, "General Fund")]);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value!.Warnings, w => w.Contains("Unknown Fund"));
    }

    [Fact]
    public async Task ParsePreview_ProgramBudget_ComputedFromPsMooeCo_WhenTotalMissing()
    {
        (LdipService sut, _, _, Mock<ILdipXlsmParser> parser, _) = Build([], offices: [Off()]);
        parser.Setup(p => p.Parse(It.IsAny<Stream>())).Returns(new Dictionary<string, List<ParsedLdipOffice>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GENERAL"] = [
                new("1000-000-1-01-010", "PPDO", "GENERAL",
                    [Prog("1000-000-1-01-010-001", "P1", ps: 100m, mooe: 200m, co: 50m)]),
            ],
        });

        ServiceResult<LdipImportPreviewDto> result =
            await sut.ParsePreviewAsync(new MemoryStream(), 2027, 2029, knownFundingSources: []);

        Assert.Equal(350m, result.Value!.Offices[0].Groups[0].Programs[0].Budget);
    }

    [Fact]
    public async Task ParsePreview_NoOfficesMatched_ReturnsEmpty_WithWarning()
    {
        (LdipService sut, _, _, Mock<ILdipXlsmParser> parser, _) = Build([], offices: [Off()]);
        parser.Setup(p => p.Parse(It.IsAny<Stream>())).Returns(new Dictionary<string, List<ParsedLdipOffice>>(StringComparer.OrdinalIgnoreCase)
        {
            ["GENERAL"] = [new("1000-000-1-99-999", "UNKNOWN OFFICE", "GENERAL", [])],
        });

        ServiceResult<LdipImportPreviewDto> result =
            await sut.ParsePreviewAsync(new MemoryStream(), 2027, 2029, knownFundingSources: []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Offices);
        Assert.Contains(result.Value.Warnings, w => w.Contains("nothing to import"));
    }

    [Fact]
    public async Task ParsePreview_ParserThrows_ReturnsBadRequest()
    {
        (LdipService sut, _, _, Mock<ILdipXlsmParser> parser, _) = Build([]);
        parser.Setup(p => p.Parse(It.IsAny<Stream>())).Throws(new LdipParseException(["bad file"]));

        ServiceResult<LdipImportPreviewDto> result =
            await sut.ParsePreviewAsync(new MemoryStream(), 2027, 2029, knownFundingSources: []);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── File upload — ConfirmImport ───────────────────────────────────────────

    [Fact]
    public async Task ConfirmImport_CreatesOneDraftRecordPerOffice_WithEntryModeUpload()
    {
        (LdipService sut, _, _, _, _) = Build([], offices: [Off(), Off2()]);
        LdipImportConfirmDto dto = new(2027, 2029, Offices:
        [
            new LdipImportOfficeResultDto(1, "PPDO", "Provincial Planning and Development Office",
                [SaveGroup("General", "PPDO", ("Program A", 350m))]),
            new LdipImportOfficeResultDto(2, "GSO", "General Services Office",
                [SaveGroup("General", "GSO", ("Program B", 250m))]),
        ]);

        ServiceResult<IReadOnlyList<LdipRecordDto>> result = await sut.ConfirmImportAsync(dto, UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Count);
        Assert.All(result.Value, r => Assert.Equal(PlanningStatus.Draft, r.Status));
        Assert.All(result.Value, r => Assert.Equal("Upload", r.EntryMode));
        Assert.Contains(result.Value, r => r.OfficeId == 1);
        Assert.Contains(result.Value, r => r.OfficeId == 2);
    }

    [Fact]
    public async Task ConfirmImport_ResolvesFundingSourceRawToId_ForMatchedProgram()
    {
        (LdipService sut, _, _, _, _) = Build([], offices: [Off()], fundingSources: [Fs(1, "General Fund")]);
        SaveLdipGroupDto group = new("General", "PPDO",
            [new SaveLdipProgramDto("Program A", 100m, FundingSourceRaw: "General Fund")]);
        LdipImportConfirmDto dto = new(2027, 2029,
            Offices: [new LdipImportOfficeResultDto(1, "PPDO", "PPDO Office", [group])]);

        ServiceResult<IReadOnlyList<LdipRecordDto>> result = await sut.ConfirmImportAsync(dto, UserId);
        Assert.True(result.IsSuccess);

        ServiceResult<LdipRecordDetailDto> detail = await sut.GetByIdAsync(result.Value![0].Id);
        LdipProgramDto program = detail.Value!.Groups[0].Programs[0];
        Assert.Equal(1, program.FundingSourceId);
        Assert.Equal("General Fund", program.FundingSourceSnapshot);
    }

    [Fact]
    public async Task ConfirmImport_UnmatchedFundingSourceRaw_SnapshotFallsBackToRawText()
    {
        (LdipService sut, _, _, _, _) = Build([], offices: [Off()], fundingSources: [Fs(1, "General Fund")]);
        SaveLdipGroupDto group = new("General", "PPDO",
            [new SaveLdipProgramDto("Program A", 100m, FundingSourceRaw: "Weird Fund")]);
        LdipImportConfirmDto dto = new(2027, 2029,
            Offices: [new LdipImportOfficeResultDto(1, "PPDO", "PPDO Office", [group])]);

        ServiceResult<IReadOnlyList<LdipRecordDto>> result = await sut.ConfirmImportAsync(dto, UserId);
        Assert.True(result.IsSuccess);

        ServiceResult<LdipRecordDetailDto> detail = await sut.GetByIdAsync(result.Value![0].Id);
        LdipProgramDto program = detail.Value!.Groups[0].Programs[0];
        Assert.Null(program.FundingSourceId);
        Assert.Equal("Weird Fund", program.FundingSourceSnapshot);
    }

    [Fact]
    public async Task ConfirmImport_CopiesThroughAllDetailFields()
    {
        (LdipService sut, _, _, _, _) = Build([], offices: [Off()]);
        SaveLdipGroupDto group = new("General", "PPDO",
        [
            new SaveLdipProgramDto("Program A", 350m,
                ImplementingOffice: "PGO", StartDate: "2027", EndDate: "2029",
                ExpectedOutputs: "Roads built", Ps: 100m, Mooe: 200m, Co: 50m,
                CcAdaptation: 10m, CcMitigation: 20m, CcTypologyCode: "Typology A",
                PdpRdp: "Chapter 14", Sdgs: "SDG 9", SendaiFramework: "Priority 3",
                NdrrmPlan: "DRRM", Nsp: "NSP tag", Pdpdfp: "PDPDFP tag"),
        ]);
        LdipImportConfirmDto dto = new(2027, 2029,
            Offices: [new LdipImportOfficeResultDto(1, "PPDO", "PPDO Office", [group])]);

        ServiceResult<IReadOnlyList<LdipRecordDto>> result = await sut.ConfirmImportAsync(dto, UserId);
        Assert.True(result.IsSuccess);

        ServiceResult<LdipRecordDetailDto> detail = await sut.GetByIdAsync(result.Value![0].Id);
        LdipProgramDto p = detail.Value!.Groups[0].Programs[0];
        Assert.Equal("PGO", p.ImplementingOffice);
        Assert.Equal("2027", p.StartDate);
        Assert.Equal("2029", p.EndDate);
        Assert.Equal("Roads built", p.ExpectedOutputs);
        Assert.Equal(100m, p.Ps);
        Assert.Equal(200m, p.Mooe);
        Assert.Equal(50m, p.Co);
        Assert.Equal(10m, p.CcAdaptation);
        Assert.Equal(20m, p.CcMitigation);
        Assert.Equal("Typology A", p.CcTypologyCode);
        Assert.Equal("Chapter 14", p.PdpRdp);
        Assert.Equal("SDG 9", p.Sdgs);
        Assert.Equal("Priority 3", p.SendaiFramework);
        Assert.Equal("DRRM", p.NdrrmPlan);
        Assert.Equal("NSP tag", p.Nsp);
        Assert.Equal("PDPDFP tag", p.Pdpdfp);
    }

    [Fact]
    public async Task ConfirmImport_OneOfficeFails_ReturnsBadRequest_MentioningOfficeCode()
    {
        (LdipService sut, _, _, _, _) = Build([], offices: [Off()]);
        // Unknown sector on the second office's group triggers ValidateGroups to fail.
        LdipImportConfirmDto dto = new(2027, 2029, Offices:
        [
            new LdipImportOfficeResultDto(1, "PPDO", "PPDO Office",
                [new SaveLdipGroupDto("Industrial", "PPDO", [new SaveLdipProgramDto("Program A", 1m)])]),
        ]);

        ServiceResult<IReadOnlyList<LdipRecordDto>> result = await sut.ConfirmImportAsync(dto, UserId);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("PPDO", result.Error);
    }

    [Fact]
    public async Task ManualCreate_DetailFieldsRemainNull()
    {
        // The manual "+ Add Program" flow only ever sends Name/Budget — detail
        // fields must stay null, never accidentally populated.
        (LdipService sut, _, _, _, _) = Build([]);
        CreateLdipDto dto = new("T", 2027, 2029, "New", OfficeId: 1,
            Groups: [SaveGroup("General", "PPDO", ("Program A", 100m))]);

        ServiceResult<LdipRecordDetailDto> result = await sut.CreateAsync(dto, UserId);

        LdipProgramDto p = result.Value!.Groups[0].Programs[0];
        Assert.Null(p.ImplementingOffice);
        Assert.Null(p.Ps);
        Assert.Null(p.FundingSourceId);
    }
}
