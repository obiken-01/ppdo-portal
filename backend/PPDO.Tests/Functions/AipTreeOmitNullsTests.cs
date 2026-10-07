using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// The AIP detail and summary reads leave null properties out of the body (PPDO-185): most
/// activities carry no division, CC figures or typology, and those nulls were about a fifth of a
/// 1.5 MB response.
///
/// <para>The rules that matter: nulls are gone, while <c>0</c> amounts, <c>false</c> flags and
/// empty lists stay (readers rely on those, which is why this is <c>WhenWritingNull</c> and not
/// <c>WhenWritingDefault</c>), and every other endpoint keeps writing its nulls.</para>
/// </summary>
public sealed class AipTreeOmitNullsTests
{
    private readonly Mock<IAipService>                _aip         = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator>              _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService>         _permissions = new(MockBehavior.Loose);
    private readonly Mock<IFundingSourceRepository> _fsRepo      = new(MockBehavior.Strict);

    private AipFunctions Sut => new(_aip.Object, _jwt.Object, _permissions.Object, _fsRepo.Object);

    private static readonly User Caller = new()
    {
        Id = Guid.NewGuid(), FullName = "Encoder", Username = "encoder", PasswordHash = "hash",
        Role = UserRole.Staff,
    };

    public AipTreeOmitNullsTests()
    {
        _jwt.Setup(j => j.ValidateAsync("Bearer test-token", It.IsAny<CancellationToken>())).ReturnsAsync(Caller);
        _permissions.Setup(p => p.CanAccessBudgetPlanningAsync(Caller, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    /// <summary>An activity shaped like most of record 13's: no division, no CC, no typology.</summary>
    private static AipActivityDto SparseActivity() => new(
        Id: 7, ProjectId: 3, RefCode: "1000-000-1-01-001-001", Name: "Office operations",
        EsreCode: null, ImplementingOffice: null, StartDate: null, EndDate: null, ExpectedOutputs: null,
        FundingSourceId: null, FundingSourceSnapshot: null,
        Ps: 0m, Mooe: 1500m, Co: null, Total: 1500m,
        CcAdaptation: null, CcMitigation: null, CcTypologyCode: null,
        IsCreation: false, IsSynthetic: false, FundCodes: [], DivisionId: null, DivisionName: null);

    private static AipRecordDetailDto Detail() => new(
        Id: 13, FiscalYear: 2028, EntrySource: "Manual", OriginalFilename: null,
        UploadedById: Guid.NewGuid(), UploadedAt: new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc),
        Status: "Draft", LdipId: null, SourceId: null,
        Offices:
        [
            new AipOfficeDto(1, 13, "1000-000-1-01-001", "PPDO", "GENERAL", OfficeId: null,
            [
                new AipProgramDto(2, 1, "1000-000-1-01-001-1", "Program", FunctionBand: null, Projects:
                [
                    new AipProjectDto(3, 2, "1000-000-1-01-001-1-01", "Project", [SparseActivity()]),
                ]),
            ]),
        ]);

    private static JsonElement Activity(HttpResponseData res) => JsonDocument.Parse(FunctionHttp.BodyText(res))
        .RootElement.GetProperty("data").GetProperty("offices")[0]
        .GetProperty("programs")[0].GetProperty("projects")[0].GetProperty("activities")[0];

    [Fact]
    public async Task Get_SparseActivity_OmitsNullsButKeepsZeroFalseAndEmptyList()
    {
        _aip.Setup(a => a.GetByIdAsync(13, Caller, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipRecordDetailDto>.Ok(Detail()));

        HttpResponseData res = await Sut.Get(FunctionHttp.Get("", path: "budget-planning/aip/13"), 13, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        JsonElement activity = Activity(res);

        foreach (string gone in new[] { "esreCode", "ccTypologyCode", "ccMitigation", "ccAdaptation",
                                        "divisionId", "divisionName", "co", "fundingSourceId" })
            Assert.False(activity.TryGetProperty(gone, out _), $"{gone} should be omitted when null");

        Assert.Equal(0m,    activity.GetProperty("ps").GetDecimal());
        Assert.False(activity.GetProperty("isSynthetic").GetBoolean());
        Assert.False(activity.GetProperty("isCreation").GetBoolean());
        Assert.Equal(0,     activity.GetProperty("fundCodes").GetArrayLength());
        Assert.Equal("Office operations", activity.GetProperty("name").GetString());
    }

    [Fact]
    public async Task GetSummary_NullFields_AreOmitted()
    {
        AipRecordSummaryDto summary = new(13, 2028,
        [
            new AipOfficeSummaryDto(1, "1000-000-1-01-001", "PPDO", "GENERAL",
            [
                new AipProgramSummaryDto(2, "1000-000-1-01-001-1", "Program",
                [
                    new AipProjectSummaryDto(3, "1000-000-1-01-001-1-01", "Project",
                    [
                        new AipActivitySummaryDto(7, "1000-000-1-01-001-001", "Office operations",
                            Ps: null, Mooe: 0m, Co: null, Total: 0m, FundingSourceId: null,
                            FundingSourceSnapshot: null, IsCreation: false),
                    ]),
                ], FunctionBand: null),
            ]),
        ]);
        _aip.Setup(a => a.GetSummaryByIdAsync(13, Caller, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AipRecordSummaryDto>.Ok(summary));

        HttpResponseData res = await Sut.GetSummary(
            FunctionHttp.Get("", path: "budget-planning/aip/13/summary"), 13, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        JsonElement program = JsonDocument.Parse(FunctionHttp.BodyText(res))
            .RootElement.GetProperty("data").GetProperty("offices")[0].GetProperty("programs")[0];
        JsonElement activity = program.GetProperty("projects")[0].GetProperty("activities")[0];

        Assert.False(program.TryGetProperty("functionBand", out _));
        Assert.False(activity.TryGetProperty("ps", out _));
        Assert.False(activity.TryGetProperty("fundingSourceSnapshot", out _));
        Assert.Equal(0m, activity.GetProperty("mooe").GetDecimal());
        Assert.False(activity.GetProperty("isCreation").GetBoolean());
    }

    [Fact]
    public async Task FromResultAsync_WithoutOptions_StillWritesNulls()
    {
        // The opt-in must not leak: every other endpoint's readers may test `=== null`.
        HttpResponseData res = await ConfigHttp.FromResultAsync(
            FunctionHttp.Get(""), ServiceResult<AipActivityDto>.Ok(SparseActivity()), CancellationToken.None);

        JsonElement data = JsonDocument.Parse(FunctionHttp.BodyText(res)).RootElement.GetProperty("data");
        Assert.Equal(JsonValueKind.Null, data.GetProperty("divisionId").ValueKind);
        Assert.Equal(JsonValueKind.Null, data.GetProperty("ccTypologyCode").ValueKind);
    }
}
