namespace PPDO.Application.Common;

/// <summary>
/// The signed request letter's file rules (v1.10.0 — PPDO-247, <c>docs/v1.10/SAIP_Spec.md</c>
/// decision 7): one per SAIP, PDF, JPG or PNG, up to 10 MB, replaceable until the SAIP is sent.
///
/// <para>
/// ⚠️ <b>The type is read from the file's first bytes, never from the name or the request's
/// Content-Type</b>, both of which the client chooses. A renamed .exe is not a PDF because it says so.
/// The name's extension must still agree, so the file opens in the right viewer when downloaded.
/// </para>
/// </summary>
public static class SaipLetterRules
{
    /// <summary>10 MB (decision 7).</summary>
    public const long MaxBytes = 10L * 1024 * 1024;

    /// <summary>The states in which the letter can still be attached or replaced.</summary>
    public static readonly IReadOnlyList<string> EditableStatuses =
    [
        SaipStatus.Draft, SaipStatus.DepartmentReview, SaipStatus.ReturnedByPpdo,
    ];

    public const string EmptyMessage    = "The letter file is empty.";
    public const string TooLargeMessage = "The letter is larger than 10 MB. Save it as a smaller PDF or image.";
    public const string TypeMessage     = "The letter must be a PDF, JPG or PNG file.";

    /// <summary>A recognised letter: its MIME type and the extension it is stored under.</summary>
    public sealed record Kind(string ContentType, string Extension);

    private static readonly Kind Pdf  = new("application/pdf", ".pdf");
    private static readonly Kind Png  = new("image/png", ".png");
    private static readonly Kind Jpeg = new("image/jpeg", ".jpg");

    /// <summary>
    /// Checks one upload. Returns null and the file's <paramref name="kind"/> and cleaned
    /// <paramref name="name"/> when it is acceptable, or the 400 message when it is not.
    /// </summary>
    public static string? Validate(string? fileName, byte[] content, out Kind? kind, out string name)
    {
        kind = null;
        name = string.Empty;

        if (content.Length == 0) return EmptyMessage;
        if (content.Length > MaxBytes) return TooLargeMessage;

        Kind? sniffed = Sniff(content);
        if (sniffed is null) return TypeMessage;

        // Path.GetFileName drops any folder a browser or a crafted request put in front of the name.
        string cleaned = Path.GetFileName((fileName ?? string.Empty).Trim());
        string ext = Path.GetExtension(cleaned).ToLowerInvariant();
        bool extOk = sniffed == Jpeg ? ext is ".jpg" or ".jpeg" : ext == sniffed.Extension;
        if (cleaned.Length > 0 && !extOk) return TypeMessage;

        if (cleaned.Length == 0) cleaned = "request-letter" + sniffed.Extension;
        if (cleaned.Length > 255) cleaned = cleaned[..(255 - ext.Length)] + ext;

        kind = sniffed;
        name = cleaned;
        return null;
    }

    /// <summary>The letter's type from its signature bytes, or null when it is none of the three.</summary>
    public static Kind? Sniff(ReadOnlySpan<byte> head)
    {
        if (head.StartsWith("%PDF-"u8)) return Pdf;
        if (head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Png;
        if (head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return Jpeg;
        return null;
    }
}
