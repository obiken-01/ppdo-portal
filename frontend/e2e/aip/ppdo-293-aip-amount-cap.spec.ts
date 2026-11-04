import { expect, test } from "@playwright/test";
import { signIn } from "./session";
import {
  encoderApi, item, itemise, openNewLineWithAccount, openSeededActivity, refusal, runName, seedLine,
  type SeededLine,
} from "./line-fixture";

// PPDO-293 — AIP amounts had no upper limit, so a huge figure was a SQL 500 or lost centavos in the
// browser. The cap is ₱999,999,999,999.99 (Ralph, 2026-11-03): the line's money inputs refuse a
// figure past it, an item whose qty × price × days passes it is named on the page, and the API
// refuses all of it with a 400.

test.describe.configure({ mode: "serial", timeout: 120_000 });

let seeded: SeededLine;

test.beforeAll(async ({ request, browser, baseURL }) => {
  test.setTimeout(120_000);
  seeded = await seedLine(request, browser, baseURL, runName("PPDO-293"));
});

test("the MOOE input takes the cap and refuses a figure past it", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openSeededActivity(page, seeded);
  const row = await openNewLineWithAccount(page);
  const mooe = row.getByRole("textbox", { name: "0.00" }).nth(1);

  await mooe.fill("999999999999.99");
  await expect(mooe).toHaveValue("999,999,999,999.99");
  await expect(row.getByRole("button", { name: "Save" })).toBeEnabled();

  // One more digit would make it ₱9.9 trillion: the keystroke is refused and the figure stays.
  await mooe.press("End");
  await mooe.pressSequentially("9");
  await expect(mooe).toHaveValue("999,999,999,999.99");

  // A paste past it is refused whole, not clamped to the cap.
  await mooe.fill("1000000000000");
  await expect(mooe).toHaveValue("999,999,999,999.99");

  await row.getByRole("button", { name: "Cancel" }).click();
  await close();
});

test("an item whose quantity × price passes the cap is named and Save stays disabled", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openSeededActivity(page, seeded);
  const row = await openNewLineWithAccount(page);
  const panel = await itemise(page);

  await panel.getByPlaceholder("Item name", { exact: true }).fill("Generator");
  await panel.getByPlaceholder("ream").fill("unit");
  await panel.getByRole("textbox", { name: "0.00" }).fill("600000000000");
  await panel.getByRole("spinbutton").first().fill("2");
  await expect(panel.getByText("Over the ₱999,999,999,999.99 limit.")).toBeVisible();
  await expect(row.getByText("The line total can be at most ₱999,999,999,999.99.")).toBeVisible();
  await expect(row.getByRole("button", { name: "Save" })).toBeDisabled();

  await row.getByRole("button", { name: "Cancel" }).click();
  await close();
});

test("the API refuses an amount, an item and a line total past the cap with a 400", async ({ request }) => {
  const api = await encoderApi(request);
  const url = `/budget-planning/aip/activities/${seeded.activityId}/expenditures`;
  const base = { accountId: seeded.accountId, fundingSourceId: seeded.fundingSourceId, ps: 0, mooe: 0, co: 0 };

  const typed = await refusal(api.call("post", url, { ...base, mooe: 1_000_000_000_000, procurementItems: [] }));
  expect(typed).toContain("→ 400");
  expect(typed).toContain("999,999,999,999.99");

  const product = await refusal(api.call("post", url,
    { ...base, procurementItems: [item({ name: "Generator", qty: 2, unitPrice: 600_000_000_000 })] }));
  expect(product).toContain("→ 400");
  expect(product).toContain("Generator");

  const sum = await refusal(api.call("post", url, {
    ...base, procurementItems: [
      item({ name: "Bond paper", qty: 1, unitPrice: 600_000_000_000 }),
      item({ name: "Toner", qty: 1, unitPrice: 600_000_000_000 }),
    ],
  }));
  expect(sum).toContain("→ 400");
  expect(sum).toContain("999,999,999,999.99");
  // "Exactly at the cap saves" is a unit test (Add_TypedAmountExactlyAtTheCap_Saves): here it would
  // also meet the E2E office's General Fund ceiling, which is a different refusal.
});
