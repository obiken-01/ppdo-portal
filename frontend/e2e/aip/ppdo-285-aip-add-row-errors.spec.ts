import { expect, test, type Page } from "@playwright/test";
import { signIn } from "./session";
import { resetAipOffice } from "./reset";
import { openEntry, pickProgram } from "./entry";

// PPDO-285 — the "+ Add project" and "+ Add activity" rows on AIP Entry, when the API refuses.
// The refusal is routed in the browser, so nothing is written for the refused add:
//   • a 409 with the server's sentence shows that sentence next to the row;
//   • a 500 with no body shows the row's own fallback;
// and in both cases the box stays open with the typed name, so the encoder can try again.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E ${new Date().toISOString().slice(5, 19).replace("T", " ")} ${Math.random().toString(36).slice(2, 6)}`;
const PROJECT = `${RUN} add-row project`;

const ADD_PROJECT = /\/budget-planning\/aip\/programs\/\d+\/projects$/;
const ADD_ACTIVITY = /\/budget-planning\/aip\/projects\/\d+\/activities$/;

/** Opens an inline "+ Add …" box and types a name, without adding it yet. */
async function typeInlineName(page: Page, button: string, placeholder: string, name: string) {
  const box = page.getByPlaceholder(placeholder);
  await expect(async () => {
    if (!(await box.isVisible())) await page.getByRole("button", { name: button }).click();
    await expect(box).toBeVisible({ timeout: 2_000 });
  }).toPass({ timeout: 15_000 });
  await box.fill(name);
  return box;
}

test.beforeAll(async ({ request }) => {
  test.setTimeout(120_000); // hooks keep the 30s default (see aip-scope.spec.ts)
  await resetAipOffice(request);
});

test("a refused project add shows the server's message and keeps the typed name", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  await pickProgram(page, /E2E PROGRAM ONE/);

  const message = "Another project was added to this program at the same moment. Please try again.";
  await page.route(ADD_PROJECT, (route) =>
    route.request().method() === "POST"
      ? route.fulfill({ status: 409, contentType: "application/json", body: JSON.stringify({ data: null, error: message }) })
      : route.fallback());

  const box = await typeInlineName(page, "+ Add project", "Project name", PROJECT);
  await page.getByRole("button", { name: "Add", exact: true }).click();

  await expect(page.getByRole("alert").filter({ hasText: message })).toBeVisible();
  await expect(box).toBeVisible();
  await expect(box).toHaveValue(PROJECT);
  await expect(page.getByRole("button", { name: "Add", exact: true })).toBeEnabled();

  // The route lifted, the same click adds it, and the message goes with the box.
  await page.unroute(ADD_PROJECT);
  await page.getByRole("button", { name: "Add", exact: true }).click();
  await expect(box).toBeHidden();
  await expect(page.getByRole("alert").filter({ hasText: message })).toBeHidden();
  await close();
});

test("a refused activity add with no body shows the fallback and keeps the typed title", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  await pickProgram(page, /E2E PROGRAM ONE/);
  await page.getByPlaceholder("Search project…").click();
  await page.getByRole("button", { name: new RegExp(PROJECT) }).first().click();

  await page.route(ADD_ACTIVITY, (route) =>
    route.request().method() === "POST" ? route.fulfill({ status: 500, body: "" }) : route.fallback());

  const title = `${RUN} refused activity`;
  const box = await typeInlineName(page, "+ Add activity", "Activity title", title);
  await page.getByRole("button", { name: "Add", exact: true }).click();

  await expect(page.getByRole("alert").filter({ hasText: "Could not add the activity." })).toBeVisible();
  await expect(box).toBeVisible();
  await expect(box).toHaveValue(title);

  // Cancel closes the box and clears the message with it.
  await page.getByRole("button", { name: "Cancel" }).click();
  await expect(box).toBeHidden();
  await expect(page.getByRole("alert").filter({ hasText: "Could not add the activity." })).toBeHidden();
  await close();
});
