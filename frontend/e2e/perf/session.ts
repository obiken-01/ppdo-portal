import { expect, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { PERSONAS, type Persona } from "../personas";

// PPDO-257 — sign-in for the perf measurement. Uses the real login form, in ONE browser context for
// the whole run (a user has one live refresh token; a second context would sign the first one out).
// The access token lives in memory in the app, so it is read off the first authorised request the
// page makes. It is held in this module's variable only: never logged, never written to the output.

export interface PerfSession {
  context: BrowserContext;
  page: Page;
  persona: Persona;
  /** Set when SA was not configured and AD stood in. */
  personaNote: string | null;
  /** The API root, e.g. http://localhost:7071/api (no trailing slash). */
  apiBase: string;
  /** The newest access token the app sent. */
  token: () => string;
}

/** SA first, AD as the stated fallback. */
export function pickPersona(): { persona: Persona; note: string | null } {
  const sa = PERSONAS.find((p) => p.key === "SA" && p.configured);
  if (sa) return { persona: sa, note: null };
  const ad = PERSONAS.find((p) => p.key === "AD" && p.configured);
  if (ad) return { persona: ad, note: "No SA credentials were set, so the AD persona was used. Admin-only rows may differ from SA." };
  throw new Error("Perf run needs E2E_SA_EMAIL / E2E_SA_PASSWORD (or E2E_AD_EMAIL / E2E_AD_PASSWORD).");
}

export async function signIn(browser: Browser, baseURL: string | undefined): Promise<PerfSession> {
  const { persona, note } = pickPersona();
  const context = await browser.newContext({ baseURL });
  // Layout-shift collector for the throttled pass. Registered before any page exists.
  await context.addInitScript(() => {
    const w = window as unknown as { __cls: number };
    w.__cls = 0;
    try {
      new PerformanceObserver((list) => {
        for (const entry of list.getEntries() as unknown as { value: number; hadRecentInput: boolean }[]) {
          if (!entry.hadRecentInput) w.__cls += entry.value;
        }
      }).observe({ type: "layout-shift", buffered: true });
    } catch {
      // Browser without layout-shift entries: __cls stays 0.
    }
  });

  let token = "";
  let apiBase = (process.env.E2E_API_URL ?? "").replace(/\/+$/, "");
  context.on("request", async (req) => {
    const auth = (await req.allHeaders())["authorization"];
    if (!auth?.startsWith("Bearer ")) return;
    token = auth.slice("Bearer ".length);
    if (!apiBase) {
      const i = req.url().indexOf("/api/");
      if (i >= 0) apiBase = req.url().slice(0, i + 4);
    }
  });

  const page = await context.newPage();
  await page.goto("/login");
  await page.getByLabel("Username").fill(persona.email!);
  await page.getByLabel("Password").fill(persona.password!);
  await page.getByRole("button", { name: "Sign In" }).click();
  await expect(page).not.toHaveURL(/\/login/, { timeout: 60_000 });
  await expect.poll(() => token, { timeout: 60_000, message: "no authorised API request seen after sign-in" }).not.toBe("");
  await expect.poll(() => apiBase, { timeout: 5_000, message: "could not work out the API root; set E2E_API_URL" }).not.toBe("");

  return { context, page, persona, personaNote: note, apiBase, token: () => token };
}
