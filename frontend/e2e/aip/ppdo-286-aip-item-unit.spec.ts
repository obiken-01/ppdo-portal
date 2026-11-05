import { expect, test } from "@playwright/test";
import { signIn } from "./session";
import {
  encoderApi, item, itemise, openNewLineWithAccount, openSeededActivity, refusal, runName, seedLine,
  type SeededLine,
} from "./line-fixture";

// PPDO-286 — a procurement item with no unit. Before the fix a null unit crashed the save (500) and
// a blank one saved. Now the page says "Unit is required." under the item and keeps Save disabled,
// and the API refuses both with a 400 in the same words.

test.describe.configure({ mode: "serial", timeout: 120_000 });

let seeded: SeededLine;

test.beforeAll(async ({ request, browser, baseURL }) => {
  test.setTimeout(120_000);
  seeded = await seedLine(request, browser, baseURL, runName("PPDO-286"));
});

test("an item with no unit shows 'Unit is required.' and Save stays disabled", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openSeededActivity(page, seeded);
  const row = await openNewLineWithAccount(page);
  const panel = await itemise(page);

  await panel.getByPlaceholder("Item name", { exact: true }).fill("Bond paper");
  await panel.getByRole("textbox", { name: "0.00" }).fill("250");
  await expect(panel.getByText("Unit is required.")).toBeVisible();
  const save = row.getByRole("button", { name: "Save" });
  await expect(save).toBeDisabled();

  // Spaces are not a unit either.
  await panel.getByPlaceholder("ream").fill("   ");
  await expect(panel.getByText("Unit is required.")).toBeVisible();
  await expect(save).toBeDisabled();

  await panel.getByPlaceholder("ream").fill("ream");
  await expect(panel.getByText("Unit is required.")).toBeHidden();
  await expect(save).toBeEnabled();

  await row.getByRole("button", { name: "Cancel" }).click();
  await close();
});

test("the API refuses a missing or blank unit with a 400, not a 500", async ({ request }) => {
  const api = await encoderApi(request);
  const url = `/budget-planning/aip/activities/${seeded.activityId}/expenditures`;
  const line = (unit: unknown) => ({
    accountId: seeded.accountId, fundingSourceId: seeded.fundingSourceId, ps: 0, mooe: 0, co: 0,
    procurementItems: [item({ unit })],
  });

  for (const unit of [null, "", "   "]) {
    const message = await refusal(api.call("post", url, line(unit)));
    expect(message, `unit ${JSON.stringify(unit)}`).toContain("→ 400");
    expect(message).toContain("Unit is required.");
  }

  // The same refusal on an edit of the line beforeAll saved.
  const put = await refusal(api.call("put", `/budget-planning/aip/expenditures/${seeded.lineId}`,
    { ...line(null), rowVersion: seeded.rowVersion }));
  expect(put).toContain("→ 400");
  expect(put).toContain("Unit is required.");
});
