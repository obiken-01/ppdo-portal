import { expect, test } from "@playwright/test";
import { encoderApi, refusal, runName, seedLine, type SeededLine } from "./line-fixture";

// PPDO-288 — the API saved an expenditure line with no account; only the submit gate noticed. The
// page side ("Pick an account.", Save disabled) is already covered by
// ppdo-271-aip-line-bad-paths.spec.ts, so this spec is the direct API call, on create and update.

test.describe.configure({ mode: "serial", timeout: 120_000 });

let seeded: SeededLine;

test.beforeAll(async ({ request, browser, baseURL }) => {
  test.setTimeout(120_000);
  seeded = await seedLine(request, browser, baseURL, runName("PPDO-288"));
});

test("the API refuses a line with no account, on create and on update", async ({ request }) => {
  const api = await encoderApi(request);
  const body = { accountId: null, fundingSourceId: seeded.fundingSourceId, ps: 0, mooe: 1_500, co: 0, procurementItems: [] };

  const create = await refusal(api.call("post", `/budget-planning/aip/activities/${seeded.activityId}/expenditures`, body));
  expect(create).toContain("→ 400");
  expect(create).toContain("Account is required.");

  const update = await refusal(api.call("put", `/budget-planning/aip/expenditures/${seeded.lineId}`,
    { ...body, rowVersion: seeded.rowVersion }));
  expect(update).toContain("→ 400");
  expect(update).toContain("Account is required.");

  // Nothing was written: the activity still has only the line beforeAll saved, with its account.
  const lines = await api.call<{ id: number; accountId: number | null }[]>(
    "get", `/budget-planning/aip/activities/${seeded.activityId}/expenditures`);
  expect(lines).toHaveLength(1);
  expect(lines[0].accountId).toBe(seeded.accountId);
});
