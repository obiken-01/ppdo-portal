using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PPDO.Infrastructure.Data;
using PPDO.Infrastructure.Data.Migrations;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The FY2028+ activity-division backfill in <c>AddAipDivisionSubmit</c> (PPDO-130 T1 / PPDO-147,
/// <c>Division_Submit_Spec.md</c> §5).
///
/// <para>
/// <b>⚠️ Runs the migration's own statement, <see cref="AddAipDivisionSubmit.BackfillSql"/>, against
/// a real database.</b> The rule lives entirely in the SQL: exactly one active division of the
/// activity's own office, FY2028+ only, and nulls left alone. A copy of the rule in C# would
/// prove nothing about the text that actually runs in production.
/// </para>
///
/// Hand-written DDL for only the tables the statement touches, in the Sqlite in-memory pattern of
/// <see cref="AipOfficeRollupRepositoryTests"/>.
/// </summary>
public sealed class AipDivisionBackfillTests : IDisposable
{
    // Config offices.
    private const int Ppdo = 7;
    private const int Opa  = 15;

    // Divisions: two active in PPDO, one inactive in PPDO, one active in OPA.
    private const int Planning    = 1;
    private const int Engineering = 2;
    private const int Retired     = 3;
    private const int OpaDivision = 4;

    // AIP records.
    private const int Fy2028 = 28;
    private const int Fy2027 = 27;
    private const int Fy2029 = 29;

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public AipDivisionBackfillTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using AppDbContext setup = new(_options);
        setup.Database.ExecuteSqlRaw("""
            CREATE TABLE aip_records (id INTEGER PRIMARY KEY, fiscal_year INTEGER NOT NULL);
            CREATE TABLE aip_offices (
                id INTEGER PRIMARY KEY, aip_record_id INTEGER NOT NULL, office_id INTEGER NULL);
            CREATE TABLE aip_programs (
                id INTEGER PRIMARY KEY, office_id INTEGER NOT NULL, ref_code TEXT NOT NULL);
            CREATE TABLE aip_projects (id INTEGER PRIMARY KEY, program_id INTEGER NOT NULL);
            CREATE TABLE aip_activities (
                id INTEGER PRIMARY KEY, project_id INTEGER NOT NULL, division_id INTEGER NULL);
            CREATE TABLE divisions (
                id INTEGER PRIMARY KEY, office_id INTEGER NOT NULL, is_active INTEGER NOT NULL);
            CREATE TABLE program_divisions (
                id INTEGER PRIMARY KEY, office_ref_code TEXT NOT NULL DEFAULT '',
                office_id INTEGER NULL, program_ref_code TEXT NOT NULL, division_id INTEGER NOT NULL);

            INSERT INTO aip_records VALUES (28, 2028), (27, 2027), (29, 2029);
            INSERT INTO divisions VALUES (1, 7, 1), (2, 7, 1), (3, 7, 0), (4, 15, 1);
            """);
    }

    public void Dispose() => _connection.Dispose();

    // ── Seeding ───────────────────────────────────────────────────────────────

    private int _nextId = 100;

    /// <summary>
    /// One activity under a fresh office → program → project chain, and the activity's id.
    /// </summary>
    private int SeedActivity(int recordId, int? configOfficeId, string programRef, int? divisionId = null)
    {
        int office = ++_nextId, program = ++_nextId, project = ++_nextId, activity = ++_nextId;

        using AppDbContext db = new(_options);
        db.Database.ExecuteSqlRaw(
            "INSERT INTO aip_offices VALUES ({0}, {1}, {2});", office, recordId, configOfficeId!);
        db.Database.ExecuteSqlRaw(
            "INSERT INTO aip_programs VALUES ({0}, {1}, {2});", program, office, programRef);
        db.Database.ExecuteSqlRaw("INSERT INTO aip_projects VALUES ({0}, {1});", project, program);
        db.Database.ExecuteSqlRaw(
            "INSERT INTO aip_activities VALUES ({0}, {1}, {2});", activity, project, divisionId!);
        return activity;
    }

    private void Assign(int? configOfficeId, string programRef, int divisionId)
    {
        using AppDbContext db = new(_options);
        db.Database.ExecuteSqlRaw(
            "INSERT INTO program_divisions (office_id, program_ref_code, division_id) VALUES ({0}, {1}, {2});",
            configOfficeId!, programRef, divisionId);
    }

    private void RunBackfill()
    {
        using AppDbContext db = new(_options);
        db.Database.ExecuteSqlRaw(AddAipDivisionSubmit.BackfillSql);
    }

    private int? DivisionOf(int activityId)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT division_id FROM aip_activities WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", activityId);
        object? value = cmd.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value);
    }

    // ── The rule ──────────────────────────────────────────────────────────────

    [Fact]
    public void Backfill_ProgramWithOneActiveDivision_TagsTheActivity()
    {
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");
        Assign(Ppdo, "P-1", Planning);

        RunBackfill();

        Assert.Equal(Planning, DivisionOf(activity));
    }

    [Fact]
    public void Backfill_LaterFiscalYear_IsTaggedToo()
    {
        int activity = SeedActivity(Fy2029, Ppdo, "P-1");
        Assign(Ppdo, "P-1", Engineering);

        RunBackfill();

        Assert.Equal(Engineering, DivisionOf(activity));
    }

    [Fact]
    public void Backfill_ProgramWithTwoDivisions_LeavesTheActivityUntagged()
    {
        // ⚠️ The case the whole column exists for. Picking one would be the "lowest division id"
        // guess AipCeilingService makes, now written into data.
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");
        Assign(Ppdo, "P-1", Planning);
        Assign(Ppdo, "P-1", Engineering);

        RunBackfill();

        Assert.Null(DivisionOf(activity));
    }

    [Fact]
    public void Backfill_Fy2027_IsNeverTouched()
    {
        // Clean fiscal-year break: FY ≤ 2027 has no division flow (spec decision 12).
        int activity = SeedActivity(Fy2027, Ppdo, "P-1");
        Assign(Ppdo, "P-1", Planning);

        RunBackfill();

        Assert.Null(DivisionOf(activity));
    }

    [Fact]
    public void Backfill_UnassignedProgram_LeavesTheActivityUntagged()
    {
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");

        RunBackfill();

        Assert.Null(DivisionOf(activity));
    }

    [Fact]
    public void Backfill_OnlyAnInactiveDivision_LeavesTheActivityUntagged()
    {
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");
        Assign(Ppdo, "P-1", Retired);

        RunBackfill();

        Assert.Null(DivisionOf(activity));
    }

    [Fact]
    public void Backfill_OneActivePlusOneInactive_TagsWithTheActiveOne()
    {
        // An inactive division is hidden from every picker, so it does not make the program
        // ambiguous.
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");
        Assign(Ppdo, "P-1", Retired);
        Assign(Ppdo, "P-1", Planning);

        RunBackfill();

        Assert.Equal(Planning, DivisionOf(activity));
    }

    [Fact]
    public void Backfill_SameDivisionListedTwice_StillCountsAsOne()
    {
        // program_divisions is unique per (office_ref_code, program_ref_code, division_id), so the
        // same division can appear twice for one config office via two ref codes.
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");
        Assign(Ppdo, "P-1", Planning);
        Assign(Ppdo, "P-1", Planning);

        RunBackfill();

        Assert.Equal(Planning, DivisionOf(activity));
    }

    [Fact]
    public void Backfill_AssignmentFromAnotherOffice_IsIgnored()
    {
        // Program ref codes repeat across offices. OPA's assignment of "P-1" says nothing about
        // PPDO's "P-1".
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");
        Assign(Opa, "P-1", OpaDivision);

        RunBackfill();

        Assert.Null(DivisionOf(activity));
    }

    [Fact]
    public void Backfill_DivisionOfAnotherOffice_IsNeverWritten()
    {
        // A malformed assignment row naming another office's division must not leak across.
        int activity = SeedActivity(Fy2028, Ppdo, "P-1");
        Assign(Ppdo, "P-1", OpaDivision);

        RunBackfill();

        Assert.Null(DivisionOf(activity));
    }

    [Fact]
    public void Backfill_AipOfficeWithNoConfigOffice_LeavesTheActivityUntagged()
    {
        int activity = SeedActivity(Fy2028, configOfficeId: null, "P-1");
        Assign(Ppdo, "P-1", Planning);

        RunBackfill();

        Assert.Null(DivisionOf(activity));
    }

    [Fact]
    public void Backfill_AlreadyTaggedActivity_IsLeftAlone()
    {
        int activity = SeedActivity(Fy2028, Ppdo, "P-1", divisionId: Engineering);
        Assign(Ppdo, "P-1", Planning);

        RunBackfill();

        Assert.Equal(Engineering, DivisionOf(activity));
    }

    [Fact]
    public void Backfill_RunTwice_GivesTheSameResult()
    {
        int tagged    = SeedActivity(Fy2028, Ppdo, "P-1");
        int ambiguous = SeedActivity(Fy2028, Ppdo, "P-2");
        Assign(Ppdo, "P-1", Planning);
        Assign(Ppdo, "P-2", Planning);
        Assign(Ppdo, "P-2", Engineering);

        RunBackfill();
        RunBackfill();

        Assert.Equal(Planning, DivisionOf(tagged));
        Assert.Null(DivisionOf(ambiguous));
    }
}
