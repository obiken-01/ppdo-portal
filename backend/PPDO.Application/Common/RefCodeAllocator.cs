using PPDO.Domain.Common;

namespace PPDO.Application.Common;

/// <summary>
/// Allocates a sibling-unique AIP reference code and persists the node holding it
/// (v1.8.0 Phase 3 — V18-44 / PPDO-50).
///
/// <para>
/// <b>What this fixes, precisely.</b> Generating the code was never the problem, and duplicates
/// were never the risk — <c>UX_aip_programs_office_id_ref_code</c> and its two siblings already
/// make a duplicate impossible. The problem was the gap between the two: every create path did
/// <i>load siblings → compute → insert</i> with nothing in between, so two encoders adding under
/// one parent computed the same code, the first committed, and the second was rejected by the
/// index and surfaced as an unhandled exception and a 500. Tracker D5 confirms two-or-more
/// encoders per office is the ordinary case, so that was a live path, not a hypothetical one.
/// </para>
///
/// <para>
/// <b>Why retry rather than computing the code in SQL.</b> The spec (§2 decision 11) originally
/// said the code would be computed in SQL so the database would serialise the allocation. Read
/// against the code, that buys nothing the unique index does not already provide, and it costs
/// the readable C# generator plus its direct testability. The index is already the authority on
/// what is unique; this type simply stops treating its verdict as a crash. Reasoning recorded in
/// the spec under the <c>SPEC_STANDARD.md</c> §3 deviation protocol.
/// </para>
///
/// <para>
/// ⚠️ <b>The re-read is the whole mechanism, not an optimisation.</b> Each attempt loads the
/// sibling set again. A retry that reuses the set it already has recomputes the same code every
/// time and simply burns the attempt budget — it looks like a fix, passes a careless test, and
/// changes nothing. <c>AddActivity_WhenASiblingWinsTheRace_RetriesOntoTheNextCode</c> exists to
/// fail in exactly that case.
/// </para>
///
/// <para>
/// ⚠️ <b>Offline clients cannot use any of this</b> (Phase 6 constraint). A sibling-unique code
/// requires seeing the siblings, and a disconnected client cannot. Phase 6 needs either a
/// provisional identity replaced on sync, or AIP entry that is online-only — it cannot mint real
/// ref codes locally and reconcile later.
/// </para>
/// </summary>
public static class RefCodeAllocator
{
    /// <summary>
    /// How many times a create will re-read and re-attempt before giving up.
    ///
    /// <para>
    /// Three is deliberate. The contention window is the microseconds between reading the
    /// siblings and committing the insert, so a genuine race resolves on the second attempt
    /// essentially always. A run that exhausts this is not a race — it is sustained contention or
    /// a real defect, and retrying it further would hold the request open instead of saying so.
    /// </para>
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>
    /// The next sibling-unique code under <paramref name="parentRefCode"/>: the parent's code
    /// plus a three-digit sequence one past the highest sibling.
    ///
    /// <para>
    /// ⚠️ <b>Highest + 1, not count + 1.</b> A gap in the middle — a deleted sibling — must not
    /// produce a code that already exists.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Two behaviours recorded here rather than changed, because neither belongs to this
    /// ticket.</b>
    /// </para>
    /// <list type="number">
    /// <item>
    /// An unparseable last segment is <b>skipped</b>, not counted as zero. FY≤2027 rows were
    /// imported by the pre-RAL-238 parser and are not guaranteed clean; the earlier version of
    /// this code mapped an unparseable segment to <c>0</c>, so a single malformed sibling could
    /// drag the next code back onto one that already existed — which the unique index would then
    /// reject, now as a retry loop that cannot make progress. Skipping is the smaller, safer
    /// reading of the same intent.
    /// </item>
    /// <item>
    /// Deleting the <i>last</i> sibling makes its code available again, so the next create reuses
    /// it. Whether that is right is a numbering question owned by <b>V18-42</b> (program numbering
    /// renumbers without gaps); it is not decided here.
    /// </item>
    /// </list>
    /// </summary>
    public static string NextRefCode(string parentRefCode, IEnumerable<string> siblingRefCodes)
    {
        int highest = 0;
        foreach (string code in siblingRefCodes)
        {
            if (string.IsNullOrWhiteSpace(code)) continue;

            string lastSegment = code.Split('-')[^1];
            // Skip rather than treat as 0 — see the second recorded behaviour above.
            if (!int.TryParse(lastSegment, out int n)) continue;
            if (n > highest) highest = n;
        }

        return $"{parentRefCode}-{highest + 1:D3}";
    }

    /// <summary>
    /// Allocates a code under <paramref name="parentRefCode"/> and persists the node, retrying
    /// on a unique-index rejection with a freshly-read sibling set each time.
    ///
    /// <para>
    /// Returns the persisted entity, or <c>null</c> when the attempt budget is exhausted — the
    /// caller turns that into a <c>Conflict</c> with wording appropriate to its own node type.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Only <see cref="UniqueConstraintViolationException"/> is caught.</b> A foreign-key or
    /// NOT NULL rejection is a defect, and retrying it would waste the budget and then report a
    /// concurrency problem that does not exist — sending whoever reads the log after the wrong
    /// thing. Everything else propagates untouched.
    /// </para>
    ///
    /// <para>
    /// The <c>try</c> deliberately wraps only the insert of one node whose sole unique index is
    /// the ref-code one. That is what makes catching the exception by type safe without inspecting
    /// which index fired — narrowing the scope is more robust than parsing a provider message, and
    /// it is why <see cref="UniqueConstraintViolationException.IndexName"/> is documentation
    /// rather than a branch.
    /// </para>
    /// </summary>
    /// <param name="parentRefCode">The parent node's code — segments 1–5 are office identity and are never generated.</param>
    /// <param name="loadSiblingRefCodes">Re-read on every attempt. Must hit the database, not a captured list.</param>
    /// <param name="build">Builds the entity from the allocated code. Called once per attempt.</param>
    /// <param name="persist">Adds and saves. Must throw <see cref="UniqueConstraintViolationException"/> when the index rejects it.</param>
    public static async Task<TEntity?> AllocateAsync<TEntity>(
        string parentRefCode,
        Func<CancellationToken, Task<IEnumerable<string>>> loadSiblingRefCodes,
        Func<string, TEntity> build,
        Func<TEntity, CancellationToken, Task> persist,
        CancellationToken ct = default)
        where TEntity : class
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            IEnumerable<string> siblings = await loadSiblingRefCodes(ct);
            TEntity entity = build(NextRefCode(parentRefCode, siblings));

            try
            {
                await persist(entity, ct);
                return entity;
            }
            catch (UniqueConstraintViolationException) when (attempt < MaxAttempts)
            {
                // Someone else took this code between the read and the insert. Loop: the next
                // iteration re-reads and will see their row.
            }
            catch (UniqueConstraintViolationException)
            {
                // Budget exhausted. Deliberately swallowed and reported as null so the caller can
                // return a Conflict the user can act on, rather than a 500 they cannot.
                return null;
            }
        }

        return null;
    }
}
