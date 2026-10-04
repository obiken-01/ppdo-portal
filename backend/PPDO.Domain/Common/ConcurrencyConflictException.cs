namespace PPDO.Domain.Common;

/// <summary>
/// A write was rejected because the row changed since the caller loaded it: its
/// <c>rowversion</c> no longer matches (v1.8.0 Demo 2.15 — PPDO-154, Investment_Proposal_Spec.md
/// decision 23).
///
/// <para>
/// The same seam as <see cref="UniqueConstraintViolationException"/>: Application cannot see EF
/// Core's <c>DbUpdateConcurrencyException</c>, so Infrastructure translates it into this, and the
/// service answers 409 with nothing written.
/// </para>
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
