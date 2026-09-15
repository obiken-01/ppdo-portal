using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// A real <see cref="ExternalAipReadService"/> response, enveloped exactly as
/// <c>ExternalAipFunctions</c> serializes it, validated against
/// <c>docs/external-api/aip-response.schema.json</c> (v1.8.0 — PPDO-12, README §7's own check,
/// now automated). If this test fails, either the service or the schema changed without the
/// other — the two are supposed to be a pair.
/// </summary>
public sealed class ExternalAipSchemaContractTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromText(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestAssets", "aip-response.schema.json")));

    // Matches ConfigHttp.Json — the options every external API response is actually serialized with.
    private static readonly JsonSerializerOptions ResponseJson = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static Office ConfigOffice(int id, string code, string name) =>
        new() { Id = id, OfficeCode = code, OfficeName = name, IsActive = true };

    private void AssertValidatesAgainstSchema(object envelope)
    {
        string json = JsonSerializer.Serialize(envelope, ResponseJson);
        JsonNode? node = JsonNode.Parse(json);

        EvaluationResults results = Schema.Evaluate(node, new EvaluationOptions
        {
            OutputFormat = OutputFormat.List,
            RequireFormatValidation = true,
        });

        if (!results.IsValid)
        {
            IEnumerable<string> failures = results.Details
                .Where(d => !d.IsValid && d.Errors is { Count: > 0 })
                .Select(d => $"{d.InstanceLocation}: {string.Join(", ", d.Errors!.Values)}");
            Assert.Fail("Response failed schema validation:\n" + string.Join("\n", failures) + "\n\n" + json);
        }
    }

    [Fact]
    public async Task Fy2028Response_WithFullTree_ValidatesAgainstSchema()
    {
        Mock<IAipRepository> aip = new();
        Mock<IAipExpenditureRepository> expenditures = new();
        Mock<IAuditRepository> audit = new();
        Mock<IOfficeRepository> offices = new();
        Mock<IRepository<FundingSource>> fundingSources = new();
        Mock<IPriceIndexItemRepository> priceIndexItems = new();

        AipRecord record = new() { Id = 1, FiscalYear = 2028, Status = PlanningStatus.Draft, EntrySource = "Manual", UploadedAt = DateTime.UtcNow };
        aip.Setup(a => a.GetLatestByFiscalYearAsync(2028, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        AipOffice group = new()
        {
            Id = 10, AipRecordId = 1, OfficeId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO",
            Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Consolidated,
        };
        aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([group]);

        AipProgram program = new() { Id = 100, OfficeId = 10, RefCode = "1000-000-1-01-010-001", Name = "Program A", FunctionBand = "CORE" };
        aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([program]);

        AipProject project = new() { Id = 200, ProgramId = 100, RefCode = "1000-000-1-01-010-001-001", Name = "Project A", IsSynthetic = false };
        aip.Setup(a => a.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([project]);

        AipActivity activity = new()
        {
            Id = 300, ProjectId = 200, RefCode = "1000-000-1-01-010-001-001-001", Name = "Activity A",
            EsreCode = "SS", ImplementingOffice = "PPDO", StartDate = "January", EndDate = "December",
            ExpectedOutputs = "50 units", CcAdaptation = 1000m, CcMitigation = 500m, CcTypologyCode = "A214-01,A222-03",
            Ps = 0m, Mooe = 1000400m, Co = 0m, IsSynthetic = false,
        };
        aip.Setup(a => a.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([activity]);

        AipExpenditure line = new()
        {
            Id = 400, ActivityId = 300, AccountId = 1, AccountNumberSnapshot = "5-02-03-010",
            AccountTitleSnapshot = "Office Supplies Expenses", FundingSourceSnapshot = "GF",
            FundingSourceNameSnapshot = "General Fund", Ps = 0m, Mooe = 1000400m, Co = 0m,
        };
        expenditures.Setup(e => e.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([line]);

        AipProcurementItem item = new()
        {
            Id = 500, ExpenditureId = 400, PriceIndexItemId = 900, Name = "Bond paper", Unit = "ream",
            UnitPrice = 250m, Qty = 4000m, NumberOfDays = 1m,
        };
        expenditures.Setup(e => e.GetProcurementItemsByExpenditureIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([item]);

        priceIndexItems.Setup(p => p.GetByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new PriceIndexItem { Id = 900, Name = "Bond paper", Unit = "ream", UnitPrice = 250m, StockCardNo = "SC-001" }]);

        audit.Setup(a => a.GetByRecordIdsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AuditLog { Id = 1, TableName = "aip_offices", RecordId = 10, Action = AuditAction.AcceptByPpdo, ChangedAt = DateTime.UtcNow, ChangedById = Guid.NewGuid() }]);

        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([ConfigOffice(1, "PPDO", "PPDO Office")]);
        fundingSources.Setup(f => f.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        ExternalAipReadService sut = new(
            aip.Object, expenditures.Object, audit.Object, offices.Object, fundingSources.Object, priceIndexItems.Object);

        ExternalAipDto? result = await sut.GetAsync(2028, null);
        Assert.NotNull(result);

        AssertValidatesAgainstSchema(ApiResponse<ExternalAipDto?>.Ok(result));
    }

    [Fact]
    public async Task LegacyResponse_ValidatesAgainstSchema()
    {
        Mock<IAipRepository> aip = new();
        Mock<IAipExpenditureRepository> expenditures = new();
        Mock<IAuditRepository> audit = new();
        Mock<IOfficeRepository> offices = new();
        Mock<IRepository<FundingSource>> fundingSources = new();
        Mock<IPriceIndexItemRepository> priceIndexItems = new();

        AipRecord record = new() { Id = 2, FiscalYear = 2027, Status = PlanningStatus.Final, EntrySource = "Upload", UploadedAt = DateTime.UtcNow };
        aip.Setup(a => a.GetLatestByFiscalYearAsync(2027, It.IsAny<CancellationToken>())).ReturnsAsync(record);

        AipOffice group = new()
        {
            Id = 20, AipRecordId = 2, OfficeId = 1, RefCode = "1000-000-1-01-010", Name = "PPDO",
            Sector = "GENERAL", WorkflowStatus = AipWorkflowStatus.Draft,
        };
        aip.Setup(a => a.GetOfficesByAipIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([group]);

        AipProgram program = new() { Id = 101, OfficeId = 20, RefCode = "1000-000-1-01-010-001", Name = "Program A" };
        aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([program]);

        AipProject project = new() { Id = 201, ProgramId = 101, RefCode = "1000-000-1-01-010-001-001", Name = "Project A" };
        aip.Setup(a => a.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([project]);

        AipActivity activity = new()
        {
            Id = 301, ProjectId = 201, RefCode = "1000-000-1-01-010-001-001-001", Name = "Activity A",
            FundingSourceSnapshot = "GF", Ps = 100m, Mooe = 200m, Co = 0m, IsSynthetic = false,
        };
        aip.Setup(a => a.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>())).ReturnsAsync([activity]);

        offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([ConfigOffice(1, "PPDO", "PPDO Office")]);
        fundingSources.Setup(f => f.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([new FundingSource { Id = 1, Code = "GF", Name = "General Fund" }]);

        ExternalAipReadService sut = new(
            aip.Object, expenditures.Object, audit.Object, offices.Object, fundingSources.Object, priceIndexItems.Object);

        ExternalAipDto? result = await sut.GetAsync(2027, null);
        Assert.NotNull(result);

        AssertValidatesAgainstSchema(ApiResponse<ExternalAipDto?>.Ok(result));
    }

    [Fact]
    public void NullDataResponse_ValidatesAgainstSchema()
        => AssertValidatesAgainstSchema(ApiResponse<ExternalAipDto?>.Ok(null));
}
