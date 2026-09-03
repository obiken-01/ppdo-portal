using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The FY partition applied at the four paths that create an AIP record, plus the one path that
/// could change a record's shape a node at a time (v1.8.0 Phase 2 — V18-37 / PPDO-40).
///
/// <para>
/// <see cref="AipShapeTests"/> pins the policy in isolation. This file pins that the policy is
/// actually <b>reached</b> — which is the half that breaks, since a create path that forgets to
/// ask still compiles and still passes every test it had before.
/// </para>
///
/// <para>
/// ⚠️ <b>Two of these were a live leak, not a hypothetical.</b> <c>CopyOfficeFromPriorYearAsync</c>
/// and <c>SeedProgramsFromLdipAsync</c> find-or-create their target record with no owner set.
/// Pointed at FY2028 before this ticket they wrote a legacy-shape record into a year that must not
/// have one — silently, with nothing downstream positioned to notice.
/// </para>
/// </summary>
public sealed partial class AipServiceTests
{
    private const int PartitionOfficeId = 7;
    private const int LastLegacyFy      = AipShape.FirstOfficeOwnedFiscalYear - 1;
    private const int FirstNewFy        = AipShape.FirstOfficeOwnedFiscalYear;

    private static List<Office> PartitionOffices() =>
        [MakeOffice(PartitionOfficeId, "PPDO", "01-010"), MakeOffice(8, "GSO", "01-015")];

    // ── Manual create — the one path that can already ask for either shape ────

    [Fact]
    public async Task CreateManualRecord_OfficeOwnedShapeInAHistoricalYear_IsRefused()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([], [], officeConfigSeed: PartitionOffices());

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(
            new CreateAipRecordDto(LastLegacyFy, OfficeConfigId: PartitionOfficeId),
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(LastLegacyFy.ToString(), result.Error!);
    }

    [Fact]
    public async Task CreateManualRecord_LegacyShapeFromTheBreakOnward_IsRefused()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([], [], officeConfigSeed: PartitionOffices());

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(
            new CreateAipRecordDto(FirstNewFy), Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(FirstNewFy.ToString(), result.Error!);
    }

    [Fact]
    public async Task CreateManualRecord_EitherShapeInItsOwnYear_IsAllowed()
    {
        // The partition must not be a blanket refusal — both shapes stay creatable, each in the
        // years that are its own. Without this the two tests above would pass on a service that
        // simply rejected everything.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) =
            Build([], [], officeConfigSeed: PartitionOffices());
        Guid by = Guid.NewGuid();

        ServiceResult<AipRecordDto> legacy = await sut.CreateManualRecordAsync(
            new CreateAipRecordDto(LastLegacyFy), by, CancellationToken.None);
        ServiceResult<AipRecordDto> owned = await sut.CreateManualRecordAsync(
            new CreateAipRecordDto(FirstNewFy, OfficeConfigId: PartitionOfficeId), by, CancellationToken.None);

        Assert.True(legacy.IsSuccess);
        Assert.Null(legacy.Value!.OfficeId);
        Assert.True(owned.IsSuccess);
        Assert.Equal(PartitionOfficeId, owned.Value!.OfficeId);
    }

    [Fact]
    public async Task CreateManualRecord_TheShapeCheckRunsBeforeTheOfficeLookup()
    {
        // Ordering matters for the message, not the outcome. A caller asking for an office-owned
        // FY2027 record has made ONE mistake — the year — and telling them the office is unknown
        // (it may well be) sends them to fix the wrong thing.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build([], [], officeConfigSeed: []);

        ServiceResult<AipRecordDto> result = await sut.CreateManualRecordAsync(
            new CreateAipRecordDto(LastLegacyFy, OfficeConfigId: 999),
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.DoesNotContain("999", result.Error!);
    }

    // ── .xlsm import — legacy only; the freeze itself is V18-38 ───────────────

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
        Assert.Null(result.Value!.OfficeId);
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
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(null, LastLegacyFy), [], officeSeed: []);

        ServiceResult<AipRecordDto> result = await sut.ConfirmImportAsync(
            ImportConfirm(FirstNewFy) with { TargetRecordId = AipRecordId },
            Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Carry-forward and LDIP seeding — the paths that leaked ────────────────

    [Fact]
    public async Task CopyOfficeFromPriorYear_IntoAYearFromTheBreakOnward_IsRefused()
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(recs, [], officeSeed: offices,
            programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: PartitionOffices());

        ServiceResult<AipOfficeDto> result = await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(SourceOfficeId: 10, TargetFiscalYear: FirstNewFy, ProgramIds: [20]),
            Guid.NewGuid(), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(FirstNewFy.ToString(), result.Error!);
    }

    [Fact]
    public async Task CopyOfficeFromPriorYear_RefusesBeforeWritingAnything()
    {
        // ⚠️ The property that actually matters. A refusal that arrives after the record has been
        // added leaves the wrong-shaped row behind and merely reports an error about it — which is
        // the original bug with an error message attached.
        var (recs, offices, programs, projects, acts) = HostOwnedTree();
        var (sut, aipRepo, _, _, _, _, _, _, _, _, _, _, _) = Build(recs, [], officeSeed: offices,
            programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: PartitionOffices());

        await sut.CopyOfficeFromPriorYearAsync(
            new CopyAipOfficeDto(SourceOfficeId: 10, TargetFiscalYear: FirstNewFy, ProgramIds: [20]),
            Guid.NewGuid(), WriteHostCaller(), CancellationToken.None);

        aipRepo.Verify(r => r.AddAsync(It.IsAny<AipRecord>(), It.IsAny<CancellationToken>()), Times.Never);
        aipRepo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SeedProgramsFromLdip_IntoAYearFromTheBreakOnward_IsRefused()
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(recs, [], officeSeed: offices,
            programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: PartitionOffices());

        ServiceResult<AipOfficeDto> result = await sut.SeedProgramsFromLdipAsync(
            new SeedAipProgramsFromLdipDto(
                TargetFiscalYear: FirstNewFy, OfficeConfigId: PartitionOfficeId,
                Sector: "GENERAL", LdipProgramIds: [1]),
            Guid.NewGuid(), WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(FirstNewFy.ToString(), result.Error!);
    }

    // ── The other door: changing shape one node at a time ────────────────────

    [Fact]
    public async Task AddOffice_AForeignOfficeOntoAnOfficeOwnedRecord_IsRefused()
    {
        // ⚠️ No record is "converted" here and no gate on the create paths would see it. Add two
        // offices to an office-owned record and it spans several offices — the legacy shape,
        // reached through a different door. OfficeScope does not cover this: it stops a GUEST
        // reaching another office's record, and says nothing about a host admin, who legitimately
        // sees every office and would otherwise be able to do exactly this.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(PartitionOfficeId, FirstNewFy), [], officeSeed: [],
            officeConfigSeed: PartitionOffices());

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(
            AipRecordId, new CreateAipOfficeDto(OfficeConfigId: 8, Sector: "GENERAL", Name: null),
            WriteHostCaller(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task AddOffice_TheOwningOfficeOntoItsOwnRecord_IsAllowed()
    {
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(PartitionOfficeId, FirstNewFy), [], officeSeed: [],
            officeConfigSeed: PartitionOffices());

        ServiceResult<AipOfficeDto> result = await sut.AddOfficeAsync(
            AipRecordId,
            new CreateAipOfficeDto(OfficeConfigId: PartitionOfficeId, Sector: "GENERAL", Name: null),
            WriteHostCaller(), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task AddOffice_AnyOfficeOntoALegacyRecord_IsUntouched()
    {
        // The legacy shape is multi-office by definition. The new rule must not reach back and
        // break the years it does not govern.
        var (sut, _, _, _, _, _, _, _, _, _, _, _, _) = Build(
            RecordSeed(null, LastLegacyFy), [], officeSeed: [],
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

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static AipImportConfirmDto ImportConfirm(int fiscalYear) =>
        new(fiscalYear, "aip.xlsm", LdipId: null, SectorOffices: []);

    /// <summary>One record in the given shape, with no offices under it yet.</summary>
    private static List<AipRecord> RecordSeed(int? ownerOfficeId, int fiscalYear) =>
    [
        new()
        {
            Id = AipRecordId, FiscalYear = fiscalYear, OfficeId = ownerOfficeId,
            EntrySource = "Manual", Status = "Draft",
            UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
        },
    ];
}
