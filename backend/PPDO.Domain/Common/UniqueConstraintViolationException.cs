namespace PPDO.Domain.Common;

/// <summary>
/// A write was rejected by a unique index (v1.8.0 Phase 3 — V18-44 / PPDO-50).
///
/// <para>
/// <b>Why this type exists at all.</b> <c>PPDO.Application</c> references only
/// <c>PPDO.Domain</c> — it has no EF Core and no <c>Microsoft.Data.SqlClient</c>, so it cannot
/// see <c>DbUpdateException</c> or <c>SqlException</c> and could not distinguish a unique-index
/// rejection from any other failure. Infrastructure knows what SQL error 2601/2627 means;
/// Application knows what to do about it. This type is the seam between those two facts, and it
/// lives in Domain because that is the only assembly both layers already share.
/// </para>
///
/// <para>
/// ⚠️ <b>Translating an exception is not the same as handling it.</b> A caller that does not
/// catch this still fails exactly as it did before — the request surfaces an unhandled exception
/// and a 500. That is deliberate: this change gives one specific caller
/// (<c>RefCodeAllocator</c>) something it can act on, and changes nothing for every other write
/// in the system. Do not start catching it broadly to make errors look tidier; a unique-index
/// violation nobody expected is a bug, and a bug that returns a polite message is a bug that
/// nobody fixes.
/// </para>
/// </summary>
public sealed class UniqueConstraintViolationException : Exception
{
    /// <summary>
    /// The index that rejected the write, when the provider reported it — best-effort, and
    /// <c>null</c> whenever it could not be parsed out.
    ///
    /// <para>
    /// ⚠️ <b>For logging and diagnosis only. Never branch on it.</b> It is scraped from an
    /// English provider message, so it is locale-fragile and version-fragile. Code that needs to
    /// know <i>which</i> constraint was hit should narrow the scope of its <c>try</c> instead —
    /// which is what <c>RefCodeAllocator</c> does, by wrapping only the one insert whose only
    /// unique index is the ref-code one.
    /// </para>
    /// </summary>
    public string? IndexName { get; }

    public UniqueConstraintViolationException(string message, string? indexName, Exception inner)
        : base(message, inner)
    {
        IndexName = indexName;
    }
}
