import { expect, test, type Page, type Response } from "@playwright/test";
import { Api, readWorld } from "./seed";
import { signIn } from "./session";
import { resetAipOffice } from "./reset";
import { addActivity, openEntry, pickActivity } from "./entry";

// PPDO-289 / 290 / 291 — the AIP activity details save refuses what the page never offers, as
// encoder A in the seeded E2E office (FY 2028, the new format):
//   • 289: an end month before the start month is caught under End and nothing is sent; the API
//     refuses free-text months, a too-long month (was a 500) and a reversed pair with a 400;
//   • 290: the API refuses a negative CC adaptation or mitigation;
//   • 291: the API refuses a CC typology code that is not in Config.
// PPDO-292 (FY 2027 negative PS/MOOE/CO) is covered by unit tests only: the seeded world is FY 2028.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E ${new Date().toISOString().slice(5, 19).replace("T", " ")} ${Math.random().toString(36).slice(2, 6)}`;
const TITLE = `${RUN} activity fields`;
let activityId = 0;

// Any well-formed version: each refusal below happens before the stale-version check runs.
const ANY_VERSION = "AAAAAAAAAAA=";

const isLineWrite = (r: Response) =>
  r.request().method() === "POST" && /\/budget-planning\/aip\/activities\/\d+\/expenditures$/.test(r.url());

async function openActivity(page: Page) {
  await openEntry(page);
  await pickActivity(page, /E2E PROGRAM ONE/, new RegExp(`${RUN} project`), new RegExp(TITLE));
}

/** The read view's value under one label, e.g. "Start". */
function readField(page: Page, label: string) {
  return page.locator("dt", { hasText: new RegExp(`^${label}$`) }).locator("xpath=following-sibling::dd[1]");
}

/** A details save straight to the API, with the eSRE code the activity already has. */
function details(overrides: Record<string, unknown>) {
  return {
    name: TITLE, esreCode: "ID", implementingOffice: null, startDate: null, endDate: null,
    expectedOutputs: null, ccAdaptation: null, ccMitigation: null, ccTypologyCode: null,
    rowVersion: ANY_VERSION, ...overrides,
  };
}

async function encoderApi(request: Parameters<typeof Api.login>[0]) {
  const a = readWorld().accounts.encoderA;
  return (await Api.login(request, a.username, a.password))!;
}

test.beforeAll(async ({ request, browser, baseURL }) => {
  test.setTimeout(120_000);
  await resetAipOffice(request);
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  const saved = page.waitForResponse(isLineWrite);
  await addActivity(page, {
    program: /E2E PROGRAM ONE/, project: `${RUN} project`, title: TITLE, account: "Office Supplies", mooe: 10_000,
  });
  const body = (await (await saved).json()) as { data?: { activityId: number }; activityId?: number };
  activityId = (body.data ?? body).activityId!;
  await close();
});

test("PPDO-289: an end month before the start month is refused under End, and nothing is sent", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openActivity(page);
  const puts: string[] = [];
  page.on("request", (r) => { if (r.method() === "PUT" && /\/details$/.test(r.url())) puts.push(r.url()); });

  await page.getByRole("button", { name: "Edit details" }).click();
  await page.getByRole("combobox", { name: "Start month" }).selectOption("December");
  await page.getByRole("combobox", { name: "End month" }).selectOption("January");

  await expect(page.getByText("The end month cannot be before the start month.")).toBeVisible();
  await expect(page.getByRole("combobox", { name: "End month" })).toHaveAttribute("aria-invalid", "true");
  const save = page.getByRole("button", { name: "Save details" });
  await expect(save).toBeDisabled();
  expect(puts, "a reversed range was sent").toEqual([]);

  // Fixing the end clears the message and the save goes through.
  await page.getByRole("combobox", { name: "End month" }).selectOption("December");
  await expect(page.getByText("The end month cannot be before the start month.")).toBeHidden();
  await save.click();
  await expect(save).toBeHidden();
  await expect(readField(page, "Start")).toHaveText("December");
  await expect(readField(page, "End")).toHaveText("December");
  await close();
});

test("PPDO-289: the API refuses free-text, too-long and reversed months with a 400", async ({ request }) => {
  const api = await encoderApi(request);
  const put = (body: Record<string, unknown>) =>
    api.call("put", `/budget-planning/aip/activities/${activityId}/details`, details(body));

  await expect(put({ startDate: "Januray", endDate: "December" })).rejects.toThrow(/→ 400.*month/);
  await expect(put({ startDate: "January", endDate: "Fund provided" })).rejects.toThrow(/→ 400.*month/);
  // Over the 50-character column: was a 500 from SQL, now the same 400.
  await expect(put({ startDate: "x".repeat(60) })).rejects.toThrow(/→ 400/);
  await expect(put({ startDate: "December", endDate: "January" }))
    .rejects.toThrow(/→ 400.*The end month cannot be before the start month\./);
});

test("PPDO-290: the API refuses a negative CC adaptation or mitigation with a 400", async ({ request }) => {
  const api = await encoderApi(request);
  const put = (body: Record<string, unknown>) =>
    api.call("put", `/budget-planning/aip/activities/${activityId}/details`, details(body));

  await expect(put({ ccAdaptation: -1 })).rejects.toThrow(/→ 400.*CC adaptation cannot be negative/);
  await expect(put({ ccMitigation: -0.01 })).rejects.toThrow(/→ 400.*CC mitigation cannot be negative/);
});

test("PPDO-291: the API refuses a CC typology code that is not in Config with a 400", async ({ request }) => {
  const api = await encoderApi(request);
  await expect(api.call("put", `/budget-planning/aip/activities/${activityId}/details`,
    details({ ccTypologyCode: "NOT-A-CODE" }))).rejects.toThrow(/→ 400.*Unknown CC typology code\./);
});

test("the refused saves changed nothing: the activity still reads December to December", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openActivity(page);
  await expect(readField(page, "Start")).toHaveText("December");
  await expect(readField(page, "End")).toHaveText("December");
  await expect(readField(page, "CC typology")).toHaveText("—");
  await close();
});
