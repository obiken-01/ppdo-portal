using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using PPDO.Domain.Interfaces;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Parses the signed PDF export of a GSO-system Purchase Request (RAL-197) using PdfPig.
///
/// PdfPig gives words with precise bounding boxes but no table structure — there is no "cell
/// B1" the way ClosedXML gives ExcelService. This class reconstructs the form's tables purely
/// from word positions:
///   1. Group words into rows by Y-position (<see cref="RowTolerance"/>).
///   2. Within each row, split into cells wherever the horizontal gap between adjacent words
///      exceeds <see cref="CellGapThreshold"/> — verified against the real sample file: normal
///      word-to-word spacing within one label/value/description is 1-3pt; genuine cell
///      boundaries are 30pt+.
///   3. Merge a row into the previous one when the vertical gap between them is small
///      (<see cref="WrapMergeGap"/>) — this is PDF line-wrapping (e.g. a hierarchy line or a
///      signatory's position title wrapping across two physical PDF lines), not a new row. Each
///      wrapped cell is appended to whichever previous-row cell it sits nearest to on the X
///      axis, not just appended in raw reading order — the wrapped text's own line starts back
///      near the left margin of its cell, not after the previous line's rightmost word.
///
/// See docs/v1.7/GSO_PR_Import_Findings.md for the verified word-position data this design is
/// based on, and <see cref="IExcelService.ParseGsoPRImport"/> for the xlsx sibling parser this
/// mirrors (same hierarchy-line / account-code / item-row rules once cells are reconstructed).
/// </summary>
public sealed class PdfService : IPdfService
{
    // Tuned against the real sample file's word positions (see docs/v1.7/GSO_PR_Import_Findings.md):
    // normal same-row inter-word gaps are 1-3pt, genuine row gaps (distinct fields/hierarchy
    // lines/items) are 15pt+, and line-wrap continuation gaps are 7-10pt — comfortably separated
    // by these thresholds.
    private const double RowTolerance     = 2.0;
    private const double CellGapThreshold = 10.0;
    private const double WrapMergeGap     = 12.0;

    private sealed record PdfWord(string Text, double Left, double Right, double Bottom);
    private sealed record PdfCell(string Text, double Left);

    /// <inheritdoc />
    public GsoPRImportRow ParseGsoPRImport(Stream stream)
    {
        using PdfDocument doc = PdfDocument.Open(stream);
        if (doc.NumberOfPages == 0)
            throw new ImportParseException(new[] { "The PDF has no pages." });

        Page page = doc.GetPage(1);
        List<PdfWord> words = page.GetWords()
            .Select(w => new PdfWord(w.Text, w.BoundingBox.Left, w.BoundingBox.Right, w.BoundingBox.Bottom))
            .ToList();

        if (!words.Any(w => w.Text.Contains("PURCHASE", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ImportParseException(new[]
            {
                "This file doesn't look like a GSO PR export — expected a 'PURCHASE REQUEST' form.",
            });
        }

        List<List<PdfCell>> rows = BuildRows(words);

        int itemHeaderIdx = rows.FindIndex(r => RowText(r).Contains("Item No.", StringComparison.OrdinalIgnoreCase));
        if (itemHeaderIdx < 0)
        {
            throw new ImportParseException(new[]
            {
                "Could not find the item table — expected an 'Item No.' column header.",
            });
        }

        // ── Header block — everything above the item table ─────────────────────────────
        string? prNo = null, fund = null, dateRaw = null;
        foreach (PdfCell cell in rows.Take(itemHeaderIdx).SelectMany(r => r))
        {
            if (TryTakeValue(cell.Text, "PR No.:", out string v)) prNo = v;
            else if (TryTakeValue(cell.Text, "Fund:", out v))     fund = v;
            else if (TryTakeValue(cell.Text, "Date.:", out v))    dateRaw = v;
        }
        DateOnly? prDate = dateRaw is not null && DateTime.TryParse(dateRaw, out DateTime parsedDate)
            ? DateOnly.FromDateTime(parsedDate)
            : null;

        // ── Item table body — rows after the header, until SUBTOTAL/TOTAL ──────────────
        List<List<PdfCell>> bodyRows = rows.Skip(itemHeaderIdx + 1)
            .TakeWhile(r => !RowText(r).StartsWith("SUBTOTAL", StringComparison.OrdinalIgnoreCase)
                         && !RowText(r).Equals("TOTAL", StringComparison.OrdinalIgnoreCase))
            .ToList();

        List<string> hierarchyLines = new();
        List<PRItemImportRow> items = new();

        foreach (List<PdfCell> row in bodyRows)
        {
            if (row.Count == 0) continue;

            if (row.Count >= 5 && int.TryParse(row[0].Text.Trim(), out int itemNo) && itemNo > 0)
            {
                string stockNo = row[1].Text.Trim();
                string desc    = row[2].Text.Trim();
                string unit    = row[3].Text.Trim();
                string qtyRaw  = row[4].Text.Trim();
                string costRaw = row.Count > 5 ? row[5].Text.Trim() : "";

                if (!decimal.TryParse(qtyRaw, out decimal qty) || qty <= 0)
                    continue; // read-only preview — skip a malformed row rather than fail the file

                decimal.TryParse(costRaw, out decimal unitCost);

                items.Add(new PRItemImportRow
                {
                    StockNo        = string.IsNullOrWhiteSpace(stockNo) ? null : stockNo,
                    Description    = string.IsNullOrWhiteSpace(desc) ? stockNo : desc,
                    Unit           = string.IsNullOrWhiteSpace(unit) ? "pcs" : unit,
                    Quantity       = qty,
                    UnitCost       = unitCost,
                    IsUnknownStock = !string.IsNullOrWhiteSpace(stockNo),
                });
            }
            else
            {
                string line = RowText(row).Trim();
                if (line.Length > 0)
                    hierarchyLines.Add(line);
            }
        }

        if (items.Count == 0)
            throw new ImportParseException(new[] { "No valid item rows (Qty > 0) were found." });

        List<string> codedLines = hierarchyLines.Where(l => l.Contains(" - ")).ToList();
        string? program  = codedLines.Count >= 2 ? codedLines[0] : null;
        string? activity = codedLines.Count >= 1 ? codedLines[^1] : null;
        string? project  = codedLines.Count >= 3
            ? string.Join("; ", codedLines.Skip(1).Take(codedLines.Count - 2))
            : null;
        string? accountNo = hierarchyLines.FirstOrDefault(l => !l.Contains(" - "));

        string? aipCode = null;
        if (activity is not null)
        {
            int dash = activity.IndexOf(" - ", StringComparison.Ordinal);
            aipCode = dash > 0 ? activity[..dash].Trim() : null;
        }

        // ── Purpose — a single row below the item table, label+value in one merged cell ──
        string? purpose = null;
        foreach (PdfCell cell in rows.SelectMany(r => r))
        {
            if (TryTakeValue(cell.Text, "Purpose:", out string v)) { purpose = v; break; }
        }

        // ── Signature block ──────────────────────────────────────────────────────────
        (string? requestedBy, string? position, string? approvedBy, string? approvingPosition) =
            ExtractSignatureBlock(rows);

        return new GsoPRImportRow
        {
            PrNo              = NullIfBlank(prNo),
            Fund              = NullIfBlank(fund),
            PRDate            = prDate,
            Purpose           = NullIfBlank(purpose),
            AIPCode           = aipCode,
            AccountNo         = accountNo,
            Program           = program,
            Project           = project,
            Activity          = activity,
            RequestedBy       = requestedBy,
            Position          = position,
            ApprovedBy        = approvedBy,
            ApprovingPosition = approvingPosition,
            Items             = items,
        };
    }

    // ── Row / cell reconstruction ────────────────────────────────────────────────────

    /// <summary>Groups words into rows by Y position, splits each row into cells by X-gap,
    /// then merges line-wrap continuation rows into their logical parent row/cell.</summary>
    private static List<List<PdfCell>> BuildRows(List<PdfWord> words)
    {
        List<PdfWord> sorted = words.OrderByDescending(w => w.Bottom).ThenBy(w => w.Left).ToList();

        List<List<PdfWord>> rawRows = new();
        foreach (PdfWord w in sorted)
        {
            List<PdfWord>? last = rawRows.Count > 0 ? rawRows[^1] : null;
            if (last is not null && Math.Abs(last[0].Bottom - w.Bottom) <= RowTolerance)
                last.Add(w);
            else
                rawRows.Add(new List<PdfWord> { w });
        }
        foreach (List<PdfWord> r in rawRows) r.Sort((a, b) => a.Left.CompareTo(b.Left));

        List<(List<PdfCell> Cells, double Bottom)> merged = new();
        foreach (List<PdfWord> row in rawRows)
        {
            List<PdfCell> cells = SplitIntoCells(row);
            double bottom = row[0].Bottom;

            if (merged.Count > 0)
            {
                double gap = merged[^1].Bottom - bottom;
                if (gap > 0 && gap <= WrapMergeGap)
                {
                    List<PdfCell> prevCells = merged[^1].Cells;
                    foreach (PdfCell cell in cells)
                    {
                        int nearest = 0;
                        double best = double.MaxValue;
                        for (int i = 0; i < prevCells.Count; i++)
                        {
                            double d = Math.Abs(prevCells[i].Left - cell.Left);
                            if (d < best) { best = d; nearest = i; }
                        }
                        prevCells[nearest] = prevCells[nearest] with
                        {
                            Text = prevCells[nearest].Text + " " + cell.Text,
                        };
                    }
                    continue;
                }
            }

            merged.Add((cells, bottom));
        }

        return merged.Select(m => m.Cells).ToList();
    }

    private static List<PdfCell> SplitIntoCells(List<PdfWord> row)
    {
        List<PdfCell> cells = new();
        List<PdfWord> current = new();

        foreach (PdfWord w in row)
        {
            if (current.Count > 0 && w.Left - current[^1].Right > CellGapThreshold)
            {
                cells.Add(new PdfCell(string.Join(" ", current.Select(x => x.Text)), current[0].Left));
                current.Clear();
            }
            current.Add(w);
        }
        if (current.Count > 0)
            cells.Add(new PdfCell(string.Join(" ", current.Select(x => x.Text)), current[0].Left));

        return cells;
    }

    private static string RowText(List<PdfCell> row) => string.Join(" ", row.Select(c => c.Text));

    /// <summary>If <paramref name="cellText"/> starts with <paramref name="label"/>
    /// (case-insensitive), returns the trimmed remainder as <paramref name="value"/>.</summary>
    private static bool TryTakeValue(string cellText, string label, out string value)
    {
        if (cellText.StartsWith(label, StringComparison.OrdinalIgnoreCase))
        {
            value = cellText[label.Length..].Trim();
            return true;
        }
        value = "";
        return false;
    }

    // ── Signature block ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The signature block is a real 2D table (Signature/Printed Name/Position rows ×
    /// Requested By/Cash Availability/Approved By columns), but "Cash Availability" — the
    /// treasurer countersignature — has no home in CreatePRDto and is discarded. Column
    /// positions come from the row containing the three column headers; the Printed Name and
    /// Position rows are then bucketed against those same X positions (nearest-match, since
    /// cell left-edges don't line up exactly row to row).
    /// </summary>
    private static (string? RequestedBy, string? Position, string? ApprovedBy, string? ApprovingPosition)
        ExtractSignatureBlock(List<List<PdfCell>> rows)
    {
        List<PdfCell>? headerRow = rows.FirstOrDefault(r =>
            r.Any(c => c.Text.Contains("Requested By:", StringComparison.OrdinalIgnoreCase))
            && r.Any(c => c.Text.Contains("Approved By:", StringComparison.OrdinalIgnoreCase)));

        if (headerRow is null || headerRow.Count < 3)
            return (null, null, null, null);

        double requestedByX = headerRow[0].Left;
        double approvedByX  = headerRow[^1].Left;

        List<PdfCell>? nameRow = rows.FirstOrDefault(r =>
            r.Count > 0 && r[0].Text.Trim().Equals("Printed Name:", StringComparison.OrdinalIgnoreCase));
        List<PdfCell>? positionRow = rows.FirstOrDefault(r =>
            r.Count > 0 && r[0].Text.Trim().Equals("Position:", StringComparison.OrdinalIgnoreCase));

        // Skip cell 0 (the row's own "Printed Name:"/"Position:" label) so a narrow/malformed
        // form can never have the label text itself picked as the "nearest" value.
        string? requestedByName = NearestCellText(nameRow?.Skip(1).ToList(), requestedByX);
        string? approvedByName  = NearestCellText(nameRow?.Skip(1).ToList(), approvedByX);
        string? requestedByPos  = NearestCellText(positionRow?.Skip(1).ToList(), requestedByX);
        string? approvedByPos   = NearestCellText(positionRow?.Skip(1).ToList(), approvedByX);

        return (
            NullIfBlank(requestedByName),
            NullIfBlank(requestedByPos),
            NullIfBlank(approvedByName),
            NullIfBlank(approvedByPos));
    }

    private static string? NearestCellText(List<PdfCell>? row, double targetX)
    {
        if (row is null || row.Count == 0) return null;

        PdfCell? nearest = null;
        double best = double.MaxValue;
        foreach (PdfCell cell in row)
        {
            double d = Math.Abs(cell.Left - targetX);
            if (d < best) { best = d; nearest = cell; }
        }
        return nearest?.Text.Trim();
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
