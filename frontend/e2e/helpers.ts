import type { Page, Response } from "@playwright/test";
import { expect } from "./fixtures";
import type { PersonaKey } from "./personas";
import type { PortalRoute } from "./routes";

// Shared by smoke.spec.ts and sweep.spec.ts (PPDO-272).

/** The persona behind an `as-<KEY>` project. */
export function personaOf(projectName: string): PersonaKey {
  return projectName.replace(/^as-/, "") as PersonaKey;
}

/** Collects what counts as broken while the test runs. */
export function watch(page: Page): string[] {
  const problems: string[] = [];
  page.on("pageerror", (err) => problems.push(`uncaught: ${err.message}`));
  page.on("response", (res) => {
    if (res.status() >= 500) problems.push(`${res.status()} ${res.request().method()} ${res.url()}`);
  });
  page.on("console", (msg) => {
    // "Failed to load resource" repeats a response status: 5xx are caught above, and a 4xx is
    // an answer the page handles (e.g. a 403 just before a guard redirects).
    if (msg.type() === "error" && !msg.text().startsWith("Failed to load resource"))
      problems.push(`console: ${msg.text().slice(0, 200)}`);
  });
  return problems;
}

export const pathname = (page: Page) => new URL(page.url()).pathname;

export async function open(page: Page, path: string) {
  await page.goto(path);
  await page.waitForLoadState("networkidle").catch(() => {});
}

export function expectSignedIn(page: Page, path: string) {
  expect(pathname(page), `signed out on ${path}: the persona's session was lost`).not.toBe("/login");
}

/**
 * PPDO-300: the Report page's office picker says "No office ready yet" (and is disabled) before its
 * offices request has even gone out, so an empty or disabled picker means nothing until that request
 * has answered. Start this before `goto`, await it once the page is known to ask (see the page's
 * `canOpenWfpReportForYear`), then wait for the "Loading offices…" option to go.
 */
export function reportOfficesAnswer(page: Page, fiscalYear: number) {
  const answer = page.waitForResponse(
    (r) => /\/budget-planning\/wfp\/report\/offices\?/.test(r.url()) && r.url().includes(`fiscalYear=${fiscalYear}`),
  );
  answer.catch(() => {}); // not awaited when the page has no reason to ask
  return answer;
}

/** The signed-in user's own /auth/me answer, read off the response the page itself got. */
export async function meFrom<T>(meResponse: Response): Promise<T> {
  const body = (await meResponse.json()) as T | { data: T };
  return (typeof body === "object" && body !== null && "data" in body ? body.data : body) as T;
}

export function marker(page: Page, route: PortalRoute) {
  const { role, name, level } = route.marker;
  return page.getByRole(role, { name, level, exact: typeof name === "string" }).first();
}

// PPDO-305 — a normally loaded page must not show a load-error band. The smoke checks above catch
// crashes and 5xx, but a 4xx that the page turns into "Could not load …" passed every test.
// The wording comes from LoadError and the inline bands in src/ ("Could not load …", "Couldn't
// load …", "Failed to load …", "Something went wrong"). Narrower than the sweep's ERROR_TEXT on
// purpose: words like "error" or "failed" also appear in normal labels.
const LOAD_ERROR_TEXT = /could not load|couldn['’]t load|can['’]t load|cannot load|failed to load|unable to load|something went wrong/i;

/**
 * "<path>" or "<path> <persona>" → why a visible alert is the correct state there.
 * Only deliberate warnings belong here, never a real load failure: file that as a bug instead
 * and list it under "Expected failures" in e2e/README.md.
 */
const ALERT_ALLOWED: Record<string, string> = {
  // None yet.
};

/** What <main> shows as a load error right now: visible role="alert" blocks and error wording. */
export async function visibleErrorBands(page: Page): Promise<string[]> {
  const main = page.locator("main").first();
  const found: string[] = [];
  for (const el of await main.getByRole("alert").all()) {
    if (await el.isVisible()) found.push(`alert: ${(await el.innerText()).trim().slice(0, 160)}`);
  }
  for (const el of await main.getByText(LOAD_ERROR_TEXT).all()) {
    if (await el.isVisible()) found.push(`text: ${(await el.innerText()).trim().slice(0, 160)}`);
  }
  return Array.from(new Set(found));
}

/** Fails when the loaded page shows an error band, unless ALERT_ALLOWED says it is correct there. */
export async function expectNoErrorBand(page: Page, path: string, persona: string) {
  if (ALERT_ALLOWED[`${path} ${persona}`] ?? ALERT_ALLOWED[path]) return;
  expect(await visibleErrorBands(page), `error band on ${path} after a normal load`).toEqual([]);
}
