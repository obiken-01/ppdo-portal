using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Which funding sources a record's office may actually use (v1.8.0 — follow-up to PPDO-109).
///
/// PPDO-109 split <see cref="FundingSource"/> in two: a row with no office is province-wide and
/// every office uses it; a row with an office belongs to that office alone (D5). It scoped every
/// READ — the config list, and the WFP/AIP fund pickers, which pass the office of the record being
/// edited. It did not scope the WRITES, which is what this type closes.
///
/// <b>The hole it closes.</b> The expenditure and activity save paths took a caller-supplied
/// <c>fundingSourceId</c> (or a free-text code) and resolved it against the whole table. Nothing in
/// the UI could reach another office's fund, but a hand-crafted request could: office A could name
/// office B's private fund and have B's code and name snapshotted onto A's line. The effect was a
/// wrong label on the caller's own row rather than a read of anyone else's data, which is why it was
/// deferred rather than fixed inside PPDO-109 (`docs/v1.8/Office_Setup_Spec.md` §3.4).
///
/// <b>Why one type rather than four checks.</b> Four save paths need the same question answered and
/// they reach the owning office four different ways — an <c>AipOffice</c> here, a
/// <c>WfpExpenditureContext</c> there. Spreading the rule across them is how one of them ends up
/// spelled <c>!= caller.OfficeId</c> and silently admits the shared rows nobody meant to exclude.
///
/// ⚠️ <b>This is the WRITE-side rule only.</b> It must never be applied to the label-resolution maps
/// that turn a stored <c>FundingSourceId</c> back into a code and name — <c>AllocationService</c>'s
/// per-request cache, <c>ExternalAipReadService</c> and <c>WfpService</c>. Those must see every row
/// or an office's own fund renders as a bare id on its own reports. Nor does it belong on the four
/// reads that are deliberately SHARED-only (<c>GetGeneralFundIdAsync</c> D7, the WFP and dashboard
/// per-fund ceiling panels D11, and the AIP/LDIP upload code lookups): those want shared rows and
/// nothing else, which is a narrower question than this one.
/// </summary>
public static class FundingSourceScope
{
    /// <summary>
    /// Whether <paramref name="fund"/> may be used by a record owned by
    /// <paramref name="owningOfficeId"/>: shared funds always, an office's own fund only by that
    /// office.
    ///
    /// ⚠️ A null <paramref name="owningOfficeId"/> — a record whose office cannot be resolved — is
    /// permitted the SHARED funds and no others. Degrading to "shared only" rather than to "none"
    /// keeps a record with an incomplete office from becoming unsaveable, and degrading to
    /// "shared only" rather than "anything" is what makes a forgotten office id fail closed.
    /// </summary>
    public static bool IsVisibleTo(FundingSource fund, int? owningOfficeId)
        => fund.OfficeId is null
        || (owningOfficeId is int officeId && fund.OfficeId == officeId);

    /// <summary>
    /// The fund with id <paramref name="fundId"/> if it exists <b>and</b> this office may use it,
    /// otherwise null.
    ///
    /// ⚠️ Missing and forbidden deliberately answer the same way, so a caller cannot learn which
    /// fund ids exist outside their office by watching the two apart — the same reasoning that makes
    /// <c>ConfigFundingSourceFunctions.DenyForeignFundAsync</c> answer 403 rather than 404.
    /// </summary>
    public static FundingSource? FindVisibleById(
        IEnumerable<FundingSource> all, int fundId, int? owningOfficeId)
        => all.FirstOrDefault(f => f.Id == fundId && IsVisibleTo(f, owningOfficeId));

    /// <summary>
    /// The fund whose <see cref="FundingSource.Code"/> matches <paramref name="code"/> (ignoring
    /// case) and which this office may use, otherwise null. For the free-text import/entry paths
    /// where the code came from a workbook cell or a raw string rather than a picker.
    /// </summary>
    public static FundingSource? FindVisibleByCode(
        IEnumerable<FundingSource> all, string? code, int? owningOfficeId)
        => string.IsNullOrWhiteSpace(code)
            ? null
            : all.FirstOrDefault(f =>
                  f.Code.Equals(code.Trim(), StringComparison.OrdinalIgnoreCase)
                  && IsVisibleTo(f, owningOfficeId));

    /// <summary>
    /// The refusal a save path returns for a fund it may not use.
    ///
    /// ⚠️ Worded as "not found" on purpose, and identical to the message for an id that does not
    /// exist at all. "Belongs to another office" would confirm the row exists and name what it is —
    /// turning a rejected write into a way to enumerate other offices' funds one id at a time.
    /// </summary>
    public static string NotFoundMessage(int fundId) => $"Funding source {fundId} not found.";
}
