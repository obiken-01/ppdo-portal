using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="PriceIndexItemRepository.GetVersionStampAsync"/> (PPDO-183), the fingerprint behind
/// the picker's ETag.
///
/// <para>Against a real database because the query shape is the risk: a constant-key
/// <c>GroupBy</c> with <c>Count</c> and <c>Max</c> has to translate to one aggregate, and the empty
/// table must come back as (0, null) rather than throw. Sqlite in-memory, hand-written DDL — the
/// pattern from <see cref="AipNotificationRepositoryTests"/>.</para>
/// </summary>
public sealed class PriceIndexVersionStampTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PriceIndexVersionStampTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE price_index_items (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                unit TEXT NOT NULL,
                unit_price TEXT NOT NULL DEFAULT '0',
                category TEXT NULL,
                stock_card_no TEXT NULL,
                price_updated_at TEXT NOT NULL DEFAULT '2026-01-01 00:00:00',
                is_active INTEGER NOT NULL DEFAULT 1,
                days_enabled INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT '2026-01-01 00:00:00',
                updated_at TEXT NOT NULL
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    private void Insert(int id, string updatedAt, bool active = true)
    {
        using AppDbContext ctx = new(_options);
        ctx.Database.ExecuteSqlRaw(
            "INSERT INTO price_index_items (id, name, unit, is_active, updated_at) VALUES ({0}, {1}, 'pc', {2}, {3})",
            id, $"Item {id}", active ? 1 : 0, updatedAt);
    }

    private async Task<(int Count, DateTime? LastUpdatedAt)> StampAsync()
    {
        using AppDbContext ctx = new(_options);
        return await new PriceIndexItemRepository(ctx).GetVersionStampAsync();
    }

    [Fact]
    public async Task GetVersionStampAsync_EmptyTable_ReturnsZeroAndNull()
    {
        (int count, DateTime? last) = await StampAsync();

        Assert.Equal(0, count);
        Assert.Null(last);
    }

    [Fact]
    public async Task GetVersionStampAsync_WithRows_CountsEveryRowAndTakesLatestUpdate()
    {
        Insert(1, "2026-09-01 08:00:00");
        Insert(2, "2026-10-04 09:30:00", active: false); // inactive rows count too: the stamp is table-wide
        Insert(3, "2026-09-15 12:00:00");

        (int count, DateTime? last) = await StampAsync();

        Assert.Equal(3, count);
        Assert.Equal(new DateTime(2026, 10, 4, 9, 30, 0), last);
    }
}
