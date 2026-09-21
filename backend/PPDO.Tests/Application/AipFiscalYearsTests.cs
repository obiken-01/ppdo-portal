using System.Text.RegularExpressions;
using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// The fiscal year at which the AIP changes process (V18-38, V18-81), and the one-place rule that
/// keeps it movable.
///
/// <para>
/// ↩️ <b>Was <c>AipShapeTests</c>, trimmed 2026-09-05 (PPDO-61).</b> Eight tests went with the
/// record-shape partition they pinned — <c>Required</c>, <c>Of</c> and the four <c>Mismatch</c>
/// cases. What is left is what the break year always really governed: from FY2028 the AIP is
/// <b>entered rather than uploaded</b>, and the constant saying so lives in exactly one place per
/// side of the stack.
/// </para>
/// </summary>
public sealed class AipFiscalYearsTests
{
    // ── The break year itself ─────────────────────────────────────────────────

    [Fact]
    public void FirstEnteredFiscalYear_IsTheDecidedBreak()
        => Assert.Equal(2028, AipFiscalYears.FirstEnteredFiscalYear);

    [Fact]
    public void IsEntered_TheBoundaryYearsThemselves_SitOnOppositeSides()
    {
        // The off-by-one worth pinning explicitly: the constant names the FIRST entered year, so
        // the year below it is the last uploaded one. Reading it as "the last uploaded year" moves
        // the whole break by one and would be caught by nothing else here.
        int firstNew = AipFiscalYears.FirstEnteredFiscalYear;

        Assert.False(AipFiscalYears.IsEntered(firstNew - 1));
        Assert.True(AipFiscalYears.IsEntered(firstNew));
    }

    // ── The .xlsm freeze (V18-38 / PPDO-41) ──────────────────────────────────

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    public void RefuseUpload_AHistoricalYear_Allows(int fiscalYear)
    {
        // The importer is frozen, not retired. FY≤2027 is the only thing it is still needed for,
        // so a freeze that also stopped those years would break the one working use it has left.
        Assert.Null(AipFiscalYears.RefuseUpload(fiscalYear));
    }

    [Theory]
    [InlineData(2028)]
    [InlineData(2029)]
    [InlineData(2035)]
    public void RefuseUpload_FromTheBreakOnward_Refuses(int fiscalYear)
        => Assert.NotNull(AipFiscalYears.RefuseUpload(fiscalYear));

    [Fact]
    public void RefuseUpload_NamesTheYearAndWhatToDoInstead()
    {
        // ⚠️ The REASON changed on 2026-09-05 and the behaviour did not, which is exactly the kind
        // of thing that rots quietly. This used to refuse because the importer could only build the
        // legacy shape; FY2028 now uses that shape, so the message must no longer claim otherwise.
        // It rests on the ground it always really stood on: FY2028 is entered by the offices.
        string refusal = AipFiscalYears.RefuseUpload(2028)!;

        Assert.Contains("2028", refusal);
        Assert.Contains("entered in the portal", refusal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("shape", refusal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefuseUpload_TracksTheBreakYearRatherThanRestatingIt()
    {
        // Pins the refusal to the SAME constant everything else reads. Were the break to move, a
        // gate carrying its own copy of the year would keep refusing the wrong set — the drift
        // TheBreakYear_IsHardcodedInExactlyOnePlace exists to prevent, in the one form that test
        // cannot see: a second literal that happens to agree today.
        Assert.Null(AipFiscalYears.RefuseUpload(AipFiscalYears.FirstEnteredFiscalYear - 1));
        Assert.NotNull(AipFiscalYears.RefuseUpload(AipFiscalYears.FirstEnteredFiscalYear));
    }

    // ── ⚠️ The rule this file mainly exists to keep ───────────────────────────

    [Fact]
    public void TheBreakYear_IsHardcodedInExactlyOnePlace()
    {
        // Moving the break — if the province ever slips FY2028 — must be ONE edit per side, not a
        // hunt. This fails the build the moment someone writes the literal onto a production code
        // path.
        //
        // ↩️ It outlived the partition it was written for (PPDO-61). The shape it guarded is gone;
        // the year is not, and three shipped guards now read this constant — the upload freeze, the
        // WFP refusal, and the LDIP closed list.
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
                if (Path.GetFileName(file) == "AipFiscalYears.cs") continue;

                foreach (string line in File.ReadLines(file))
                {
                    if (Regex.IsMatch(StripComment(line), @"\b2028\b"))
                        offenders.Add($"{project}/{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        // The frontend is scanned on the same rule for the same reason: moving the break must stay
        // one edit per side. The sweep is deliberately narrow — frontend/src only, so no build
        // output and no node_modules.
        string frontendSrc = RepoPath(Path.Combine("frontend", "src"));
        foreach (string file in Directory.EnumerateFiles(frontendSrc, "*.ts*", SearchOption.AllDirectories))
        {
            // The policy itself is the one place on this side allowed to name it.
            if (Path.GetFileName(file) == "aip-fiscal-years.ts") continue;

            foreach (string line in File.ReadLines(file))
            {
                if (Regex.IsMatch(StripComment(line), @"\b2028\b"))
                    offenders.Add($"frontend/src/{Path.GetFileName(file)}: {line.Trim()}");
            }
        }

        Assert.True(offenders.Count == 0,
            "The AIP break year is hardcoded outside AipFiscalYears:\n  " + string.Join("\n  ", offenders));
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
