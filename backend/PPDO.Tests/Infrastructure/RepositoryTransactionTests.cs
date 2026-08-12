using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Proves <see cref="Repository{T}.ExecuteInTransactionAsync"/> actually rolls back on
/// failure (RAL-207) — the guarantee <c>StockBalanceService.CommitImportAsync</c> depends
/// on to keep a bulk import atomic. Application-layer tests mock the repository and can
/// only verify the *call shape*; this is the layer that owns the real transaction, so it's
/// the one that needs a real database underneath it.
///
/// Uses a Sqlite in-memory connection instead of <c>Database.EnsureCreated()</c> against the
/// full <see cref="AppDbContext"/> model — that model's DDL targets SQL Server (e.g.
/// <c>HasDefaultValueSql("GETUTCDATE()")</c>) across ~90 entities, more surface than this test
/// needs. Only the one table under test (<c>stock_balances</c>) is created, by hand, matching
/// <c>StockBalanceConfiguration</c>'s column names.
/// </summary>
public sealed class RepositoryTransactionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public RepositoryTransactionTests()
    {
        // The in-memory Sqlite database lives only as long as this connection stays open —
        // every AppDbContext in a test opens a new connection-scoped context against it.
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE stock_balances (
                id TEXT PRIMARY KEY,
                stock_no TEXT NOT NULL,
                counted_qty TEXT NOT NULL,
                system_on_hand_at_entry TEXT NOT NULL,
                variance_qty TEXT NOT NULL,
                effective_date TEXT NOT NULL,
                reason TEXT NULL,
                recorded_by_user_id TEXT NOT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    private static StockBalance MakeEntry(string stockNo) => new()
    {
        Id = Guid.NewGuid(),
        StockNo = stockNo,
        CountedQty = 10m,
        SystemOnHandAtEntry = 0m,
        VarianceQty = 10m,
        EffectiveDate = DateOnly.FromDateTime(DateTime.UtcNow),
        RecordedByUserId = Guid.NewGuid(),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public async Task ExecuteInTransactionAsync_AllStepsSucceed_CommitsEverything()
    {
        await using (AppDbContext context = new(_options))
        {
            Repository<StockBalance> repo = new(context);

            await repo.ExecuteInTransactionAsync(async () =>
            {
                await repo.AddAsync(MakeEntry("A01"));
                await repo.SaveChangesAsync();

                await repo.AddAsync(MakeEntry("B01"));
                await repo.SaveChangesAsync();
            });
        }

        await using AppDbContext verify = new(_options);
        int count = await verify.Set<StockBalance>().CountAsync();
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task ExecuteInTransactionAsync_LaterStepThrows_RollsBackEarlierSaveChangesToo()
    {
        // Mirrors StockBalanceService.CommitImportAsync's shape: row 1 saves successfully
        // (its own SaveChangesAsync call completes), then row 2 fails — without the
        // transaction wrap, row 1 would already be committed. This is the exact partial-commit
        // bug RAL-207 closes: a multi-row import where a later row throws must leave zero
        // rows persisted, not "everything up to the failure."
        await using (AppDbContext context = new(_options))
        {
            Repository<StockBalance> repo = new(context);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                repo.ExecuteInTransactionAsync(async () =>
                {
                    await repo.AddAsync(MakeEntry("A01"));
                    await repo.SaveChangesAsync(); // this row's save completes...

                    throw new InvalidOperationException("row 2 failed");
                }));
        }

        await using AppDbContext verify = new(_options);
        int count = await verify.Set<StockBalance>().CountAsync();
        Assert.Equal(0, count); // ...but the transaction rolls it back along with everything else
    }
}
