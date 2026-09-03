using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>The two shapes an <see cref="AipRecord"/> can have. See <see cref="AipShape"/>.</summary>
public enum AipRecordShape
{
    /// <summary>
    /// One record holds <see cref="AipOffice"/> children for every office in the province;
    /// <see cref="AipRecord.OfficeId"/> is null. How every FY≤2027 record was imported.
    /// </summary>
    LegacyMultiOffice = 0,

    /// <summary>
    /// One record per office per fiscal year, exactly like <c>LdipRecord</c>;
    /// <see cref="AipRecord.OfficeId"/> is set. The FY≥2028 shape.
    /// </summary>
    OfficeOwned = 1,
}

/// <summary>
/// The fiscal-year partition (v1.8.0 Phase 2 — V18-37 / PPDO-40): which record shape a fiscal year
/// is allowed to have. <b>FY≤2027 is legacy multi-office, FY≥2028 is office-owned, and no record
/// ever converts between them.</b>
///
/// <para>
/// The v1.8.0 approach is <b>redesign, not retrofit</b>. A multi-office record with no ownership FK
/// cannot be retrofitted into an owned one — that is structural, not a matter of effort — so the
/// break is clean: historical years keep the shape they were imported in, FY2028 onward uses the
/// new one, and there is deliberately no migration in either direction.
/// </para>
///
/// <para>
/// ⚠️ <b>Shape partitions. Units do not.</b> <c>AipActivity.Total</c> means pesos on every row in
/// every fiscal year after V18-35. Reading "clean break" as covering units would reintroduce
/// exactly the bug V18-35 exists to remove. This type partitions <i>structure only</i>.
/// </para>
///
/// <para>
/// <b>Why this type exists at all — P2-b, settled 2026-09-03 (spec §5.5).</b> The alternative on
/// the table was new endpoints beside untouched old ones, so that the new path would carry no
/// legacy branch. That does not survive contact with the code: a new office-owned create endpoint
/// would still have to refuse FY2027, and the legacy one would still have to refuse FY2028, so the
/// fiscal-year check exists either way and forking the routes only adds surface on top of it. Two
/// of the four gated paths — carry-forward and LDIP seeding — are <i>find-or-create</i> and do not
/// read as record-creation endpoints from outside, which is how one of them gets missed.
/// </para>
///
/// <para>
/// The objection that alternative was protecting against is nonetheless the right one: a bare
/// <c>fiscalYear &gt;= 2028</c> branch sitting in a file nobody re-reads. The answer is for the
/// branch to exist <b>once</b>, named and directly tested — the same move
/// <see cref="AipReadScope"/>, <see cref="OfficeScope"/>, <see cref="BudgetPlanningScope"/>,
/// <see cref="AipOfficeOwnership"/> and <see cref="ReviewerWriteGuard"/> each make, for the same
/// reason.
/// </para>
/// </summary>
public static class AipShape
{
    /// <summary>
    /// The first fiscal year on the office-owned shape. <b>The break year lives here and nowhere
    /// else</b>, so moving it — should the province ever slip FY2028 — is one edit rather than a
    /// hunt through four create paths. <c>AipShapeTests.TheBreakYear_IsHardcodedInExactlyOnePlace</c>
    /// fails the build if the literal reappears on a production code path.
    ///
    /// <para>
    /// ⚠️ This names the <b>first new</b> year, not the last legacy one. FY2027 is the last record
    /// on the old shape.
    /// </para>
    /// </summary>
    public const int FirstOfficeOwnedFiscalYear = 2028;

    /// <summary>The shape <paramref name="fiscalYear"/> must use.</summary>
    public static AipRecordShape Required(int fiscalYear)
        => fiscalYear >= FirstOfficeOwnedFiscalYear
            ? AipRecordShape.OfficeOwned
            : AipRecordShape.LegacyMultiOffice;

    /// <summary>
    /// The shape a record actually has, read from its owner.
    ///
    /// <para>
    /// ⚠️ A null owner here is <b>permanent and correct</b>, not a backfill that has yet to run —
    /// the opposite of <c>AipOffice.OfficeId</c>, whose nulls are unmatched rows to resolve. A
    /// pre-FY2028 record genuinely spans every office, so there is no value that could be filled in.
    /// </para>
    /// </summary>
    public static AipRecordShape Of(int? officeId)
        => officeId.HasValue ? AipRecordShape.OfficeOwned : AipRecordShape.LegacyMultiOffice;

    /// <inheritdoc cref="Of(int?)"/>
    public static AipRecordShape Of(AipRecord record) => Of(record.OfficeId);

    /// <summary>
    /// Why <paramref name="officeId"/>'s shape is not allowed in <paramref name="fiscalYear"/>, or
    /// null when it is. Call sites read as:
    /// <code>
    /// if (AipShape.Mismatch(dto.FiscalYear, dto.OfficeConfigId) is string reason)
    ///     return ServiceResult&lt;T&gt;.BadRequest(reason);
    /// </code>
    ///
    /// <para>
    /// The refusal is a <c>BadRequest</c>, not the <c>NotFound</c> the ownership guard uses
    /// (PPDO-46). That one hides whether a record exists; this one hides nothing — the caller is
    /// entitled to be here and is being told the operation is wrong for the year they named. So the
    /// message says which year and which shape that year takes, because the person reading it has
    /// to decide what to do instead.
    /// </para>
    /// </summary>
    public static string? Mismatch(int fiscalYear, int? officeId)
    {
        AipRecordShape required = Required(fiscalYear);
        if (Of(officeId) == required) return null;

        return required == AipRecordShape.OfficeOwned
            ? $"FY {fiscalYear} AIP records belong to a single office. Choose the office this "
              + "record is for — a record spanning every office is only valid up to FY "
              + $"{FirstOfficeOwnedFiscalYear - 1}."
            : $"FY {fiscalYear} predates the office-owned AIP and cannot be created for one "
              + $"office. Office-owned records start at FY {FirstOfficeOwnedFiscalYear}.";
    }

    /// <summary>
    /// Why an <see cref="AipOffice"/> for <paramref name="officeConfigId"/> may not be added to
    /// <paramref name="record"/>, or null when it may.
    ///
    /// <para>
    /// ⚠️ <b>The other door into a shape change, and the one no create-path gate would see.</b> Add
    /// two different offices to an office-owned record and it now spans several — the legacy shape,
    /// arrived at a node at a time without any record ever being "converted".
    /// </para>
    ///
    /// <para>
    /// This is not a scope check and does not replace one. <see cref="OfficeScope"/> already stops
    /// a guest office reaching another office's record; it says nothing about a host-office admin,
    /// who legitimately sees every office and would otherwise be free to do exactly this. A legacy
    /// record is multi-office by definition and is left alone.
    /// </para>
    /// </summary>
    public static string? RefuseForeignOffice(AipRecord record, int officeConfigId, string officeName)
    {
        if (Of(record) != AipRecordShape.OfficeOwned) return null;
        if (record.OfficeId == officeConfigId) return null;

        return $"This AIP record belongs to a single office, so '{officeName}' cannot be added to "
             + $"it. Create a separate FY {record.FiscalYear} record for that office instead.";
    }
}
