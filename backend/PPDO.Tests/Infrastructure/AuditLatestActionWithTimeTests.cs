using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="AuditRepository.GetLatestActionWithTimeAsync"/> (PPDO-175): the office's latest
/// AIP hand-off and when it happened, for the dashboard's "with PPDO since Oct 3".
///
/// <para>Against a real database (Sqlite in-memory, hand-written DDL of only the columns the query
/// touches) because the ordering and the filters are the risk: the newest row among the office's
/// groups, only hand-off actions, only this table, and the time handed back as UTC.</para>
/// </summary>
public sealed class AuditLatestActionWithTimeTests : IDisposable
{
    private static readonly string[] HandOffs = ["SUBMIT_DH", "SUBMIT_PPD", "RETURN_PPD", "RETURN_DH"];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AuditLatestActionWithTimeTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE audit_log (
                id INTEGER PRIMARY KEY,
                table_name TEXT NOT NULL,
                record_id INTEGER NULL,
                record_guid TEXT NULL,
                action TEXT NOT NULL,
                changed_by TEXT NULL,
                changed_at TEXT NOT NULL,
                old_values TEXT NULL,
                new_values TEXT NULL
            );
            INSERT INTO audit_log (id, table_name, record_id, action, changed_at) VALUES
                (1, 'aip_offices',   50, 'SUBMIT_DH',  '2026-09-30 01:00:00'),
                (2, 'aip_offices',   51, 'SUBMIT_PPD', '2026-10-03 02:30:00'),
                (3, 'aip_offices',   50, 'UPDATE',     '2026-10-04 08:00:00'),
                (4, 'aip_activities', 50, 'RETURN_PPD', '2026-10-05 00:00:00'),
                (5, 'aip_offices',   99, 'RETURN_PPD', '2026-10-06 00:00:00');
            """);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<(string Action, DateTime ChangedAt)?> LatestAsync(params int[] groupIds)
    {
        using AppDbContext ctx = new(_options);
        return await new AuditRepository(ctx).GetLatestActionWithTimeAsync("aip_offices", groupIds, HandOffs);
    }

    [Fact]
    public async Task GetLatestActionWithTimeAsync_PicksTheNewestHandOffAcrossTheOfficesGroups()
    {
        // Row 3 is newer but not a hand-off; row 4 is a hand-off on another table; row 5 belongs
        // to another office. The answer is row 2.
        (string Action, DateTime ChangedAt)? latest = await LatestAsync(50, 51);

        Assert.NotNull(latest);
        Assert.Equal("SUBMIT_PPD", latest!.Value.Action);
        Assert.Equal(new DateTime(2026, 10, 3, 2, 30, 0), latest.Value.ChangedAt);
    }

    [Fact]
    public async Task GetLatestActionWithTimeAsync_ReturnsUtc()
    {
        (string Action, DateTime ChangedAt)? latest = await LatestAsync(50, 51);

        Assert.Equal(DateTimeKind.Utc, latest!.Value.ChangedAt.Kind);
    }

    [Fact]
    public async Task GetLatestActionWithTimeAsync_NoHandOffYet_ReturnsNull()
        => Assert.Null(await LatestAsync(77));

    [Fact]
    public async Task GetLatestActionWithTimeAsync_NoGroups_ReturnsNullWithoutQuerying()
        => Assert.Null(await LatestAsync());
}
