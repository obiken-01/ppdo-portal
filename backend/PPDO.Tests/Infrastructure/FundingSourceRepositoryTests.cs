using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// PPDO-282 — <see cref="FundingSourceRepository.NameExistsAsync"/> runs as one query in the
/// database, not over a loaded list. Proved against a real (Sqlite in-memory) database: SQLite
/// compares text case-sensitively, so a pass here shows the query itself ignores case and outer
/// spaces rather than leaning on SQL Server's case-insensitive collation. Only the one table is
/// created by hand, matching <c>FundingSourceConfiguration</c>'s column names (the pattern of
/// <see cref="AipExpenditureRepositoryTests"/>).
/// </summary>
public sealed class FundingSourceRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public FundingSourceRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE funding_sources (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                code TEXT NOT NULL,
                name TEXT NOT NULL,
                description TEXT NULL,
                color TEXT NULL,
                aliases TEXT NULL,
                office_id INTEGER NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                enforce_aip_ceiling INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            INSERT INTO funding_sources (id, code, name, office_id, is_active, created_at, updated_at) VALUES
                (1, 'GF', 'General Fund', NULL, 1, '2026-01-01', '2026-01-01'),
                (2, 'TF', ' Trust Fund ', 7,    0, '2026-01-01', '2026-01-01');
            """);
    }

    public void Dispose() => _connection.Dispose();

    private async Task<bool> Exists(string name, int? exceptId = null)
    {
        await using AppDbContext ctx = new(_options);
        return await new FundingSourceRepository(ctx).NameExistsAsync(name, exceptId);
    }

    [Theory]
    [InlineData("General Fund")]
    [InlineData("general fund")]
    [InlineData("  GENERAL FUND  ")]
    public async Task NameExistsAsync_SameNameIgnoringCaseAndOuterSpaces_ReturnsTrue(string name)
        => Assert.True(await Exists(name));

    [Fact]
    public async Task NameExistsAsync_StoredWithOuterSpaces_InactiveAndOfficeOwned_ReturnsTrue()
        => Assert.True(await Exists("trust fund"));

    [Fact]
    public async Task NameExistsAsync_NewName_ReturnsFalse()
        => Assert.False(await Exists("General Fund 2"));

    [Fact]
    public async Task NameExistsAsync_OnlyTheFundBeingEditedHasIt_ReturnsFalse()
        => Assert.False(await Exists("General Fund", exceptId: 1));

    [Fact]
    public async Task NameExistsAsync_AnotherFundHasIt_WhileEditingADifferentOne_ReturnsTrue()
        => Assert.True(await Exists("General Fund", exceptId: 2));
}
