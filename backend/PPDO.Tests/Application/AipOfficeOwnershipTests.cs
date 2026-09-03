using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="AipOfficeOwnership.ResolveOfficeId"/> (V18-32 / PPDO-33) — the rule that establishes
/// <c>aip_offices.office_id</c> when a row arrives from an upload.
///
/// <para>
/// ⚠️ <b>The same rule is written twice more, in SQL</b> — this migration's backfill and RAL-249's
/// <c>AddProgramDivisionOfficeFk</c>. A row matched at import and the same row matched by a backfill
/// must resolve to the same office, or the FK means different things depending on how the row
/// arrived. These cases are the shared definition; if one changes, all three change.
/// </para>
/// </summary>
public sealed class AipOfficeOwnershipTests
{
    private static Office Off(int id, string? refCode) => new()
    {
        Id = id, OfficeCode = $"O{id}", OfficeName = $"Office {id}", OfficeRefCode = refCode,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    [Fact]
    public void ResolveOfficeId_LongestSuffixWins_NotTheFirstMatch()
    {
        // ⚠️ Both are suffixes of the AIP ref code. "01-010" is the more specific office, and the
        // list is deliberately ordered so that taking the first match would pick the wrong one.
        List<Office> offices = [Off(1, "010"), Off(2, "01-010")];

        Assert.Equal(2, AipOfficeOwnership.ResolveOfficeId("1000-000-1-01-010", offices));
    }

    [Fact]
    public void ResolveOfficeId_MatchIsCaseInsensitive()
    {
        List<Office> offices = [Off(7, "01-01A")];

        Assert.Equal(7, AipOfficeOwnership.ResolveOfficeId("1000-000-1-01-01a", offices));
    }

    [Fact]
    public void ResolveOfficeId_NoSuffixMatch_ReturnsNull_RatherThanGuessing()
    {
        // Null is the answer for an office that was never configured. The caller records it; the
        // row keeps its data and stays findable, but no scoped read returns it.
        List<Office> offices = [Off(1, "01-010"), Off(2, "02-020")];

        Assert.Null(AipOfficeOwnership.ResolveOfficeId("1000-000-1-09-099", offices));
    }

    [Fact]
    public void ResolveOfficeId_OfficesWithNoRefCode_AreSkipped_NotTreatedAsMatchingEverything()
    {
        // An empty suffix is a suffix of every string. Without the guard, the first unconfigured
        // office would silently own every AIP row in the system.
        List<Office> offices = [Off(1, null), Off(2, ""), Off(3, "   "), Off(4, "01-010")];

        Assert.Equal(4, AipOfficeOwnership.ResolveOfficeId("1000-000-1-01-010", offices));
    }

    [Fact]
    public void ResolveOfficeId_NoConfiguredOfficesAtAll_ReturnsNull()
    {
        Assert.Null(AipOfficeOwnership.ResolveOfficeId("1000-000-1-01-010", []));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveOfficeId_BlankAipRefCode_ReturnsNull(string? aipRefCode)
    {
        Assert.Null(AipOfficeOwnership.ResolveOfficeId(aipRefCode, [Off(1, "01-010")]));
    }
}
