using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>An Annex B sheet's rows in print order, and the TOTAL beneath them.</summary>
public sealed record AipFormSheet(IReadOnlyList<AipConsolidatedRowDto> Rows, AipPrintedAmountsDto Total)
{
    public static readonly AipFormSheet Empty = new([], AipPrintedFigures.Zero);
}

/// <summary>
/// Builds one Annex B sheet's rows from an office set's tree (v1.8.0 — PPDO-73).
///
/// <para>
/// ⚠️ <b>One builder for the screen and the Excel.</b> The consolidated grid is the preview of Phase
/// 5's export (V18-60), so the rows and the printed figures are built here, once, and the export is
/// meant to write exactly these. A second derivation in the page or the export is how the preview
/// and the file would come to disagree.
/// </para>
///
/// <para>
/// Pure, like <see cref="AipTreeMapper"/>: no repository, no scope. Which offices belong on the
/// sheet is the caller's decision — the consolidated view passes only offices already with PPDO.
/// </para>
/// </summary>
public static class AipFormRowBuilder
{
    public const string OfficeRow   = "Office";
    public const string ProgramRow  = "Program";
    public const string ProjectRow  = "Project";
    public const string ActivityRow = "Activity";

    /// <summary>
    /// The sheet for <paramref name="groups"/>: each office row, then its programs, projects and
    /// activities, all ordered by reference code; the office row carries the sum of its activities'
    /// printed figures, and the TOTAL the sum of the office rows (<c>AIP_Form_Spec.md</c> §5).
    /// </summary>
    /// <param name="fundCodes">
    /// Activity id → its expenditure lines' fund codes, in entry order
    /// (<see cref="AipTreeMapper.GroupFundCodes"/>). An absent id means no funded line.
    /// </param>
    public static AipFormSheet Build(
        IReadOnlyList<AipOffice>   groups,
        IReadOnlyList<AipProgram>  programs,
        IReadOnlyList<AipProject>  projects,
        IReadOnlyList<AipActivity> activities,
        IReadOnlyDictionary<int, IReadOnlyList<string>> fundCodes,
        int fiscalYear)
    {
        // Grouped once, never searched per row — the sheet can run to ~800 rows.
        ILookup<int, AipProgram>  programsByGroup     = programs.ToLookup(p => p.OfficeId);
        ILookup<int, AipProject>  projectsByProgram   = projects.ToLookup(j => j.ProgramId);
        ILookup<int, AipActivity> activitiesByProject = activities.ToLookup(a => a.ProjectId);

        List<AipConsolidatedRowDto> rows = [];
        List<AipPrintedAmountsDto> officeTotals = [];

        foreach (AipOffice group in groups
                     .OrderBy(g => g.RefCode, StringComparer.Ordinal)
                     .ThenBy(g => g.Name, StringComparer.Ordinal))
        {
            List<AipConsolidatedRowDto> body = [];
            List<AipPrintedAmountsDto> lines = [];

            foreach (AipProgram program in programsByGroup[group.Id].OrderBy(p => p.RefCode, StringComparer.Ordinal))
            {
                // Everything under this program, built first so an empty program can be dropped.
                List<AipConsolidatedRowDto> underProgram = [];

                foreach (AipProject project in projectsByProgram[program.Id].OrderBy(j => j.RefCode, StringComparer.Ordinal))
                {
                    // ⚠️ A synthetic project exists only to hold a line the source file recorded
                    // directly on its program row (RAL-108). It has no row of its own in the
                    // province's file, so printing one would add a line that was never there.
                    if (!project.IsSynthetic)
                        underProgram.Add(Heading(ProjectRow, project.RefCode, project.Name));

                    foreach (AipActivity activity in activitiesByProject[project.Id].OrderBy(a => a.RefCode, StringComparer.Ordinal))
                    {
                        AipPrintedAmountsDto amounts = AipPrintedFigures.ForActivity(activity, fiscalYear);
                        lines.Add(amounts);
                        underProgram.Add(new AipConsolidatedRowDto(
                            ActivityRow,
                            activity.RefCode,
                            activity.Name,
                            activity.Id,
                            WorkflowStatus: null,
                            activity.EsreCode,
                            activity.ImplementingOffice,
                            activity.StartDate,
                            activity.EndDate,
                            activity.ExpectedOutputs,
                            FundingSourceOf(activity, fundCodes),
                            activity.CcTypologyCode,
                            amounts));
                    }
                }

                // ⚠️ A program with no PPAs under it — no project row and no activity — is left off
                // the sheet (Ralph, 2026-09-14). Offices are seeded with every LDIP program, so without
                // this the grid fills with program headings that carry nothing. A program whose only
                // project is synthetic and empty counts as empty too: it would print no row beneath.
                if (underProgram.Count == 0) continue;

                body.Add(Heading(ProgramRow, program.RefCode, program.Name));
                body.AddRange(underProgram);
            }

            AipPrintedAmountsDto subtotal = AipPrintedFigures.Sum(lines);
            officeTotals.Add(subtotal);

            rows.Add(new AipConsolidatedRowDto(
                OfficeRow, group.RefCode, group.Name, ActivityId: null, group.WorkflowStatus,
                null, null, null, null, null, null, null, subtotal));
            rows.AddRange(body);
        }

        return new AipFormSheet(rows, AipPrintedFigures.Sum(officeTotals));
    }

    private static AipConsolidatedRowDto Heading(string kind, string refCode, string name)
        => new(kind, refCode, name, null, null, null, null, null, null, null, null, null, Amounts: null);

    /// <summary>
    /// Column (7). The fund codes of the activity's lines joined in entry order — the same
    /// <c>GF/GAD Fund</c> shape the review tree shows — or the activity's own snapshot when no line
    /// names a fund.
    /// </summary>
    private static string? FundingSourceOf(
        AipActivity activity, IReadOnlyDictionary<int, IReadOnlyList<string>> fundCodes)
    {
        if (fundCodes.TryGetValue(activity.Id, out IReadOnlyList<string>? codes) && codes.Count > 0)
            return string.Join("/", codes.Distinct(StringComparer.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(activity.FundingSourceSnapshot) ? null : activity.FundingSourceSnapshot;
    }
}
