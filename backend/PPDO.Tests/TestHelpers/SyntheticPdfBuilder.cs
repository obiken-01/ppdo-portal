using System.Globalization;
using System.Text;

namespace PPDO.Tests.TestHelpers;

/// <summary>
/// Builds minimal, spec-valid single-page PDFs directly — no external PDF-writer NuGet package.
/// PdfPig (used by <c>PdfService</c>) can only read PDFs, not write them, and after the RAL-197
/// "UglyToad.PdfPig" package-name scare this test project deliberately avoids adding another
/// third-party dependency just to generate fixtures. This also means test fixtures never need to
/// check in a real signed PR PDF (which would carry real signatories' names).
///
/// Each <see cref="WordPlacement"/> is emitted as its own content-stream <c>BT/Tm/Tj/ET</c> block
/// at an absolute position, so the caller controls layout directly rather than relying on natural
/// text flow. A placement's <see cref="WordPlacement.Text"/> may contain spaces — PdfPig's own
/// word tokenizer splits on them exactly as it would for real generated PDF text, and
/// <c>PdfService.SplitIntoCells</c> then regroups those words back into cells by X-gap, same as
/// production. Verified against a throwaway spike (see docs/v1.7/GSO_PR_Import_Findings.md) that
/// confirmed PdfPig round-trips these placements with sane, predictable bounding boxes.
/// </summary>
public static class SyntheticPdfBuilder
{
    public sealed record WordPlacement(string Text, double X, double Y, double FontSize = 10);

    public static byte[] Build(IEnumerable<WordPlacement> words, double pageWidth = 612, double pageHeight = 792)
    {
        StringBuilder content = new();
        foreach (WordPlacement w in words)
        {
            content.Append("BT\n");
            content.Append($"/F1 {Fmt(w.FontSize)} Tf\n");
            content.Append($"1 0 0 1 {Fmt(w.X)} {Fmt(w.Y)} Tm\n");
            content.Append($"({Escape(w.Text)}) Tj\n");
            content.Append("ET\n");
        }
        byte[] contentBytes = Encoding.ASCII.GetBytes(content.ToString());

        List<string> objects = new()
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Fmt(pageWidth)} {Fmt(pageHeight)}] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        };

        using MemoryStream ms = new();
        void Write(string s) => ms.Write(Encoding.ASCII.GetBytes(s));

        Write("%PDF-1.4\n");
        List<long> offsets = new();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            Write($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        offsets.Add(ms.Position);
        Write($"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n");
        ms.Write(contentBytes);
        Write("\nendstream\nendobj\n");

        long xrefStart = ms.Position;
        int objCount = offsets.Count + 1;
        Write($"xref\n0 {objCount}\n");
        Write("0000000000 65535 f \n");
        foreach (long off in offsets)
            Write($"{off:D10} 00000 n \n");
        Write($"trailer\n<< /Size {objCount} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF");

        return ms.ToArray();
    }

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
