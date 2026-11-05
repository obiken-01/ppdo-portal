// PPDO-257 — number helpers for the perf output. Pure; no Playwright imports.

export function median(values: number[]): number {
  if (values.length === 0) return NaN;
  const s = [...values].sort((a, b) => a - b);
  const mid = Math.floor(s.length / 2);
  return s.length % 2 ? s[mid] : (s[mid - 1] + s[mid]) / 2;
}

/** KB as the audit doc writes them (1 KB = 1,024 B), thousands-separated. */
export function kb(bytes: number | null | undefined): string {
  if (bytes == null || Number.isNaN(bytes)) return "n/a";
  const v = bytes / 1024;
  return `${v >= 100 ? Math.round(v).toLocaleString("en-US") : v.toFixed(1)} KB`;
}

export function seconds(ms: number | null | undefined): string {
  if (ms == null || Number.isNaN(ms)) return "n/a";
  return ms < 1000 ? `${Math.round(ms)} ms` : `${(ms / 1000).toFixed(2)} s`;
}

/** Keeps a free-text cell on one table row. */
export function cell(text: string): string {
  return text.replace(/\|/g, "\\|").replace(/\s+/g, " ").trim();
}
