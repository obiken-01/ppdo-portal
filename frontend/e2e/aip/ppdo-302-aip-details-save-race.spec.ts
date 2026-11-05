import { expect, test, type Response, type Route } from "@playwright/test";
import { signIn } from "./session";
import {
  encoderApi, openNewLineWithAccount, openSeededActivity, runName, seedLine, type SeededLine,
} from "./line-fixture";

// PPDO-302 — an encoder clicked Save details on an activity, then saved an account line before the
// details save answered. Both write the activity's row (the line write recomputes its totals), and
// the line write came back as a 500 ("An unexpected error occurred…"). Now:
//   • the page holds the line's Save (and Delete) back until Save details answers, and says why;
//   • the API answers a line write that still loses that race with a 409, never a 500. It first
//     retries the totals once, so in practice the write simply succeeds.

test.describe.configure({ mode: "serial", timeout: 120_000 });

let seeded: SeededLine;

const isDetailsPut = (url: string, method: string) =>
  method === "PUT" && /\/budget-planning\/aip\/activities\/\d+\/details$/.test(url);
const isLinePost = (r: Response) =>
  r.request().method() === "POST" && /\/budget-planning\/aip\/activities\/\d+\/expenditures$/.test(r.url());

test.beforeAll(async ({ request, browser, baseURL }) => {
  test.setTimeout(120_000);
  seeded = await seedLine(request, browser, baseURL, runName("PPDO-302"));
});

test("a line's Save waits while Save details is still answering, then saves without a 500", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openSeededActivity(page, seeded);

  // Hold the details save back until the test lets it go: a slow connection, made deterministic.
  let release!: () => void;
  const held = new Promise<void>((resolve) => { release = resolve; });
  let detailsSent = false;
  await page.route(/\/budget-planning\/aip\/activities\/\d+\/details$/, async (route: Route) => {
    if (!isDetailsPut(route.request().url(), route.request().method())) return route.continue();
    detailsSent = true;
    await held;
    await route.continue();
  });
  const linePosts: string[] = [];
  page.on("request", (r) => {
    if (r.method() === "POST" && /\/expenditures$/.test(r.url())) linePosts.push(r.url());
  });

  // A new line, filled in and ready to save.
  const row = await openNewLineWithAccount(page);
  await row.getByRole("textbox", { name: "0.00" }).nth(1).fill("2500");
  const lineSave = row.getByRole("button", { name: "Save", exact: true });
  await expect(lineSave).toBeEnabled();

  // Save details, and it does not answer.
  await page.getByRole("button", { name: "Edit details" }).click();
  await page.getByRole("button", { name: "Save details" }).click();
  await expect.poll(() => detailsSent).toBe(true);

  // The line's Save waits, and the page says why. Nothing is sent.
  await expect(lineSave).toBeDisabled();
  await expect(page.getByText("Wait for the activity details to finish saving.")).toBeVisible();
  await lineSave.click({ force: true });
  expect(linePosts, "a line write was sent while the details save was in flight").toEqual([]);

  // The details save answers; the line's Save comes back and goes through.
  release();
  await expect(page.getByRole("button", { name: "Edit details" })).toBeVisible();
  await expect(page.getByText("Wait for the activity details to finish saving.")).toBeHidden();
  await expect(lineSave).toBeEnabled();
  const posted = page.waitForResponse(isLinePost);
  await lineSave.click();
  const res = await posted;
  expect(res.status(), await res.text()).toBe(201);
  await expect(page.getByText(/An unexpected error occurred/)).toBeHidden();
  await close();
});

test("the API never answers a line add that races a details save with a 500", async ({ request }) => {
  // The race itself, straight to the API: a line POST and a details PUT on one activity, several
  // rounds. Each may succeed or 409; a 500 is the bug. Red-tested: on the old backend this run
  // got 500s ("The row changed since it was loaded" in the host log).
  const api = await encoderApi(request);
  const linesUrl = `/budget-planning/aip/activities/${seeded.activityId}/expenditures`;
  const detailsUrl = `/budget-planning/aip/activities/${seeded.activityId}/details`;
  const line = { accountId: seeded.accountId, fundingSourceId: seeded.fundingSourceId, ps: 0, mooe: 100, co: 0, procurementItems: [] };
  type Written = { activityRowVersion: string | null };

  const outcome = (call: Promise<unknown>) => call.then(() => "ok", (e: Error) => e.message);
  // The PUT must carry the activity's current version to reach the database at all; a line add
  // hands it back.
  let version = (await api.call<Written>("post", linesUrl, line)).activityRowVersion;

  // The window is short: the PUT has to commit after the POST loaded the activity and before it
  // saves the totals. So the PUT goes out a little later each round, to sweep across it.
  const pause = (ms: number) => new Promise((r) => setTimeout(r, ms));
  const failures: string[] = [];
  for (let round = 0; round < 20; round++) {
    const [post, put] = await Promise.all([
      outcome(api.call("post", linesUrl, line)),
      pause(round * 3).then(() => outcome(api.call("put", detailsUrl, {
        name: seeded.title, esreCode: "ID", implementingOffice: null, startDate: null, endDate: null,
        expectedOutputs: `PPDO-302 round ${round}`, ccAdaptation: null, ccMitigation: null,
        ccTypologyCode: null, rowVersion: version,
      }))),
    ]);
    failures.push(...[put, post].filter((o) => o.includes("→ 500")));
    version = (await api.call<Written>("post", linesUrl, line)).activityRowVersion;
  }
  expect(failures).toEqual([]);
});
