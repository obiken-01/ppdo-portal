using System.Reflection;
using PPDO.Application.Common;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="SaipTreeCopier"/> — the SAIP draft copy's field-by-field rules (v1.10.0 — PPDO-246).
///
/// <para>
/// ⚠️ <see cref="Copy_CopiesEveryWritableProperty"/> is the guard against a column added to an AIP
/// entity later and forgotten here: an SAIP would drop it, and T3's apply would write the default
/// back over the approved value. It walks every public settable value property by reflection, so it
/// fails the day such a column appears. Red-tested by removing <c>CcTypologyCode</c> from the copy.
/// </para>
/// </summary>
public sealed class SaipTreeCopierTests
{
    private const int SaipId = 42;

    /// <summary>Properties that are deliberately NOT copied as-is, with the reason.</summary>
    private static readonly HashSet<string> NotCopied =
    [
        "Id",             // the database assigns new ids
        "SourceId",       // set to the source row's Id instead (asserted separately)
        "SaipId",         // set to the new SAIP (asserted separately)
        "WorkflowStatus", // the copy is a Draft for the office (asserted separately)
        "RowVersion",     // SQL Server maintains it
        // Parent FKs: the copy's parents are new rows, linked through navigation and resolved on save.
        "ProgramId", "ProjectId", "ActivityId", "ExpenditureId",
    ];

    private static (SaipTreeCopier.DraftCopy Copy, AipOffice Group, AipProgram Program, AipProject Project,
        AipActivity Activity, AipExpenditure Line, AipProcurementItem Item) Build()
    {
        AipOffice group = new()
        {
            Id = 10, AipRecordId = 28, RefCode = "1000-000-1-01-020", Name = "PTO", Sector = "GENERAL",
            OfficeId = 3, WorkflowStatus = AipWorkflowStatus.Consolidated,
        };
        AipProgram program = new() { Id = 20, OfficeId = 10, RefCode = "P1", Name = "Program", FunctionBand = "CORE" };
        AipProject project = new()
        {
            Id = 30, ProgramId = 20, RefCode = "J1", Name = "Project", Description = "d", Objective = "o",
            IsSynthetic = true,
        };
        AipActivity activity = new()
        {
            Id = 40, ProjectId = 30, RefCode = "A1", Name = "Activity", EsreCode = "SS", ImplementingOffice = "PTO",
            StartDate = "January", EndDate = "December", ExpectedOutputs = "outputs", FundingSourceId = 5,
            FundingSourceSnapshot = "GF", Ps = 1m, Mooe = 2m, Co = 3m, Total = 6m, CcAdaptation = 7m,
            CcMitigation = 8m, CcTypologyCode = "A111", IsCreation = true, IsSynthetic = true, DivisionId = 6,
            UpdatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), UpdatedById = Guid.NewGuid(),
            RowVersion = [1, 2, 3],
        };
        AipExpenditure line = new()
        {
            Id = 50, ActivityId = 40, AccountId = 9, AccountNumberSnapshot = "5-02", AccountTitleSnapshot = "Supplies",
            FundingSourceId = 5, FundingSourceSnapshot = "GF", FundingSourceNameSnapshot = "General Fund",
            Ps = 0m, Mooe = 1500m, Co = 0m,
            CreatedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc), UpdatedById = Guid.NewGuid(),
        };
        line.Recalculate();
        AipProcurementItem item = new()
        {
            Id = 60, ExpenditureId = 50, PeriodNo = 3, PriceIndexItemId = 11, Name = "Bond paper", Unit = "ream",
            UnitPrice = 250m, Qty = 3m, NumberOfDays = 2m,
        };
        item.Recalculate();

        SaipTreeCopier.DraftCopy copy = SaipTreeCopier.Copy(
            SaipId, [group], [program], [project], [activity], [line], [item]);
        return (copy, group, program, project, activity, line, item);
    }

    [Fact]
    public void Copy_BuildsTheSameTreeShapeWithSourceIdsBackToTheApprovedRows()
    {
        var (copy, group, program, project, activity, line, item) = Build();

        AipOffice g = Assert.Single(copy.Groups);
        AipProgram p = Assert.Single(g.Programs);
        AipProject j = Assert.Single(p.Projects);
        AipActivity a = Assert.Single(j.Activities);
        AipExpenditure e = Assert.Single(copy.Expenditures);
        AipProcurementItem i = Assert.Single(e.ProcurementItems);

        Assert.Equal(group.Id, g.SourceId);
        Assert.Equal(program.Id, p.SourceId);
        Assert.Equal(project.Id, j.SourceId);
        Assert.Equal(activity.Id, a.SourceId);
        Assert.Equal(line.Id, e.SourceId);
        Assert.Equal(item.Id, i.SourceId);

        // New rows: no ids yet, and the line hangs off the COPIED activity, never the approved one.
        Assert.All(new[] { g.Id, p.Id, j.Id, a.Id, e.Id, i.Id }, id => Assert.Equal(0, id));
        Assert.Same(a, e.Activity);
    }

    [Fact]
    public void Copy_MarksTheGroupsAsThisSaipsDraft()
    {
        AipOffice g = Assert.Single(Build().Copy.Groups);

        Assert.Equal(SaipId, g.SaipId);
        Assert.Equal(AipWorkflowStatus.Draft, g.WorkflowStatus);
        Assert.Equal(28, g.AipRecordId);   // same record: the copy is a second set of rows in it
    }

    [Fact]
    public void Copy_RecomputesTheComputedTotalsFromTheCopiedComponents()
    {
        var (copy, _, _, _, _, line, item) = Build();
        AipExpenditure e = Assert.Single(copy.Expenditures);

        Assert.Equal(line.Total, e.Total);
        Assert.Equal(item.LineTotal, Assert.Single(e.ProcurementItems).LineTotal);
        Assert.Equal(1500m, e.Total);
    }

    [Fact]
    public void Copy_OnlyCopiesRowsUnderTheGivenGroups()
    {
        AipOffice group = new() { Id = 10, AipRecordId = 28, OfficeId = 3 };
        AipProgram mine   = new() { Id = 20, OfficeId = 10, RefCode = "P1" };
        AipProgram others = new() { Id = 21, OfficeId = 99, RefCode = "P9" };

        SaipTreeCopier.DraftCopy copy = SaipTreeCopier.Copy(SaipId, [group], [mine, others], [], [], [], []);

        Assert.Equal([20], Assert.Single(copy.Groups).Programs.Select(p => p.SourceId!.Value));
    }

    [Fact]
    public void Copy_CopiesEveryWritableProperty()
    {
        var (copy, group, program, project, activity, line, item) = Build();
        AipOffice g = copy.Groups[0];
        AipProgram p = g.Programs.Single();
        AipProject j = p.Projects.Single();
        AipActivity a = j.Activities.Single();
        AipExpenditure e = copy.Expenditures[0];
        AipProcurementItem i = e.ProcurementItems.Single();

        AssertCopied(group, g, extraSkip: []);
        AssertCopied(program, p, extraSkip: ["OfficeId"]);   // on a program, OfficeId is the parent FK
        AssertCopied(project, j, extraSkip: []);
        AssertCopied(activity, a, extraSkip: []);
        AssertCopied(line, e, extraSkip: []);
        AssertCopied(item, i, extraSkip: []);
    }

    /// <summary>Every public settable scalar property is equal on source and copy, minus the documented exceptions.</summary>
    private static void AssertCopied<T>(T source, T copy, string[] extraSkip)
    {
        IEnumerable<PropertyInfo> props = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(pi => pi.CanWrite && pi.SetMethod!.IsPublic)
            .Where(pi => IsScalar(pi.PropertyType))
            .Where(pi => !NotCopied.Contains(pi.Name) && !extraSkip.Contains(pi.Name));

        foreach (PropertyInfo pi in props)
            Assert.True(Equals(pi.GetValue(source), pi.GetValue(copy)),
                $"{typeof(T).Name}.{pi.Name} was not copied (source '{pi.GetValue(source)}', copy '{pi.GetValue(copy)}'). "
                + "Add it to SaipTreeCopier, or to NotCopied here with the reason.");
    }

    private static bool IsScalar(Type t)
    {
        Type u = Nullable.GetUnderlyingType(t) ?? t;
        return u.IsPrimitive || u.IsEnum || u == typeof(string) || u == typeof(decimal)
            || u == typeof(DateTime) || u == typeof(Guid);
    }
}
