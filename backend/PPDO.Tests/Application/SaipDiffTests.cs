using System.Reflection;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="SaipDiff"/> — the one function behind the live "changes so far" list and the saved
/// before → after (v1.10.0 — PPDO-247, spec decision 17).
///
/// <para>
/// Each change kind has its own test, untouched rows must produce nothing, and the per-fund net
/// totals are checked against hand sums. <see cref="EveryFieldIsComparedOrDeliberatelyIgnored"/> is
/// the guard against a column added to an AIP entity later and silently left out of the diff — an
/// SAIP that changed only that column would show "No changes yet".
/// </para>
/// </summary>
public sealed class SaipDiffTests
{
    // ── Builders ──────────────────────────────────────────────────────────────

    /// <summary>One approved tree (program 1 → project 10 → activity 100 → line 1000 on fund 5).</summary>
    private static SaipDiff.Tree Approved() => new(
        [new AipProgram { Id = 1, OfficeId = 900, RefCode = "P1", Name = "Program", FunctionBand = "CORE" }],
        [new AipProject { Id = 10, ProgramId = 1, RefCode = "P1-001", Name = "Project", Description = "d", Objective = "o" }],
        [Activity(100, 10, total: 1500m)],
        [Line(1000, 100, fund: 5, mooe: 1500m)]);

    /// <summary>A faithful copy of <see cref="Approved"/>: new ids, every row pointing at its source.</summary>
    private static SaipDiff.Tree Copy() => new(
        [new AipProgram { Id = 2, OfficeId = 901, RefCode = "P1", Name = "Program", FunctionBand = "CORE", SourceId = 1 }],
        [new AipProject { Id = 20, ProgramId = 2, RefCode = "P1-001", Name = "Project", Description = "d", Objective = "o", SourceId = 10 }],
        [Activity(200, 20, total: 1500m, sourceId: 100)],
        [Line(2000, 200, fund: 5, mooe: 1500m, sourceId: 1000)]);

    private static AipActivity Activity(int id, int projectId, decimal total, int? sourceId = null) => new()
    {
        Id = id, ProjectId = projectId, RefCode = "P1-001-001", Name = "Activity", EsreCode = "SS",
        ImplementingOffice = "PTO", StartDate = "January", EndDate = "December", ExpectedOutputs = "out",
        FundingSourceSnapshot = "GF", Ps = 0m, Mooe = total, Co = 0m, Total = total, DivisionId = 6,
        SourceId = sourceId,
    };

    private static AipExpenditure Line(
        int id, int activityId, int? fund, decimal mooe, decimal co = 0m, int? sourceId = null, int accountId = 9)
    {
        AipExpenditure line = new()
        {
            Id = id, ActivityId = activityId, AccountId = accountId, AccountNumberSnapshot = $"5-0{accountId}",
            AccountTitleSnapshot = "Supplies", FundingSourceId = fund, FundingSourceSnapshot = fund is null ? null : $"F{fund}",
            Mooe = mooe, Co = co, SourceId = sourceId,
        };
        line.Recalculate();
        return line;
    }

    private static SaipDiffResult Run(SaipDiff.Tree copy) => SaipDiff.Compute(Approved(), copy);

    // ── No change ─────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_FaithfulCopy_ReportsNoChangesAndZeroNet()
    {
        SaipDiffResult result = Run(Copy());

        Assert.Empty(result.Changes);
        SaipFundNetDto fund = Assert.Single(result.NetByFund);
        Assert.Equal(1500m, fund.Approved);
        Assert.Equal(1500m, fund.Amended);
        Assert.Equal(0m, fund.Difference);
        Assert.Equal(0m, result.NetChange);
    }

    [Fact]
    public void Compute_IgnoresBookkeepingFields_UpdatedAtRowVersionRefCodeIds()
    {
        SaipDiff.Tree copy = Copy();
        AipActivity a = copy.Activities[0];
        a.UpdatedAt   = DateTime.UtcNow;
        a.UpdatedById = Guid.NewGuid();
        a.RowVersion  = [9, 9];
        copy.Lines[0].UpdatedAt = DateTime.UtcNow;

        Assert.Empty(Run(copy).Changes);
    }

    // ── Changed ───────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_ActivityAmountRaised_ReportsChangedWithBeforeAndAfter()
    {
        SaipDiff.Tree copy = Copy();
        copy.Activities[0].Mooe  = 2500m;
        copy.Activities[0].Total = 2500m;

        SaipDiffResult result = Run(copy);

        SaipChangeDto mooe = Assert.Single(result.Changes, c => c.NodeType == SaipNodeType.Activity && c.Field == "Mooe");
        Assert.Equal(SaipChangeKind.Changed, mooe.Change);
        Assert.Equal("1500.00", mooe.Before);
        Assert.Equal("2500.00", mooe.After);
        Assert.Equal(200, mooe.NodeId);
        Assert.Equal(100, mooe.SourceId);
        Assert.Equal(200, mooe.ActivityId);
        Assert.Contains(result.Changes, c => c.NodeType == SaipNodeType.Activity && c.Field == "Total"
                                          && c.Change == SaipChangeKind.Changed);
    }

    [Fact]
    public void Compute_ActivityTextAndDivisionChanged_ReportsEachField()
    {
        SaipDiff.Tree copy = Copy();
        copy.Activities[0].Name       = "Renamed";
        copy.Activities[0].DivisionId = 7;

        SaipDiffResult result = SaipDiff.Compute(Approved(), copy,
            new Dictionary<int, string> { [6] = "Admin", [7] = "Treasury" });

        SaipChangeDto name = Assert.Single(result.Changes, c => c.Field == "Name");
        Assert.Equal(("Activity", "Renamed"), (name.Before, name.After));
        SaipChangeDto div = Assert.Single(result.Changes, c => c.Field == "Division");
        Assert.Equal(("Admin", "Treasury"), (div.Before, div.After));
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public void Compute_LineAmountChanged_ReportsTheLineAndItsActivity()
    {
        SaipDiff.Tree copy = Copy();
        AipExpenditure line = copy.Lines[0];
        line.Co = 500m;
        line.Recalculate();

        SaipDiffResult result = Run(copy);

        SaipChangeDto co = Assert.Single(result.Changes, c => c.NodeType == SaipNodeType.Expenditure && c.Field == "Co");
        Assert.Equal(("0.00", "500.00"), (co.Before, co.After));
        Assert.Equal(200, co.ActivityId);   // the PPA the line belongs to, for grouping in the panel
        Assert.Equal(2000m, Assert.Single(result.NetByFund).Amended);
        Assert.Equal(500m, result.NetChange);
    }

    [Fact]
    public void Compute_LineMovedToAnotherAccountAndFund_ReportsBothByName()
    {
        SaipDiff.Tree copy = Copy();
        AipExpenditure line = copy.Lines[0];
        line.AccountId = 11; line.AccountNumberSnapshot = "5-11"; line.AccountTitleSnapshot = "Fuel";
        line.FundingSourceId = 8; line.FundingSourceSnapshot = "F8";

        SaipDiffResult result = Run(copy);

        SaipChangeDto account = Assert.Single(result.Changes, c => c.Field == "Account");
        Assert.Equal(("5-09 Supplies", "5-11 Fuel"), (account.Before, account.After));
        SaipChangeDto fund = Assert.Single(result.Changes, c => c.Field == "FundingSource");
        Assert.Equal(("F5", "F8"), (fund.Before, fund.After));

        // The money moved between funds: fund 5 down, fund 8 up, office net unchanged.
        Assert.Equal(-1500m, result.NetByFund.Single(f => f.FundingSourceId == 5).Difference);
        Assert.Equal(1500m, result.NetByFund.Single(f => f.FundingSourceId == 8).Difference);
        Assert.Equal(0m, result.NetChange);
    }

    [Fact]
    public void Compute_ProjectAndProgramText_AreCompared()
    {
        SaipDiff.Tree copy = Copy();
        copy.Projects[0].Objective   = "new objective";
        copy.Programs[0].FunctionBand = "SUPPORT";

        SaipDiffResult result = Run(copy);

        Assert.Contains(result.Changes, c => c.NodeType == SaipNodeType.Project && c.Field == "Objective");
        Assert.Contains(result.Changes, c => c.NodeType == SaipNodeType.Program && c.Field == "FunctionBand");
        Assert.Equal(2, result.Changes.Count);
    }

    // ── Reduced to zero ───────────────────────────────────────────────────────

    [Fact]
    public void Compute_ActivityAndLineLoweredToZero_ReportsReducedToZeroOnTotal()
    {
        SaipDiff.Tree copy = Copy();
        copy.Activities[0].Mooe  = 0m;
        copy.Activities[0].Total = 0m;
        copy.Lines[0].Mooe = 0m;
        copy.Lines[0].Recalculate();

        SaipDiffResult result = Run(copy);

        SaipChangeDto activityTotal = Assert.Single(result.Changes,
            c => c.NodeType == SaipNodeType.Activity && c.Field == "Total");
        Assert.Equal(SaipChangeKind.ReducedToZero, activityTotal.Change);
        Assert.Equal(("1500.00", "0.00"), (activityTotal.Before, activityTotal.After));
        Assert.Equal(SaipChangeKind.ReducedToZero,
            Assert.Single(result.Changes, c => c.NodeType == SaipNodeType.Expenditure && c.Field == "Total").Change);
        // The component that went to zero is an ordinary change; only Total carries the kind.
        Assert.Equal(SaipChangeKind.Changed,
            Assert.Single(result.Changes, c => c.NodeType == SaipNodeType.Activity && c.Field == "Mooe").Change);
        Assert.Equal(-1500m, result.NetChange);
    }

    [Fact]
    public void Compute_RaisedFromZero_IsChangedNotReducedToZero()
    {
        SaipDiff.Tree approved = Approved();
        approved.Activities[0].Mooe = 0m; approved.Activities[0].Total = 0m;
        SaipDiff.Tree copy = Copy();
        copy.Activities[0].Mooe = 0m; copy.Activities[0].Total = 300m;

        SaipChangeDto total = Assert.Single(SaipDiff.Compute(approved, copy).Changes, c => c.Field == "Total");
        Assert.Equal(SaipChangeKind.Changed, total.Change);
    }

    // ── Added ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_ProjectActivityAndLineAddedInTheSaip_ReportOneAddedRowEach_AndCountInTheNet()
    {
        SaipDiff.Tree baseCopy = Copy();
        SaipDiff.Tree copy = new(
            baseCopy.Programs,
            [.. baseCopy.Projects, new AipProject { Id = 21, ProgramId = 2, RefCode = "P1-002", Name = "New project" }],
            [.. baseCopy.Activities, Activity(201, 21, total: 800m)],
            [.. baseCopy.Lines, Line(2001, 201, fund: 5, mooe: 800m)]);

        SaipDiffResult result = Run(copy);

        Assert.Equal(3, result.Changes.Count);
        Assert.All(result.Changes, c =>
        {
            Assert.Equal(SaipChangeKind.Added, c.Change);
            Assert.Null(c.SourceId);
            Assert.Null(c.Field);
        });
        SaipChangeDto activity = result.Changes.Single(c => c.NodeType == SaipNodeType.Activity);
        Assert.Equal("800.00", activity.After);
        Assert.Equal(201, activity.ActivityId);
        Assert.Null(result.Changes.Single(c => c.NodeType == SaipNodeType.Project).ActivityId);
        Assert.Equal(800m, result.NetChange);
        Assert.Equal(2300m, result.AmendedTotal);
    }

    [Fact]
    public void Compute_LineAddedToAnApprovedActivity_IsAddedAndTheActivityIsNot()
    {
        SaipDiff.Tree copy = Copy();
        SaipDiff.Tree withLine = copy with { Lines = [.. copy.Lines, Line(2001, 200, fund: null, mooe: 0m, co: 40m)] };

        SaipDiffResult result = Run(withLine);

        SaipChangeDto added = Assert.Single(result.Changes);
        Assert.Equal((SaipNodeType.Expenditure, SaipChangeKind.Added), (added.NodeType, added.Change));
        Assert.Equal(200, added.ActivityId);
        // A line with no fund still moves the office's money; it is netted under a null fund.
        Assert.Equal(40m, result.NetByFund.Single(f => f.FundingSourceId is null).Difference);
    }

    // ── Removed (defensive) ───────────────────────────────────────────────────

    [Fact]
    public void Compute_ApprovedRowMissingFromTheCopy_IsReportedRemoved_NotHidden()
    {
        // The delete rule makes this unreachable through the endpoints. If a row ever goes missing
        // anyway, the diff must say so rather than show "no changes" over a lost row.
        SaipDiff.Tree copy = Copy() with { Lines = [] };

        SaipChangeDto removed = Assert.Single(Run(copy).Changes);
        Assert.Equal((SaipNodeType.Expenditure, SaipChangeKind.Removed), (removed.NodeType, removed.Change));
        Assert.Equal(1000, removed.SourceId);
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_OrdersChangesByRefCodeThenNodeDepth()
    {
        SaipDiff.Tree copy = Copy();
        copy.Projects[0].Name = "Renamed project";
        copy.Activities[0].Name = "Renamed activity";

        IReadOnlyList<SaipChangeDto> changes = Run(copy).Changes;

        Assert.Equal([SaipNodeType.Project, SaipNodeType.Activity], changes.Select(c => c.NodeType).ToArray());
    }

    // ── The forgotten-column guard ────────────────────────────────────────────

    /// <summary>
    /// Every public settable value property of the four diffed entities must be either compared or
    /// listed here as deliberately ignored. A new column fails this until someone decides.
    /// Red-tested by removing "CcTypologyCode" from <c>SaipDiff</c>'s activity fields.
    /// </summary>
    [Fact]
    public void EveryFieldIsComparedOrDeliberatelyIgnored()
    {
        HashSet<string> ignored =
        [
            "Id", "SourceId", "RowVersion", "UpdatedAt", "UpdatedById", "CreatedAt",
            "OfficeId", "ProgramId", "ProjectId", "ActivityId",  // parent links: new ids in the copy
            "RefCode",               // an SAIP never renumbers (spec §7)
            "IsSynthetic",           // an import artefact, not something an office edits
            "FundingSourceId",       // compared through its snapshot ("FundingSource")
            "FundingSourceNameSnapshot",
            "AccountId", "AccountNumberSnapshot", "AccountTitleSnapshot", // compared as "Account"
        ];

        foreach ((Type type, IReadOnlySet<string> compared) in SaipDiff.ComparedProperties)
        {
            IEnumerable<string> props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanWrite || p.Name == "Total")
                .Where(p => p.PropertyType.IsValueType || p.PropertyType == typeof(string) || p.PropertyType == typeof(byte[]))
                .Select(p => p.Name);
            foreach (string prop in props)
                Assert.True(compared.Contains(prop) || ignored.Contains(prop),
                    $"{type.Name}.{prop} is neither compared by SaipDiff nor listed as ignored.");
        }
    }
}
