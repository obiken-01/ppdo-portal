import type { CDPSession, Page } from "@playwright/test";
import { median } from "./format";
import type { PerfSession } from "./session";

// PPDO-257 §3 — API sizes and times.
//
// Each request is a real browser fetch from a blank same-origin page, so the browser negotiates
// Accept-Encoding itself and decodes the body. CDP supplies what fetch cannot see:
//   wire bytes  = Network.loadingFinished.encodedDataLength (what actually crossed the network,
//                 headers included, the same figure DevTools shows as "transferred"),
//   encoding    = the Content-Encoding response header.
// Decoded bytes and the time come from the page (time runs until the body is fully read).
// Read-only GETs, except the opt-in bulk export (E2E_PERF_BULK_EXPORT=1), which writes one job row.

export interface ApiRow {
  group: "baseline" | "v190";
  label: string;
  /** The status the first recorded run returned; anything but 2xx is explained in `note`. */
  status: number | null;
  wireBytes: number | null;
  decodedBytes: number | null;
  encoding: string | null;
  ms: number | null;
  runs: number[];
  note: string;
}

interface Probe {
  status: number;
  ms: number;
  decoded: number;
  wire: number | null;
  encoding: string | null;
  body: string;
}

const WARM_RUNS = 3;

class ApiProbe {
  private fullUrl = "";
  private requestId: string | null = null;
  private headers: Record<string, string> = {};
  private wire: number | null = null;

  private constructor(private readonly page: Page, private readonly cdp: CDPSession, private readonly session: PerfSession) {}

  static async open(session: PerfSession): Promise<ApiProbe> {
    // A same-origin page that is not part of the portal: no auth guard, no background polling.
    const page = await session.context.newPage();
    await page.goto("/perf-probe-blank");
    const cdp = await session.context.newCDPSession(page);
    await cdp.send("Network.enable");
    const probe = new ApiProbe(page, cdp, session);
    cdp.on("Network.responseReceived", (e) => {
      if (e.type === "Preflight" || e.response.url !== probe.fullUrl) return;
      probe.requestId = e.requestId;
      probe.headers = Object.fromEntries(Object.entries(e.response.headers).map(([k, v]) => [k.toLowerCase(), String(v)]));
    });
    cdp.on("Network.loadingFinished", (e) => {
      if (e.requestId === probe.requestId) probe.wire = e.encodedDataLength;
    });
    return probe;
  }

  async wait(ms: number): Promise<void> {
    await this.page.waitForTimeout(ms);
  }

  async close(): Promise<void> {
    await this.page.close();
  }

  async fetchOnce(path: string, method: "GET" | "POST" = "GET", jsonBody?: unknown): Promise<Probe> {
    this.fullUrl = `${this.session.apiBase}${path}`;
    this.requestId = null;
    this.headers = {};
    this.wire = null;
    const r = await this.page.evaluate(
      async ({ url, token, method, body }) => {
        const t0 = performance.now();
        const res = await fetch(url, {
          method,
          headers: { Authorization: `Bearer ${token}`, ...(body ? { "Content-Type": "application/json" } : {}) },
          body,
          cache: "no-store",
          credentials: "omit",
        });
        const buf = await res.arrayBuffer();
        const ms = performance.now() - t0;
        // Only small bodies are kept: the caller reads ids out of list responses.
        const text = buf.byteLength < 3_000_000 ? new TextDecoder().decode(buf) : "";
        return { status: res.status, ms, decoded: buf.byteLength, text };
      },
      { url: this.fullUrl, token: this.session.token(), method, body: jsonBody === undefined ? undefined : JSON.stringify(jsonBody) },
    );
    // loadingFinished can land just after the page's promise resolves.
    for (let i = 0; i < 40 && this.wire == null; i++) await this.page.waitForTimeout(50);
    return {
      status: r.status,
      ms: r.ms,
      decoded: r.decoded,
      wire: this.wire,
      encoding: this.headers["content-encoding"] ?? null,
      body: r.text,
    };
  }
}

async function json(probe: ApiProbe, path: string): Promise<unknown> {
  const r = await probe.fetchOnce(path);
  if (r.status < 200 || r.status >= 300 || !r.body) return null;
  try {
    const parsed = JSON.parse(r.body) as { data?: unknown };
    return parsed.data ?? parsed;
  } catch {
    return null;
  }
}

function asArray(v: unknown): Record<string, unknown>[] {
  return Array.isArray(v) ? (v as Record<string, unknown>[]) : [];
}

function num(v: unknown): number | null {
  return typeof v === "number" && Number.isFinite(v) ? v : null;
}

async function measure(probe: ApiProbe, group: ApiRow["group"], label: string, path: string | null, skipNote: string): Promise<ApiRow> {
  const empty: ApiRow = { group, label, status: null, wireBytes: null, decodedBytes: null, encoding: null, ms: null, runs: [], note: skipNote };
  if (!path) return empty;
  await probe.fetchOnce(path); // warm-up, discarded
  const runs: Probe[] = [];
  for (let i = 0; i < WARM_RUNS; i++) runs.push(await probe.fetchOnce(path));
  const ok = runs.every((r) => r.status >= 200 && r.status < 300);
  const last = runs[runs.length - 1];
  const wires = runs.map((r) => r.wire).filter((w): w is number => w != null);
  return {
    group,
    label,
    status: runs[0].status,
    wireBytes: wires.length ? median(wires) : null,
    decodedBytes: median(runs.map((r) => r.decoded)),
    encoding: last.encoding ?? "none",
    ms: median(runs.map((r) => r.ms)),
    runs: runs.map((r) => r.ms),
    note: ok ? "" : `HTTP ${runs[0].status}: the persona is refused or has no data here`,
  };
}

/** The 2026-10-04 list endpoints and the v1.9.0 ones, in the audit doc's order. */
/** `proposalId` is handed on so §4 can open the editor on a real proposal. */
export async function measureApis(session: PerfSession): Promise<{ rows: ApiRow[]; proposalId: number | null }> {
  const probe = await ApiProbe.open(session);
  const rows: ApiRow[] = [];
  let proposalId: number | null = null;
  try {
    // ── Resolve ids from list endpoints ──────────────────────────────────────
    const aips = asArray(await json(probe, "/budget-planning/aip"));
    // "Largest" = most offices; ties go to the later fiscal year.
    const aip = [...aips].sort(
      (a, b) => (num(b.officeCount) ?? 0) - (num(a.officeCount) ?? 0) || (num(b.fiscalYear) ?? 0) - (num(a.fiscalYear) ?? 0),
    )[0];
    const aipId = num(aip?.id);
    const aipYear = num(aip?.fiscalYear);
    let officeId: number | null = null;
    if (aipId != null) {
      for (const path of [`/budget-planning/aip/${aipId}/summary`, `/budget-planning/aip/${aipId}`]) {
        const rec = (await json(probe, path)) as { offices?: Record<string, unknown>[] } | null;
        officeId = num(rec?.offices?.find((o) => num(o.officeId) != null)?.officeId);
        if (officeId != null) break;
      }
    }
    const years = Array.from(new Set(aips.map((a) => num(a.fiscalYear)).filter((y): y is number => y != null))).sort((a, b) => b - a);

    for (const y of years) {
      const page = (await json(probe, `/budget-planning/proposals?fiscalYear=${y}&pageSize=50`)) as { items?: Record<string, unknown>[] } | null;
      proposalId = num(page?.items?.find((i) => num(i.proposalId) != null)?.proposalId);
      if (proposalId != null) break;
    }

    let wfpYear: number | null = null;
    let wfpOffice: number | null = null;
    for (const y of years) {
      const offices = asArray(await json(probe, `/budget-planning/wfp/report/offices?fiscalYear=${y}`));
      wfpOffice = num(offices[0]?.officeId);
      if (wfpOffice != null) {
        wfpYear = y;
        break;
      }
    }

    const noAip = "No AIP record is visible to this persona.";
    const yearNote = aipYear != null ? `FY${aipYear}, ${num(aip?.officeCount) ?? "?"} offices` : "";

    // ── §3 baseline table ────────────────────────────────────────────────────
    const detail = await measure(probe, "baseline", "GET /budget-planning/aip/{id} (largest FY record)", aipId != null ? `/budget-planning/aip/${aipId}` : null, noAip);
    if (!detail.note) detail.note = yearNote;
    rows.push(detail);
    rows.push(await measure(probe, "baseline", "GET /budget-planning/aip/{id}/summary", aipId != null ? `/budget-planning/aip/${aipId}/summary` : null, noAip));
    rows.push(await measure(probe, "baseline", "GET /config/price-index/picker?active=true", "/config/price-index/picker?active=true", ""));
    rows.push(await measure(probe, "baseline", "GET /config/price-index/picker?search=paper", "/config/price-index/picker?search=paper", ""));
    rows.push(await measure(probe, "baseline", "GET /config/cc-typologies", "/config/cc-typologies", ""));
    rows.push(await measure(probe, "baseline", "GET /config/accounts", "/config/accounts", ""));

    // ── §3 new in v1.9.0 ─────────────────────────────────────────────────────
    rows.push(await measure(probe, "v190", "GET /notifications/unread-count", "/notifications/unread-count", ""));
    rows.push(await measure(probe, "v190", "GET /notifications", "/notifications", ""));
    rows.push(await measure(probe, "v190", "GET /budget-planning/aip/{aipId}/offices/{officeId}/comments",
      aipId != null && officeId != null ? `/budget-planning/aip/${aipId}/offices/${officeId}/comments` : null,
      "No AIP office with an officeId was found."));
    rows.push(await measure(probe, "v190", "GET /budget-planning/proposals/{id}/comments",
      proposalId != null ? `/budget-planning/proposals/${proposalId}/comments` : null, "No saved proposal was found."));
    rows.push(await measure(probe, "v190", "GET /budget-planning/wfp/report/preview (initial WFP)",
      wfpYear != null && wfpOffice != null ? `/budget-planning/wfp/report/preview?officeId=${wfpOffice}&fiscalYear=${wfpYear}` : null,
      "No office is eligible for the WFP report."));
    rows.push(await measureBulkExport(probe, years[0] ?? null));
    rows.push(await measure(probe, "v190", "GET /budget-planning/aip/{aipId}/ceiling",
      aipId != null ? `/budget-planning/aip/${aipId}/ceiling` : null, noAip));
  } finally {
    await probe.close();
  }
  return { rows, proposalId };
}

async function measureBulkExport(probe: ApiProbe, fiscalYear: number | null): Promise<ApiRow> {
  const label = "POST /budget-planning/wfp/report/bulk-exports → job → download";
  const row: ApiRow = { group: "v190", label, status: null, wireBytes: null, decodedBytes: null, encoding: null, ms: null, runs: [], note: "" };
  if (process.env.E2E_PERF_BULK_EXPORT !== "1") {
    row.note = "Skipped: it writes a job row. Set E2E_PERF_BULK_EXPORT=1 to measure it.";
    return row;
  }
  if (fiscalYear == null) {
    row.note = "Skipped: no AIP record, so no fiscal year to export.";
    return row;
  }
  const started = Date.now();
  const post = await probe.fetchOnce("/budget-planning/wfp/report/bulk-exports", "POST", { fiscalYear });
  row.status = post.status;
  if (post.status !== 200 && post.status !== 202) {
    row.note = `Request refused: HTTP ${post.status} (no office Consolidated yet, or FY <= 2027).`;
    return row;
  }
  const jobId = num((JSON.parse(post.body) as { data?: { id?: number } }).data?.id);
  if (jobId == null) {
    row.note = "Job request returned no id.";
    return row;
  }
  let status = "Queued";
  while (Date.now() - started < 10 * 60_000) {
    const job = await probe.fetchOnce(`/budget-planning/wfp/report/bulk-exports/${jobId}`);
    status = (JSON.parse(job.body) as { data?: { status?: string } }).data?.status ?? "Unknown";
    if (status !== "Queued" && status !== "Running") break;
    await probe.wait(2000);
  }
  if (status === "Queued" || status === "Running") {
    row.note = `Gave up after 10 min, still ${status}.`;
    return row;
  }
  const download = await probe.fetchOnce(`/budget-planning/wfp/report/bulk-exports/${jobId}/download`);
  row.ms = Date.now() - started;
  row.runs = [row.ms];
  row.status = download.status;
  row.wireBytes = download.wire;
  row.decodedBytes = download.decoded;
  row.encoding = download.encoding ?? "none";
  row.note = `Time to ready, then downloaded (job ended ${status}). One run, not a median.`;
  return row;
}
