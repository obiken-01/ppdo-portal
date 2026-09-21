/**
 * Thresholds for AIP activity names (PPDO-85), derived from the Annex B Excel export's
 * activity-name column — Arial Narrow 10pt, column E width 38, wrap enabled
 * (`AipFormExcelService.cs`). Excel caps any row at 409pt regardless of content, which is
 * roughly AIP_EXCEL_MAX_PRINTABLE_LINES lines at this font/width; AIP_EXCEL_CHARS_PER_LINE
 * approximates how many characters wrap onto one line of that column (the column's Excel-unit
 * width, less its built-in cell padding). Both are estimates, not an exact Excel remeasurement —
 * 33 × 30 ≈ 990, matching the ticket's own "≈1,000 characters" figure from checking the real file.
 */
export const AIP_EXCEL_CHARS_PER_LINE = 33;
export const AIP_EXCEL_MAX_PRINTABLE_LINES = 30;

/**
 * Estimates how many lines `name` would occupy wrapped into the Annex B export's activity-name
 * column. Each line break the encoder types forces its own Excel line, however short — a name
 * typed as many short lines (e.g. one position per line) can pass the threshold well under 1,000
 * total characters, which a plain character count would miss. A single long line with no breaks
 * still wraps within the column width. A blank line still takes up one line, matching Excel.
 */
export function estimateAipActivityNameExcelLines(name: string): number {
  if (!name) return 0;
  return name
    .split(/\r\n|\r|\n/)
    .reduce((total, line) => total + Math.max(1, Math.ceil(line.length / AIP_EXCEL_CHARS_PER_LINE)), 0);
}

/** True once `name` would print past Excel's row-height cap — a warning only, never a save-blocker. */
export function aipActivityNamePastPrintableHeight(name: string): boolean {
  return estimateAipActivityNameExcelLines(name) > AIP_EXCEL_MAX_PRINTABLE_LINES;
}
