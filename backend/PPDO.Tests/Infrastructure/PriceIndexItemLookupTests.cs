using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The two single-item reads PPDO-186 added to <see cref="PriceIndexItemRepository"/>:
/// <see cref="PriceIndexItemRepository.GetByIntIdAsync"/> and
/// <see cref="PriceIndexItemRepository.NameAndUnitExistsAsync"/>.
///
/// <para>The service tests mock the repository, so nothing exercised these queries against a
/// database. The risk is in the <c>excludeId</c> branch (an item must not collide with itself on
/// update, but must collide with any other row) and in case-insensitivity, which the service no
/// longer does in C#: it now relies on the column's collation.</para>
///
/// <para>⚠️ Sqlite stands in for SQL Server here, so <c>name</c> and <c>unit</c> are declared
/// <c>COLLATE NOCASE</c> to match production's <c>SQL_Latin1_General_CP1_CI_AS</c> (checked on
/// the local database, 2026-10-04). These tests prove the query shape, not SQL Server's collation.</para>
/// </summary>
public sealed class PriceIndexItemLookupTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public PriceIndexItemLookupTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE price_index_items (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL COLLATE NOCASE,
                unit TEXT NOT NULL COLLATE NOCASE,
                unit_price TEXT NOT NULL DEFAULT '0',
                category TEXT NULL,
                stock_card_no TEXT NULL,
                price_updated_at TEXT NOT NULL DEFAULT '2026-01-01 00:00:00',
                is_active INTEGER NOT NULL DEFAULT 1,
                days_enabled INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL DEFAULT '2026-01-01 00:00:00',
                updated_at TEXT NOT NULL DEFAULT '2026-01-01 00:00:00'
            );
            INSERT INTO price_index_items (id, name, unit, unit_price, is_active) VALUES
                (1, 'Bond paper, A4', 'ream', '250.00', 1),
                (2, 'Ballpen, black', 'box',  '120.00', 1),
                (3, 'Old stapler',    'pc',   '80.00',  0);
            """);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<T> WithRepo<T>(Func<PriceIndexItemRepository, Task<T>> act)
    {
        using AppDbContext ctx = new(_options);
        return await act(new PriceIndexItemRepository(ctx));
    }

    // ── GetByIntIdAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIntIdAsync_Existing_ReturnsThatItem()
    {
        PriceIndexItem? item = await WithRepo(r => r.GetByIntIdAsync(2));

        Assert.NotNull(item);
        Assert.Equal("Ballpen, black", item!.Name);
    }

    [Fact]
    public async Task GetByIntIdAsync_Inactive_StillReturned()
    {
        // Deactivated rows are still addressable: the edit dialog and re-activation need them.
        PriceIndexItem? item = await WithRepo(r => r.GetByIntIdAsync(3));

        Assert.NotNull(item);
        Assert.False(item!.IsActive);
    }

    [Fact]
    public async Task GetByIntIdAsync_Missing_ReturnsNull()
        => Assert.Null(await WithRepo(r => r.GetByIntIdAsync(999)));

    [Fact]
    public async Task GetByIntIdAsync_ReturnsATrackedEntity()
    {
        // Update and deactivate modify what this returns and then save, so it must be tracked.
        using AppDbContext ctx = new(_options);
        PriceIndexItem? item = await new PriceIndexItemRepository(ctx).GetByIntIdAsync(1);

        Assert.Equal(EntityState.Unchanged, ctx.Entry(item!).State);
    }

    // ── NameAndUnitExistsAsync ────────────────────────────────────────────────

    [Theory]
    [InlineData("Bond paper, A4", "ream")]
    [InlineData("BOND PAPER, A4", "REAM")] // the old C# check was OrdinalIgnoreCase
    [InlineData("bond paper, a4", "Ream")]
    public async Task NameAndUnitExistsAsync_OnCreate_FindsAnExistingPairInAnyCase(string name, string unit)
        => Assert.True(await WithRepo(r => r.NameAndUnitExistsAsync(name, unit, excludeId: null)));

    [Fact]
    public async Task NameAndUnitExistsAsync_SameNameDifferentUnit_IsNotADuplicate()
        => Assert.False(await WithRepo(r => r.NameAndUnitExistsAsync("Bond paper, A4", "box", excludeId: null)));

    [Fact]
    public async Task NameAndUnitExistsAsync_InactiveRow_StillCounts()
        // The unique index covers inactive rows too, so the check must, or the save would 500.
        => Assert.True(await WithRepo(r => r.NameAndUnitExistsAsync("Old stapler", "pc", excludeId: null)));

    [Fact]
    public async Task NameAndUnitExistsAsync_OnUpdate_AnItemDoesNotCollideWithItself()
        => Assert.False(await WithRepo(r => r.NameAndUnitExistsAsync("Bond paper, A4", "ream", excludeId: 1)));

    [Fact]
    public async Task NameAndUnitExistsAsync_OnUpdate_RenamingOntoAnotherItem_Collides()
        => Assert.True(await WithRepo(r => r.NameAndUnitExistsAsync("ballpen, BLACK", "Box", excludeId: 1)));
}
