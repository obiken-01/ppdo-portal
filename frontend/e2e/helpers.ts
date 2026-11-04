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
