using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Sub-office groups, and the rule that AIP program ref codes are <b>inherited from the LDIP and
/// never renumbered</b> (V18-42 / PPDO-52).
///
/// <para>
/// <b>⚠️ Settled 2026-09-07, against a line in PPDO-52's own acceptance list.</b> That list asks
/// that "removing a middle program renumbers without leaving a gap". It should not, and these tests
/// exist to stop someone implementing it later in good faith.
/// </para>
///
/// <para>
/// The numbering the ticket describes <b>already exists, one document upstream</b>:
/// <c>LdipService.BuildHierarchy</c> keys its sequence on the group ref code, so LDIP programs are
/// numbered continuously across groups sharing a code, and because LDIP saves full-replace the
/// hierarchy, removals there renumber with no gaps. The AIP copies those codes verbatim.
/// </para>
///
/// <para>
/// <b>Renumbering in the AIP would break the correspondence</b> between an AIP program and the LDIP
/// program it came from — which is the whole point of the closed list (V18-41). A gap in a printed
/// sequence is cosmetic; a program that no longer maps back to its source is not.
/// </para>
///
/// <para>
/// ℹ️ This also closes P3-b. The re-link of <c>ProgramDivision</c> on renumber was folded into this
/// ticket from the cancelled PPDO-65 — with no renumbering there is no trigger, and no re-link is
/// needed. <c>ProgramDivision</c> keeps matching on a ref code that does not change.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private const int EnteredFy = 2028;

    /// <summary>An open, Draft, Manual FY2028 record — what an Admin's year-opening leaves behind.</summary>
    private static List<AipRecord> OpenEnteredYear() =>
    [
        new()
        {
            Id = 300, FiscalYear = EnteredFy, EntrySource = "Manual", Status = PlanningStatus.Draft,
            UploadedById = UserId, UploadedAt = DateTime.UtcNow,
        },
    ];

    // ── The rule this file exists for ─────────────────────────────────────────

    /// <summary>
    /// ⚠️ The ref code is the LDIP's, byte for byte. Asserted as a literal rather than compared to
    /// the fixture's own field, so a change that started allocating codes here fails instead of
    /// quietly agreeing with itself.
    /// </summary>
    [Fact]
    public async Task AddProgramsWithGroup_ProgramRefCodeIsInheritedFromTheLdipNotAllocated()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = BuildForGroups();

        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "GENERAL", [80]), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("1000-000-1-01-010-003", result.Value!.Programs.Single().RefCode);
    }

    /// <summary>
    /// A gap in the LDIP's own numbering survives into the AIP untouched. If anything ever
    /// renumbers on this path, the second code below becomes <c>-002</c> and this fails.
    /// </summary>
    [Fact]
    public async Task AddProgramsWithGroup_AGapInTheLdipNumberingIsPreservedNotClosed()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = BuildForGroups();

        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "GENERAL", [80, 81]), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["1000-000-1-01-010-003", "1000-000-1-01-010-007"],
            result.Value!.Programs.Select(p => p.RefCode).OrderBy(c => c).ToArray());
    }

    // ── Sub-office groups ─────────────────────────────────────────────────────
    //
    // ⚠️ These used to drive the group off a CALLER-SUPPLIED name. It is now derived from the
    // ticked LDIP programs, because the LDIP already carries the grouping — so the fixture below
    // holds two real groups under one ref code rather than one group and a free-text box.

    /// <summary>
    /// The reason this endpoint exists. <c>SeedProgramsFromLdipAsync</c> used to match its target
    /// office on ref code alone and so could only ever reach the first group; a program from a
    /// second LDIP group must start a second <c>AipOffice</c> row under the same code.
    /// </summary>
    [Fact]
    public async Task AddProgramsWithGroup_AProgramFromASecondLdipGroupStartsASecondOfficeRowUnderTheSameRefCode()
    {
        List<AipOffice> existing =
        [
            new()
            {
                Id = 400, AipRecordId = 300, RefCode = "1000-000-1-01-010",
                Name = "PPDO", Sector = "GENERAL", OfficeId = 7,
            },
        ];
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = BuildForGroups(existing);

        // 90 belongs to the AKAP-HUB group, not to the "PPDO" row that already exists.
        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "GENERAL", [90]), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("PPDO - AKAP-HUB", result.Value!.Name);
        Assert.Equal("1000-000-1-01-010", result.Value.RefCode); // same code, different block
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// A program from a group that already has its row adds into that row rather than creating a
    /// duplicate. The match is case-insensitive on the group name.
    /// </summary>
    [Fact]
    public async Task AddProgramsWithGroup_AProgramFromAnAlreadyPresentGroupAddsIntoThatRow()
    {
        List<AipOffice> existing =
        [
            new()
            {
                Id = 400, AipRecordId = 300, RefCode = "1000-000-1-01-010",
                Name = "ppdo", Sector = "GENERAL", OfficeId = 7,
            },
        ];
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = BuildForGroups(existing);

        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "GENERAL", [80]), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(400, result.Value!.Id);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// ⚠️ The group name is the LDIP group's, never the caller's. This is the test that fails if
    /// anyone reintroduces a caller-supplied name — the AIP prints this string, and a name that
    /// matches no LDIP row is how the printed form and the source document drift apart.
    /// </summary>
    [Fact]
    public async Task AddProgramsWithGroup_TheGroupNameComesFromTheLdipGroupTheProgramsBelongTo()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = BuildForGroups();

        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "GENERAL", [90]), HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal("PPDO - AKAP-HUB", result.Value!.Name);
    }

    /// <summary>
    /// ⚠️ Refused, not silently split into two rows. Each group is its own printed office row with
    /// its own subtotal, so fanning one request into two would invent structure the encoder never
    /// asked for and cannot see on the page.
    /// </summary>
    [Fact]
    public async Task AddProgramsWithGroup_ASelectionSpanningTwoGroupsIsRefusedNotSplit()
    {
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = BuildForGroups();

        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "GENERAL", [80, 90]), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        // Both group names, so the encoder can see what to untick.
        Assert.Contains("PPDO - AKAP-HUB", result.Error!);
        officeRepo.Verify(r => r.AddAsync(It.IsAny<AipOffice>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The closed list ───────────────────────────────────────────────────────

    [Fact]
    public async Task AddProgramsWithGroup_AProgramFromAnotherOfficesLdipIsRefused()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = BuildForGroups();

        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "GENERAL", [999]), HostCaller());

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("999", result.Error!);
    }

    [Fact]
    public async Task AddProgramsWithGroup_AnOfficeWithNoLdipForTheSectorIsToldSo()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = BuildForGroups();

        ServiceResult<AipOfficeDto> result = await sut.AddProgramsWithGroupAsync(
            300, new AddAipProgramsWithGroupDto(7, "SOCIAL", [80]), HostCaller());

        Assert.False(result.IsSuccess);
        // ⚠️ Names the LDIP as the prerequisite. An empty picker would leave the encoder with no
        // idea what to do next — spec §3.2's empty-LDIP case.
        Assert.Contains("LDIP", result.Error!);
    }

    // ── The picker sees every group ───────────────────────────────────────────

    /// <summary>
    /// ⚠️ The bug this file's fixture was widened for. The picker and the seed both went through one
    /// resolver that returned a single group, so a sector's second, third and fourth sub-offices had
    /// no route into the AIP from any direction and produced no error — reported live as "the
    /// programs under AKAP are not displayed".
    /// </summary>
    [Fact]
    public async Task GetAddablePrograms_ReturnsEverySubOfficeGroupInTheSector()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = BuildForGroups();

        ServiceResult<AipAddableProgramsDto> result =
            await sut.GetAddableProgramsAsync(7, "GENERAL", HostCaller());

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["PPDO", "PPDO - AKAP-HUB"],
            result.Value!.Groups.Select(g => g.GroupName).OrderBy(n => n, StringComparer.Ordinal).ToArray());

        // Same ref code on both — the name is what separates them.
        Assert.All(result.Value.Groups, g => Assert.Equal("1000-000-1-01-010", g.GroupRefCode));

        // And the programs land under the right group, not pooled.
        Assert.Equal(
            [80, 81],
            result.Value.Groups.Single(g => g.GroupName == "PPDO")
                .Programs.Select(p => p.LdipProgramId).OrderBy(i => i).ToArray());
        Assert.Equal(
            [90],
            result.Value.Groups.Single(g => g.GroupName == "PPDO - AKAP-HUB")
                .Programs.Select(p => p.LdipProgramId).ToArray());
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Config office 7 ("PPDO", ref "01-010") with a Final LDIP whose GENERAL sector holds
    /// <b>two</b> groups under one ref code — "PPDO" (programs <c>-003</c> and <c>-007</c>) and
    /// "PPDO - AKAP-HUB" (program <c>-011</c>).
    ///
    /// <para>
    /// ⚠️ <b>Two groups, not one, and that is load-bearing.</b> A single-group fixture passed the
    /// whole time the resolver was dropping every group but the first — it had nothing to drop.
    /// This mirrors the province's real LDIP, where <c>3000-000-1-01-001</c> carries WARDEN,
    /// AKAP-HUB, HOUSING and LOCAL SCHOOL BOARD.
    /// </para>
    ///
    /// <para>
    /// ⚠️ The program numbers are deliberately non-contiguous and deliberately not <c>-001</c>. A
    /// fixture numbered 001, 002 would pass whether codes were inherited or freshly allocated, and
    /// would therefore prove nothing about the rule this file exists to defend.
    /// </para>
    /// </summary>
    private static (AipService, Mock<IAipRepository>, Mock<IRepository<FundingSource>>,
                    Mock<IUserRepository>, Mock<IAipXlsmParser>, Mock<IAuditService>,
                    Mock<IRepository<AipOffice>>, Mock<IWfpRepository>, Mock<IOfficeRepository>,
                    Mock<IRepository<AipProgram>>, Mock<IRepository<AipProject>>,
                    Mock<IRepository<AipActivity>>, Mock<ILdipRepository>)
        BuildForGroups(List<AipOffice>? officeSeed = null)
    {
        LdipRecord ldipRec = LdipRec(5, 7);

        LdipOffice group = LdipGroup(70, 5);
        group.Programs.Add(LdipProg(80, 70, "1000-000-1-01-010-003", "LDIP PROGRAM A"));
        group.Programs.Add(LdipProg(81, 70, "1000-000-1-01-010-007", "LDIP PROGRAM B"));

        LdipOffice subOffice = LdipGroup(71, 5, name: "PPDO - AKAP-HUB");
        subOffice.Programs.Add(LdipProg(90, 71, "1000-000-1-01-010-011", "LDIP PROGRAM C"));

        return Build(
            OpenEnteredYear(), [],
            officeSeed: officeSeed ?? [],
            officeConfigSeed: [MakeOffice(7, "PPDO", "01-010")],
            ldipRecordSeed: [ldipRec],
            ldipOfficeSeed: [group, subOffice]);
    }
}
