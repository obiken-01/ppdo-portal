using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="AipRepository.GetRecordsAsync"/> (PPDO-42): the AIP list page's fiscal-year and
/// status filters, which used to run in memory over every AIP record.
///
/// <para>The service tests mock the repository, so these are what prove the filters and the
/// ordering actually reach SQL.</para>
///
/// <para>⚠️ Sqlite stands in for SQL Server, so <c>status</c> is declared <c>COLLATE NOCASE</c> to
/// match production's case-insensitive collation — the in-memory version compared with
/// <c>OrdinalIgnoreCase</c>, and the SQL version now relies on the column instead. These tests
/// prove the query shape, not SQL Server's collation.</para>
/// </summary>
public sealed class AipRecordListRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipRecordListRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_records (
                id INTEGER PRIMARY KEY,
                fiscal_year INTEGER NOT NULL,
                entry_source TEXT NOT NULL DEFAULT 'Upload',
                original_filename TEXT NULL,
                uploaded_by TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                uploaded_at TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft' COLLATE NOCASE,
                ldip_id INTEGER NULL,
                source_id INTEGER NULL
            );
            INSERT INTO aip_records (id, fiscal_year, uploaded_at, status) VALUES
                (1, 2027, '2026-07-01 00:00:00', 'Final'),
                (2, 2028, '2026-09-01 00:00:00', 'Draft'),
                (3, 2027, '2026-08-01 00:00:00', 'Draft'),
                (4, 2026, '2026-06-01 00:00:00', 'Archived');
            """);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<IReadOnlyList<AipRecord>> Run(int? fiscalYear, string? status)
    {
        await using AppDbContext ctx = new(_options);
        return await new AipRepository(ctx).GetRecordsAsync(fiscalYear, status);
    }

    [Fact]
    public async Task GetRecordsAsync_NoFilters_ReturnsAllNewestUploadFirst()
    {
        IReadOnlyList<AipRecord> result = await Run(null, null);

        Assert.Equal([2, 3, 1, 4], result.Select(r => r.Id));
    }

    [Fact]
    public async Task GetRecordsAsync_FiscalYear_ReturnsOnlyThatYear()
    {
        IReadOnlyList<AipRecord> result = await Run(2027, null);

        Assert.Equal([3, 1], result.Select(r => r.Id));
    }

    [Fact]
    public async Task GetRecordsAsync_Status_IsCaseInsensitiveAndTrimmed()
    {
        IReadOnlyList<AipRecord> result = await Run(null, "  draft ");

        Assert.Equal([2, 3], result.Select(r => r.Id));
    }

    [Fact]
    public async Task GetRecordsAsync_BothFilters_Combine()
    {
        IReadOnlyList<AipRecord> result = await Run(2027, "Final");

        Assert.Equal([1], result.Select(r => r.Id));
    }

    [Fact]
    public async Task GetRecordsAsync_BlankStatus_MeansNoStatusFilter()
    {
        IReadOnlyList<AipRecord> result = await Run(2027, "   ");

        Assert.Equal([3, 1], result.Select(r => r.Id));
    }

    [Fact]
    public async Task GetRecordsAsync_ReturnsUntrackedRows()
    {
        await using AppDbContext ctx = new(_options);
        await new AipRepository(ctx).GetRecordsAsync(null, null);

        Assert.Empty(ctx.ChangeTracker.Entries());
    }
}
