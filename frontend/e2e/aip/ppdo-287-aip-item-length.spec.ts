import { expect, test } from "@playwright/test";
import { signIn } from "./session";
import {
  encoderApi, item, itemise, openNewLineWithAccount, openSeededActivity, refusal, runName, seedLine,
  type SeededLine,
} from "./line-fixture";

// PPDO-287 — a procurement item name over 300 characters, or a unit over 50, used to reach the
// database and fail there as a 500. Now the inputs stop at the column limit, and the API refuses a
// longer value with a readable 400.

test.describe.configure({ mode: "serial", timeout: 120_000 });

let seeded: SeededLine;

test.beforeAll(async ({ request, browser, baseURL }) => {
  test.setTimeout(120_000);
  seeded = await seedLine(request, browser, baseURL, runName("PPDO-287"));
});

test("pasting past the limit keeps 300 characters of the name and 50 of the unit", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openSeededActivity(page, seeded);
  const row = await openNewLineWithAccount(page);
  const panel = await itemise(page);

  const name = panel.getByPlaceholder("Item name", { exact: true });
  const unit = panel.getByPlaceholder("ream");
  await name.fill("n".repeat(301));
  await unit.fill("u".repeat(51));
  await expect(name).toHaveValue("n".repeat(300));
  await expect(unit).toHaveValue("u".repeat(50));

  await row.getByRole("button", { name: "Cancel" }).click();
  await close();
});

test("the API refuses a 301-character name and a 51-character unit with a 400", async ({ request }) => {
  const api = await encoderApi(request);
  const url = `/budget-planning/aip/activities/${seeded.activityId}/expenditures`;
  const line = (over: Record<string, unknown>) => ({
    accountId: seeded.accountId, fundingSourceId: seeded.fundingSourceId, ps: 0, mooe: 0, co: 0,
    procurementItems: [item(over)],
  });

  const longName = await refusal(api.call("post", url, line({ name: "n".repeat(301) })));
  expect(longName).toContain("→ 400");
  expect(longName).toContain("300 characters");

  const longUnit = await refusal(api.call("post", url, line({ unit: "u".repeat(51) })));
  expect(longUnit).toContain("→ 400");
  expect(longUnit).toContain("50 characters");
});
