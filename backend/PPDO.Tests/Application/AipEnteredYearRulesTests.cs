using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// What FY2028 onward does differently, enforced at the service (v1.8.0 Phase 3).
///
/// <para>
/// ↩️ <b>Was <c>AipFyPartitionTests</c>, retitled 2026-09-05 (PPDO-61).</b> Nine tests went with the
/// record-shape partition they pinned. The break year survives them: from FY2028 the AIP is
/// <b>entered rather than uploaded</b> (V18-38) and its programs come from the <b>LDIP as a closed
/// list</b> (V18-41). The record's structure is no longer part of it — one base record per fiscal
/// year holds every office, in every year.
/// </para>
///
/// <para>
/// <see cref="AipFiscalYearsTests"/> pins the policy in isolation. This file pins that the policy
/// is actually <b>reached</b> — which is the half that breaks, since a path that forgets to ask
/// still compiles and still passes every test it had before.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private const int PartitionOfficeId = 7;
    private const int LastLegacyFy      = AipFiscalYears.FirstEnteredFiscalYear - 1;
    private const int FirstNewFy        = AipFiscalYears.FirstEnteredFiscalYear;

    private static List<Office> PartitionOffices() =>
        [MakeOffice(PartitionOfficeId, "PPDO", "01-010"), MakeOffice(8, "GSO", "01-015")];

    // ── The .xlsm freeze (V18-38 / PPDO-41) ───────────────────────────

    [Fact]
    public async Task ConfirmImport_FromTheBreakOnward_IsRefused()
    {
        // The workbook carries every office in one file, so an import is legacy-shaped by
        // construction. V18-38 disables the button; this is the server-side half, and it has to
        // exist independently — a disabled button is not a guard.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(
            ImportConfirm(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(FirstNewFy.ToString(), result.Error!);
    }

    [Fact]
    public async Task ConfirmImport_AHistoricalYear_IsStillAllowed()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(
            ImportConfirm(LastLegacyFy), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ConfirmImport_ReUploadCannotCarryARecordAcrossTheBoundary()
    {
        // ⚠️ "No record changes shape when its fiscal year changes" is the ticket's headline
        // clause, and THIS is the angle it is reachable from — not any create path. Re-upload
        // assigns rec.FiscalYear = dto.FiscalYear outright, so pointing a legacy FY2027 record at
        // FY2028 would leave a record whose year demands an owner and whose OfficeId is null: an
        // illegal combination reached without one line that looks like a conversion.
        //
        // It is blocked because the shape check sits ABOVE the re-upload branch rather than beside
        // the create guard. If it is ever moved down to "where records are created", this test is
        // what notices.
        //
        // ⚠️ Seeded as an Upload record on purpose (V18-38). Replace-import refuses a non-Upload
        // target before it reaches anything else, so a Manual seed here is refused for a reason
        // that has nothing to do with the partition — and this test went on passing with the shape
        // guard deleted. Asserting the message names the year is the other half of that fix.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(LastLegacyFy, entrySource: "Upload"), [], officeSeed: []);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(
            ImportConfirm(FirstNewFy) with { TargetRecordId = AipRecordId },
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(FirstNewFy.ToString(), result.Error!);
    }

    [Fact]
    public async Task ConfirmImport_ReUploadIntoAHistoricalRecord_IsStillAllowed()
    {
        // The freeze's blast radius, checked from the direction the ticket asks about. Re-upload
        // is the path most easily broken by a guard placed a line too high, and FY≤2027 records
        // are the ones people still correct this way.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(LastLegacyFy, entrySource: "Upload"), [], officeSeed: []);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(
            ImportConfirm(LastLegacyFy) with { TargetRecordId = AipRecordId },
            Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LastLegacyFy, result.Value!.FiscalYear);
    }

    [Fact]
    public async Task ParsePreview_FromTheBreakOnward_IsRefusedBeforeTheWorkbookIsRead()
    {
        // V18-38 — the freeze the user actually meets. Confirm is guarded too and is the guard
        // that counts, but refusing only there walks someone through parsing a 20 MB workbook and
        // reviewing a preview for a year that could never have accepted it. Asserting the parser
        // is never invoked is the whole point: a refusal placed after Parse would pass a test that
        // only checked the status code.
        var (sut, _, _, _, parser, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipImportPreviewDto> result = await sut.ParsePreviewAsync(
            new MemoryStream(), FirstNewFy, [], CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(FirstNewFy.ToString(), result.Error!);
        parser.Verify(pr => pr.Parse(It.IsAny<Stream>()), Times.Never);
    }

    [Fact]
    public async Task ParsePreview_AHistoricalYear_StillReachesTheParser()
    {
        // The other half, and the one that keeps the freeze from becoming a retirement. FY≤2027
        // is the only reason the parser still exists; a gate that stopped those years too would
        // pass every refusal test above and break the single working use.
        var (sut, _, _, _, parser, _, _, _, _, _, _, _, _) = Build([], []);
        parser.Setup(pr => pr.Parse(It.IsAny<Stream>()))
            .Returns(new Dictionary<string, List<ParsedAipOffice>>());

        ServiceResult<AipImportPreviewDto> result = await sut.ParsePreviewAsync(
            new MemoryStream(), LastLegacyFy, [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LastLegacyFy, result.Value!.FiscalYear);
        parser.Verify(pr => pr.Parse(It.IsAny<Stream>()), Times.Once);
    }

    [Fact]
    public async Task Import_TheRefusal_TellsTheUploaderSomethingAnUploaderCanAct_On()
    {
        // Both import gates refuse through RefuseUpload rather than the general Mismatch. Mismatch
        // says "choose the office this record is for", which an importer cannot do — the workbook
        // decides its offices, not the person uploading it — so that message reads as a portal bug
        // to the only person who ever sees it here. Named at the service level, not just on the
        // policy, because swapping the call back is a one-word edit.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], []);

        ServiceResult<AipImportPreviewDto> preview = await sut.ParsePreviewAsync(
            new MemoryStream(), FirstNewFy, [], CancellationToken.None);
        ServiceResult<AipRecordDto> confirm = await sut.ConfirmImportAsync(
            ImportConfirm(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        foreach (string message in new[] { preview.Error!, confirm.Error! })
        {
            Assert.Contains("entered in the portal", message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Choose the office", message, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ── Opening a fiscal year (V18-41 / PPDO-62) ──────────────────────

    [Fact]
    public async Task OpenFiscalYear_PopulatesEachOfficeFromItsOwnLdip()
    {
        // The whole point of the action: an office should find its programs already there when it
        // first opens the page, without anyone having pressed a per-office button.
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build([], [], officeSeed: [],
            officeConfigSeed: PartitionOffices(), ldipRecordSeed: LdipSeedFor(PartitionOfficeId),
            ldipOfficeSeed: LdipGroupsFor(PartitionOfficeId));

        ServiceResult<OpenAipFiscalYearResultDto> result = await sut.OpenFiscalYearAsync(
            new OpenAipFiscalYearDto(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.OfficesPopulated);
        officeRepo.Verify(r => r.AddAsync(
            It.Is<AipOffice>(o => o.OfficeId == PartitionOfficeId && o.Programs.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenFiscalYear_TheSeededOffices_CarryTheirOwnershipFk()
    {
        // ⚠️ Without this the whole year is INVISIBLE to the offices it was opened for.
        // AipReadScope filters on AipOffice.OfficeId, and after PPDO-61 that column is the only
        // carrier of office identity on the AIP — a null means the office opens its own AIP and
        // sees nothing, with no error and no empty-state explanation.
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build([], [], officeSeed: [],
            officeConfigSeed: PartitionOffices(), ldipRecordSeed: LdipSeedFor(PartitionOfficeId),
            ldipOfficeSeed: LdipGroupsFor(PartitionOfficeId));

        await sut.OpenFiscalYearAsync(
            new OpenAipFiscalYearDto(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        officeRepo.Verify(r => r.AddAsync(
            It.Is<AipOffice>(o => o.OfficeId == null), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OpenFiscalYear_AnOfficeWithNoLdip_IsReportedRatherThanSilentlySkipped()
    {
        // ⚠️ The report is the point, not a nicety. An office absent from the base record cannot
        // build its AIP and has no way to find out why — it opens the page and there is nothing
        // there. GSO (office 8) has no LDIP in this fixture; PPDO does.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeSeed: [],
            officeConfigSeed: PartitionOffices(), ldipRecordSeed: LdipSeedFor(PartitionOfficeId),
            ldipOfficeSeed: LdipGroupsFor(PartitionOfficeId));

        ServiceResult<OpenAipFiscalYearResultDto> result = await sut.OpenFiscalYearAsync(
            new OpenAipFiscalYearDto(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("GSO", result.Value!.OfficesWithoutLdip);
        Assert.DoesNotContain("PPDO", result.Value.OfficesWithoutLdip);
    }

    [Fact]
    public async Task OpenFiscalYear_CopiesNoAmountsFromTheLdip()
    {
        // ⚠️ LdipProgram.Budget is a MULTI-YEAR total across FiscalYearStart..End, not a single-FY
        // figure. Copying it would seed every office with numbers that mean something else, and
        // V18-46's ceiling check would then be measuring a quantity nobody entered.
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build([], [], officeSeed: [],
            officeConfigSeed: PartitionOffices(), ldipRecordSeed: LdipSeedFor(PartitionOfficeId),
            ldipOfficeSeed: LdipGroupsFor(PartitionOfficeId));

        await sut.OpenFiscalYearAsync(
            new OpenAipFiscalYearDto(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        // Bare shells only: name and ref code, no project/activity subtree to carry money in.
        officeRepo.Verify(r => r.AddAsync(
            It.Is<AipOffice>(o => o.Programs.All(prog => prog.Projects.Count == 0)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OpenFiscalYear_AYearThatIsAlreadyOpen_IsRefused()
    {
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(FirstNewFy), [], officeSeed: [], officeConfigSeed: PartitionOffices());

        ServiceResult<OpenAipFiscalYearResultDto> result = await sut.OpenFiscalYearAsync(
            new OpenAipFiscalYearDto(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains(FirstNewFy.ToString(), result.Error!);
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── LDIP seeding ────────────────────────────────────────────

    [Fact]
    public async Task SeedProgramsFromLdip_IntoAYearThatIsNotOpen_IsRefused()
    {
        // ↩️ Was "CreatesTheTargetRecordForTheRequestedYear" (PPDO-62), which was itself a rewrite
        // of V18-37's shape test. It has now inverted: seeding no longer brings a year into
        // existence, it syncs into one an Admin already opened. Third meaning for the same lines,
        // and each rewrite kept whatever was still true rather than starting from a blank test.
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeSeed: [],
            officeConfigSeed: PartitionOffices(), ldipRecordSeed: LdipSeedFor(PartitionOfficeId),
            ldipOfficeSeed: LdipGroupsFor(PartitionOfficeId));

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(
                TargetFiscalYear: LastLegacyFy, OfficeConfigId: PartitionOfficeId,
                Sector: "GENERAL", LdipProgramIds: [SeededLdipProgramId]),
            Guid.NewGuid(), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not been opened", result.Error!);
        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedProgramsFromLdip_TheSeededOffice_CarriesItsOwnershipFk()
    {
        // ⚠️ Without this the seeded programs are INVISIBLE to the office that just seeded them.
        // AipReadScope filters on AipOffice.OfficeId, so a null there means the office opens its
        // own FY2028 AIP and sees nothing — no error, no empty-state explanation, just absence.
        // The same failure V18-32's migration warns unmatched rows cause. The seed path never set
        // this field before V18-41; only the importer did.
        var (sut, _, _, _, _, _, officeRepo, _, _, _, _, _, _) = Build(
            RecordSeed(FirstNewFy), [], officeSeed: [],
            officeConfigSeed: PartitionOffices(), ldipRecordSeed: LdipSeedFor(PartitionOfficeId),
            ldipOfficeSeed: LdipGroupsFor(PartitionOfficeId));

        await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(
                TargetFiscalYear: FirstNewFy, OfficeConfigId: PartitionOfficeId,
                Sector: "GENERAL", LdipProgramIds: [SeededLdipProgramId]),
            Guid.NewGuid(), WriteHostCaller(), CancellationToken.None);

        officeRepo.Verify(r => r.AddAsync(
            It.Is<AipOffice>(o => o.OfficeId == PartitionOfficeId), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── The closed list (V18-41 / PPDO-51) ────────────────────────────

    [Fact]
    public async Task AddProgram_AFreeTypedNameFromTheBreakOnward_IsRefused()
    {
        // The LDIP is a closed list from the break year on, so the free-typed path is the one
        // door that has to close — otherwise "programs come from the LDIP" is a convention the
        // UI follows and the API does not.
        List<AipRecord> recs = RecordSeed(FirstNewFy);
        List<AipOffice> offices =
        [
            new() { Id = 10, AipRecordId = AipRecordId, RefCode = "1000-000-1-01-010",
                    Name = "PPDO", Sector = AipSector.General, OfficeId = PartitionOfficeId },
        ];
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _, _) =
            Build(recs, [], officeSeed: offices, officeConfigSeed: PartitionOffices());

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(
            10, new CreateAipProgramDto("Invented program"), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("LDIP", result.Error!);
        programRepo.Verify(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AddProgram_AFreeTypedNameInAHistoricalYear_IsStillAllowed()
    {
        // FY≤2027 manual entry has always let an office name its own programs, and those records
        // came from a workbook containing whatever the province typed. Closing the free-typed path
        // for them would break the one working use — the same shape of mistake as freezing the
        // importer for historical years would have been.
        List<AipRecord> recs = RecordSeed(LastLegacyFy);
        List<AipOffice> offices =
        [
            new() { Id = 10, AipRecordId = AipRecordId, RefCode = "1000-000-1-01-010",
                    Name = "PPDO", Sector = AipSector.General, OfficeId = PartitionOfficeId },
        ];
        var (sut, _, _, _, _, _, _, _, _, programRepo, _, _, _) =
            Build(recs, [], officeSeed: offices, officeConfigSeed: PartitionOffices());

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(
            10, new CreateAipProgramDto("Hand-entered program"), WriteHostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        programRepo.Verify(r => r.AddAsync(It.IsAny<AipProgram>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AddProgram_TheRefusal_ComesBeforeTheNameRequiredCheck()
    {
        // Ordering matters for the message, not the outcome. A caller in FY2028 with a blank name
        // has ONE problem — the year — and "Program name is required" would send them to fill in
        // a field that was never going to be accepted. Same reasoning as V18-37 putting its shape
        // check above the office lookup.
        List<AipRecord> recs = RecordSeed(FirstNewFy);
        List<AipOffice> offices =
        [
            new() { Id = 10, AipRecordId = AipRecordId, RefCode = "1000-000-1-01-010",
                    Name = "PPDO", Sector = AipSector.General, OfficeId = PartitionOfficeId },
        ];
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build(recs, [], officeSeed: offices, officeConfigSeed: PartitionOffices());

        ServiceResult<AipProgramDto> result = await sut.AddProgramAsync(
            10, new CreateAipProgramDto("   "), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("LDIP", result.Error!);
        Assert.DoesNotContain("name is required", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheClosedListRefusal_TracksTheBreakYear_AndNamesTheLdip()
    {
        Assert.Null(AipProgramSource.RefuseFreeTypedProgram(AipFiscalYears.FirstEnteredFiscalYear - 1));

        string refusal = AipProgramSource.RefuseFreeTypedProgram(AipFiscalYears.FirstEnteredFiscalYear)!;
        Assert.Contains("LDIP", refusal);
        Assert.Contains(AipFiscalYears.FirstEnteredFiscalYear.ToString(), refusal);
    }

    // ── Adding offices to a record ─────────────────────────────────────

    [Fact]
    public async Task AddOffice_AnyOfficeOntoALegacyRecord_IsUntouched()
    {
        // The legacy shape is multi-office by definition. The new rule must not reach back and
        // break the years it does not govern.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(LastLegacyFy), [], officeSeed: [],
            officeConfigSeed: PartitionOffices());

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(
            AipRecordId, new CreateAipOfficeDto(OfficeConfigId: 8, Sector: "GENERAL", Name: null),
            WriteHostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // ── There is no conversion, by construction ──────────────────────────────

    [Fact]
    public void NoServiceMethod_ChangesTheOwnerOfAnExistingRecord()
    {
        // "No record changes shape" is the ticket's headline requirement, and the honest way to
        // assert it is that no seam exists to do it with: nothing on IAipService takes a record id
        // together with an office id. Archive and recreate is the only route between shapes.
        List<string> offenders = typeof(IAipService).GetMethods()
            .Where(m => m.Name.Contains("Record", StringComparison.Ordinal))
            .Where(m => m.GetParameters().Any(p =>
                p.ParameterType == typeof(int) &&
                p.Name!.Contains("office", StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A record-level method now takes an office id — that is a shape conversion seam: "
            + string.Join(", ", offenders));
    }

    // ── LDIP fixtures for V18-41's seeding tests ────────────────────────

    private const int SeededLdipRecordId  = 400;
    private const int SeededLdipOfficeId  = 401;
    private const int SeededLdipProgramId = 402;

    /// <summary>
    /// A Tier-1 LDIP record — one the office owns outright (<c>LdipRecord.OfficeId</c> set), which
    /// the seed path resolves by sector text. Deliberately not the Tier-2 multi-office shape: that
    /// one is matched by computed ref code and would make these tests about ref-code derivation
    /// rather than about the record shape they exist to pin.
    /// </summary>
    private static List<LdipRecord> LdipSeedFor(int officeConfigId) =>
    [
        new()
        {
            Id = SeededLdipRecordId, OfficeId = officeConfigId,
            RefCode = "LDIP-2028-2030", Title = "LDIP",
            FiscalYearStart = 2028, FiscalYearEnd = 2030,
            EntryMode = "New", Status = PlanningStatus.Draft,
            CreatedById = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        },
    ];

    private static List<LdipOffice> LdipGroupsFor(int officeConfigId) =>
    [
        new()
        {
            Id = SeededLdipOfficeId, LdipRecordId = SeededLdipRecordId,
            RefCode = "1000-000-1-01-010", Name = "PPDO", Sector = AipSector.General,
            Programs =
            [
                new LdipProgram
                {
                    Id = SeededLdipProgramId, LdipOfficeId = SeededLdipOfficeId,
                    RefCode = "1000-000-1-01-010-001", Name = "Seeded program",
                },
            ],
        },
    ];

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static AipImportConfirmDto ImportConfirm(int fiscalYear) =>
        new(fiscalYear, "aip.xlsm", LdipId: null, SectorOffices: []);

    /// <summary>
    /// One record in the given shape, with no offices under it yet.
    ///
    /// <para>
    /// ⚠️ <paramref name="entrySource"/> is not cosmetic on the re-upload path: replace-import
    /// refuses anything that is not an <c>Upload</c> record before it looks at anything else, so a
    /// <c>Manual</c> seed never reaches the shape guard and a test written over one passes whether
    /// that guard exists or not (V18-38 — found by disabling the guard and watching the test stay
    /// green).
    /// </para>
    /// </summary>
    private static List<AipRecord> RecordSeed(int fiscalYear, string entrySource = "Manual") =>
    [
        new()
        {
            Id = AipRecordId, FiscalYear = fiscalYear,
            EntrySource = entrySource, Status = "Draft",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
        },
    ];
}
