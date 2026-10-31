import type { Page } from "@playwright/test";
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

export function marker(page: Page, route: PortalRoute) {
  const { role, name, level } = route.marker;
  return page.getByRole(role, { name, level, exact: typeof name === "string" }).first();
}
