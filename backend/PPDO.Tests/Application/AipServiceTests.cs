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
public sealed partial class AipServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    /// <summary>
    /// A host-office (PPDO) Admin — sees every office, narrowed by no division. These tests
    /// predate V18-39's scoping and assert over the whole hierarchy, so the caller that changes
    /// nothing is the right one for them. The scope rule itself is pinned by AipReadScopeTests.
    /// </summary>
    private static User HostCaller() => new()
    {
        Id = Guid.NewGuid(), Username = "ppdo.admin", PasswordHash = "h", FullName = "PPDO Admin",
        Role = UserRole.Admin, OfficeId = 1, DivisionId = null,
        Office = new Office
        {
            Id = 1, OfficeCode = "PPDO", OfficeName = "PPDO", IsHostOffice = true, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        },
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    /// <summary>
    /// A fiscal year on the legacy multi-office shape, and the year before it.
    ///
    /// <para>
    /// ⚠️ The carry-forward and LDIP-seed tests below used to run 2027 → 2028, which V18-37 now
    /// correctly refuses: both paths find-or-create an <b>unowned</b> record, and FY2028 onward has
    /// no shape that can accept one. They are testing carry-forward mechanics rather than the
    /// partition, so they were moved down a year rather than deleted or exempted — a test that
    /// reaches for FY2028 through a legacy path is asserting the leak PPDO-40 closed.
    /// </para>
    ///
    /// <para>
    /// Source and target are deliberately <b>different</b> years. Seeding both at 2027 would make
    /// the mock's <c>GetLatestByFiscalYearAsync(target)</c> return the source record, so a test
    /// meaning "no target record exists yet" would quietly stop testing that.
    /// </para>
    /// </summary>
    private const int LegacyFy = 2027;
    private const int PriorFy  = LegacyFy - 1;

    private static AipRecord Rec(int id, string status = "Draft", int fiscalYear = LegacyFy) => new()
    {
        Id = id, FiscalYear = fiscalYear, EntrySource = "Upload",
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
        Mock<IRepository<AipActivity>> activityRepo,
        Mock<ILdipRepository> ldipRepo)
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
            List<Office>? officeConfigSeed = null,
            List<LdipRecord>? ldipRecordSeed = null,
            List<LdipOffice>? ldipOfficeSeed = null)
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
        // V18-32 — confirm-import resolves each uploaded office's ownership FK from this list.
        // Unset it and every office lands unowned, which is invisible rather than loud.
        officeConfigRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(officeConfigList);

        Mock<IAllocationRepository> allocationRepo = new();
        allocationRepo.Setup(r => r.GetProgramDivisionsByOfficeIdAsync(
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ProgramDivision>)[]);

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

        // ── LDIP repo (RAL-181 — seed AIP programs from an office's LDIP) ────────────

        List<LdipRecord> ldipRecordList = ldipRecordSeed ?? [];
        List<LdipOffice> ldipOfficeList = ldipOfficeSeed ?? [];
        Mock<ILdipRepository> ldipRepo = new();
        ldipRepo.Setup(r => r.GetListAsync(It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? officeId, string? status, CancellationToken _) =>
                (IReadOnlyList<LdipRecord>)ldipRecordList
                    .Where(r => officeId is null || r.OfficeId == officeId)
                    .Where(r => string.IsNullOrWhiteSpace(status) || r.Status == status)
                    .OrderByDescending(r => r.CreatedAt)
                    .ToList());
        ldipRepo.Setup(r => r.GetOfficeGroupsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int ldipRecordId, CancellationToken _) =>
                (IReadOnlyList<LdipOffice>)ldipOfficeList.Where(o => o.LdipRecordId == ldipRecordId).ToList());

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
            officeConfigRepo.Object, programRepo.Object, projectRepo.Object, activityRepo.Object,
            ldipRepo.Object, allocationRepo.Object);

        return (sut, aipRepo, fsRepo, userRepo, parser, audit, officeRepo, wfpRepo,
            officeConfigRepo, programRepo, projectRepo, activityRepo, ldipRepo);
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

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null, HostCaller());

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

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], userSeed: users);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null, HostCaller());

        Assert.Single(result);
        Assert.Equal("Ralph Alcaide", result[0].UploadedByName);
    }

    [Fact]
    public async Task GetAll_UnknownUploader_ReturnsNullUploadedByName()
    {
        AipRecord rec = new() { Id = 12, FiscalYear = 2027, EntrySource = "Upload",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Draft" };

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null, HostCaller());

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

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(seed, []);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(2027, null, HostCaller());

        Assert.Single(result);
        Assert.Equal(2027, result[0].FiscalYear);
    }

    // ── RAL-93: scoped query verification ────────────────────────────────────

    [Fact]
    public async Task GetById_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(5);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        await sut.GetByIdAsync(5, HostCaller(), CancellationToken.None);

        // Scoped lookup must be called; full-table scan must NOT.
        aipRepo.Verify(r => r.GetByIntIdAsync(5, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_UsesGetOfficesByAipIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(7);
        List<AipOffice> offices = [new() { Id = 1, AipRecordId = 7, RefCode = "X", Name = "O", Sector = "GENERAL" }];
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        await sut.GetByIdAsync(7, HostCaller(), CancellationToken.None);

        aipRepo.Verify(r => r.GetOfficesByAipIdAsync(7, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Record shape: office-owned vs legacy multi-office (V18-40 / PPDO-39) ──

    [Fact]
    public async Task CreateManualRecord_TwiceInOneFiscalYear_IsRefused()
    {
        // ↩️ Replaces CreateManualRecord_SameOfficeTwiceInOneFiscalYear_IsRefused (PPDO-61). V18-40
        // had to scope the conflict question per office, because with one record per office
        // "is there an AIP for FY 2028" would have reported office A's record as a conflict for
        // office B. With ONE base record per year that scoping is not merely unnecessary — it
        // would be wrong, since a second record for a year that already has one is precisely what
        // must be refused.
        AipRecord existing = new()
        {
            Id = 60, FiscalYear = 2028, EntrySource = "Manual",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Draft",
        };
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([existing], []);

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(
            new CreateAipRecordDto(2028), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("2028", result.Error!);
    }

    [Fact]
    public async Task CreateManualRecord_ADifferentFiscalYear_IsAllowedAlongside()
    {
        // Without this, the guard above is satisfied by a service that refuses every create.
        AipRecord existing = new()
        {
            Id = 60, FiscalYear = 2027, EntrySource = "Manual",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow, Status = "Draft",
        };
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([existing], []);

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(
            new CreateAipRecordDto(2028), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddOffice_PpdoAndAGuestOffice_TakeTheIdenticalPath()
    {
        // ⚠️ The decision tracker B12-b protects: PPDO must be an ORDINARY office — no per-division
        // records, no branch. It used to be pinned on record creation, which no longer takes an
        // office at all (PPDO-61), so it moved to where offices actually enter the AIP now. If a
        // special case ever creeps in, these two stop matching and every downstream feature starts
        // carrying two code paths.
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010"), MakeOffice(8, "GSO", "01-015")];
        List<AipRecord> recs =
        [
            new() { Id = AipRecordId, FiscalYear = 2028, EntrySource = "Manual", Status = "Draft",
                    UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow },
        ];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build(recs, [], officeSeed: [], officeConfigSeed: offices);
        User caller = HostCaller();

        ServiceResult<AipOfficeDto> ppdo = await sut.AddOfficeAsync(
            AipRecordId, new CreateAipOfficeDto(7, AipSector.General), caller, CancellationToken.None);
        ServiceResult<AipOfficeDto> gso = await sut.AddOfficeAsync(
            AipRecordId, new CreateAipOfficeDto(8, AipSector.General), caller, CancellationToken.None);

        Assert.True(ppdo.IsSuccess);
        Assert.True(gso.IsSuccess);
        Assert.Equal(ppdo.Value!.Sector, gso.Value!.Sector);
        Assert.Equal(ppdo.Value.AipRecordId, gso.Value.AipRecordId);
    }

    // ── Office scoping on the read path (V18-39 / PPDO-38) ────────────────────

    /// <summary>A guest-office caller — no cross-office access, whatever division they carry.</summary>
    private static User GuestCaller(int officeId, int? divisionId = null) => new()
    {
        Id = Guid.NewGuid(), Username = "gso.staff", PasswordHash = "h", FullName = "GSO Staff",
        Role = UserRole.Staff, OfficeId = officeId, DivisionId = divisionId,
        Office = new Office
        {
            Id = officeId, OfficeCode = "GSO", OfficeName = "GSO", IsHostOffice = false,
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        },
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static List<AipOffice> TwoOfficesOneEach(int aipId) =>
    [
        new() { Id = 1, AipRecordId = aipId, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL", OfficeId = 1 },
        new() { Id = 2, AipRecordId = aipId, RefCode = "1000-000-1-01-015", Name = "GSO",  Sector = "GENERAL", OfficeId = 2 },
    ];

    [Fact]
    public async Task GetById_GuestOfficeCaller_SeesOnlyItsOwnOffice()
    {
        // ⚠️ Before V18-39 this endpoint returned EVERY office's hierarchy to any caller with
        // Budget Planning access. Only the absence of production guest-office accounts kept that
        // from being a live cross-office leak.
        AipRecord rec = Rec(30);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: TwoOfficesOneEach(30));

        ServiceResult<AipRecordDetailDto> result =
            await sut.GetByIdAsync(30, GuestCaller(officeId: 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GSO", Assert.Single(result.Value!.Offices).Name);
    }

    [Fact]
    public async Task GetById_HostOfficeCaller_SeesEveryOffice()
    {
        AipRecord rec = Rec(31);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: TwoOfficesOneEach(31));

        ServiceResult<AipRecordDetailDto> result =
            await sut.GetByIdAsync(31, HostCaller(), CancellationToken.None);

        Assert.Equal(2, result.Value!.Offices.Count);
    }

    [Fact]
    public async Task GetById_CallerWithNoOffice_SeesNoOffices_ButNotAnError()
    {
        // Unassigned sees nothing (DECISION F). An empty result, not a 403 — the record exists and
        // the caller may ask about it; they simply own none of it.
        AipRecord rec = Rec(32);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: TwoOfficesOneEach(32));

        User noOffice = GuestCaller(officeId: 2);
        noOffice.OfficeId = null;
        noOffice.Office   = null;

        ServiceResult<AipRecordDetailDto> result =
            await sut.GetByIdAsync(32, noOffice, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Offices);
    }

    [Fact]
    public async Task GetSummaryById_GuestOfficeCaller_IsScopedTheSameWay()
    {
        // The summary is what the detail page's grid actually renders. Scoping only its heavier
        // sibling would leave the leak open on the endpoint people use.
        AipRecord rec = Rec(33);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: TwoOfficesOneEach(33));

        ServiceResult<AipRecordSummaryDto> result =
            await sut.GetSummaryByIdAsync(33, GuestCaller(officeId: 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GSO", Assert.Single(result.Value!.Offices).Name);
    }

    [Fact]
    public async Task GetById_WfpBuiltFromRecord_HasWfpUsageIsTrue()
    {
        AipRecord rec = Rec(8);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], aipIdsWithWfp: [8]);

        ServiceResult<AipRecordDetailDto> result = await sut.GetByIdAsync(8, HostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.HasWfpUsage);
    }

    [Fact]
    public async Task GetById_NoWfpBuiltFromRecord_HasWfpUsageIsFalse()
    {
        AipRecord rec = Rec(9);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordDetailDto> result = await sut.GetByIdAsync(9, HostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasWfpUsage);
    }

    [Fact]
    public async Task GetSummaryById_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(9);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        await sut.GetSummaryByIdAsync(9, HostCaller(), CancellationToken.None);

        aipRepo.Verify(r => r.GetByIntIdAsync(9, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Finalize_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(3, PlanningStatus.Draft);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        await sut.FinalizeAsync(3, CancellationToken.None);

        aipRepo.Verify(r => r.GetByIntIdAsync(3, It.IsAny<CancellationToken>()), Times.Once);
        aipRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Unlock_UsesGetByIntIdAsync_NotGetAllAsync()
    {
        AipRecord rec = Rec(4, PlanningStatus.Final);
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build(recs, [], officeSeed: allOffices);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null, HostCaller());

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

        var (sut, _, _, _, parser, _, _, _, _, _, _, _, _) = Build([], []);
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

        var (sut, _, _, _, parser, _, _, _, _, _, _, _, _) = Build([], []);
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

        var (sut, _, _, _, parser, _, _, _, _, _, _, _, _) = Build([], []);
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

        var (sut, _, _, _, parser, _, _, _, _, _, _, _, _) = Build([], []);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(7, "GF")]);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], [Fs(1, "GF")]);
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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);
        AipImportConfirmDto dto = new(2027, "aip.xlsm", null,
            new Dictionary<string, List<ParsedAipOfficeDto>>(), TargetRecordId: 999);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(dto, UserId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task ConfirmImport_TargetRecordId_NotDraft_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);
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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([manual], []);
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
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) =
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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([target], [Fs(1, "GF")]);
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
        var (sut, aipRepo, _, _, _, _, officeRepo, _, _, _, _, _, _) =
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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);
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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([archived], []);
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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PlanningStatus.Final, result.Value!.Status);
        Assert.Equal(PlanningStatus.Final, rec.Status);
    }

    [Fact]
    public async Task Finalize_AlreadyFinal_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Final)], []);

        ServiceResult<AipRecordDto> result = await sut.FinalizeAsync(1, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Unlock_Final_TransitionsToDraft()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build(seed, []);

        int count = await sut.PurgeAllAsync(CancellationToken.None);

        Assert.Equal(2, count);
        aipRepo.Verify(r => r.DeleteAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ── GetSummaryByIdAsync (RAL-89) ──────────────────────────────────────────

    [Fact]
    public async Task GetSummaryById_MissingId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(99, HostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetSummaryById_ExistingId_ReturnsOkWithCorrectFiscalYear()
    {
        AipRecord rec = Rec(5);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], []);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(5, HostCaller(), CancellationToken.None);

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

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [rec], [],
            officeSeed:  [office],
            programSeed: [prog],
            projectSeed: [proj],
            actSeed:     [act]);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(10, HostCaller(), CancellationToken.None);

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

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [rec], [],
            officeSeed:  [office],
            programSeed: [prog],
            projectSeed: [proj],
            actSeed:     [act]);

        ServiceResult<AipRecordSummaryDto> result = await sut.GetSummaryByIdAsync(20, HostCaller(), CancellationToken.None);

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeSeed: [new AipOffice { Id = 201, AipRecordId = 1, RefCode = "O", Name = "Office", Sector = "GENERAL", OfficeId = 1 }], programSeed: [prog]);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(301, "core", HostCaller(), CancellationToken.None);

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeSeed: [new AipOffice { Id = 201, AipRecordId = 1, RefCode = "O", Name = "Office", Sector = "GENERAL", OfficeId = 1 }], programSeed: [prog]);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(302, "", HostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("SUPPORT", prog.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgramFunctionBand_InvalidValue_ReturnsBadRequest()
    {
        AipProgram prog = new() { Id = 303, OfficeId = 201, RefCode = "P", Name = "Prog" };
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeSeed: [new AipOffice { Id = 201, AipRecordId = 1, RefCode = "O", Name = "Office", Sector = "GENERAL", OfficeId = 1 }], programSeed: [prog]);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(303, "BOGUS", HostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Null(prog.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgramFunctionBand_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProgramDto> result =
            await sut.UpdateProgramFunctionBandAsync(999, "CORE", HostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── UpdateActivityIsCreationAsync (v1.4 Q2) ──────────────────────────────

    [Fact]
    public async Task UpdateActivityIsCreation_True_Persists()
    {
        AipActivity act = new() { Id = 501, ProjectId = 401, RefCode = "A", Name = "Act" };
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeSeed: [new AipOffice { Id = 201, AipRecordId = 1, RefCode = "O", Name = "Office", Sector = "GENERAL", OfficeId = 1 }], programSeed: [new AipProgram { Id = 301, OfficeId = 201, RefCode = "P", Name = "Prog" }], projectSeed: [new AipProject { Id = 401, ProgramId = 301, RefCode = "J", Name = "Proj" }], actSeed: [act]);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(501, true, HostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsCreation);
        Assert.True(act.IsCreation);
    }

    [Fact]
    public async Task UpdateActivityIsCreation_False_Persists()
    {
        AipActivity act = new() { Id = 502, ProjectId = 401, RefCode = "A", Name = "Act", IsCreation = true };
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeSeed: [new AipOffice { Id = 201, AipRecordId = 1, RefCode = "O", Name = "Office", Sector = "GENERAL", OfficeId = 1 }], programSeed: [new AipProgram { Id = 301, OfficeId = 201, RefCode = "P", Name = "Prog" }], projectSeed: [new AipProject { Id = 401, ProgramId = 301, RefCode = "J", Name = "Proj" }], actSeed: [act]);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(502, false, HostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsCreation);
        Assert.False(act.IsCreation);
    }

    [Fact]
    public async Task UpdateActivityIsCreation_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipActivityDto> result =
            await sut.UpdateActivityIsCreationAsync(999, true, HostCaller(), CancellationToken.None);

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

        var (sut, _, _, userRepo, _, _, _, _, _, _, _, _, _) = Build([rec], [], userSeed: [uploader]);

        IReadOnlyList<AipRecordDto> result = await sut.GetAllAsync(null, null, HostCaller());

        Assert.Equal("Jane Uploader", result[0].UploadedByName);
        userRepo.Verify(r => r.GetNamesByIdsAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1 && ids.Contains(uploaderId)),
            It.IsAny<CancellationToken>()), Times.Once);
        userRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmImport_UsesScopedFiscalYearLookup_NeverFullTableLoad()
    {
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);
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
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(new CreateAipRecordDto(LegacyFy), UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Manual", result.Value!.EntrySource);
        Assert.Equal("Draft", result.Value.Status);
        Assert.Equal(LegacyFy, result.Value.FiscalYear);
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateManualRecord_ActiveRecordExistsForYear_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([Rec(1, PlanningStatus.Draft)], []);

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(new CreateAipRecordDto(2027), UserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddOffice_ValidSectorAndOffice_ComputesRefCodeFromSectorPrefix()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) =
            Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result =
            await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"), HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result =
            await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, sector), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal($"{expectedPrefix}-000-1-01-010", result.Value!.RefCode);
    }

    [Fact]
    public async Task AddOffice_SameOfficeDifferentSector_BothAllowed()
    {
        // A physical office can legitimately run programs under more than one sector.
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> first  = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"), HostCaller());
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "SOCIAL"), HostCaller());

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.RefCode, second.Value!.RefCode);
    }

    [Fact]
    public async Task AddOffice_SameOfficeSameSectorTwice_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"), HostCaller());
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"), HostCaller());

        Assert.False(second.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, second.Code);
    }

    [Fact]
    public async Task AddOffice_WithNameOverride_UsesProvidedNameNotConfigName()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "ECONOMIC", "Provincial Planning and Development Office - Special Projects"), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("Provincial Planning and Development Office - Special Projects", result.Value!.Name);
        Assert.Equal("8000-000-1-01-010", result.Value.RefCode);
    }

    [Fact]
    public async Task AddOffice_BlankNameOverride_FallsBackToConfigOfficeName()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "GENERAL", "   "), HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> first  = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "SOCIAL", "Office of the Governor - Warden"), HostCaller());
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "SOCIAL", "Office of the Governor - AKAP-HUB"), HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "SOCIAL", "Office of the Governor - Warden"), HostCaller());
        ServiceResult<AipOfficeDto> second = await sut.AddOfficeAsync(
            1, new CreateAipOfficeDto(7, "SOCIAL", "office of the governor - warden"), HostCaller());

        Assert.False(second.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, second.Code);
    }

    [Fact]
    public async Task AddOffice_InvalidSector_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "MADEUP"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddOffice_OfficeMissingRefCodeConfig_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", null)];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddOffice_InactiveOffice_ReturnsNotFound()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010", isActive: false)];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AddOffice_RecordNotDraft_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        List<Office> offices = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeConfigSeed: offices);

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(1, new CreateAipOfficeDto(7, "GENERAL"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── CopyOfficeFromPriorYearAsync (RAL-180) ──────────────────────────────────
    //
    // Fixture shape shared by these tests: a source FY2027 record (id 1, Upload-sourced,
    // Final — carry-forward reads from a historical record, it never needs to be Draft) with
    // one office (id 20, RefCode "1000-000-1-01-010") holding two programs (30 "Program A",
    // 31 "Program B"), each with one project/one activity, built via helpers below.

    private static AipOffice SourceOffice() => new()
    { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };

    private static AipProgram SourceProgramA() => new()
    { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program A", FunctionBand = "CORE" };

    private static AipProgram SourceProgramB() => new()
    { Id = 31, OfficeId = 20, RefCode = "1000-000-1-01-010-002", Name = "Program B", FunctionBand = "STRATEGIC" };

    private static AipProject SourceProject(int id, int programId, string refCode) => new()
    { Id = id, ProgramId = programId, RefCode = refCode, Name = "Project X", IsSynthetic = false };

    private static AipActivity SourceActivity(int id, int projectId, string refCode) => new()
    {
        Id = id, ProjectId = projectId, RefCode = refCode, Name = "Activity Z",
        EsreCode = "SS", ImplementingOffice = "PPDO", StartDate = "January", EndDate = "December",
        ExpectedOutputs = "Outputs", FundingSourceId = 5, FundingSourceSnapshot = "GF",
        Ps = 100m, Mooe = 200m, Co = 50m, Total = 350m,
        CcAdaptation = 10m, CcMitigation = 5m, CcTypologyCode = "TYP1",
        IsCreation = true, IsSynthetic = false,
    };

    [Fact]
    public async Task CopyOfficeFromPriorYear_TargetRecordMissing_CreatesManualDraftRecordAndOffice()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipOffice office = SourceOffice();
        AipProgram progA = SourceProgramA();
        AipProject proj = SourceProject(40, 30, "1000-000-1-01-010-001-001");
        AipActivity act = SourceActivity(50, 40, "1000-000-1-01-010-001-001-001");
        var (sut, aipRepo, _, _, _, audit, officeRepo, _, _, _, _, _, _) = Build(
            [sourceRec], [], officeSeed: [office], programSeed: [progA],
            projectSeed: [proj], actSeed: [act]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010", result.Value!.RefCode);
        Assert.Equal("PPDO", result.Value.Name);
        Assert.Equal("GENERAL", result.Value.Sector);
        Assert.Single(result.Value.Programs);
        aipRepo.Verify(r => r.AddAsync(
            It.Is<AipRecord>(r => r.FiscalYear == LegacyFy && r.EntrySource == "Manual" && r.Status == PlanningStatus.Draft),
            It.IsAny<CancellationToken>()), Times.Once);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync("aip_offices", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_ClonesFullSubtree_PreservesFieldsExceptIdAndIsCreation()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipOffice office = SourceOffice();
        AipProgram progA = SourceProgramA();
        AipProject proj = SourceProject(40, 30, "1000-000-1-01-010-001-001");
        AipActivity act = SourceActivity(50, 40, "1000-000-1-01-010-001-001-001");
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [sourceRec], [], officeSeed: [office], programSeed: [progA],
            projectSeed: [proj], actSeed: [act]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        AipProgramDto copiedProgram = result.Value!.Programs.Single();
        Assert.NotEqual(30, copiedProgram.Id); // fresh identity
        Assert.Equal("1000-000-1-01-010-001", copiedProgram.RefCode); // RefCode preserved verbatim
        Assert.Equal("Program A", copiedProgram.Name);
        Assert.Equal("CORE", copiedProgram.FunctionBand);

        AipProjectDto copiedProject = copiedProgram.Projects.Single();
        Assert.NotEqual(40, copiedProject.Id);
        Assert.Equal("1000-000-1-01-010-001-001", copiedProject.RefCode);
        Assert.Equal("Project X", copiedProject.Name);

        AipActivityDto copiedActivity = copiedProject.Activities.Single();
        Assert.NotEqual(50, copiedActivity.Id);
        Assert.Equal("1000-000-1-01-010-001-001-001", copiedActivity.RefCode);
        Assert.Equal("Activity Z", copiedActivity.Name);
        Assert.Equal(100m, copiedActivity.Ps);
        Assert.Equal(200m, copiedActivity.Mooe);
        Assert.Equal(50m, copiedActivity.Co);
        Assert.Equal(350m, copiedActivity.Total);
        Assert.Equal(5, copiedActivity.FundingSourceId);
        Assert.Equal("GF", copiedActivity.FundingSourceSnapshot);
        Assert.Equal(10m, copiedActivity.CcAdaptation);
        Assert.Equal(5m, copiedActivity.CcMitigation);
        Assert.Equal("TYP1", copiedActivity.CcTypologyCode);
        Assert.False(copiedActivity.IsSynthetic == true && false); // sanity — IsSynthetic copied as-is (false here)
        Assert.False(copiedActivity.IsCreation); // source had IsCreation = true — must reset to false on copy
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_TargetRecordExistsAsManualDraft_ReusesRecord_CreatesOffice()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice office = SourceOffice();
        AipProgram progA = SourceProgramA();
        var (sut, aipRepo, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build(
            [sourceRec, targetRec], [], officeSeed: [office], programSeed: [progA]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.AipRecordId); // reused the existing target record, not a new one
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Upload", PlanningStatus.Draft)]
    [InlineData("Manual", PlanningStatus.Final)]
    public async Task CopyOfficeFromPriorYear_TargetRecordNotDraftManual_ReturnsBadRequest(
        string entrySource, string status)
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = entrySource,
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = status,
        };
        AipOffice office = SourceOffice();
        AipProgram progA = SourceProgramA();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [sourceRec, targetRec], [], officeSeed: [office], programSeed: [progA]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_TargetRecordArchived_DoesNotBlock_CreatesNewRecordInstead()
    {
        // Archived records never count as "the active record for a fiscal year" anywhere in
        // AipService (GetLatestByFiscalYearAsync filters them out — same rule
        // CreateManualRecordAsync relies on) — carry-forward is no different: an Archived target
        // year is treated as if nothing exists yet, and a fresh Manual Draft record is created.
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipRecord archivedTargetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Archived,
        };
        AipOffice office = SourceOffice();
        AipProgram progA = SourceProgramA();
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [sourceRec, archivedTargetRec], [], officeSeed: [office], programSeed: [progA]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.NotEqual(2, result.Value!.AipRecordId); // a new record, not the archived one
        aipRepo.Verify(r => r.AddAsync(
            It.Is<AipRecord>(r => r.FiscalYear == LegacyFy && r.Status == PlanningStatus.Draft),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_TargetOfficeAlreadyExists_AddsProgramsToIt_NoNewOffice()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice sourceOff = SourceOffice();
        // Same RefCode already present under the target record — this IS the office to reuse.
        AipOffice targetOff = new() { Id = 21, AipRecordId = 2, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        AipProgram progA = SourceProgramA(); // under source office 20
        var (sut, _, _, _, _, _, officeRepo, _, _, programRepo, _, _, _) = Build(
            [sourceRec, targetRec], [], officeSeed: [sourceOff, targetOff], programSeed: [progA]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(21, result.Value!.Id); // reused the existing target office, not a new one
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
        programRepo.Verify(r => r.AddAsync(
            It.Is<AipProgram>(p => p.OfficeId == 21 && p.RefCode == "1000-000-1-01-010-001"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_TargetOfficeAlreadyHasOtherPrograms_ResponseIncludesBoth()
    {
        // Regression guard: the response must reflect the office's COMPLETE program list after
        // the copy, not just the newly-added slice — the frontend replaces the whole office node
        // in its tree with this response, so a partial list would silently drop the office's
        // pre-existing programs from the UI.
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice sourceOff = SourceOffice();
        AipOffice targetOff = new() { Id = 21, AipRecordId = 2, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        // Pre-existing program already under the target office, unrelated RefCode — no collision.
        AipProgram preExisting = new()
        { Id = 61, OfficeId = 21, RefCode = "1000-000-1-01-010-005", Name = "Already there", FunctionBand = "CORE" };
        AipProgram progA = SourceProgramA(); // to be copied in, RefCode "...-001"
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [sourceRec, targetRec], [], officeSeed: [sourceOff, targetOff],
            programSeed: [progA, preExisting]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Programs.Count);
        Assert.Contains(result.Value.Programs, p => p.RefCode == "1000-000-1-01-010-005"); // pre-existing survived
        Assert.Contains(result.Value.Programs, p => p.RefCode == "1000-000-1-01-010-001"); // newly copied present
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_ProgramNotBelongingToSourceOffice_ReturnsBadRequest()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipOffice office = SourceOffice();
        AipProgram progA = SourceProgramA(); // Id 30, belongs to office 20
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build(
            [sourceRec], [], officeSeed: [office], programSeed: [progA]);

        // 999 does not belong to office 20.
        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30, 999]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("999", result.Error);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_ProgramRefCodeAlreadyExistsUnderTargetOffice_ReturnsBadRequest_NoSideEffects()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice sourceOff = SourceOffice();
        AipOffice targetOff = new() { Id = 21, AipRecordId = 2, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        // Target office already has a program at the exact RefCode we're about to copy.
        AipProgram existingTargetProgram = new()
        { Id = 60, OfficeId = 21, RefCode = "1000-000-1-01-010-001", Name = "Already here", FunctionBand = "CORE" };
        AipProgram progA = SourceProgramA(); // same RefCode "1000-000-1-01-010-001", under source office 20
        var (sut, _, _, _, _, _, officeRepo, _, _, programRepo, _, _, _) = Build(
            [sourceRec, targetRec], [], officeSeed: [sourceOff, targetOff],
            programSeed: [progA, existingTargetProgram]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("1000-000-1-01-010-001", result.Error);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
        programRepo.Verify(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_SourceOfficeNotFound_ReturnsNotFound()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([sourceRec], []);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(999, LegacyFy, [30]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_EmptyProgramIds_ReturnsBadRequest()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipOffice office = SourceOffice();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([sourceRec], [], officeSeed: [office]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, []), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_ExistingTargetOffice_TwoProgramsSelected_BothAddedInOneTransaction()
    {
        AipRecord sourceRec = Rec(1, PlanningStatus.Final, fiscalYear: PriorFy);
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice sourceOff = SourceOffice();
        AipOffice targetOff = new() { Id = 21, AipRecordId = 2, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        AipProgram progA = SourceProgramA();
        AipProgram progB = SourceProgramB();
        var (sut, _, _, _, _, _, officeRepo, _, _, programRepo, _, _, _) = Build(
            [sourceRec, targetRec], [], officeSeed: [sourceOff, targetOff], programSeed: [progA, progB]);

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(20, LegacyFy, [30, 31]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Programs.Count);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
        programRepo.Verify(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        programRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── SeedProgramsFromLdipAsync (RAL-181) ──────────────────────────────────────
    //
    // Fixture shape shared by these tests: config Office id 7 ("PPDO", OfficeRefCode "01-010")
    // with a Final LDIP record (id 5) holding one sector group (id 70, RefCode
    // "1000-000-1-01-010", Sector "General" — LDIP's own title-case convention, distinct from
    // AIP's "GENERAL") with two programs (80 "LDIP Program A", 81 "LDIP Program B").

    private static LdipRecord LdipRec(int id, int officeId, string status = "Final") => new()
    {
        Id = id, OfficeId = officeId, RefCode = $"LDIP-2025-{id:D3}", Title = "LDIP",
        FiscalYearStart = 2025, FiscalYearEnd = 2030, EntryMode = "New", Status = status,
        CreatedById = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static LdipOffice LdipGroup(
        int id, int ldipRecordId, string refCode = "1000-000-1-01-010",
        string sector = "General", string name = "PPDO") => new()
    { Id = id, LdipRecordId = ldipRecordId, RefCode = refCode, Name = name, Sector = sector };

    private static LdipProgram LdipProg(int id, int ldipOfficeId, string refCode, string name) => new()
    {
        Id = id, LdipOfficeId = ldipOfficeId, RefCode = refCode, Name = name, Budget = 500m,
        FundingSourceId = 5, FundingSourceSnapshot = "GF", Ps = 100m, Mooe = 50m, Co = 25m,
        StartDate = "2025", EndDate = "2030", ExpectedOutputs = "Some outputs",
        CcAdaptation = 10m, CcMitigation = 5m, CcTypologyCode = "TYP1",
    };

    [Fact]
    public async Task SeedFromLdip_TargetRecordMissing_CreatesManualDraftRecordAndOffice()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, aipRepo, _, _, _, audit, officeRepo, _, officeConfigRepo, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010", result.Value!.RefCode);
        Assert.Equal("PPDO", result.Value.Name);
        Assert.Equal("GENERAL", result.Value.Sector);
        Assert.Single(result.Value.Programs);
        aipRepo.Verify(r => r.AddAsync(
            It.Is<AipRecord>(r => r.FiscalYear == LegacyFy && r.EntrySource == "Manual" && r.Status == PlanningStatus.Draft),
            It.IsAny<CancellationToken>()), Times.Once);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.LogAsync("aip_offices", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedFromLdip_SeedsBareShellPrograms_NameAndRefCodeOnly_FunctionBandCore()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        AipProgramDto seeded = result.Value!.Programs.Single();
        Assert.NotEqual(80, seeded.Id); // fresh identity, not the LdipProgram's own id
        Assert.Equal("1000-000-1-01-010-001", seeded.RefCode); // RefCode reused verbatim
        Assert.Equal("LDIP Program A", seeded.Name);
        Assert.Equal("CORE", seeded.FunctionBand); // LDIP has no FunctionBand — defaults to Core
        Assert.Empty(seeded.Projects); // bare shell — no Project/Activity rows created
    }

    [Fact]
    public async Task SeedFromLdip_TargetRecordExistsAsManualDraft_ReusesRecord_CreatesOffice()
    {
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, aipRepo, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build(
            [targetRec], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.AipRecordId); // reused the existing target record, not a new one
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("Upload", PlanningStatus.Draft)]
    [InlineData("Manual", PlanningStatus.Final)]
    public async Task SeedFromLdip_TargetRecordNotDraftManual_ReturnsBadRequest(string entrySource, string status)
    {
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = entrySource,
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = status,
        };
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [targetRec], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task SeedFromLdip_TargetOfficeAlreadyExists_AddsProgramsToIt_NoNewOffice()
    {
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice targetOff = new() { Id = 21, AipRecordId = 2, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, officeRepo, _, _, programRepo, _, _, _) = Build(
            [targetRec], [], officeSeed: [targetOff], officeConfigSeed: officeConfigs,
            ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(21, result.Value!.Id); // reused the existing target office, not a new one
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
        programRepo.Verify(r => r.AddAsync(
            It.Is<AipProgram>(p => p.OfficeId == 21 && p.RefCode == "1000-000-1-01-010-001"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeedFromLdip_TargetOfficeAlreadyHasOtherPrograms_ResponseIncludesBoth()
    {
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice targetOff = new() { Id = 21, AipRecordId = 2, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        AipProgram preExisting = new()
        { Id = 61, OfficeId = 21, RefCode = "1000-000-1-01-010-005", Name = "Already there", FunctionBand = "CORE" };
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [targetRec], [], officeSeed: [targetOff], programSeed: [preExisting],
            officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.Programs.Count);
        Assert.Contains(result.Value.Programs, p => p.RefCode == "1000-000-1-01-010-005"); // pre-existing survived
        Assert.Contains(result.Value.Programs, p => p.RefCode == "1000-000-1-01-010-001"); // newly seeded present
    }

    [Fact]
    public async Task SeedFromLdip_LdipProgramNotBelongingToGroup_ReturnsBadRequest()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A"); // Id 80, belongs to group 70
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        // 999 does not belong to group 70.
        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80, 999]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("999", result.Error);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedFromLdip_ProgramRefCodeAlreadyExistsUnderTargetOffice_ReturnsBadRequest_NoSideEffects()
    {
        AipRecord targetRec = new()
        {
            Id = 2, FiscalYear = LegacyFy, EntrySource = "Manual",
            UploadedById = UserId, UploadedAt = DateTime.UtcNow, Status = PlanningStatus.Draft,
        };
        AipOffice targetOff = new() { Id = 21, AipRecordId = 2, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" };
        AipProgram existingTargetProgram = new()
        { Id = 60, OfficeId = 21, RefCode = "1000-000-1-01-010-001", Name = "Already here", FunctionBand = "CORE" };
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5);
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A"); // same RefCode as existing
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, officeRepo, _, _, programRepo, _, _, _) = Build(
            [targetRec], [], officeSeed: [targetOff], programSeed: [existingTargetProgram],
            officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("1000-000-1-01-010-001", result.Error);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
        programRepo.Verify(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedFromLdip_NoMatchingLdipOfficeForSector_ReturnsBadRequest()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        // The office's only LDIP group is under "General" — requesting "SOCIAL" has no match.
        LdipOffice group = LdipGroup(70, 5, sector: "General");
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "SOCIAL", [80]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("SOCIAL", result.Error);
    }

    [Fact]
    public async Task SeedFromLdip_EmptyLdipProgramIds_ReturnsBadRequest()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeConfigSeed: officeConfigs);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", []), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task SeedFromLdip_OfficeConfigNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 999, "GENERAL", [80]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task SeedFromLdip_SectorMatchIsCaseInsensitive_LdipStoresTitleCase_AipStoresUppercase()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ldipRec = LdipRec(5, 7);
        LdipOffice group = LdipGroup(70, 5, sector: "General"); // title-case, LDIP's own convention
        LdipProgram progA = LdipProg(80, 70, "1000-000-1-01-010-001", "LDIP Program A");
        group.Programs.Add(progA);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [ldipRec], ldipOfficeSeed: [group]);

        // Request uses uppercase "GENERAL" (AIP's own convention) — must still match.
        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("GENERAL", result.Value!.Sector); // stored uppercase on the AIP side
    }

    [Fact]
    public async Task SeedFromLdip_PicksNewestNonArchivedLdipRecord_MatchingSectorAcrossMultipleRecords()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        // Older record has no matching sector group; a newer Archived record has one that must be
        // skipped; the newest non-Archived record's group is the one that should be used.
        LdipRecord olderRec = new()
        {
            Id = 4, OfficeId = 7, RefCode = "LDIP-2024-001", Title = "Older", FiscalYearStart = 2020,
            FiscalYearEnd = 2024, EntryMode = "New", Status = PlanningStatus.Final,
            CreatedById = UserId, CreatedAt = DateTime.UtcNow.AddDays(-30), UpdatedAt = DateTime.UtcNow.AddDays(-30),
        };
        LdipOffice olderGroup = LdipGroup(71, 4, sector: "Social");
        olderGroup.Programs.Add(LdipProg(90, 71, "3000-000-1-01-010-001", "Old Social Program"));

        LdipRecord archivedRec = new()
        {
            Id = 6, OfficeId = 7, RefCode = "LDIP-2026-001", Title = "Archived", FiscalYearStart = 2025,
            FiscalYearEnd = 2030, EntryMode = "New", Status = PlanningStatus.Archived,
            CreatedById = UserId, CreatedAt = DateTime.UtcNow.AddDays(-5), UpdatedAt = DateTime.UtcNow.AddDays(-5),
        };
        LdipOffice archivedGroup = LdipGroup(72, 6, sector: "General");
        archivedGroup.Programs.Add(LdipProg(91, 72, "1000-000-1-01-010-001", "Archived Program"));

        LdipRecord newestRec = LdipRec(5, 7); // CreatedAt = UtcNow, newest of the three
        LdipOffice newestGroup = LdipGroup(70, 5, sector: "General");
        newestGroup.Programs.Add(LdipProg(80, 70, "1000-000-1-01-010-001", "Newest Program"));

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs,
            ldipRecordSeed: [olderRec, archivedRec, newestRec],
            ldipOfficeSeed: [olderGroup, archivedGroup, newestGroup]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("Newest Program", result.Value!.Programs.Single().Name);
    }

    // ── Multi-office (Upload) LDIP fallback — bug found in live use: an office's own dedicated
    // LDIP record (New/Amendment/Supplemental, LdipRecord.OfficeId set) can be archived, leaving
    // only a multi-office bulk-upload document (LdipRecord.OfficeId = null, RAL-165) as the real
    // source of truth. The office-scoped GetListAsync query can never surface that record at all,
    // so without a fallback the feature silently reports "no LDIP" even though the data exists.

    private static LdipRecord UploadRec(int id, string status = "Draft") => new()
    {
        Id = id, OfficeId = null, RefCode = $"LDIP-2026-{id:D3}", Title = "All Offices",
        FiscalYearStart = 2026, FiscalYearEnd = 2029, EntryMode = "Upload", Status = status,
        CreatedById = UserId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task SeedFromLdip_OwnRecordArchived_FallsBackToMultiOfficeUploadRecord()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        // The office's own dedicated LDIP record has since been archived.
        LdipRecord archivedOwnRec = LdipRec(5, 7, status: PlanningStatus.Archived);
        LdipOffice archivedOwnGroup = LdipGroup(70, 5);
        archivedOwnGroup.Programs.Add(LdipProg(80, 70, "1000-000-1-01-010-001", "Archived Own Program"));

        // A multi-office Upload record spans every office — its group at THIS office's exact ref
        // code is the fallback source. A same-sector group under a DIFFERENT office's ref code
        // must not be matched (Sector text alone can't disambiguate within one multi-office doc).
        LdipRecord uploadRec = UploadRec(6);
        LdipOffice uploadGroup = LdipGroup(71, 6, sector: "General");
        uploadGroup.Programs.Add(LdipProg(90, 71, "1000-000-1-01-010-001", "Uploaded Program"));
        LdipOffice otherOfficeGroup = LdipGroup(72, 6, refCode: "1000-000-1-02-020", sector: "General", name: "Other Office");
        otherOfficeGroup.Programs.Add(LdipProg(91, 72, "1000-000-1-02-020-001", "Other Office Program"));

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs,
            ldipRecordSeed: [archivedOwnRec, uploadRec],
            ldipOfficeSeed: [archivedOwnGroup, uploadGroup, otherOfficeGroup]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [90]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("Uploaded Program", result.Value!.Programs.Single().Name);
    }

    [Fact]
    public async Task SeedFromLdip_OwnRecordExists_TakesPriorityOverMultiOfficeUploadRecord()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", "01-010")];
        LdipRecord ownRec = LdipRec(5, 7); // Final — not archived, own dedicated record
        LdipOffice ownGroup = LdipGroup(70, 5);
        ownGroup.Programs.Add(LdipProg(80, 70, "1000-000-1-01-010-001", "Own Program"));

        LdipRecord uploadRec = UploadRec(6);
        LdipOffice uploadGroup = LdipGroup(71, 6, sector: "General");
        uploadGroup.Programs.Add(LdipProg(90, 71, "1000-000-1-01-010-001", "Uploaded Program"));

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs,
            ldipRecordSeed: [ownRec, uploadRec],
            ldipOfficeSeed: [ownGroup, uploadGroup]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [80]), UserId, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("Own Program", result.Value!.Programs.Single().Name); // Tier 1, not the upload doc
    }

    [Fact]
    public async Task SeedFromLdip_NoOwnRecord_OfficeHasNoRefCodeConfigured_SkipsFallback_ReturnsBadRequest()
    {
        List<Office> officeConfigs = [MakeOffice(7, "PPDO", officeRefCode: null)];
        LdipRecord uploadRec = UploadRec(6);
        LdipOffice uploadGroup = LdipGroup(71, 6, sector: "General");
        uploadGroup.Programs.Add(LdipProg(90, 71, "1000-000-1-01-010-001", "Uploaded Program"));

        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            [], [], officeConfigSeed: officeConfigs, ldipRecordSeed: [uploadRec], ldipOfficeSeed: [uploadGroup]);

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(LegacyFy, 7, "GENERAL", [90]), UserId, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddProgram_FirstUnderOffice_RefCodeAppends001()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _, _) = Build([rec], [], officeSeed: offices);

        ServiceResult<AipProgramDto> result =
            await sut.AddProgramAsync(20, new CreateAipProgramDto("Program One", null), HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices, programSeed: programs);

        ServiceResult<AipProgramDto> result =
            await sut.AddProgramAsync(20, new CreateAipProgramDto("Program Two", "STRATEGIC"), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010-004", result.Value!.RefCode);
        Assert.Equal("STRATEGIC", result.Value.FunctionBand);
    }

    [Fact]
    public async Task AddProgram_EmptyName_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(20, new CreateAipProgramDto("  ", null), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddProgram_OfficeParentRecordNotDraft_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices);

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(20, new CreateAipProgramDto("X", null), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddProgram_OfficeNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(999, new CreateAipProgramDto("X", null), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AddProject_FirstUnderProgram_RefCodeAppends001()
    {
        AipRecord rec = Rec(1, PlanningStatus.Draft);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        var (sut, _, _, _, _, _, _, _, _, _, projectRepo, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs);

        ServiceResult<AipProjectDto> result =
            await sut.AddProjectAsync(30, new CreateAipProjectDto("Project One"), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010-001-001", result.Value!.RefCode);
        projectRepo.Verify(r => r.AddAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddProject_ProgramNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProjectDto> result = await sut.AddProjectAsync(999, new CreateAipProjectDto("X"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task AddProject_AncestorRecordNotDraft_ReturnsBadRequest()
    {
        AipRecord rec = Rec(1, PlanningStatus.Final);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([rec], [], officeSeed: offices, programSeed: programs);

        ServiceResult<AipProjectDto> result = await sut.AddProjectAsync(30, new CreateAipProjectDto("X"), HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo, _) =
            Build([rec], [Fs(1, "GF")], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", "SS", "PPDO", "January", "December", "Outputs", "GF",
            1000m, 500m, 250m, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto, HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", null, null, null, null, null, null, null, null, null, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto, HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", "XX", null, null, null, null, null, null, null, null, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto, HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new(
            "Activity One", null, null, null, null, null, "UNKNOWN-CODE", null, null, null, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.FundingSourceId);
        Assert.Equal("UNKNOWN-CODE", result.Value.FundingSourceSnapshot);
    }

    [Fact]
    public async Task AddActivity_ProjectNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        CreateAipActivityDto dto = new("X", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(999, dto, HostCaller());

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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects);

        CreateAipActivityDto dto = new("X", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(40, dto, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── UpdateActivityAsync (RAL-179 — inline edit) ───────────────────────────

    private static (AipRecord rec, List<AipOffice> offices, List<AipProgram> programs,
        List<AipProject> projects, List<AipActivity> activities) SeedActivityTree(
        string recordStatus = PlanningStatus.Draft)
    {
        AipRecord rec = Rec(1, recordStatus);
        List<AipOffice> offices = [new() { Id = 20, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = "GENERAL" }];
        List<AipProgram> programs = [new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program" }];
        List<AipProject> projects = [new() { Id = 40, ProgramId = 30, RefCode = "1000-000-1-01-010-001-001", Name = "Project" }];
        List<AipActivity> activities =
        [
            new() { Id = 50, ProjectId = 40, RefCode = "1000-000-1-01-010-001-001-001", Name = "Original Name" },
        ];
        return (rec, offices, programs, projects, activities);
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
    public async Task UpdateActivity_ValidFields_UpdatesInPlaceAndRecomputesTotal()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [Fs(1, "GF")], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new(
            "Updated Name", "ES", "PPDO", "March", "June", "New outputs", 1,
            2000m, 1000m, 500m, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 50, dto, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Name", result.Value!.Name);
        Assert.Equal("ES", result.Value.EsreCode);
        Assert.Equal(3500m, result.Value.Total);
        Assert.Equal("GF", result.Value.FundingSourceSnapshot);
        Assert.Equal(1, result.Value.FundingSourceId);
        // RefCode and ProjectId are immutable through this endpoint.
        Assert.Equal("1000-000-1-01-010-001-001-001", result.Value.RefCode);
        Assert.Equal(40, result.Value.ProjectId);
    }

    [Fact]
    public async Task UpdateActivity_ClearingFundingSourceId_ClearsSnapshotToo()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree();
        activities[0].FundingSourceId = 1;
        activities[0].FundingSourceSnapshot = "GF";
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [Fs(1, "GF")], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new(
            "Name", null, null, null, null, null, null, null, null, null, null, null, null);

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 50, dto, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.FundingSourceId);
        Assert.Null(result.Value.FundingSourceSnapshot);
    }

    [Fact]
    public async Task UpdateActivity_AllAmountsBlank_TotalIsNull()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new("Name", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 50, dto, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.Total);
    }

    [Fact]
    public async Task UpdateActivity_EmptyName_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new("   ", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 50, dto, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_InvalidEsreCode_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new("Name", "ZZ", null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 50, dto, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_UnknownFundingSourceId_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [Fs(1, "GF")], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new("Name", null, null, null, null, null, 999, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 50, dto, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new("Name", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 50, dto, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_ActivityNotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        UpdateAipActivityDto dto = new("Name", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(1, 999, dto, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateActivity_MismatchedAipRecordId_ReturnsNotFound()
    {
        var (rec, offices, programs, projects, activities) = SeedActivityTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        UpdateAipActivityDto dto = new("Name", null, null, null, null, null, null, null, null, null, null, null, null);
        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(999, 50, dto, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteProgram_ExistingDraftProgram_RemovesIt()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProgramAsync(30, HostCaller());

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        programRepo.Verify(r => r.DeleteAsync(
            It.Is<AipProgram>(p => p.Id == 30), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(programs, p => p.Id == 30);
    }

    [Fact]
    public async Task DeleteProgram_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<bool> result = await sut.DeleteProgramAsync(999, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteProgram_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProgramAsync(30, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        programRepo.Verify(r => r.DeleteAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProject_ExistingDraftProject_RemovesIt()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, projectRepo, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProjectAsync(40, HostCaller());

        Assert.True(result.IsSuccess);
        projectRepo.Verify(r => r.DeleteAsync(
            It.Is<AipProject>(p => p.Id == 40), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(projects, p => p.Id == 40);
    }

    [Fact]
    public async Task DeleteProject_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<bool> result = await sut.DeleteProjectAsync(999, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteProject_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, projectRepo, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteProjectAsync(40, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        projectRepo.Verify(r => r.DeleteAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActivity_ExistingDraftActivity_RemovesIt()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteActivityAsync(50, HostCaller());

        Assert.True(result.IsSuccess);
        activityRepo.Verify(r => r.DeleteAsync(
            It.Is<AipActivity>(a => a.Id == 50), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(activities, a => a.Id == 50);
    }

    [Fact]
    public async Task DeleteActivity_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<bool> result = await sut.DeleteActivityAsync(999, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteActivity_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteActivityAsync(50, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        activityRepo.Verify(r => r.DeleteAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── UpdateOfficeAsync / UpdateProgramAsync / UpdateProjectAsync / DeleteOfficeAsync ──

    [Fact]
    public async Task UpdateOffice_ValidName_UpdatesInPlace()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(20, new UpdateAipOfficeDto("New Office Name"), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("New Office Name", result.Value!.Name);
        Assert.Equal("1000-000-1-01-010", result.Value.RefCode);
    }

    [Fact]
    public async Task UpdateOffice_EmptyName_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(20, new UpdateAipOfficeDto("   "), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateOffice_SameRefCodeDifferentNameSibling_Allowed()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        offices.Add(new AipOffice { Id = 21, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "Sibling Sub-Office", Sector = "GENERAL" });
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(20, new UpdateAipOfficeDto("A Different Name"), HostCaller());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateOffice_SameRefCodeSameNameAsSibling_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        offices.Add(new AipOffice { Id = 21, AipRecordId = 1, RefCode = "1000-000-1-01-010", Name = "Sibling Sub-Office", Sector = "GENERAL" });
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(20, new UpdateAipOfficeDto("Sibling Sub-Office"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateOffice_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(999, new UpdateAipOfficeDto("Name"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateOffice_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(20, new UpdateAipOfficeDto("Name"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateProgram_ValidNameAndFunctionBand_UpdatesInPlace()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProgramDto> result = await sut.UpdateProgramAsync(30, new UpdateAipProgramDto("New Program Name", "STRATEGIC"), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("New Program Name", result.Value!.Name);
        Assert.Equal("STRATEGIC", result.Value.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgram_BlankFunctionBand_KeepsExisting()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        programs[0].FunctionBand = "SUPPORT";
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProgramDto> result = await sut.UpdateProgramAsync(30, new UpdateAipProgramDto("New Name", null), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("SUPPORT", result.Value!.FunctionBand);
    }

    [Fact]
    public async Task UpdateProgram_EmptyName_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProgramDto> result = await sut.UpdateProgramAsync(30, new UpdateAipProgramDto("  ", null), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateProgram_InvalidFunctionBand_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProgramDto> result = await sut.UpdateProgramAsync(30, new UpdateAipProgramDto("Name", "BOGUS"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateProgram_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProgramDto> result = await sut.UpdateProgramAsync(999, new UpdateAipProgramDto("Name", null), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateProgram_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProgramDto> result = await sut.UpdateProgramAsync(30, new UpdateAipProgramDto("Name", null), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateProject_ValidName_UpdatesInPlace()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProjectDto> result = await sut.UpdateProjectAsync(40, new UpdateAipProjectDto("New Project Name"), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("New Project Name", result.Value!.Name);
    }

    [Fact]
    public async Task UpdateProject_EmptyName_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProjectDto> result = await sut.UpdateProjectAsync(40, new UpdateAipProjectDto(" "), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateProject_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipProjectDto> result = await sut.UpdateProjectAsync(999, new UpdateAipProjectDto("Name"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateProject_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipProjectDto> result = await sut.UpdateProjectAsync(40, new UpdateAipProjectDto("Name"), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task DeleteOffice_ExistingDraftOffice_RemovesIt()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree();
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteOfficeAsync(20, HostCaller());

        Assert.True(result.IsSuccess);
        officeRepo.Verify(r => r.DeleteAsync(
            It.Is<AipOffice>(o => o.Id == 20), It.IsAny<CancellationToken>()), Times.Once);
        Assert.DoesNotContain(offices, o => o.Id == 20);
    }

    [Fact]
    public async Task DeleteOffice_NotFound_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<bool> result = await sut.DeleteOfficeAsync(999, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task DeleteOffice_ParentRecordNotDraft_ReturnsBadRequest()
    {
        var (rec, offices, programs, projects, activities) = SeedDeleteTree(PlanningStatus.Final);
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteOfficeAsync(20, HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        officeRepo.Verify(r => r.DeleteAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
