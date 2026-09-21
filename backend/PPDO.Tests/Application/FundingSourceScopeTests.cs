using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="FundingSourceScope"/> — the WRITE-side rule for which funds a record's
/// office may name (v1.8.0, follow-up to PPDO-109).
///
/// The four save paths that use it are tested in their own service suites; this file pins the rule
/// itself, because that is where a "simplification" would land.
/// </summary>
public sealed class FundingSourceScopeTests
{
    private const int GsoOffice = 7;
    private const int PhoOffice = 9;

    private static FundingSource Fs(int id, string code, int? officeId = null) => new()
    {
        Id = id, Code = code, Name = $"Fund {code}", IsActive = true, OfficeId = officeId,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static List<FundingSource> Seed() =>
    [
        Fs(1, "GF"),
        Fs(2, "GAD"),
        Fs(3, "GSOX", GsoOffice),
        Fs(4, "PHOX", PhoOffice),
    ];

    // ── IsVisibleTo ───────────────────────────────────────────────────────────

    [Fact]
    public void IsVisibleTo_SharedFund_IsVisibleToEveryOffice()
    {
        Assert.True(FundingSourceScope.IsVisibleTo(Fs(1, "GF"), GsoOffice));
        Assert.True(FundingSourceScope.IsVisibleTo(Fs(1, "GF"), PhoOffice));
    }

    [Fact]
    public void IsVisibleTo_OwnOfficeFund_IsVisible()
        => Assert.True(FundingSourceScope.IsVisibleTo(Fs(3, "GSOX", GsoOffice), GsoOffice));

    [Fact]
    public void IsVisibleTo_AnotherOfficesFund_IsNotVisible()
        => Assert.False(FundingSourceScope.IsVisibleTo(Fs(4, "PHOX", PhoOffice), GsoOffice));

    [Fact]
    public void IsVisibleTo_WithNoOwningOffice_SeesSharedFundsOnly()
    {
        // ⚠️ The fail-closed case. A record whose office cannot be resolved keeps the province-wide
        // funds — so it stays saveable — and gets no office's private funds, so a forgotten office
        // id degrades to LESS access rather than to all of it. The same shape as OfficeScope's
        // "forgetting the Include degrades to more restrictive, never to full access".
        Assert.True(FundingSourceScope.IsVisibleTo(Fs(1, "GF"), owningOfficeId: null));
        Assert.False(FundingSourceScope.IsVisibleTo(Fs(3, "GSOX", GsoOffice), owningOfficeId: null));
    }

    // ── FindVisibleById ───────────────────────────────────────────────────────

    [Fact]
    public void FindVisibleById_SharedFund_Resolves()
        => Assert.Equal("GF", FundingSourceScope.FindVisibleById(Seed(), 1, GsoOffice)?.Code);

    [Fact]
    public void FindVisibleById_OwnOfficeFund_Resolves()
        => Assert.Equal("GSOX", FundingSourceScope.FindVisibleById(Seed(), 3, GsoOffice)?.Code);

    [Fact]
    public void FindVisibleById_AnotherOfficesFund_ResolvesNull()
    {
        // The hole this whole change closes: id 4 exists, and GSO must not be able to use it.
        Assert.NotNull(Seed().SingleOrDefault(f => f.Id == 4));
        Assert.Null(FundingSourceScope.FindVisibleById(Seed(), 4, GsoOffice));
    }

    [Fact]
    public void FindVisibleById_MissingAndForbidden_AreIndistinguishable()
    {
        // ⚠️ Both answer null, and NotFoundMessage words both the same way. Telling them apart would
        // let a caller enumerate other offices' fund ids one rejected save at a time.
        Assert.Null(FundingSourceScope.FindVisibleById(Seed(), 4,    GsoOffice));  // exists, foreign
        Assert.Null(FundingSourceScope.FindVisibleById(Seed(), 4242, GsoOffice));  // does not exist

        // The wording must not give the distinction away either: nothing about ownership, and the
        // only thing that varies between the two messages is the id the caller already sent.
        Assert.Equal(
            FundingSourceScope.NotFoundMessage(4).Replace("4", "<id>"),
            FundingSourceScope.NotFoundMessage(4242).Replace("4242", "<id>"));
        Assert.DoesNotContain("office", FundingSourceScope.NotFoundMessage(4), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindVisibleById_InactiveFund_StillResolves()
    {
        // ⚠️ Scope is not the same question as IsActive, and this type answers only the first. A
        // deactivated fund stays resolvable so an EXISTING line that names it can still be edited
        // and re-saved; hiding it from the pickers is what IsActive is for, and that filtering lives
        // in the read paths. Folding the two together here would make every line on a retired fund
        // unsaveable the moment PPDO retired it.
        List<FundingSource> seed = [Fs(5, "OLD") ];
        seed[0].IsActive = false;

        Assert.NotNull(FundingSourceScope.FindVisibleById(seed, 5, GsoOffice));
    }

    // ── FindVisibleByCode ─────────────────────────────────────────────────────

    [Fact]
    public void FindVisibleByCode_MatchesIgnoringCaseAndSurroundingSpace()
    {
        Assert.Equal(1, FundingSourceScope.FindVisibleByCode(Seed(), "gf",    GsoOffice)?.Id);
        Assert.Equal(1, FundingSourceScope.FindVisibleByCode(Seed(), "  GF ", GsoOffice)?.Id);
    }

    [Fact]
    public void FindVisibleByCode_AnotherOfficesCode_ResolvesNull()
    {
        // Codes are globally unique (D6), so without the scope check this would have resolved to
        // PHO's private fund from a GSO workbook cell.
        Assert.Null(FundingSourceScope.FindVisibleByCode(Seed(), "PHOX", GsoOffice));
        Assert.Equal("GSOX", FundingSourceScope.FindVisibleByCode(Seed(), "GSOX", GsoOffice)?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindVisibleByCode_BlankCode_ResolvesNull(string? code)
        => Assert.Null(FundingSourceScope.FindVisibleByCode(Seed(), code, GsoOffice));

    [Fact]
    public void FindVisibleByCode_UnknownCode_ResolvesNull()
        => Assert.Null(FundingSourceScope.FindVisibleByCode(Seed(), "NOPE", GsoOffice));
}
