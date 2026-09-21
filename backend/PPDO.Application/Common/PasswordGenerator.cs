using System.Security.Cryptography;

namespace PPDO.Application.Common;

/// <summary>
/// Generates the one-time passwords issued when an account is created or an admin resets
/// it (RAL-254). Every issued password is unique and cryptographically random — the portal
/// previously handed every created and every reset account the same documented default.
///
/// The alphabet omits characters that are easily misread when a password is relayed by
/// phone or copied off a screen (O/0, I/l/1). Output always satisfies the policy enforced
/// by <c>UserService.ChangePasswordAsync</c>: at least 8 characters, one uppercase letter
/// and one digit.
/// </summary>
public static class PasswordGenerator
{
    private const string Upper  = "ABCDEFGHJKLMNPQRSTUVWXYZ";  // no I, no O
    private const string Lower  = "abcdefghijkmnopqrstuvwxyz"; // no l
    private const string Digits = "23456789";                  // no 0, no 1
    private const string All    = Upper + Lower + Digits;

    /// <summary>Length of an issued password — comfortably above the 8-character policy floor.</summary>
    public const int DefaultLength = 12;

    /// <summary>Minimum length the policy in <c>ChangePasswordAsync</c> will accept.</summary>
    private const int MinimumLength = 8;

    /// <summary>
    /// Returns a new random password. Callers must treat the result as write-once —
    /// hash it, show it to the admin, and never persist or log the plaintext.
    /// </summary>
    public static string Generate(int length = DefaultLength)
    {
        if (length < MinimumLength)
            throw new ArgumentOutOfRangeException(
                nameof(length), length,
                $"Generated passwords must be at least {MinimumLength} characters.");

        char[] chars = new char[length];

        // Seed one character of each required class so the result always satisfies the
        // policy, then fill the remainder from the full alphabet.
        chars[0] = PickFrom(Upper);
        chars[1] = PickFrom(Lower);
        chars[2] = PickFrom(Digits);

        for (int i = 3; i < length; i++)
            chars[i] = PickFrom(All);

        Shuffle(chars);
        return new string(chars);
    }

    private static char PickFrom(string alphabet) =>
        alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

    // Fisher-Yates, so the three seeded characters do not always occupy the first slots.
    private static void Shuffle(char[] chars)
    {
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}
