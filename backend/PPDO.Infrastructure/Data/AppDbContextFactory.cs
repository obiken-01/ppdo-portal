using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PPDO.Infrastructure.Data;

/// <summary>
/// Design-time factory used exclusively by EF Core CLI tooling (dotnet ef migrations / database update).
/// This class is never instantiated at runtime.
///
/// Connection string priority:
///   1. SqlConnectionString environment variable — set this in CI/CD to target a different database.
///   2. Fallback — local SQL Server Express (.\SQLEXPRESS) with Windows Authentication.
///
/// Usage (from solution root):
///   dotnet ef migrations add MigrationName --project backend/PPDO.Infrastructure --startup-project backend/PPDO.Functions
///   dotnet ef database update             --project backend/PPDO.Infrastructure --startup-project backend/PPDO.Functions
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        string connectionString =
            Environment.GetEnvironmentVariable("SqlConnectionString")
            ?? @"Server=.\SQLEXPRESS;Database=PPDOPortalDev;Trusted_Connection=True;TrustServerCertificate=True;";

        DbContextOptionsBuilder<AppDbContext> optionsBuilder = new();
        optionsBuilder.UseSqlServer(connectionString);

        return new AppDbContext(optionsBuilder.Options);
    }
}
