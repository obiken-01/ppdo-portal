import { expect, type Browser, type BrowserContext, type Page } from "@playwright/test";
import { readWorld, type AipAccount, type AipRole, type AipWorld } from "./seed";

// Signs one of the seeded accounts in through the real login form, in its own browser context.
// Each account is used by one context at a time (one live refresh token per user), and a journey
// closes a context before it signs the same account in again.

export interface Session {
  context: BrowserContext;
  page: Page;
  close: () => Promise<void>;
}

export function world(): AipWorld {
  return readWorld();
}

export async function signIn(browser: Browser, role: AipRole, baseURL: string | undefined): Promise<Session> {
  return signInAs(browser, world().accounts[role], baseURL);
}

/** Same, for any seeded account (the inventory journey has its own). */
export async function signInAs(browser: Browser, account: AipAccount, baseURL: string | undefined): Promise<Session> {
  const context = await browser.newContext({ baseURL });
  const page = await context.newPage();
  await page.goto("/login");
  await page.getByLabel("Username").fill(account.username);
  await page.getByLabel("Password").fill(account.password);
  await page.getByRole("button", { name: "Sign In" }).click();
  await expect(page).not.toHaveURL(/\/login/, { timeout: 60_000 });
  return { context, page, close: () => context.close() };
}
