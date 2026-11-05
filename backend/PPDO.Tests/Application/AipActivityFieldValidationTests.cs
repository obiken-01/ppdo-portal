using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// PPDO-289 / 290 / 291 / 292 — the AIP activity saves refuse what the page never offers.
///
/// <list type="bullet">
///   <item>289 — start and end are month names, and the end is not before the start.</item>
///   <item>290 — CC adaptation and mitigation are not negative.</item>
///   <item>291 — the CC typology code is an active Config code.</item>
///   <item>292 — the FY 2027 inline editors refuse negative PS / MOOE / CO, and nothing else
///         changes there (clean-break rule).</item>
/// </list>
///
/// ⚠️ 289–291 are checked on the details save only (<c>UpdateActivityDetailsAsync</c>, the FY 2028+
/// entry page). A value the activity already holds is never re-checked: FY 2027 rows carry months
/// like "Januray" and "2027", and a typo saved long ago must not block a rename.
/// </summary>
public sealed partial class AipServiceTests
{
    /// <summary>The scoped tree, with <paramref name="setUp"/> applied to the activity under test.</summary>
    private static (AipService sut, AipActivity activity, Mock<IClimateChangeTypologyRepository> typologies)
        BuildForFieldChecks(Action<AipActivity>? setUp = null, params string[] activeTypologyCodes)
    {
        var (recs, offices, programs, projects, acts) = HostOwnedTree();
        AipActivity activity = acts.Single(a => a.Id == ConflictActivityId);
        setUp?.Invoke(activity);

        Mock<IClimateChangeTypologyRepository> typologies = KnownTypologies(activeTypologyCodes);

        var built = Build(recs, [], officeSeed: offices, programSeed: programs, projectSeed: projects, actSeed: acts,
            officeConfigSeed: [ConfigOffice(HostOfficeId, true), ConfigOffice(GuestOfficeId, false)],
            ccTypologies: typologies);
        return (built.Item1, activity, typologies);
    }

    /// <summary>A typology repository whose active codes are exactly <paramref name="codes"/> (PPDO-291).</summary>
    private static Mock<IClimateChangeTypologyRepository> KnownTypologies(params string[] codes)
    {
        Mock<IClimateChangeTypologyRepository> typologies = new();
        typologies.Setup(r => r.ActiveCodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string code, CancellationToken _) => codes.Contains(code));
        return typologies;
    }

    private static UpdateAipActivityDetailsDto DetailsWith(
        string? start = null, string? end = null,
        decimal? ccAdaptation = null, decimal? ccMitigation = null, string? ccTypology = null) =>
        new("Activity", null, null, start, end, null, ccAdaptation, ccMitigation, ccTypology);

    private static Task<ServiceResult<AipActivityDto>> SaveDetails(AipService sut, UpdateAipActivityDetailsDto dto) =>
        sut.UpdateActivityDetailsAsync(ConflictActivityId, dto, WriteHostCaller(), expectedRowVersion: AipTestVersion.Any);

    // ── PPDO-289: start and end month ─────────────────────────────────────────

    [Theory]
    [InlineData("Januray", null)]
    [InlineData(null, "Decemebr")]
    [InlineData("2027", "December")]
    [InlineData("January", "Fund provided")]
    public async Task UpdateActivityDetailsAsync_WithTextThatIsNotAMonth_ReturnsBadRequest(string? start, string? end)
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(start, end));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("month", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Null(activity.StartDate);
        Assert.Null(activity.EndDate);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithMonthOverTheColumnWidth_ReturnsBadRequestNotA500()
    {
        var (sut, _, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(new string('x', 60), null));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithEndBeforeStart_ReturnsBadRequest()
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith("December", "January"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("The end month cannot be before the start month.", result.Error);
        Assert.Null(activity.StartDate);
    }

    [Theory]
    [InlineData("March", "March")]
    [InlineData("January", "December")]
    [InlineData("July", null)]
    [InlineData(null, "June")]
    [InlineData(null, null)]
    public async Task UpdateActivityDetailsAsync_WithValidMonths_Saves(string? start, string? end)
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(start, end));

        Assert.True(result.IsSuccess);
        Assert.Equal(start, activity.StartDate);
        Assert.Equal(end, activity.EndDate);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithMonthInAnyCase_StoresTheMonthName()
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(" march ", "DECEMBER"));

        Assert.True(result.IsSuccess);
        Assert.Equal("March", activity.StartDate);
        Assert.Equal("December", activity.EndDate);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithLegacyMonthsAlreadySaved_StillSavesWhenUnchanged()
    {
        // Real FY 2027 shapes from the local data: a typo start and a reversed pair.
        var (sut, activity, _) = BuildForFieldChecks(a => { a.StartDate = "Februray"; a.EndDate = "January"; });

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith("Februray", "January"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Februray", activity.StartDate);
        Assert.Equal("January", activity.EndDate);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_ChangingOneMonthBeforeAnUnchangedOne_ChecksTheOrder()
    {
        // The end was saved before this rule; moving the start past it is a new reversal.
        var (sut, _, _) = BuildForFieldChecks(a => { a.StartDate = "January"; a.EndDate = "March"; });

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith("June", "March"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── PPDO-290: CC amounts ──────────────────────────────────────────────────

    [Theory]
    [InlineData(-1.0, null)]
    [InlineData(null, -0.01)]
    public async Task UpdateActivityDetailsAsync_WithNegativeCcAmount_ReturnsBadRequest(double? adaptation, double? mitigation)
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await SaveDetails(sut,
            DetailsWith(ccAdaptation: (decimal?)adaptation, ccMitigation: (decimal?)mitigation));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("cannot be negative", result.Error);
        Assert.Null(activity.CcAdaptation);
        Assert.Null(activity.CcMitigation);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithZeroAndPositiveCcAmounts_Saves()
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(ccAdaptation: 0m, ccMitigation: 2500m));

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, activity.CcAdaptation);
        Assert.Equal(2500m, activity.CcMitigation);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithCcAboveTheActivityTotal_StillSaves()
    {
        // PPDO-290 — Ralph, 2026-11-04: a warning on the page, not a refusal. A hard rule may come
        // in v1.10; until then the API must accept it.
        var (sut, activity, _) = BuildForFieldChecks(a => a.Total = 10_000m);

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(ccAdaptation: 8_000m, ccMitigation: 5_000m));

        Assert.True(result.IsSuccess);
        Assert.Equal(8_000m, activity.CcAdaptation);
        Assert.Equal(5_000m, activity.CcMitigation);
    }

    // ── PPDO-291: CC typology code ────────────────────────────────────────────

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithUnknownTypologyCode_ReturnsBadRequest()
    {
        var (sut, activity, _) = BuildForFieldChecks(null, "A113-01");

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(ccTypology: "TEST"));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal("Unknown CC typology code.", result.Error);
        Assert.Null(activity.CcTypologyCode);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithKnownTypologyCode_SavesItAfterOneLookup()
    {
        var (sut, activity, typologies) = BuildForFieldChecks(null, "A113-01");

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(ccTypology: "A113-01"));

        Assert.True(result.IsSuccess);
        Assert.Equal("A113-01", activity.CcTypologyCode);
        typologies.Verify(r => r.ActiveCodeExistsAsync("A113-01", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_WithUnchangedLegacyTypologyCode_SavesWithoutALookup()
    {
        var (sut, activity, typologies) = BuildForFieldChecks(a => a.CcTypologyCode = "A222-03, A224-05");

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(ccTypology: "A222-03, A224-05"));

        Assert.True(result.IsSuccess);
        Assert.Equal("A222-03, A224-05", activity.CcTypologyCode);
        typologies.Verify(r => r.ActiveCodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateActivityDetailsAsync_ClearingTheTypologyCode_Saves()
    {
        var (sut, activity, _) = BuildForFieldChecks(a => a.CcTypologyCode = "A113-01");

        ServiceResult<AipActivityDto> result = await SaveDetails(sut, DetailsWith(ccTypology: null));

        Assert.True(result.IsSuccess);
        Assert.Null(activity.CcTypologyCode);
    }

    // ── PPDO-292: FY 2027 inline editors ──────────────────────────────────────

    private static UpdateAipActivityDto LegacyUpdate(decimal? ps, decimal? mooe, decimal? co,
        string? start = null, string? end = null, string? ccTypology = null) =>
        new("Activity", null, null, start, end, null, null, ps, mooe, co, null, null, ccTypology);

    private static CreateAipActivityDto LegacyCreate(decimal? ps, decimal? mooe, decimal? co) =>
        new("New activity", null, null, null, null, null, null, ps, mooe, co, null, null, null);

    [Theory]
    [InlineData(-1.0, null, null, "PS")]
    [InlineData(null, -0.5, null, "MOOE")]
    [InlineData(10.0, 20.0, -30.0, "CO")]
    public async Task UpdateActivityAsync_WithNegativeAmount_ReturnsBadRequest(
        double? ps, double? mooe, double? co, string field)
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, LegacyUpdate((decimal?)ps, (decimal?)mooe, (decimal?)co),
            WriteHostCaller(), expectedRowVersion: AipTestVersion.Any);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal($"{field} cannot be negative.", result.Error);
        Assert.Null(activity.Total);
    }

    [Fact]
    public async Task UpdateActivityAsync_WithZeroAndPositiveAmounts_Saves()
    {
        var (sut, activity, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, LegacyUpdate(0m, 100m, 50m),
            WriteHostCaller(), expectedRowVersion: AipTestVersion.Any);

        Assert.True(result.IsSuccess);
        Assert.Equal(150m, activity.Total);
    }

    [Theory]
    [InlineData(-1.0, null, null, "PS")]
    [InlineData(null, -1.0, null, "MOOE")]
    [InlineData(null, null, -1.0, "CO")]
    public async Task AddActivityAsync_WithNegativeAmount_ReturnsBadRequestAndAddsNothing(
        double? ps, double? mooe, double? co, string field)
    {
        var (sut, _, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(
            30, LegacyCreate((decimal?)ps, (decimal?)mooe, (decimal?)co), WriteHostCaller());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Equal($"{field} cannot be negative.", result.Error);
    }

    [Fact]
    public async Task AddActivityAsync_WithPositiveAmounts_Saves()
    {
        var (sut, _, _) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await sut.AddActivityAsync(
            30, LegacyCreate(100m, 0m, null), WriteHostCaller());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateActivityAsync_FreeTextMonthsAndTypology_StillSaveUnchecked()
    {
        // Clean-break rule (PPDO-292): the FY 2027 path gains the negative check and nothing else.
        var (sut, activity, typologies) = BuildForFieldChecks();

        ServiceResult<AipActivityDto> result = await sut.UpdateActivityAsync(
            AipRecordId, ConflictActivityId, LegacyUpdate(null, null, null, "Januray", "Jan", "A222-03, A224-05"),
            WriteHostCaller(), expectedRowVersion: AipTestVersion.Any);

        Assert.True(result.IsSuccess);
        Assert.Equal("Januray", activity.StartDate);
        Assert.Equal("Jan", activity.EndDate);
        Assert.Equal("A222-03, A224-05", activity.CcTypologyCode);
        typologies.Verify(r => r.ActiveCodeExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
