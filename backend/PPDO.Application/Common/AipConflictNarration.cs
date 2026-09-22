using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// The two rules a concurrency conflict's wording depends on, in one place (V18-71 / PPDO-118):
/// who to name, and what to say when there is nobody to name.
///
/// <para>
/// Shared by <c>AipService</c> and <c>AipExpenditureService</c> rather than written twice. The
/// payload assembly stays in each service because its shape is type-specific, but these two rules
/// are not — and this project's recurring failure is exactly a rule that lives in several places
/// and gets updated in some of them (PPDO-92's three-place route gate, PPDO-115's reset path).
/// </para>
/// </summary>
public static class AipConflictNarration
{
    /// <summary>
    /// Resolves the display name of whoever saved last.
    ///
    /// <para>
    /// ⚠️ <b>Call this only AFTER reloading the row.</b> The write paths stamp the current caller
    /// onto the entity before saving, so reading <paramref name="updatedById"/> from an
    /// un-reloaded entity reports the person being refused as the person who made the change.
    /// </para>
    ///
    /// <para>
    /// Returns <c>sameUser: true</c> when the last writer is the caller — two tabs, or one shared
    /// login, which this project has seen (RAL-198). No name is resolved in that case because
    /// "changed by &lt;your own name&gt;" reads as a bug rather than an explanation.
    /// </para>
    /// </summary>
    public static async Task<(string? Name, bool SameUser)> ResolveEditorAsync(
        Guid? updatedById, Guid callerId, IUserRepository users, CancellationToken ct)
    {
        if (updatedById is not Guid editorId) return (null, false);
        if (editorId == callerId)             return (null, true);

        // GetNamesByIdsAsync projects the name in SQL rather than materialising the user and its
        // division for one string. This runs on a failure path, while somebody is waiting.
        IReadOnlyDictionary<Guid, string> names = await users.GetNamesByIdsAsync([editorId], ct);
        return (names.GetValueOrDefault(editorId), false);
    }

    /// <summary>
    /// The sentence the encoder reads. <paramref name="noun"/> is the thing that changed —
    /// "activity", "expenditure line" — so the message names what they were actually editing.
    /// </summary>
    public static string Message(string noun, string? changedByName, bool sameUser) => sameUser
        ? $"This {noun} was changed from another window signed in as you, while you were editing it."
        : changedByName is null
            ? $"This {noun} was changed by someone else while you were editing it."
            : $"This {noun} was changed by {changedByName} while you were editing it.";

    /// <summary>The 404 for the other outcome: the row was deleted, not edited (spec §3).</summary>
    public static string DeletedMessage(string noun)
        => $"This {noun} was deleted by someone else while you were editing it.";
}
