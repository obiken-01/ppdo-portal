using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// AIP Entry on an SAIP's working copy (v1.10.0 — PPDO-247): the delete rule (decision 5), the copy
/// lookup on by-id writes, and the tree read that swaps in the copy (spec §4).
///
/// <para>
/// ⚠️ <b>Red-tested.</b> Removing each <c>CheckSaipDelete</c> fails its delete test; removing the
/// SAIP short-circuit in <c>RenumbersOnDeleteAsync</c> fails the no-renumber test; removing the
/// <c>GetDraftGroupByIdAsync</c> fallback in <c>AipGroupLookup</c> fails
/// <see cref="Write_OnACopyRowTheFilteredLookupCannotSee_ResolvesThroughTheCopyLookup"/>; removing the
/// swap fails <see cref="GetById_ForOneOffice_WithAnSaipOpen_ReturnsTheCopy"/>.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private const int CopySaipId = 9;

    /// <summary><see cref="SeedSiblingTree"/>, made into an SAIP copy: every row copied except 51.</summary>
    private static (AipRecord rec, List<AipOffice> offices, List<AipProgram> programs,
        List<AipProject> projects, List<AipActivity> activities) SeedCopy()
    {
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        offices[0].SaipId = CopySaipId;
        offices[0].SourceId = 2;
        foreach (AipProgram p in programs) p.SourceId = p.Id + 1000;
        foreach (AipProject j in projects) j.SourceId = j.Id + 1000;
        foreach (AipActivity a in activities) a.SourceId = a.Id == 51 ? null : a.Id + 1000;   // 51: added in the SAIP
        return (rec, offices, programs, projects, activities);
    }

    // ── The delete rule ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteActivity_InSaipCopy_ApprovedRow_IsRefusedWithTheSpecMessage()
    {
        var (rec, offices, programs, projects, activities) = SeedCopy();
        var (sut, _, _, _, _, _, _, _, _, _, _, activityRepo, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(52, HostCaller(), expectedRowVersion: AipTestVersion.Any);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal(AipWriteGuard.SaipApprovedDeleteMessage, result.Error);
        activityRepo.Verify(r => r.DeleteAsync(It.IsAny<AipActivity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteActivity_InSaipCopy_RowAddedInTheSaip_IsDeleted_AndNothingIsRenumbered()
    {
        var (rec, offices, programs, projects, activities) = SeedCopy();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller(), expectedRowVersion: AipTestVersion.Any);

        Assert.True(result.IsSuccess, result.Error);
        // In the AIP, deleting -001-002 would move -001-003 up. An SAIP never renumbers (spec §7).
        Assert.Empty(result.Value!.Renumbered);
        Assert.Equal($"{ProgramCode}-001-003", activities.Single(a => a.Id == 52).RefCode);
    }

    [Fact]
    public async Task DeleteProject_InSaipCopy_ApprovedRow_IsRefused()
    {
        var (rec, offices, programs, projects, activities) = SeedCopy();
        var (sut, _, _, _, _, _, _, _, _, _, projectRepo, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProjectAsync(41, HostCaller());

        Assert.Equal(AipWriteGuard.SaipApprovedDeleteMessage, result.Error);
        projectRepo.Verify(r => r.DeleteAsync(It.IsAny<AipProject>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteProject_InSaipCopy_ProjectAddedInTheSaip_IsDeleted()
    {
        var (rec, offices, programs, projects, activities) = SeedCopy();
        projects.Single(j => j.Id == 41).SourceId = null;
        activities.Single(a => a.Id == 60).SourceId = null;
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProjectAsync(41, HostCaller());

        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(result.Value!.Renumbered);
    }

    [Fact]
    public async Task DeleteProgram_InSaipCopy_ApprovedRow_IsRefused()
    {
        var (rec, offices, programs, projects, activities) = SeedCopy();
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteProgramAsync(30, HostCaller());

        Assert.Equal(AipWriteGuard.SaipApprovedDeleteMessage, result.Error);
        programRepo.Verify(r => r.DeleteAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteOffice_InSaipCopy_IsRefused()
    {
        var (rec, offices, programs, projects, activities) = SeedCopy();
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<bool> result = await sut.DeleteOfficeAsync(20, HostCaller());

        Assert.Equal(AipWriteGuard.SaipApprovedDeleteMessage, result.Error);
        officeRepo.Verify(r => r.DeleteAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateOffice_InSaipCopy_IsRefused()
    {
        var (rec, offices, programs, projects, activities) = SeedCopy();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipOfficeDto> result = await sut.UpdateOfficeAsync(20, new UpdateAipOfficeDto("Renamed"), HostCaller());

        Assert.Equal(AipService.SaipOfficeGroupMessage, result.Error);
        Assert.Equal("PPDO", offices[0].Name);
    }

    [Fact]
    public async Task DeleteActivity_OutsideAnSaip_StillRenumbersAsBefore()
    {
        // The SAIP rules must not leak into the AIP: same tree, no saip_id, the usual renumber.
        var (rec, offices, programs, projects, activities) = SeedSiblingTree(EnteredFy);
        foreach (AipActivity a in activities) a.SourceId = 1;   // a source alone means nothing outside an SAIP
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller(), expectedRowVersion: AipTestVersion.Any);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Single(result.Value!.Renumbered);
    }

    // ── The copy lookup ───────────────────────────────────────────────────────

    [Fact]
    public async Task Write_OnACopyRowTheFilteredLookupCannotSee_ResolvesThroughTheCopyLookup()
    {
        // In the database GetOfficeByIdAsync is blind to copies (the query filter); the write reaches
        // the copy's group through GetDraftGroupByIdAsync instead.
        var (rec, offices, programs, projects, activities) = SeedCopy();
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: activities);
        aipRepo.Setup(r => r.GetOfficeByIdAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync((AipOffice?)null);
        aipRepo.Setup(r => r.GetDraftGroupByIdAsync(20, It.IsAny<CancellationToken>())).ReturnsAsync(offices[0]);

        ServiceResult<AipDeleteResultDto> result = await sut.DeleteActivityAsync(51, HostCaller(), expectedRowVersion: AipTestVersion.Any);

        Assert.True(result.IsSuccess, result.Error);
    }

    // ── The tree read ─────────────────────────────────────────────────────────

    /// <summary>An approved group (20) for config office 3 and its copy (21), each with one program.</summary>
    private static (AipRecord rec, List<AipOffice> approved, AipOffice copy, List<AipProgram> programs) SeedApprovedAndCopy()
    {
        AipRecord rec = Rec(1, fiscalYear: EnteredFy);
        List<AipOffice> approved =
        [
            new() { Id = 20, AipRecordId = 1, OfficeId = 3, RefCode = "1000-000-1-01-020", Name = "PTO", Sector = "GENERAL",
                    WorkflowStatus = AipWorkflowStatus.Consolidated },
        ];
        AipOffice copy = new()
        {
            Id = 21, AipRecordId = 1, OfficeId = 3, RefCode = "1000-000-1-01-020", Name = "PTO", Sector = "GENERAL",
            WorkflowStatus = AipWorkflowStatus.Draft, SaipId = CopySaipId, SourceId = 20,
        };
        List<AipProgram> programs =
        [
            new() { Id = 30, OfficeId = 20, RefCode = "1000-000-1-01-020-001", Name = "Approved program" },
            new() { Id = 31, OfficeId = 21, RefCode = "1000-000-1-01-020-001", Name = "Copy program", SourceId = 30 },
        ];
        return (rec, approved, copy, programs);
    }

    [Fact]
    public async Task GetById_ForOneOffice_WithAnSaipOpen_ReturnsTheCopy()
    {
        var (rec, approved, copy, programs) = SeedApprovedAndCopy();
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: approved, programSeed: programs);
        aipRepo.Setup(r => r.GetDraftGroupsForOfficeAsync(1, 3, It.IsAny<CancellationToken>())).ReturnsAsync([copy]);

        ServiceResult<AipRecordDetailDto> result = await sut.GetByIdAsync(1, HostCaller(), onlyOfficeId: 3);

        Assert.True(result.IsSuccess, result.Error);
        AipOfficeDto office = Assert.Single(result.Value!.Offices);
        Assert.Equal(21, office.Id);
        Assert.Equal(CopySaipId, office.SaipId);
        AipProgramDto program = Assert.Single(office.Programs);
        Assert.Equal(("Copy program", (int?)30), (program.Name, program.SourceId));
    }

    [Fact]
    public async Task GetById_RecordWide_StaysOnTheApprovedTree_EvenWithAnSaipOpen()
    {
        var (rec, approved, copy, programs) = SeedApprovedAndCopy();
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: approved, programSeed: programs);
        aipRepo.Setup(r => r.GetDraftGroupsForOfficeAsync(1, 3, It.IsAny<CancellationToken>())).ReturnsAsync([copy]);

        ServiceResult<AipRecordDetailDto> result = await sut.GetByIdAsync(1, HostCaller());

        AipOfficeDto office = Assert.Single(result.Value!.Offices);
        Assert.Equal(20, office.Id);
        Assert.Null(office.SaipId);
        aipRepo.Verify(r => r.GetDraftGroupsForOfficeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetById_ForAnOfficeTheCallerCannotSee_NeverSwapsInACopy()
    {
        // The swap runs only after the read scope has kept the office, so a copy cannot widen it.
        var (rec, approved, copy, programs) = SeedApprovedAndCopy();
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) =
            Build([rec], [], officeSeed: approved, programSeed: programs);
        aipRepo.Setup(r => r.GetDraftGroupsForOfficeAsync(1, 3, It.IsAny<CancellationToken>())).ReturnsAsync([copy]);
        User guest = new()
        {
            Id = Guid.NewGuid(), Username = "gso", PasswordHash = "h", FullName = "GSO", Role = UserRole.Staff, OfficeId = 9,
            Office = new Office { Id = 9, OfficeCode = "GSO", OfficeName = "GSO", IsActive = true },
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };

        ServiceResult<AipRecordDetailDto> result = await sut.GetByIdAsync(1, guest, onlyOfficeId: 3);

        Assert.Empty(result.Value!.Offices);
        aipRepo.Verify(r => r.GetDraftGroupsForOfficeAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
