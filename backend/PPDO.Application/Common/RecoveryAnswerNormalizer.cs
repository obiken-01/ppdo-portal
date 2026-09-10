namespace PPDO.Application.Common;

/// <summary>
/// Normalizes a recovery answer before it is hashed or compared (RAL-253).
///
/// "Manila " and "manila" must be the same answer — that is a support call, not a security
/// boundary. RAL-266 (setting the answer) and RAL-265 (verifying it) must both normalize through
/// this exact function; a divergence between the two silently locks users out of their own answer.
/// </summary>
public static class RecoveryAnswerNormalizer
{
    public static string Normalize(string answer) => answer.Trim().ToLowerInvariant();
}
