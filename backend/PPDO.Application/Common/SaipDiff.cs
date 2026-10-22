using System.Globalization;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// What an SAIP changes: its working copy compared with the approved AIP, row by row through
/// <c>SourceId</c> (v1.10.0 — PPDO-247, <c>docs/v1.10/SAIP_Spec.md</c> decision 17).
///
/// <para>
/// <b>One function, two uses.</b> The live "changes so far" panel calls it on every read while the
/// SAIP is open, and approval (PPDO-248) calls it once more to write <c>aip_saip_changes</c>. So what
/// the office saw just before approval is exactly what gets saved. Pure: no database, no clock.
/// </para>
///
/// <para>
/// <b>Rules.</b> A copy row with no source was added in the SAIP: one <c>Added</c> entry, no field.
/// A copy row with a source is compared field by field (<see cref="ComparedProperties"/>); each field
/// that differs is one <c>Changed</c> entry, except a <c>Total</c> that went from non-zero to zero,
/// which is <c>ReducedToZero</c> — the SAIP's way of "removing" an approved row (decision 5). An
/// approved row with no copy is <c>Removed</c>; the delete rule makes that unreachable, and it is
/// reported rather than hidden if it ever happens.
/// </para>
///
/// <para>
/// ↩️ <b>Procurement items are not diffed on their own</b>, though the spec lists "item" as a node
/// type. Editing a line's items replaces them wholesale (<c>ReplaceProcurementItemsAsync</c>), so an
/// item has no stable identity to compare by; their effect shows as the line's amount change, which
/// is what the panel and the report show. PPDO-248 applies a changed line's items as a set.
/// </para>
/// </summary>
public static class SaipDiff
{
    /// <summary>One side of the comparison: the rows of one office's tree, flat.</summary>
    public sealed record Tree(
        IReadOnlyList<AipProgram>     Programs,
        IReadOnlyList<AipProject>     Projects,
        IReadOnlyList<AipActivity>    Activities,
        IReadOnlyList<AipExpenditure> Lines);

    // ── The fields ────────────────────────────────────────────────────────────

    private sealed record Field<T>(string Name, string Property, Func<T, string?> Read);

    private static readonly Field<AipProgram>[] ProgramFields =
    [
        new("Name",         nameof(AipProgram.Name),         p => p.Name),
        new("FunctionBand", nameof(AipProgram.FunctionBand), p => p.FunctionBand),
    ];

    private static readonly Field<AipProject>[] ProjectFields =
    [
        new("Name",        nameof(AipProject.Name),        j => j.Name),
        new("Description", nameof(AipProject.Description), j => j.Description),
        new("Objective",   nameof(AipProject.Objective),   j => j.Objective),
    ];

    // Division is added per call, because it reads a name lookup.
    private static readonly Field<AipActivity>[] ActivityFields =
    [
        new("Name",               nameof(AipActivity.Name),                  a => a.Name),
        new("EsreCode",           nameof(AipActivity.EsreCode),              a => a.EsreCode),
        new("ImplementingOffice", nameof(AipActivity.ImplementingOffice),    a => a.ImplementingOffice),
        new("StartDate",          nameof(AipActivity.StartDate),             a => a.StartDate),
        new("EndDate",            nameof(AipActivity.EndDate),               a => a.EndDate),
        new("ExpectedOutputs",    nameof(AipActivity.ExpectedOutputs),       a => a.ExpectedOutputs),
        new("FundingSource",      nameof(AipActivity.FundingSourceSnapshot), a => a.FundingSourceSnapshot),
        new("CcAdaptation",       nameof(AipActivity.CcAdaptation),          a => Money(a.CcAdaptation)),
        new("CcMitigation",       nameof(AipActivity.CcMitigation),          a => Money(a.CcMitigation)),
        new("CcTypologyCode",     nameof(AipActivity.CcTypologyCode),        a => a.CcTypologyCode),
        new("IsCreation",         nameof(AipActivity.IsCreation),            a => a.IsCreation ? "Yes" : "No"),
        new("Ps",                 nameof(AipActivity.Ps),                    a => Money(a.Ps)),
        new("Mooe",               nameof(AipActivity.Mooe),                  a => Money(a.Mooe)),
        new("Co",                 nameof(AipActivity.Co),                    a => Money(a.Co)),
        new("Total",              nameof(AipActivity.Total),                 a => Money(a.Total)),
    ];

    private static readonly Field<AipExpenditure>[] LineFields =
    [
        new("Account",       nameof(AipExpenditure.AccountId),
            e => e.AccountId is null ? null : $"{e.AccountNumberSnapshot} {e.AccountTitleSnapshot}".Trim()),
        new("FundingSource", nameof(AipExpenditure.FundingSourceSnapshot), e => e.FundingSourceSnapshot),
        new("Ps",            nameof(AipExpenditure.Ps),                    e => Money(e.Ps)),
        new("Mooe",          nameof(AipExpenditure.Mooe),                  e => Money(e.Mooe)),
        new("Co",            nameof(AipExpenditure.Co),                    e => Money(e.Co)),
        new("Total",         nameof(AipExpenditure.Total),                 e => Money(e.Total)),
    ];

    /// <summary>
    /// The entity properties each node type compares, for <c>SaipDiffTests</c>' forgotten-column
    /// guard. Kept beside the field lists so the two cannot drift.
    /// </summary>
    public static readonly IReadOnlyList<(Type Type, IReadOnlySet<string> Properties)> ComparedProperties =
    [
        (typeof(AipProgram),     ProgramFields.Select(f => f.Property).ToHashSet()),
        (typeof(AipProject),     ProjectFields.Select(f => f.Property).ToHashSet()),
        (typeof(AipActivity),    ActivityFields.Select(f => f.Property).Append(nameof(AipActivity.DivisionId)).ToHashSet()),
        (typeof(AipExpenditure), LineFields.Select(f => f.Property).ToHashSet()),
    ];

    // ── Compute ───────────────────────────────────────────────────────────────

    /// <param name="divisionNames">Division id → name, so a re-tag reads "Admin → Treasury".</param>
    public static SaipDiffResult Compute(
        Tree approved, Tree copy, IReadOnlyDictionary<int, string>? divisionNames = null)
    {
        Field<AipActivity>[] activityFields =
        [
            .. ActivityFields,
            new("Division", nameof(AipActivity.DivisionId), a => a.DivisionId is int d
                ? divisionNames?.GetValueOrDefault(d) ?? d.ToString(CultureInfo.InvariantCulture)
                : null),
        ];

        List<(string Sort, int Depth, SaipChangeDto Change)> rows = [];

        Diff(rows, 0, SaipNodeType.Program, approved.Programs, copy.Programs,
            p => p.Id, p => p.SourceId, p => p.RefCode, p => p.Name, _ => null, _ => null, ProgramFields);
        Diff(rows, 1, SaipNodeType.Project, approved.Projects, copy.Projects,
            j => j.Id, j => j.SourceId, j => j.RefCode, j => j.Name, _ => null, _ => null, ProjectFields);
        Diff(rows, 2, SaipNodeType.Activity, approved.Activities, copy.Activities,
            a => a.Id, a => a.SourceId, a => a.RefCode, a => a.Name, a => a.Id, a => Money(a.Total), activityFields);

        // A line has no ref code or name of its own: it is shown under its activity, as account.
        Dictionary<int, AipActivity> approvedActs = approved.Activities.ToDictionary(a => a.Id);
        Dictionary<int, AipActivity> copyActs     = copy.Activities.ToDictionary(a => a.Id);
        Dictionary<int, int> approvedToCopyAct = copy.Activities
            .Where(a => a.SourceId is not null)
            .ToDictionary(a => a.SourceId!.Value, a => a.Id);

        Diff(rows, 3, SaipNodeType.Expenditure, approved.Lines, copy.Lines,
            e => e.Id, e => e.SourceId,
            e => RefOf(e, approvedActs, copyActs),
            e => e.AccountId is null ? "(no account)" : $"{e.AccountNumberSnapshot} {e.AccountTitleSnapshot}".Trim(),
            // The copy's activity id, whichever side the line came from, so the panel groups it.
            e => copyActs.ContainsKey(e.ActivityId) ? e.ActivityId
               : approvedToCopyAct.TryGetValue(e.ActivityId, out int copyId) ? copyId : e.ActivityId,
            e => Money(e.Total), LineFields);

        IReadOnlyList<SaipChangeDto> changes = rows
            .OrderBy(r => r.Sort, StringComparer.Ordinal)
            .ThenBy(r => r.Depth)
            .Select(r => r.Change)
            .ToList();

        return new SaipDiffResult(changes, NetByFund(approved.Lines, copy.Lines));
    }

    private static void Diff<T>(
        List<(string, int, SaipChangeDto)> rows, int depth, string nodeType,
        IReadOnlyList<T> approved, IReadOnlyList<T> copy,
        Func<T, int> id, Func<T, int?> sourceId, Func<T, string> refCode, Func<T, string> name,
        Func<T, int?> activityId, Func<T, string?> addedValue, IReadOnlyList<Field<T>> fields)
    {
        Dictionary<int, T> byId = approved.ToDictionary(id);
        HashSet<int> copied = [];

        foreach (T row in copy)
        {
            string sort = refCode(row);
            if (sourceId(row) is not int src || !byId.TryGetValue(src, out T? before))
            {
                rows.Add((sort, depth, new SaipChangeDto(nodeType, id(row), sourceId(row), activityId(row),
                    refCode(row), name(row), SaipChangeKind.Added, null, null, addedValue(row))));
                continue;
            }

            copied.Add(src);
            foreach (Field<T> field in fields)
            {
                string? was = field.Read(before);
                string? now = field.Read(row);
                if (string.Equals(was, now, StringComparison.Ordinal)) continue;

                string kind = field.Name == "Total" && IsZero(now) && !IsZero(was)
                    ? SaipChangeKind.ReducedToZero
                    : SaipChangeKind.Changed;
                rows.Add((sort, depth, new SaipChangeDto(nodeType, id(row), src, activityId(row),
                    refCode(row), name(row), kind, field.Name, was, now)));
            }
        }

        foreach (T gone in approved.Where(a => !copied.Contains(id(a))))
            rows.Add((refCode(gone), depth, new SaipChangeDto(nodeType, id(gone), id(gone), activityId(gone),
                refCode(gone), name(gone), SaipChangeKind.Removed, null, addedValue(gone), null)));
    }

    private static string RefOf(
        AipExpenditure e, Dictionary<int, AipActivity> approvedActs, Dictionary<int, AipActivity> copyActs)
        => copyActs.TryGetValue(e.ActivityId, out AipActivity? a) || approvedActs.TryGetValue(e.ActivityId, out a)
            ? a.RefCode
            : string.Empty;

    private static IReadOnlyList<SaipFundNetDto> NetByFund(
        IReadOnlyList<AipExpenditure> approved, IReadOnlyList<AipExpenditure> copy)
    {
        Dictionary<int, string?> codes = approved.Concat(copy)
            .Where(e => e.FundingSourceId is not null)
            .GroupBy(e => e.FundingSourceId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(e => e.FundingSourceSnapshot).FirstOrDefault(c => c is not null));

        // Null fund keyed as -1 inside, so it groups like the rest; it is reported as null.
        Dictionary<int, decimal> was = approved.GroupBy(e => e.FundingSourceId ?? -1).ToDictionary(g => g.Key, g => g.Sum(e => e.Total));
        Dictionary<int, decimal> now = copy.GroupBy(e => e.FundingSourceId ?? -1).ToDictionary(g => g.Key, g => g.Sum(e => e.Total));

        return was.Keys.Union(now.Keys)
            .OrderBy(k => k == -1 ? int.MaxValue : k)
            .Select(k =>
            {
                decimal a = was.GetValueOrDefault(k), b = now.GetValueOrDefault(k);
                return new SaipFundNetDto(k == -1 ? null : k, k == -1 ? null : codes.GetValueOrDefault(k), a, b, b - a);
            })
            .ToList();
    }

    /// <summary>Money as invariant text with two decimals; null stays null (an uncosted activity).</summary>
    private static string? Money(decimal? value)
        => value?.ToString("0.00", CultureInfo.InvariantCulture);

    private static bool IsZero(string? money)
        => money is not null && decimal.Parse(money, CultureInfo.InvariantCulture) == 0m;
}

/// <summary>The output of <see cref="SaipDiff.Compute"/>.</summary>
public sealed record SaipDiffResult(IReadOnlyList<SaipChangeDto> Changes, IReadOnlyList<SaipFundNetDto> NetByFund)
{
    public decimal ApprovedTotal => NetByFund.Sum(f => f.Approved);
    public decimal AmendedTotal  => NetByFund.Sum(f => f.Amended);
    public decimal NetChange     => AmendedTotal - ApprovedTotal;
}
