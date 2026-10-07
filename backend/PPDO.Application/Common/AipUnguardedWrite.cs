using Microsoft.Extensions.Logging;

namespace PPDO.Application.Common;

/// <summary>
/// The warning logged when an AIP write arrives with no <c>rowVersion</c> and is saved unguarded
/// (V18-71, spec §8). Added in PPDO-193.
///
/// <para>
/// ⚠️ <b>This warning is PPDO-121's entry condition.</b> PPDO-121 turns a missing version into a
/// 400, and may only ship once this warning has stopped appearing in UAT/production Application
/// Insights after every client sends a version. Until PPDO-193 nothing was logged at all, so that
/// count would have read zero from day one and proved nothing.
/// </para>
///
/// <para>
/// One helper rather than four inline calls, so the text App Insights is searched for exists in
/// exactly one place: <c>traces | where message startswith "AIP write without rowVersion"</c>.
/// Do not reword <see cref="MessagePrefix"/> without updating PPDO-121.
/// </para>
/// </summary>
public static class AipUnguardedWrite
{
    /// <summary>The searchable start of the message. Do not reword (see the class remarks).</summary>
    public const string MessagePrefix = "AIP write without rowVersion";

    /// <summary>Logs the warning when <paramref name="expectedRowVersion"/> is null; otherwise does nothing.</summary>
    public static void WarnIfMissing(
        ILogger logger, byte[]? expectedRowVersion, string entity, int entityId, string operation, Guid userId)
    {
        if (expectedRowVersion is not null) return;

        logger.LogWarning(
            MessagePrefix + ", saved unguarded. Entity: {Entity}, EntityId: {EntityId}, Operation: {Operation}, UserId: {UserId}",
            entity, entityId, operation, userId);
    }
}
