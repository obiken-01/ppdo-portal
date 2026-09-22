namespace PPDO.Domain.Common;

/// <summary>
/// A write was rejected because the row changed since the caller loaded it
/// (v1.8.x Phase 7 — V18-71 / PPDO-118).
///
/// <para>
/// <b>Why this type exists.</b> Same seam as
/// <see cref="UniqueConstraintViolationException"/>, for the same reason:
/// <c>PPDO.Application</c> references only <c>PPDO.Domain</c>, so it cannot see EF Core's
/// <c>DbUpdateConcurrencyException</c> and could not tell a stale-version rejection from any
/// other save failure. Infrastructure knows what a zero-row UPDATE against a <c>rowversion</c>
/// predicate means; Application knows it should become a 409 naming who changed the row.
/// </para>
///
/// <para>
/// ⚠️ <b>Unlike its sibling, this one is meant to be caught.</b>
/// <see cref="UniqueConstraintViolationException"/> deliberately stays unhandled almost
/// everywhere, because an unexpected unique-index violation is a bug. A concurrency conflict is
/// not a bug — it is two encoders doing exactly what the office asks of them, and the entire
/// point of V18-71 is that the second one is told rather than silently overwriting the first.
/// Every AIP write path that can conflict catches this.
/// </para>
///
/// <para>
/// ⚠️ <b>It carries no detail about the conflict, on purpose.</b> Resolving who changed the row
/// and what it now says needs a fresh read, and a read belongs in the service that owns the
/// unit of work — not in an exception constructor inside a failing <c>SaveChanges</c>.
/// </para>
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
