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
        IReadOnlyList<AuditLog>? items = null, int totalCount = 0)
    {
        Mock<IAuditRepository> repo = new();
        repo.Setup(r => r.GetPagedAsync(
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items ?? [], totalCount));
        return (new AuditLogService(repo.Object), repo);
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
        AuditLogService sut = new(repo.Object);

        IReadOnlyList<string> result = await sut.GetTableNamesAsync();

        Assert.Equal(["accounts", "users", "wfp_expenditures"], result);
    }
}
