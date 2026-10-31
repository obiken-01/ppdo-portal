import type { Page } from "@playwright/test";
import { median } from "./format";
import { PORTAL_ROUTES, type PortalRoute } from "../routes";
import type { PerfSession } from "./session";

// PPDO-257 §4 — the throttled pass the loading-guard audit left out.
//
// Network: Chrome DevTools "Fast 4G" preset values. CPU: 4x slowdown. Cache disabled for the pass, so
// every page pays for its own JS like a first visit. Each page is a fresh tab in the signed-in context
// (the silent refresh on load is part of what a user waits for).

export const FAST_4G = {
  offline: false,
  // DevTools: 9 Mbit/s down, 1.5 Mbit/s up (x0.9 for protocol overhead), 165 ms round trip.
  downloadThroughput: (9 * 1000 * 1000 * 0.9) / 8,
  uploadThroughput: (1.5 * 1000 * 1000 * 0.9) / 8,
  latency: 165,
} as const;
export const CPU_RATE = 4;
const MARKER_TIMEOUT_MS = 60_000;
const QUIET_MS = 500;
const SETTLE_CAP_MS = 60_000;
/** Web Vitals "good" ceiling for CLS. */
const CLS_GOOD = 0.1;

export interface PageTarget {
  label: string;
  path: string;
  marker: PortalRoute["marker"];
  note?: string;
}

export interface PageRow {
  label: string;
  path: string;
  /** Marker visible, in ms from navigation start; null if it never showed. */
  firstContentMs: number | null;
  /** All API calls quiet for 500 ms. */
  dataReadyMs: number | null;
  cls: number | null;
  /** Enabled buttons in <main> at the moment the marker showed, while API calls were still running. */
  earlyButtons: string[];
  inFlightAtMarker: number;
  note: string;
}

function route(path: string): PortalRoute["marker"] {
  const r = PORTAL_ROUTES.find((x) => x.path === path);
  if (!r) throw new Error(`routes.ts has no marker for ${path}`);
  return r.marker;
}

/** The §4 table's rows. `proposalId` is resolved by the caller from the proposals list, if any exists. */
export function pageTargets(proposalId: number | null): PageTarget[] {
  return [
    { label: "AIP Entry", path: "/budget-planning/aip/entry", marker: route("/budget-planning/aip/entry") },
    { label: "AIP Review", path: "/budget-planning/aip/review/search", marker: route("/budget-planning/aip/review/search") },
    {
      label: "Proposals editor",
      path: proposalId != null ? `/budget-planning/proposals/edit?id=${proposalId}` : "/budget-planning/proposals/edit",
      marker: { role: "heading", name: "PGOM Investment Proposal", level: 1 },
      note: proposalId != null ? "" : "No saved proposal, so this is the empty editor.",
    },
    { label: "WFP Entry", path: "/budget-planning/wfp/entry", marker: route("/budget-planning/wfp/entry") },
    { label: "Allocation", path: "/budget-planning/allocation", marker: route("/budget-planning/allocation") },
    { label: "Office Ceilings", path: "/budget-planning/office-ceilings", marker: route("/budget-planning/office-ceilings") },
    { label: "Report", path: "/budget-planning/report", marker: route("/budget-planning/report") },
    { label: "Receive Delivery", path: "/inventory/receive-delivery", marker: route("/inventory/receive-delivery") },
    { label: "Distribution", path: "/inventory/distribution", marker: route("/inventory/distribution") },
    { label: "Create PR", path: "/inventory/create-pr", marker: route("/inventory/create-pr") },
    { label: "Config: Offices", path: "/config/offices", marker: route("/config/offices") },
    { label: "Config: Price Index", path: "/config/price-index", marker: route("/config/price-index") },
  ];
}

async function enabledButtons(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const out: string[] = [];
    for (const b of Array.from(document.querySelectorAll("main button"))) {
      const el = b as HTMLButtonElement;
      const r = el.getBoundingClientRect();
      if (r.width === 0 || r.height === 0 || el.disabled || el.getAttribute("aria-disabled") === "true") continue;
      const name = (el.getAttribute("aria-label") || el.textContent || "").replace(/\s+/g, " ").trim();
      if (name) out.push(name.length > 40 ? `${name.slice(0, 37)}...` : name);
    }
    return out;
  });
}

async function measureOnce(session: PerfSession, target: PageTarget): Promise<PageRow> {
  const row: PageRow = {
    label: target.label,
    path: target.path,
    firstContentMs: null,
    dataReadyMs: null,
    cls: null,
    earlyButtons: [],
    inFlightAtMarker: 0,
    note: target.note ?? "",
  };
  const page = await session.context.newPage();
  const cdp = await session.context.newCDPSession(page);
  const inFlight = new Set<unknown>();
  const isApi = (r: { url(): string; resourceType(): string }) =>
    r.url().startsWith(session.apiBase) && (r.resourceType() === "fetch" || r.resourceType() === "xhr");
  page.on("request", (r) => {
    if (isApi(r)) inFlight.add(r);
  });
  page.on("requestfinished", (r) => inFlight.delete(r));
  page.on("requestfailed", (r) => inFlight.delete(r));

  try {
    await cdp.send("Network.enable");
    await cdp.send("Network.setCacheDisabled", { cacheDisabled: true });
    await cdp.send("Network.emulateNetworkConditions", { ...FAST_4G });
    await cdp.send("Emulation.setCPUThrottlingRate", { rate: CPU_RATE });

    const t0 = Date.now();
    await page.goto(target.path, { waitUntil: "commit" });
    const marker = page.getByRole(target.marker.role, { name: target.marker.name, level: target.marker.level }).first();
    try {
      await marker.waitFor({ state: "visible", timeout: MARKER_TIMEOUT_MS });
      row.firstContentMs = Date.now() - t0;
      row.inFlightAtMarker = inFlight.size;
      if (inFlight.size > 0) row.earlyButtons = await enabledButtons(page);
    } catch {
      const where = new URL(page.url()).pathname;
      row.note = [row.note, where !== new URL(target.path, "http://x").pathname ? `Redirected to ${where}.` : "Marker not seen in 60 s."]
        .filter(Boolean)
        .join(" ");
    }

    // Data ready: no API call in flight for QUIET_MS in a row.
    let quietSince: number | null = null;
    for (let now = Date.now(); now - t0 < SETTLE_CAP_MS; now = Date.now()) {
      if (inFlight.size === 0) {
        quietSince ??= now;
        if (now - quietSince >= QUIET_MS) break;
      } else {
        quietSince = null;
      }
      await page.waitForTimeout(100);
    }
    if (quietSince != null) row.dataReadyMs = quietSince - t0;
    else row.note = [row.note, "Calls still running after 60 s."].filter(Boolean).join(" ");

    await page.waitForTimeout(500); // let the last layout shifts report
    row.cls = await page.evaluate(() => (window as unknown as { __cls?: number }).__cls ?? 0);
  } finally {
    await page.close();
  }
  return row;
}

export async function measurePages(session: PerfSession, targets: PageTarget[], runsPerPage: number): Promise<PageRow[]> {
  const rows: PageRow[] = [];
  for (const target of targets) {
    const runs: PageRow[] = [];
    for (let i = 0; i < runsPerPage; i++) runs.push(await measureOnce(session, target));
    const pick = <T>(f: (r: PageRow) => T | null): T[] => runs.map(f).filter((v): v is T => v != null);
    const first = pick((r) => r.firstContentMs);
    const ready = pick((r) => r.dataReadyMs);
    const cls = pick((r) => r.cls);
    const early = runs.find((r) => r.earlyButtons.length > 0) ?? runs[0];
    rows.push({
      ...runs[0],
      firstContentMs: first.length ? median(first) : null,
      dataReadyMs: ready.length ? median(ready) : null,
      // The worst run: a shift that happens only sometimes still hurts.
      cls: cls.length ? Math.max(...cls) : null,
      earlyButtons: early.earlyButtons,
      inFlightAtMarker: early.inFlightAtMarker,
      note: Array.from(new Set(runs.map((r) => r.note).filter(Boolean))).join(" "),
    });
  }
  return rows;
}

export function clsVerdict(cls: number | null): string {
  if (cls == null) return "n/a";
  return `${cls > CLS_GOOD ? "yes" : "no"} (CLS ${cls.toFixed(3)})`;
}
