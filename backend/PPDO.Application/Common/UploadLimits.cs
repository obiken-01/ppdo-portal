using System.IO.Compression;

namespace PPDO.Application.Common;

/// <summary>
/// Size limits for uploaded files — the .xlsx/.xlsm/.pdf imports and the config CSV imports
/// (v1.8.3 — PPDO-199). Before this every import copied the whole request body into memory, so
/// the only limit was the Functions host's own (~100 MB).
///
/// Two checks, both before any parser sees the file:
/// <list type="number">
///   <item><description><b>Upload size.</b> Refused from the declared Content-Length when there is
///   one, and otherwise while reading — the body is never buffered past
///   <see cref="MaxUploadBytes"/>.</description></item>
///   <item><description><b>Workbook expansion.</b> An .xlsx is a zip. A small file can declare
///   gigabytes of XML (a zip bomb), which ClosedXML would then inflate. The zip's central
///   directory is read — cheap, nothing is decompressed — and a workbook whose parts add up to
///   more than <see cref="MaxExpandedWorkbookBytes"/>, or that has an absurd number of parts, is
///   refused. ⚠️ This trusts the sizes the zip declares. It stops the ordinary zip bomb, whose
///   declared sizes are honest; a hand-crafted zip that understates them is not caught here, and
///   whether the zip reader underneath ClosedXML stops at the declared size has not been verified
///   (PPDO-199).</description></item>
/// </list>
/// The limits sit far above any real file: the province's AIP and LDIP workbooks and the GSO PR
/// PDFs are a few MB at most.
/// </summary>
public static class UploadLimits
{
    /// <summary>Largest accepted upload: 20 MB.</summary>
    public const long MaxUploadBytes = 20L * 1024 * 1024;

    /// <summary>Largest total a workbook's parts may expand to: 200 MB.</summary>
    public const long MaxExpandedWorkbookBytes = 200L * 1024 * 1024;

    /// <summary>Most parts a workbook may hold. Real ones have tens.</summary>
    public const int MaxWorkbookEntries = 10_000;

    public const string TooLargeMessage =
        "The file is larger than 20 MB, which is more than any import here needs. Check that the right file was chosen.";

    public const string WorkbookTooLargeMessage =
        "This workbook expands to more data than any import here needs, so it was not opened. Check that the right file was chosen.";

    /// <summary>
    /// Reads an upload into a seekable buffer, refusing it as soon as it is known to be too large.
    /// <paramref name="declaredLength"/> is the request's Content-Length, when it sent one; it is
    /// checked first so an honest oversized upload is refused without reading it at all.
    /// </summary>
    public static async Task<UploadBuffer> BufferAsync(
        Stream body, long? declaredLength, CancellationToken ct = default)
    {
        if (declaredLength > MaxUploadBytes)
            return UploadBuffer.Refused(UploadRefusal.TooLarge, TooLargeMessage);

        MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, ct)) > 0)
        {
            // ⚠️ Checked per chunk, not after the copy: a body with no (or a lying) Content-Length
            // must not get the whole thing into memory before the limit is noticed.
            if (buffer.Length + read > MaxUploadBytes)
            {
                await buffer.DisposeAsync();
                return UploadBuffer.Refused(UploadRefusal.TooLarge, TooLargeMessage);
            }
            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        if (CheckWorkbook(buffer) is string workbookError)
        {
            await buffer.DisposeAsync();
            return UploadBuffer.Refused(UploadRefusal.WorkbookTooLarge, workbookError);
        }

        buffer.Position = 0;
        return UploadBuffer.Accepted(buffer);
    }

    /// <summary>
    /// Returns an error when <paramref name="file"/> is a zip (an .xlsx/.xlsm) whose parts expand
    /// past the limits, else null. Anything that is not a zip, or a zip too damaged to list, is
    /// passed through unchanged: the parser that follows gives that file its own, clearer error.
    /// Leaves <paramref name="file"/> open; its position is not preserved.
    /// </summary>
    public static string? CheckWorkbook(Stream file)
    {
        if (!LooksLikeZip(file)) return null;

        try
        {
            using ZipArchive zip = new(file, ZipArchiveMode.Read, leaveOpen: true);

            if (zip.Entries.Count > MaxWorkbookEntries) return WorkbookTooLargeMessage;

            long expanded = 0;
            foreach (ZipArchiveEntry entry in zip.Entries)
            {
                expanded += entry.Length;
                if (expanded > MaxExpandedWorkbookBytes) return WorkbookTooLargeMessage;
            }
            return null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool LooksLikeZip(Stream file)
    {
        file.Position = 0;
        Span<byte> magic = stackalloc byte[4];
        int read = file.Read(magic);
        file.Position = 0;
        return read == 4 && magic[0] == 0x50 && magic[1] == 0x4B && magic[2] == 0x03 && magic[3] == 0x04;
    }
}

/// <summary>Why an upload was refused.</summary>
public enum UploadRefusal
{
    None,
    /// <summary>Over <see cref="UploadLimits.MaxUploadBytes"/> — answer 413.</summary>
    TooLarge,
    /// <summary>A workbook that would expand past the limits — answer 413.</summary>
    WorkbookTooLarge,
}

/// <summary>
/// The outcome of <see cref="UploadLimits.BufferAsync"/>: a seekable buffer at position 0, or a
/// refusal with its message. The caller owns and disposes <see cref="Body"/>.
/// </summary>
public sealed record UploadBuffer(MemoryStream? Body, UploadRefusal Refusal, string? Error)
{
    public bool IsRefused => Refusal != UploadRefusal.None;

    public static UploadBuffer Accepted(MemoryStream body) => new(body, UploadRefusal.None, null);

    public static UploadBuffer Refused(UploadRefusal refusal, string error) => new(null, refusal, error);
}
