using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// The one rule that turns an AIP office ref code into the config <c>offices</c> row that owns it
/// (V18-32 / PPDO-33).
///
/// <para>
/// ⚠️ <b>This is a re-link rule, not a scoping rule.</b> Since V18-32, ownership is read from
/// <see cref="AipOffice.OfficeId"/> — nothing filters by ref-code suffix any more. What is left for
/// this helper is the moment the FK has to be *established*: a new AIP office arriving from an
/// upload, and the assignment written by <c>UpsertProgramAssignmentAsync</c>. Do not reintroduce it
/// on a read path.
/// </para>
///
/// <para>
/// <b>Longest match wins.</b> Two config offices can both be suffixes of one AIP ref code — e.g.
/// <c>01-010</c> and <c>010</c> against <c>1000-000-1-01-010</c> — and taking the first would attach
/// the row to whichever office happened to sort first. The longer suffix is the more specific
/// office.
/// </para>
///
/// <para>
/// It exists as one shared function because the same rule is also written in SQL, in
/// <c>AddAipOfficeOwnershipFk</c>'s backfill and in RAL-249's <c>AddProgramDivisionOfficeFk</c>.
/// Three copies of a matching rule drift; a row matched at import and the same row matched by a
/// backfill must resolve to the same office or the FK means different things depending on how a row
/// arrived. <b>If this rule ever changes, change the two backfills with it.</b>
/// </para>
/// </summary>
public static class AipOfficeOwnership
{
    /// <summary>
    /// The id of the office whose <see cref="Office.OfficeRefCode"/> is the longest suffix of
    /// <paramref name="aipOfficeRefCode"/>, or null when nothing matches.
    ///
    /// <para>
    /// Null is a real answer, not a failure to handle: an office that was never configured has
    /// nothing to match. The caller records it — an unmatched row keeps its data and stays
    /// findable, but is invisible to every scoped read until someone resolves it.
    /// </para>
    /// </summary>
    public static int? ResolveOfficeId(string? aipOfficeRefCode, IEnumerable<Office> offices)
    {
        if (string.IsNullOrWhiteSpace(aipOfficeRefCode)) return null;

        return offices
            .Where(o => !string.IsNullOrWhiteSpace(o.OfficeRefCode)
                     && aipOfficeRefCode.EndsWith(o.OfficeRefCode!, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.OfficeRefCode!.Length)
            .Select(o => (int?)o.Id)
            .FirstOrDefault();
    }
}
