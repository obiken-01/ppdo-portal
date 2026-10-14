namespace PPDO.Application.Common;

/// <summary>
/// The refusal for an AIP write that arrives without a <c>rowVersion</c> (V18-71, spec §8 — PPDO-121).
///
/// <para>
/// PPDO-119 let a missing version through with a warning so clients could be updated first; every
/// client sends one now, so a missing version is a 400. ⚠️ <b>There is deliberately no way back to
/// warn-and-save:</b> no flag, no config switch. A switch left reachable during an incident is a switch
/// that quietly stays on, and the guard is only protection while it cannot be skipped by omission.
/// </para>
///
/// <para>
/// ⚠️ <b>Call it AFTER the permission, scope and division guards, and BEFORE any mutation.</b> After the
/// guards so a caller who may not touch the row gets the same 403/404 whether or not they sent a
/// version (a 400 would confirm the row exists); before mutation so a refused write leaves no tracked
/// entity dirty and no partial write behind.
/// </para>
/// </summary>
public static class AipRowVersionRequired
{
    /// <summary>The message the client shows — keep "rowVersion" in it, the handlers' base64 error says the same.</summary>
    public const string Message = "rowVersion is required. Reload the page and try again.";

    /// <summary>Returns the 400 when <paramref name="expectedRowVersion"/> is null; otherwise null (carry on).</summary>
    public static ServiceResult<T>? Check<T>(byte[]? expectedRowVersion)
        => expectedRowVersion is null ? ServiceResult<T>.BadRequest(Message) : null;
}
