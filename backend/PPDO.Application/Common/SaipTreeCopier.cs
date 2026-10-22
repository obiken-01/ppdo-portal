using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Builds an SAIP's draft copy from an office's approved AIP tree (v1.10.0 — PPDO-246,
/// <c>docs/v1.10/SAIP_Spec.md</c> decision 4). Pure: it reads loaded rows and returns new, unsaved
/// entities, so the copy rules are tested without a database (<c>SaipTreeCopierTests</c>).
///
/// <para>
/// Every copied row gets <c>SourceId</c> = the approved row's id, so T3 can apply the changes back
/// onto the approved rows <b>keeping their ids</b> (decision 11). Ids of the new rows are left at 0
/// for the database; parent links are set through navigation, which EF resolves on save.
/// </para>
///
/// <para>
/// ⚠️ <b>Every value column is copied.</b> When a column is added to one of these entities, add it
/// here too, or an SAIP silently drops it — and on approval would write the default back over the
/// approved value. <c>SaipTreeCopierTests.Copy_CopiesEveryWritableProperty</c> fails when one is missed.
/// </para>
/// </summary>
public static class SaipTreeCopier
{
    /// <summary>The copy: group rows with programs → projects → activities attached, and the lines.</summary>
    public sealed record DraftCopy(IReadOnlyList<AipOffice> Groups, IReadOnlyList<AipExpenditure> Expenditures);

    public static DraftCopy Copy(
        int saipId,
        IReadOnlyList<AipOffice>           groups,
        IReadOnlyList<AipProgram>          programs,
        IReadOnlyList<AipProject>          projects,
        IReadOnlyList<AipActivity>         activities,
        IReadOnlyList<AipExpenditure>      expenditures,
        IReadOnlyList<AipProcurementItem>  items)
    {
        ILookup<int, AipProgram>         programsByGroup    = programs.ToLookup(p => p.OfficeId);
        ILookup<int, AipProject>         projectsByProgram  = projects.ToLookup(j => j.ProgramId);
        ILookup<int, AipActivity>        activitiesByProject = activities.ToLookup(a => a.ProjectId);
        ILookup<int, AipExpenditure>     linesByActivity    = expenditures.ToLookup(e => e.ActivityId);
        ILookup<int, AipProcurementItem> itemsByLine        = items.ToLookup(i => i.ExpenditureId);

        List<AipOffice>      copiedGroups = [];
        List<AipExpenditure> copiedLines  = [];

        foreach (AipOffice g in groups)
        {
            AipOffice group = new()
            {
                AipRecordId    = g.AipRecordId,
                RefCode        = g.RefCode,
                Name           = g.Name,
                Sector         = g.Sector,
                OfficeId       = g.OfficeId,
                // The copy is a working draft for the office, whatever the approved rows say.
                WorkflowStatus = AipWorkflowStatus.Draft,
                SaipId         = saipId,
                SourceId       = g.Id,
            };

            foreach (AipProgram p in programsByGroup[g.Id])
            {
                AipProgram program = new()
                {
                    RefCode      = p.RefCode,
                    Name         = p.Name,
                    FunctionBand = p.FunctionBand,
                    SourceId     = p.Id,
                };
                group.Programs.Add(program);

                foreach (AipProject j in projectsByProgram[p.Id])
                {
                    AipProject project = new()
                    {
                        RefCode     = j.RefCode,
                        Name        = j.Name,
                        Description = j.Description,
                        Objective   = j.Objective,
                        IsSynthetic = j.IsSynthetic,
                        SourceId    = j.Id,
                    };
                    program.Projects.Add(project);

                    foreach (AipActivity a in activitiesByProject[j.Id])
                    {
                        AipActivity activity = CopyActivity(a);
                        project.Activities.Add(activity);

                        foreach (AipExpenditure e in linesByActivity[a.Id])
                            copiedLines.Add(CopyLine(e, activity, itemsByLine[e.Id]));
                    }
                }
            }

            copiedGroups.Add(group);
        }

        return new DraftCopy(copiedGroups, copiedLines);
    }

    private static AipActivity CopyActivity(AipActivity a) => new()
    {
        RefCode               = a.RefCode,
        Name                  = a.Name,
        EsreCode              = a.EsreCode,
        ImplementingOffice    = a.ImplementingOffice,
        StartDate             = a.StartDate,
        EndDate               = a.EndDate,
        ExpectedOutputs       = a.ExpectedOutputs,
        FundingSourceId       = a.FundingSourceId,
        FundingSourceSnapshot = a.FundingSourceSnapshot,
        Ps                    = a.Ps,
        Mooe                  = a.Mooe,
        Co                    = a.Co,
        Total                 = a.Total,
        CcAdaptation          = a.CcAdaptation,
        CcMitigation          = a.CcMitigation,
        CcTypologyCode        = a.CcTypologyCode,
        IsCreation            = a.IsCreation,
        IsSynthetic           = a.IsSynthetic,
        DivisionId            = a.DivisionId,
        UpdatedAt             = a.UpdatedAt,
        UpdatedById           = a.UpdatedById,
        SourceId              = a.Id,
    };

    private static AipExpenditure CopyLine(
        AipExpenditure e, AipActivity copiedActivity, IEnumerable<AipProcurementItem> items)
    {
        AipExpenditure line = new()
        {
            Activity                  = copiedActivity,
            AccountId                 = e.AccountId,
            AccountNumberSnapshot     = e.AccountNumberSnapshot,
            AccountTitleSnapshot      = e.AccountTitleSnapshot,
            FundingSourceId           = e.FundingSourceId,
            FundingSourceSnapshot     = e.FundingSourceSnapshot,
            FundingSourceNameSnapshot = e.FundingSourceNameSnapshot,
            Ps                        = e.Ps,
            Mooe                      = e.Mooe,
            Co                        = e.Co,
            CreatedAt                 = e.CreatedAt,
            UpdatedAt                 = e.UpdatedAt,
            UpdatedById               = e.UpdatedById,
            SourceId                  = e.Id,
        };
        line.Recalculate();

        foreach (AipProcurementItem i in items)
        {
            AipProcurementItem item = new()
            {
                PeriodNo         = i.PeriodNo,
                PriceIndexItemId = i.PriceIndexItemId,
                Name             = i.Name,
                Unit             = i.Unit,
                UnitPrice        = i.UnitPrice,
                Qty              = i.Qty,
                NumberOfDays     = i.NumberOfDays,
                SourceId         = i.Id,
            };
            item.Recalculate();
            line.ProcurementItems.Add(item);
        }

        return line;
    }
}
