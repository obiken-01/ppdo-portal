using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="AipDivisionSubmissionRepository"/> (PPDO-130 T1 / PPDO-147). Runs against Sqlite so
/// the office-and-record scoping is proven on a query that actually runs, in the in-memory pattern
/// of <see cref="AipOfficeRollupRepositoryTests"/>.
/// </summary>
public sealed class AipDivisionSubmissionRepositoryTests : IDisposable
{
    private const int Record   = 28;
    private const int OtherRec = 29;
    private const int Ppdo     = 7;
    private const int Opa      = 15;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipDivisionSubmissionRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_division_submissions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                aip_record_id INTEGER NOT NULL,
                office_id INTEGER NOT NULL,
                division_id INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                submitted_at TEXT NULL,
                submitted_by_user_id TEXT NULL,
                returned_at TEXT NULL,
                returned_by_user_id TEXT NULL,
                UNIQUE (aip_record_id, division_id)
            );
            INSERT INTO aip_division_submissions (aip_record_id, office_id, division_id, status) VALUES
                (28, 7, 2, 'Submitted'),
                (28, 7, 1, 'Draft'),
                (28, 15, 4, 'Submitted'),
                (29, 7, 1, 'Submitted');
            """);
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task GetForOfficeAsync_ReturnsOnlyThatOfficeInThatRecord_OrderedByDivision()
    {
        await using AppDbContext db = new(_options);
        AipDivisionSubmissionRepository repo = new(db);

        IReadOnlyList<AipDivisionSubmission> rows = await repo.GetForOfficeAsync(Record, Ppdo);

        Assert.Equal(new[] { 1, 2 }, rows.Select(r => r.DivisionId).ToArray());
        Assert.All(rows, r => Assert.Equal(Record, r.AipRecordId));
        Assert.All(rows, r => Assert.Equal(Ppdo, r.OfficeId));
    }

    [Fact]
    public async Task GetForOfficeAsync_NoRows_ReturnsEmpty()
    {
        // No row means every division is Draft, so an empty list is the normal starting state.
        await using AppDbContext db = new(_options);
        AipDivisionSubmissionRepository repo = new(db);

        Assert.Empty(await repo.GetForOfficeAsync(OtherRec, Opa));
    }

    [Fact]
    public async Task GetForOfficeAsync_RowsAreTracked_SoATransitionSavesInPlace()
    {
        await using (AppDbContext db = new(_options))
        {
            AipDivisionSubmissionRepository repo = new(db);
            AipDivisionSubmission planning =
                (await repo.GetForOfficeAsync(Record, Ppdo)).Single(r => r.DivisionId == 1);

            planning.Status = AipDivisionStatus.Submitted;
            await repo.SaveChangesAsync();
        }

        await using AppDbContext verify = new(_options);
        Assert.Equal(AipDivisionStatus.Submitted, (await verify.AipDivisionSubmissions
            .SingleAsync(r => r.AipRecordId == Record && r.DivisionId == 1)).Status);
    }

    [Fact]
    public async Task AddAsync_ThenSave_WritesANewRow()
    {
        await using (AppDbContext db = new(_options))
        {
            AipDivisionSubmissionRepository repo = new(db);
            await repo.AddAsync(new AipDivisionSubmission
            {
                AipRecordId = OtherRec,
                OfficeId    = Opa,
                DivisionId  = 4,
                Status      = AipDivisionStatus.Submitted,
            });
            await repo.SaveChangesAsync();
        }

        await using AppDbContext verify = new(_options);
        AipDivisionSubmissionRepository check = new(verify);
        Assert.Single(await check.GetForOfficeAsync(OtherRec, Opa));
    }
}
