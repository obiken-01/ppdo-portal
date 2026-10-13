using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="AuditLogService"/>. IAuditRepository is mocked; no database access.
/// </summary>
public sealed class AuditLogServiceTests
{
    private static AuditLog MakeLog(long id, string table = "accounts", string action = "CREATE") => new()
    {
        Id = id,
        TableName = table,
        Action = action,
        RecordId = (int)id,
        ChangedById = Guid.NewGuid(),
        ChangedAt = DateTime.SpecifyKind(new DateTime(2026, 7, 17, 9, 0, 0), DateTimeKind.Unspecified),
        NewValues = """{"accountTitle":"Office Supplies"}""",
        ChangedBy = new User { Id = Guid.NewGuid(), FullName = "R. Alcaide", Username = "ralpharmand", PasswordHash = "x" },
    };

    private static (AuditLogService sut, Mock<IAuditRepository> repo) Build(
        IReadOnlyList<AuditLog>? items = null, int totalCount = 0,
        Dictionary<int, AipRecordLabel>? liveActivities = null)
    {
        // PPDO-110 — the ref-code resolver, over a label repository that knows these activities.
        Mock<IActivityLabelRepository> labels = new();
        labels.Setup(r => r.GetAipRecordLabelsAsync(
                It.IsAny<AipRecordKind>(), It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipRecordKind kind, IReadOnlyCollection<int> ids, CancellationToken _) =>
                (IReadOnlyDictionary<int, AipRecordLabel>)(kind == AipRecordKind.Activity && liveActivities is not null
                    ? liveActivities.Where(kv => ids.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value)
                    : new Dictionary<int, AipRecordLabel>()));

        Mock<IAuditRepository> repo = new();
        repo.Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items ?? [], totalCount));
        return (new AuditLogService(repo.Object, new AuditRecordCodeResolver(labels.Object)), repo);
    }

    [Fact]
    public async Task GetPagedAsync_MapsEntriesToDto_IncludingDescription()
    {
        (AuditLogService sut, _) = Build([MakeLog(1)], totalCount: 1);

        AuditLogPageDto result = await sut.GetPagedAsync(new AuditLogFilterDto(1, 50, null, null, null, null, null));

        Assert.Single(result.Items);
        Assert.Equal("accounts", result.Items[0].TableName);
        Assert.Equal("R. Alcaide", result.Items[0].ActorName);
        Assert.Contains("Account Title", result.Items[0].Description);
    }

    [Fact]
    public async Task GetPagedAsync_ChangedAt_StampedAsUtc()
    {
        // Mirrors RAL-172: EF Core returns Kind=Unspecified for datetime2 columns.
        (AuditLogService sut, _) = Build([MakeLog(1)], totalCount: 1);

        AuditLogPageDto result = await sut.GetPagedAsync(new AuditLogFilterDto(1, 50, null, null, null, null, null));

        Assert.Equal(DateTimeKind.Utc, result.Items[0].ChangedAt.Kind);
    }

    [Fact]
    public async Task GetPagedAsync_ReturnsTotalCountFromRepository()
    {
        (AuditLogService sut, _) = Build([MakeLog(1), MakeLog(2)], totalCount: 137);

        AuditLogPageDto result = await sut.GetPagedAsync(new AuditLogFilterDto(1, 50, null, null, null, null, null));

        Assert.Equal(137, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_PageBelowOne_ClampedToOne()
    {
        (AuditLogService sut, Mock<IAuditRepository> repo) = Build();

        await sut.GetPagedAsync(new AuditLogFilterDto(0, 50, null, null, null, null, null));

        repo.Verify(r => r.GetPagedAsync(
            1, It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_PageSizeAboveMax_ClampedToMax()
    {
        (AuditLogService sut, Mock<IAuditRepository> repo) = Build();

        await sut.GetPagedAsync(new AuditLogFilterDto(1, 10_000, null, null, null, null, null));

        repo.Verify(r => r.GetPagedAsync(
            It.IsAny<int>(), 200, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_PageSizeZeroOrNegative_ClampedToOne()
    {
        (AuditLogService sut, Mock<IAuditRepository> repo) = Build();

        await sut.GetPagedAsync(new AuditLogFilterDto(1, -5, null, null, null, null, null));

        repo.Verify(r => r.GetPagedAsync(
            It.IsAny<int>(), 1, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPagedAsync_ForwardsFiltersToRepository()
    {
        (AuditLogService sut, Mock<IAuditRepository> repo) = Build();
        DateTime from = new(2026, 7, 1);
        DateTime to = new(2026, 7, 31);

        await sut.GetPagedAsync(new AuditLogFilterDto(2, 25, "users", "UPDATE", "alcaide", from, to));

        repo.Verify(r => r.GetPagedAsync(2, 25, "users", "UPDATE", "alcaide", from, to, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetTableNamesAsync_DelegatesToRepository()
    {
        Mock<IAuditRepository> repo = new();
        repo.Setup(r => r.GetDistinctTableNamesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(["accounts", "users", "wfp_expenditures"]);
        AuditLogService sut = new(repo.Object, new AuditRecordCodeResolver(new Mock<IActivityLabelRepository>().Object));

        IReadOnlyList<string> result = await sut.GetTableNamesAsync();

        Assert.Equal(["accounts", "users", "wfp_expenditures"], result);
    }

    // ── PPDO-110: ref codes instead of #id ────────────────────────────────────

    [Fact]
    public async Task GetPagedAsync_AipActivityRow_CarriesItsRefCodeAndName()
    {
        (AuditLogService sut, _) = Build(
            [MakeLog(5, table: "aip_activities", action: "UPDATE")], totalCount: 1,
            liveActivities: new() { [5] = new AipRecordLabel("1000-000-1-01-010-001-001-001", "Conduct of LDC meetings", "PPDO") });

        AuditLogEntryDto entry = Assert.Single((await sut.GetPagedAsync(new AuditLogFilterDto(1, 50, null, null, null, null, null))).Items);

        Assert.Equal("1000-000-1-01-010-001-001-001", entry.RecordCode);
        Assert.Equal("Conduct of LDC meetings", entry.RecordName);
        Assert.Equal(5, entry.RecordId);
    }

    [Fact]
    public async Task GetPagedAsync_OtherTables_HaveNoRecordCode_SoThePageKeepsTheId()
    {
        (AuditLogService sut, _) = Build([MakeLog(1, table: "accounts")], totalCount: 1);

        AuditLogEntryDto entry = Assert.Single((await sut.GetPagedAsync(new AuditLogFilterDto(1, 50, null, null, null, null, null))).Items);

        Assert.Null(entry.RecordCode);
        Assert.Null(entry.RecordName);
    }
}
