import { expect, type APIRequestContext, type Browser, type Page, type Response } from "@playwright/test";
import { Api, readWorld } from "./seed";
import { signIn } from "./session";
import { resetAipOffice } from "./reset";
import { addActivity, openEntry, pickActivity } from "./entry";

// Shared by the PPDO-286 / 287 / 288 / 293 specs: one activity in the E2E office with one saved
// MOOE line, written as encoder A, plus the ids a direct API call needs. Same set-up as
// ppdo-271-aip-line-bad-paths.spec.ts, which predates this file and keeps its own copy.

export interface SeededLine {
  run: string;
  title: string;
  activityId: number;
  lineId: number;
  rowVersion: string;
  accountId: number;
  fundingSourceId: number;
}

const isLinePost = (r: Response) =>
  r.request().method() === "POST" && /\/budget-planning\/aip\/activities\/\d+\/expenditures$/.test(r.url());

export function runName(ticket: string): string {
  const stamp = new Date().toISOString().slice(5, 19).replace("T", " ");
  return `E2E ${stamp} ${Math.random().toString(36).slice(2, 6)} ${ticket}`;
}

export async function seedLine(
  request: APIRequestContext, browser: Browser, baseURL: string | undefined, run: string,
): Promise<SeededLine> {
  await resetAipOffice(request);
  const title = `${run} activity`;
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  const saved = page.waitForResponse(isLinePost);
  await addActivity(page, {
    program: /E2E PROGRAM ONE/, project: `${run} project`, title, account: "Office Supplies", mooe: 10_000,
  });
  type Written = { line: { id: number; accountId: number; fundingSourceId: number; rowVersion: string }; activityId: number };
  const body = (await (await saved).json()) as { data?: Written } & Partial<Written>;
  const written = (body.data ?? body) as Written;
  await close();
  return {
    run, title,
    activityId: written.activityId,
    lineId: written.line.id,
    rowVersion: written.line.rowVersion,
    accountId: written.line.accountId,
    fundingSourceId: written.line.fundingSourceId,
  };
}

export async function openSeededActivity(page: Page, s: SeededLine) {
  await openEntry(page);
  await pickActivity(page, /E2E PROGRAM ONE/, new RegExp(`${s.run} project`), new RegExp(s.title));
}

/** The editor row that "+ Add Account" opens, with Office Supplies (MOOE) picked. */
export async function openNewLineWithAccount(page: Page) {
  await page.getByRole("button", { name: "+ Add Account" }).click();
  const row = page.getByRole("row").filter({ has: page.getByPlaceholder("Search accounts…") });
  await expect(row).toBeVisible();
  await row.getByPlaceholder("Search accounts…").click();
  await row.getByPlaceholder("Search accounts…").fill("Office Supplies");
  await page.getByRole("button", { name: /Office Supplies/i }).first().click();
  return row;
}

/**
 * Turns the open line into an itemised one and returns the panel holding its items. The panel is
 * the table row right after the editor row, so it is found by its checkbox's label.
 */
export async function itemise(page: Page) {
  await page.getByLabel("Cost this line from the Price Index").check();
  const panel = page.getByRole("row").filter({ has: page.getByPlaceholder("Item name", { exact: true }) });
  await expect(panel).toBeVisible();
  return panel;
}

export async function encoderApi(request: APIRequestContext): Promise<Api> {
  const a = readWorld().accounts.encoderA;
  const api = await Api.login(request, a.username, a.password);
  expect(api, `login as ${a.username} failed`).not.toBeNull();
  return api!;
}

/** The status line and body of a refused call, e.g. "POST … → 400: {"error":"Unit is required."}". */
export async function refusal(call: Promise<unknown>): Promise<string> {
  try {
    await call;
  } catch (e) {
    return (e as Error).message;
  }
  throw new Error("the API accepted a request it should have refused");
}

export const item = (over: Record<string, unknown> = {}) => ({
  priceIndexItemId: null, name: "Bond paper", unit: "ream", unitPrice: 250, qty: 4, numberOfDays: 1, periodNo: 1,
  ...over,
});
