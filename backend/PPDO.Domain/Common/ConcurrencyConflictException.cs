namespace PPDO.Domain.Common;

/// <summary>
/// A write was rejected because the row changed since the caller loaded it: its
/// <c>rowversion</c> no longer matches. One exception for every concurrency-guarded write:
/// investment proposals (v1.8.0 Demo 2.15 — PPDO-154, Investment_Proposal_Spec.md decision 23) and
/// AIP activities and expenditures (V18-71 / PPDO-118).
///
/// <para>
/// <b>Why this type exists.</b> Same seam as <see cref="UniqueConstraintViolationException"/>, for
/// the same reason: <c>PPDO.Application</c> references only <c>PPDO.Domain</c>, so it cannot see EF
/// Core's <c>DbUpdateConcurrencyException</c> and could not tell a stale-version rejection from any
/// other save failure. Infrastructure translates it into this, and the service answers 409 with
/// nothing written.
/// </para>
///
/// <para>
/// ⚠️ <b>Unlike its sibling, this one is meant to be caught.</b>
/// <see cref="UniqueConstraintViolationException"/> deliberately stays unhandled almost everywhere,
/// because an unexpected unique-index violation is a bug. A concurrency conflict is not a bug — it is
/// two encoders doing exactly what the office asks of them, and the point of the guard is that the
/// second one is told rather than silently overwriting the first. Every write path that can conflict
/// catches this.
/// </para>
///
/// <para>
/// ⚠️ <b>It carries no detail about the conflict, on purpose.</b> Resolving who changed the row and
/// what it now says needs a fresh read, and a read belongs in the service that owns the unit of work —
/// not in an exception constructor inside a failing <c>SaveChanges</c>.
/// </para>
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
