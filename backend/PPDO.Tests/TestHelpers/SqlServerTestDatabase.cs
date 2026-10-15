using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PPDO.Infrastructure.Data;

namespace PPDO.Tests.TestHelpers;

/// <summary>
/// A throwaway SQL Server database, built from the EF model, for the few tests that need
/// SQL Server's own behaviour rather than Sqlite's (v1.9.0 — PPDO-228: the job claim's locking hints
/// and the filtered unique index).
///
/// <para>
/// ⚠️ <b>Never the dev database.</b> Each fixture creates <c>PPDOPortalTest_{guid}</c> on the local SQL
/// Server and drops it afterwards, so <c>PPDOPortalDev</c>'s real data is never touched. The server is
/// <c>.\SQLEXPRESS</c> unless <c>PPDO_TEST_SQLSERVER</c> names another (a connection string without a
/// database). Tests that use it are <see cref="SqlServerFactAttribute"/>s, skipped where there is no
/// SQL Server, as on the Linux CI runner.
/// </para>
/// </summary>
public sealed class SqlServerTestDatabase : IAsyncLifetime
{
    private const string LocalServer =
        @"Server=.\SQLEXPRESS;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=3;";

    internal static string ServerConnectionString
        => Environment.GetEnvironmentVariable("PPDO_TEST_SQLSERVER") ?? LocalServer;

    private static readonly Lazy<bool> Reachable = new(() =>
    {
        // A named instance on Linux would wait on name resolution; only probe where it can exist.
        if (!OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("PPDO_TEST_SQLSERVER") is null)
            return false;
        try
        {
            using SqlConnection connection = new(ServerConnectionString);
            connection.Open();
            return true;
        }
        catch (SqlException)
        {
            return false;
        }
    });

    /// <summary>Whether a SQL Server is there to test against.</summary>
    internal static bool IsAvailable => Reachable.Value;

    public string ConnectionString { get; } = new SqlConnectionStringBuilder(ServerConnectionString)
    {
        InitialCatalog = $"PPDOPortalTest_{Guid.NewGuid():N}",
        ConnectTimeout = 30,
    }.ConnectionString;

    /// <summary>A fresh context on the throwaway database. One per concurrent caller: a context is not thread-safe.</summary>
    public AppDbContext NewContext()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        if (!IsAvailable) return;
        // EnsureCreated (the model), not Migrate: replaying the migrations on an empty database stops at
        // the one that backfills funding_source_id from a seeded General Fund. The model is what the
        // migrations were generated from, and the real migration is applied to PPDOPortalDev separately.
        await using AppDbContext context = NewContext();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (!IsAvailable) return;
        SqlConnection.ClearAllPools();   // pooled connections would keep the database "in use"
        await using AppDbContext context = NewContext();
        await context.Database.EnsureDeletedAsync();
    }
}

/// <summary>A <see cref="FactAttribute"/> that skips itself when no SQL Server is reachable (see <see cref="SqlServerTestDatabase"/>).</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!SqlServerTestDatabase.IsAvailable)
            Skip = "No SQL Server reachable (.\\SQLEXPRESS, or PPDO_TEST_SQLSERVER).";
    }
}
