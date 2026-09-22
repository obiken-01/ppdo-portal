namespace PPDO.Domain.Common;

/// <summary>
/// An entity carrying a SQL Server <c>rowversion</c> optimistic-concurrency token
/// (V18-71 / PPDO-117).
///
/// <para>
/// Exists so <c>IRepository&lt;T&gt;.ExpectRowVersion</c> can be typed rather than taking
/// <c>object</c> and a property name. A magic string would compile for any entity and fail at
/// runtime on the ones without a token — for a guard whose whole job is to stop silent data
/// loss, "fails at runtime on the wrong entity" is the wrong trade.
/// </para>
///
/// <para>
/// ⚠️ Implementing this is not enough on its own. The property must also be mapped with
/// <c>.IsRowVersion()</c> in the entity's <c>IEntityTypeConfiguration</c>, or EF will treat it as
/// an ordinary column and never put it in the UPDATE's WHERE clause.
/// </para>
/// </summary>
public interface IRowVersioned
{
    /// <summary>
    /// The concurrency token. <b>SQL Server maintains it — never assign to it.</b>
    /// </summary>
    byte[] RowVersion { get; set; }
}
