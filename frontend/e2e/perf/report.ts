import { cell, kb, seconds } from "./format";
import type { ApiRow } from "./api-measure";
import { clsVerdict, CPU_RATE, FAST_4G, type PageRow } from "./page-measure";

// PPDO-257 — turns the measurements into the two tables of docs/v1.9/Performance_Audit_2026-11.md
// (§3 and §4), ready to paste. No URLs, hosts, ids, usernames or tokens go in: the file is uploaded
// from a public repository's workflow.

/** The "10-04 (local, uncompressed)" column of §3, from the 2026-10-04 audit. */
const BASELINE_1004: Record<string, string> = {
  "GET /budget-planning/aip/{id} (largest FY record)": "1,520 KB",
  "GET /budget-planning/aip/{id}/summary": "724 KB",
  "GET /config/price-index/picker?active=true": "713 KB",
  "GET /config/price-index/picker?search=paper": "14 KB",
  "GET /config/cc-typologies": "78 KB",
  "GET /config/accounts": "70 KB",
};

export interface PerfResult {
  generatedAt: string;
  env: string;
  persona: string;
  personaNote: string | null;
  apis: ApiRow[];
  pages: PageRow[];
  pageRuns: number;
}

export function toMarkdown(r: PerfResult): string {
  const lines: string[] = [];
  const envNote =
    r.env === "local" ? " **Local numbers are not the measurement; run the E2E (UAT) workflow for the audit.**" : "";
  lines.push(`<!-- PPDO-257 perf run, ${r.generatedAt}, ${r.env}, persona ${r.persona} -->`);
  lines.push(`Run: ${r.generatedAt} · ${r.env} · signed in as ${r.persona}.${envNote}`);
  if (r.personaNote) lines.push(`> ${r.personaNote}`);
  lines.push("");

  lines.push(`## 3. API responses on ${r.env}`, "");
  lines.push(`Median of 3 warm runs after one discarded warm-up. "Transferred" is the wire size from CDP (headers included), "resource" is the decoded body.`, "");
  lines.push(`| Request | 10-04 (local, uncompressed) | ${r.env} transferred | ${r.env} resource | Time | \`Content-Encoding\` |`);
  lines.push("|---|---|---|---|---|---|");
  for (const a of r.apis.filter((x) => x.group === "baseline")) {
    lines.push(
      `| \`${cell(a.label)}\` | ${BASELINE_1004[a.label] ?? ""} | ${kb(a.wireBytes)} | ${kb(a.decodedBytes)} | ${seconds(a.ms)} | ${a.encoding ?? ""}${a.note ? ` (${cell(a.note)})` : ""} |`,
    );
  }
  lines.push("", "**New in v1.9.0** (no baseline):", "");
  lines.push("| Request | Transferred | Resource | Time | Note |");
  lines.push("|---|---|---|---|---|");
  for (const a of r.apis.filter((x) => x.group === "v190")) {
    const enc = a.encoding ? `Content-Encoding: ${a.encoding}. ` : "";
    lines.push(`| \`${cell(a.label)}\` | ${kb(a.wireBytes)} | ${kb(a.decodedBytes)} | ${seconds(a.ms)} | ${cell(enc + a.note)} |`);
  }

  lines.push("", `## 4. Throttled pass`, "");
  lines.push(
    `Chromium, network "Fast 4G" (${((FAST_4G.downloadThroughput * 8) / 0.9 / 1e6).toFixed(0)} Mbit/s down, ` +
      `${FAST_4G.latency} ms round trip), CPU ${CPU_RATE}x slowdown, cache off. ` +
      `${r.pageRuns} run${r.pageRuns === 1 ? "" : "s"} per page (median time, worst CLS).`,
    "",
    `"First content" is the page's title (or first control) visible. "Data ready" is the first moment no API call ` +
      `had been running for 0.5 s. "Early-clickable" lists the enabled buttons in the page body at the moment the title showed ` +
      `while API calls were still running: **candidates to check by eye**, not all of them are a problem.`,
    "",
  );
  lines.push("| Page | First content | Layout jump? | Early-clickable control? | Notes |");
  lines.push("|---|---|---|---|---|");
  for (const p of r.pages) {
    const early =
      p.firstContentMs == null
        ? "n/a"
        : p.earlyButtons.length > 0
          ? `yes: ${p.earlyButtons.slice(0, 4).map((b) => `"${b}"`).join(", ")}${p.earlyButtons.length > 4 ? ` +${p.earlyButtons.length - 4}` : ""} (${p.inFlightAtMarker} call${p.inFlightAtMarker === 1 ? "" : "s"} in flight)`
          : "no";
    const notes = [p.dataReadyMs != null ? `Data ready at ${seconds(p.dataReadyMs)}.` : "", p.note].filter(Boolean).join(" ");
    lines.push(`| ${cell(p.label)} | ${seconds(p.firstContentMs)} | ${clsVerdict(p.cls)} | ${cell(early)} | ${cell(notes)} |`);
  }
  lines.push("");
  return lines.join("\n");
}
