using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using PPDO.Domain.Entities;
using PPDO.Infrastructure.Data;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Guards the SuperAdmin seed credential (RAL-274).
///
/// The seed once shipped a BCrypt hash of a password that was also written in plaintext in
/// CLAUDE.md and in the seeding file's own comments — in a PUBLIC repository. Rotating the
/// live accounts did not fix that: the seed recreates the account on every fresh
/// `dotnet ef database update`, so any new environment was born on a published credential.
///
/// These assertions exist so the regression is caught in CI rather than by someone reading
/// the file. They are deliberately about the SHAPE of the seed, not about any specific
/// password — a future rotation should keep them passing without edits.
/// </summary>
public class SuperAdminSeedTests
{
    /// <summary>The password published in CLAUDE.md and in the seed's comments until 2026-08-27.</summary>
    private const string RetiredPlaintext = "PPDOAdmin2026!";

    private const string RetiredHash =
        "$2a$11$HaBMPo0zwTrOTJt3jqY8Ou8RNcYTfedkTJCDuP2AW5RFvofq0wQEO";

    /// <summary>
    /// Reads the seeded SuperAdmin row straight out of the model. Seed data lives only on the
    /// design-time model — the runtime model drops it — hence <see cref="IDesignTimeModel"/>.
    /// No database is touched: the connection string is never opened.
    /// </summary>
    private static IDictionary<string, object?> SeededSuperAdmin()
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True;")
            .Options;

        using AppDbContext context = new(options);
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType userType = model.FindEntityType(typeof(User))!;

        return Assert.Single(userType.GetSeedData());
    }

    private static string SeededHash() => (string)SeededSuperAdmin()["PasswordHash"]!;

    [Fact]
    public void Seed_DoesNotUseTheRetiredPublishedHash()
    {
        Assert.NotEqual(RetiredHash, SeededHash());
    }

    [Fact]
    public void Seed_HashDoesNotVerifyAgainstTheRetiredPassword()
    {
        // Stronger than comparing hashes: catches a re-hash of the same published plaintext,
        // which would produce a different string and slip past the equality check above.
        Assert.False(BCrypt.Net.BCrypt.Verify(RetiredPlaintext, SeededHash()));
    }

    [Fact]
    public void Seed_ForcesAPasswordChangeAtFirstLogin()
    {
        // Without this, a fresh environment sits on the bootstrap password indefinitely and the
        // only thing standing between it and the portal is a comment asking someone to rotate.
        Assert.True((bool)SeededSuperAdmin()["MustChangePassword"]!);
    }

    [Fact]
    public void Seed_UsesABCryptHashAtTheProjectWorkFactor()
    {
        Assert.StartsWith("$2a$11$", SeededHash());
    }
}
