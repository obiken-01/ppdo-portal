using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Repositories;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// <see cref="AipRepository.GetOfficeRollupsAsync"/> — the one grouped read behind the dashboard's
/// Offices band, table and readiness board alike (PPDO-20, extended by PPDO-78).
///
/// <para>
/// <b>⚠️ Against a real database, because the risk is in the SQL.</b> The rollup left-joins
/// program → project → activity and groups the result, so a program with two activities is two
/// joined rows. Counting programs off that join would report three programs where there are two;
/// only a query that actually runs shows whether <see cref="AipOfficeRollupDto.ProgramCount"/> is
/// counted independently of it.
/// </para>
///
/// Uses the Sqlite in-memory pattern from <see cref="AipReviewSearchRepositoryTests"/>.
/// </summary>
public sealed class AipOfficeRollupRepositoryTests : IDisposable
{
    private const int Record   = 44;
    private const int OtherRec = 45;

    private const int Ppdo = 7;
    private const int Opa  = 15;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipOfficeRollupRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_offices (
                id INTEGER PRIMARY KEY,
                aip_record_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                sector TEXT NOT NULL DEFAULT '',
                office_id INTEGER NULL,
                workflow_status TEXT NOT NULL DEFAULT 'Draft'
            );
            CREATE TABLE aip_programs (
                id INTEGER PRIMARY KEY,
                office_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                function_band TEXT NOT NULL DEFAULT 'Core'
            );
            CREATE TABLE aip_projects (
                id INTEGER PRIMARY KEY,
                program_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                description TEXT NULL,
                objective TEXT NULL,
                is_synthetic INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE aip_activities (
                id INTEGER PRIMARY KEY,
                project_id INTEGER NOT NULL,
                ref_code TEXT NOT NULL DEFAULT '',
                name TEXT NOT NULL DEFAULT '',
                esre_code TEXT NULL,
                implementing_office TEXT NULL,
                start_date TEXT NULL,
                end_date TEXT NULL,
                expected_outputs TEXT NULL,
                funding_source_id INTEGER NULL,
                funding_source_snapshot TEXT NULL,
                ps TEXT NULL,
                mooe TEXT NULL,
                co TEXT NULL,
                total TEXT NULL,
                cc_adaptation TEXT NULL,
                cc_mitigation TEXT NULL,
                cc_typology_code TEXT NULL,
                is_creation INTEGER NOT NULL DEFAULT 0,
                is_synthetic INTEGER NOT NULL DEFAULT 0
            );
            """);
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// PPDO holds two programs, one with two activities (one costed) and one untouched. OPA holds two
    /// programs LDIP seeded and nobody has opened — the readiness board's Not Started office. A group
    /// on another record must not leak in.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AppDbContext ctx = new(_options);

        ctx.Set<AipOffice>().AddRange(
            Office(1, Record, "1000-000-1-01-010", Ppdo, AipWorkflowStatus.DepartmentReview),
            Office(2, Record, "1000-000-1-01-015", Opa, AipWorkflowStatus.Draft),
            Office(3, OtherRec, "1000-000-1-01-010", Ppdo, AipWorkflowStatus.Consolidated));

        ctx.Set<AipProgram>().AddRange(
            Program(10, 1, "1000-000-1-01-010-001"),
            Program(11, 1, "1000-000-1-01-010-002"),
            Program(12, 2, "1000-000-1-01-015-001"),
            Program(13, 2, "1000-000-1-01-015-002"),
            Program(14, 3, "1000-000-1-01-010-001"));

        ctx.Set<AipProject>().Add(
            new AipProject { Id = 20, ProgramId = 10, RefCode = "1000-000-1-01-010-001-001", Name = "Project" });

        ctx.Set<AipActivity>().AddRange(
            new AipActivity { Id = 30, ProjectId = 20, RefCode = "1000-000-1-01-010-001-001-001", Name = "Costed", Total = 100m },
            new AipActivity { Id = 31, ProjectId = 20, RefCode = "1000-000-1-01-010-001-001-002", Name = "Not yet" });

        await ctx.SaveChangesAsync();
    }

    private static AipOffice Office(int id, int rec, string refCode, int officeId, string status) => new()
    {
        Id = id, AipRecordId = rec, RefCode = refCode, Name = "Office", Sector = "GENERAL",
        OfficeId = officeId, WorkflowStatus = status,
    };

    private static AipProgram Program(int id, int officeId, string refCode) => new()
    {
        Id = id, OfficeId = officeId, RefCode = refCode, Name = "Program",
        FunctionBand = AipFunctionBand.Core,
    };

    private AipRepository Sut() => new(new AppDbContext(_options));

    [Fact]
    public async Task GetOfficeRollupsAsync_CountsProgramsApartFromTheActivityJoin()
    {
        await SeedAsync();

        IReadOnlyList<AipOfficeRollupDto> rows = await Sut().GetOfficeRollupsAsync(Record);

        Assert.Equal(2, rows.Count);

        AipOfficeRollupDto ppdo = rows.Single(r => r.OfficeId == Ppdo);
        Assert.Equal(2, ppdo.ActivityCount);
        Assert.Equal(1, ppdo.CostedActivityCount);
        Assert.Equal(100m, ppdo.CostedTotal);
        // Two programs, not three: program 10 comes back as two joined activity rows.
        Assert.Equal(2, ppdo.ProgramCount);
    }

    [Fact]
    public async Task GetOfficeRollupsAsync_AnUntouchedOffice_StillCountsItsSeededPrograms()
    {
        await SeedAsync();

        IReadOnlyList<AipOfficeRollupDto> rows = await Sut().GetOfficeRollupsAsync(Record);

        AipOfficeRollupDto opa = rows.Single(r => r.OfficeId == Opa);
        Assert.Equal(0, opa.ActivityCount);
        Assert.Equal(2, opa.ProgramCount);
    }

    [Fact]
    public async Task GetOfficeRollupsAsync_CarriesEachGroupsWorkflowStatus()
    {
        await SeedAsync();

        IReadOnlyList<AipOfficeRollupDto> rows = await Sut().GetOfficeRollupsAsync(Record);

        Assert.Equal(AipWorkflowStatus.DepartmentReview, rows.Single(r => r.OfficeId == Ppdo).WorkflowStatus);
        Assert.Equal(AipWorkflowStatus.Draft, rows.Single(r => r.OfficeId == Opa).WorkflowStatus);
    }
}
