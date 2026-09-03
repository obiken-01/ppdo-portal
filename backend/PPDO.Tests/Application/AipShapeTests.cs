using System.Text.RegularExpressions;
using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// The FY partition (v1.8.0 Phase 2 — V18-37 / PPDO-40): which record shape a fiscal year is
/// allowed to have, and the refusal when a caller asks for the other one.
///
/// <para>
/// <b>P2-b was settled here rather than by forking the routes</b> (spec §5.5, 2026-09-03). The
/// spec's original default was new endpoints beside untouched old ones; it does not survive
/// contact with the code, because a new office-owned create endpoint would still have to refuse
/// FY2027 and the legacy one would still have to refuse FY2028 — so the check exists either way
/// and the split only adds surface on top of it. The objection that default was protecting
/// against is nonetheless real: a bare <c>fiscalYear &gt;= 2028</c> branch in a file nobody
/// re-reads. The answer is this type — the branch exists <b>once</b>, named, and tested directly.
/// </para>
/// </summary>
public sealed class AipShapeTests
{
    // ── The partition itself ──────────────────────────────────────────────────

    [Theory]
    [InlineData(2024)]
    [InlineData(2026)]
    [InlineData(2027)]
    public void Required_HistoricalYears_AreLegacyMultiOffice(int fiscalYear)
        => Assert.Equal(AipRecordShape.LegacyMultiOffice, AipShape.Required(fiscalYear));

    [Theory]
    [InlineData(2028)]
    [InlineData(2029)]
    [InlineData(2040)]
    public void Required_FromTheBreakOnward_IsOfficeOwned(int fiscalYear)
        => Assert.Equal(AipRecordShape.OfficeOwned, AipShape.Required(fiscalYear));

    [Fact]
    public void Required_TheBoundaryYearsThemselves_SitOnOppositeSides()
    {
        // The off-by-one worth pinning explicitly: the constant names the FIRST office-owned year,
        // so the year BELOW it is the last legacy one. Reading it as "the last legacy year" moves
        // the whole break by one and would be caught by nothing else here.
        int firstNew = AipShape.FirstOfficeOwnedFiscalYear;

        Assert.Equal(AipRecordShape.LegacyMultiOffice, AipShape.Required(firstNew - 1));
        Assert.Equal(AipRecordShape.OfficeOwned,       AipShape.Required(firstNew));
    }

    [Fact]
    public void FirstOfficeOwnedFiscalYear_IsTheDecidedBreak()
        => Assert.Equal(2028, AipShape.FirstOfficeOwnedFiscalYear);

    // ── Reading an existing record's shape ────────────────────────────────────

    [Fact]
    public void Of_AnOwnerlessRecord_IsLegacy_AndAnOwnedOneIsOfficeOwned()
    {
        Assert.Equal(AipRecordShape.LegacyMultiOffice, AipShape.Of(officeId: null));
        Assert.Equal(AipRecordShape.OfficeOwned,       AipShape.Of(officeId: 7));
    }

    // ── The mismatch reason ───────────────────────────────────────────────────

    [Fact]
    public void Mismatch_WhenTheShapeMatchesTheYear_IsNull()
    {
        Assert.Null(AipShape.Mismatch(2027, officeId: null));
        Assert.Null(AipShape.Mismatch(2028, officeId: 7));
    }

    [Fact]
    public void Mismatch_AnOfficeOwnedRecordInAHistoricalYear_IsRefused()
    {
        string? reason = AipShape.Mismatch(2027, officeId: 7);

        Assert.NotNull(reason);
        Assert.Contains("2027", reason);
    }

    [Fact]
    public void Mismatch_AnOwnerlessRecordFromTheBreakOnward_IsRefused()
    {
        // ⚠️ This is the direction that was silently wrong before this ticket. Carry-forward and
        // LDIP seeding both find-or-create a record with no owner; pointed at FY2028 they produced
        // a legacy-shape record in a year that must not have one, with no error raised anywhere.
        string? reason = AipShape.Mismatch(2028, officeId: null);

        Assert.NotNull(reason);
        Assert.Contains("2028", reason);
    }

    [Fact]
    public void Mismatch_BothReasons_NameTheFiscalYearAndTheShapeItNeeds()
    {
        // The refusal is read by a person deciding what to do instead, so it has to say which year
        // is wrong and which shape that year takes — not merely that something was rejected.
        string tooEarly = AipShape.Mismatch(2027, officeId: 7)!;
        string tooLate  = AipShape.Mismatch(2028, officeId: null)!;

        Assert.Contains("office", tooEarly, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("office", tooLate,  StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(tooEarly, tooLate);
    }

    // ── ⚠️ The decision this file mainly exists to keep ───────────────────────

    [Fact]
    public void TheBreakYear_IsHardcodedInExactlyOnePlace()
    {
        // The whole point of P2-b: moving the break — if the province ever slips FY2028 — must be
        // ONE edit, not a hunt through four create paths. This fails the build the moment someone
        // writes the literal onto a production code path, which is precisely the drift the "new
        // endpoints" alternative was trying to prevent and would not have prevented.
        //
        // Two exemptions, both deliberate:
        //   Prose — a comment or XML doc explaining the break is not a second source of truth, and
        //           forbidding the number there would make the rule undocumentable.
        //   Tests — PPDO.Tests is not scanned. A test naming a concrete year is pinning real-world
        //           behaviour, which is its job; one derived from the constant would go on passing
        //           even if the constant itself were wrong.
        List<string> offenders = [];

        foreach (string project in ProductionProjects)
        {
            string root = RepoPath(Path.Combine("backend", project));

            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                // Generated, and the model snapshot embeds unrelated numbers wholesale.
                if (file.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}"))
                    continue;
                // The policy itself is the one place allowed to name it.
                if (Path.GetFileName(file) == "AipShape.cs") continue;

                foreach (string line in File.ReadLines(file))
                {
                    if (Regex.IsMatch(StripComment(line), @"\b2028\b"))
                        offenders.Add($"{project}/{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "The FY break year is hardcoded outside AipShape:\n  " + string.Join("\n  ", offenders));
    }

    private static readonly string[] ProductionProjects =
        ["PPDO.Domain", "PPDO.Application", "PPDO.Infrastructure", "PPDO.Functions"];

    /// <summary>Everything before a <c>//</c>, and nothing at all for a comment-only line.</summary>
    private static string StripComment(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
            return string.Empty;

        int idx = line.IndexOf("//", StringComparison.Ordinal);
        return idx < 0 ? line : line[..idx];
    }

    private static string RepoPath(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, relative)))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }
}
