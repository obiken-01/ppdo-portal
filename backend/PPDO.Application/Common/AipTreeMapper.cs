using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// Assembles loaded AIP rows into the nested office → program → project → activity DTO tree
/// (v1.8.0 Phase 4 — PPDO-74).
///
/// <para>
/// <b>⚠️ Extracted so there is one tree shape, not two.</b> <c>AipService.GetByIdAsync</c> built
/// this inline, and PPDO-74 needs the same tree for a single office on the review screen. A second
/// copy would drift the day a column is added to <see cref="AipActivityDto"/> — and it would drift
/// silently, because both copies compile and only one of them would be wrong on the screen nobody
/// was looking at.
/// </para>
///
/// <para>
/// ⚠️ <b>This type applies no scope of any kind.</b> It maps whatever it is handed. Deciding which
/// offices and programs a caller may see is <see cref="AipReadScope"/>'s job on the entry side and
/// <see cref="OfficeScope.ResolveForReview"/>'s on the review side, and the two rules are
/// deliberately different — see <c>AipReviewService.GetOfficeForReviewAsync</c>. Adding a filter
/// here would apply one of them to both.
/// </para>
/// </summary>
public static class AipTreeMapper
{
    /// <summary>
    /// One activity, optionally carrying the fund codes of its expenditure lines (the form's
    /// Funding Source column 7).
    /// </summary>
    public static AipActivityDto MapActivity(
        AipActivity a, IReadOnlyList<string>? fundCodes = null) => new(
        a.Id, a.ProjectId, a.RefCode, a.Name, a.EsreCode, a.ImplementingOffice,
        a.StartDate, a.EndDate, a.ExpectedOutputs, a.FundingSourceId, a.FundingSourceSnapshot,
        a.Ps, a.Mooe, a.Co, a.Total, a.CcAdaptation, a.CcMitigation, a.CcTypologyCode,
        a.IsCreation, a.IsSynthetic, fundCodes ?? []);

    /// <summary>
    /// The nested tree for <paramref name="offices"/>, drawing each level from the flat lists
    /// above it.
    ///
    /// <para>
    /// ⚠️ <paramref name="fundCodes"/> is a lookup keyed by activity id, <b>grouped once by the
    /// caller</b> rather than searched per activity — the linear scan would be
    /// O(activities × lines), which is invisible on a small office and not on PPDO's own.
    /// An absent id means "no funded line", which becomes an empty list.
    /// </para>
    /// </summary>
    public static IReadOnlyList<AipOfficeDto> BuildOffices(
        IReadOnlyList<AipOffice>   offices,
        IReadOnlyList<AipProgram>  programs,
        IReadOnlyList<AipProject>  projects,
        IReadOnlyList<AipActivity> activities,
        IReadOnlyDictionary<int, IReadOnlyList<string>>? fundCodes = null)
        => offices.Select(o => new AipOfficeDto(
            o.Id, o.AipRecordId, o.RefCode, o.Name, o.Sector, o.OfficeId,
            programs
                .Where(p => p.OfficeId == o.Id)
                .Select(p => new AipProgramDto(
                    p.Id, p.OfficeId, p.RefCode, p.Name,
                    projects
                        .Where(j => j.ProgramId == p.Id)
                        .Select(j => new AipProjectDto(
                            j.Id, j.ProgramId, j.RefCode, j.Name,
                            activities
                                .Where(a => a.ProjectId == j.Id)
                                .Select(a => MapActivity(
                                    a,
                                    fundCodes is null ? null : fundCodes.GetValueOrDefault(a.Id)))
                                .ToList(),
                            j.IsSynthetic))
                        .ToList(),
                    p.FunctionBand))
                .ToList()))
            .ToList();

    /// <summary>
    /// The fund-code lookup <see cref="BuildOffices"/> expects, grouped from the flat rows
    /// <c>IAipExpenditureRepository.GetFundCodesByAipRecordAsync</c> returns.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<string>> GroupFundCodes(
        IReadOnlyList<AipActivityFundCodeDto> rows)
        => rows
            .GroupBy(r => r.ActivityId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(r => r.Code).ToList());
}
