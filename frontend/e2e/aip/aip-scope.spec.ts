import { expect, test } from "@playwright/test";
import { Api, readWorld, type AipRole } from "./seed";
import { signIn } from "./session";
import { resetAipOffice } from "./reset";
import { addActivity, openEntry } from "./entry";

// PPDO-259 — scope leaks (PPDO-195 T1 and T6), on the seeded E2E office:
//   an office user asking for another office's data gets their own office, a 403 or a redirect,
//   never the other office's rows; an encoder cannot write to another division's activity.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E ${new Date().toISOString().slice(5, 19).replace("T", " ")} ${Math.random().toString(36).slice(2, 6)}`;
type Json = Record<string, unknown>;

async function as(request: Parameters<typeof Api.login>[0], role: AipRole): Promise<Api> {
  const a = readWorld().accounts[role];
  return (await Api.login(request, a.username, a.password))!;
}

test.beforeAll(async ({ request, browser, baseURL }) => {
  // Hooks keep the 30s default; describe.configure's timeout covers the tests only. Reset plus
  // an activity in the browser runs past 30s when the persona suites share the dev server.
  test.setTimeout(120_000);
  await resetAipOffice(request);
  // Division B needs one activity with a line for the division-lock check.
  const { page, close } = await signIn(browser, "encoderB", baseURL);
  await openEntry(page);
  await addActivity(page, {
    program: /E2E PROGRAM TWO/, project: `${RUN} project B`, title: `${RUN} B only`, account: "Office Supplies", mooe: 10_000,
  });
  await close();
});

test("API reads for another office never return that office's data", async ({ request }) => {
  const w = readWorld();
  const f = w.foreignOffice;
  const reads = [
    `/budget-planning/aip/${w.aipId}`,
    `/budget-planning/aip/${w.aipId}/offices/${f.id}/review`,
    `/budget-planning/dashboard/office?officeId=${f.id}`,
    `/budget-planning/aip/review/search?officeId=${f.id}&fiscalYear=2028`,
    `/budget-planning/ldip?officeId=${f.id}`,
    `/budget-planning/allocation/divisions?officeId=${f.id}&fiscalYear=2028&fundingSourceId=${w.gfId}`,
    `/budget-planning/wfp/expenditures?officeId=${f.id}&fiscalYear=2028`,
    `/budget-planning/aip/consolidated?fiscalYear=2028`,
  ];
  for (const role of ["encoderA", "head"] as const) {
    const a = w.accounts[role];
    const login = await request.post(`${w.apiUrl}/auth/login`, { data: { username: a.username, password: a.password } });
    const { accessToken } = (await login.json()) as { accessToken: string };
    for (const url of reads) {
      const res = await request.get(`${w.apiUrl}${url}`, { headers: { Authorization: `Bearer ${accessToken}` } });
      // 200 must be scoped to their own office; 400/403/404 are refusals.
      expect([200, 400, 403, 404], `${role} GET ${url}`).toContain(res.status());
      if (res.status() !== 200) continue;
      const body = await res.text();
      // Case-insensitive: AIP rows carry office names in upper case.
      expect(body.toLowerCase(), `${role} GET ${url} leaked ${f.name}`).not.toContain(f.name.toLowerCase());
      expect(body, `${role} GET ${url} leaked ref code ${f.refCode}`).not.toContain(`-${f.refCode}`);
    }
  }
});

test("another office's review screen and the AIP record list are closed to the encoder", async ({ browser, baseURL }) => {
  const w = readWorld();
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  for (const path of [
    `/budget-planning/aip/review?officeId=${w.foreignOffice.id}&fiscalYear=2028`,
    `/budget-planning/aip/detail?id=${w.aipId}`,
  ]) {
    await page.goto(path);
    await expect.poll(() => new URL(page.url()).pathname, { message: path, timeout: 20_000 }).not.toBe(path.split("?")[0]);
    await expect(page.getByText(w.foreignOffice.name)).toHaveCount(0);
  }
  await close();
});

test("division lock: encoder A sees only division A's programs and cannot add a line to B's activity", async ({ browser, baseURL, request }) => {
  const w = readWorld();
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  await page.getByPlaceholder("Search program…").click();
  await expect(page.getByRole("button", { name: /E2E PROGRAM ONE/ })).toBeVisible();
  await expect(page.getByRole("button", { name: /E2E PROGRAM TWO/ })).toHaveCount(0);
  await close();

  // Find B's activity and its line (as the head, who reads the whole office).
  const head = await as(request, "head");
  const detail = await head.call<Json>("get", `/budget-planning/aip/${w.aipId}`);
  const activity = (detail.offices as Json[]).filter((o) => o.officeId === w.officeId)
    .flatMap((o) => o.programs as Json[]).flatMap((p) => p.projects as Json[]).flatMap((p) => p.activities as Json[])
    .find((a) => String(a.name).includes(`${RUN} B only`));
  expect(activity, "division B's activity").toBeTruthy();
  const lines = await head.call<Json[]>("get", `/budget-planning/aip/activities/${activity!.id}/expenditures`);
  const line = { accountId: lines[0].accountId, fundingSourceId: lines[0].fundingSourceId, ps: 0, mooe: 1_000, co: 0 };
  const url = `${w.apiUrl}/budget-planning/aip/activities/${activity!.id}/expenditures`;

  const post = async (role: AipRole) => {
    const a = w.accounts[role];
    const login = await request.post(`${w.apiUrl}/auth/login`, { data: { username: a.username, password: a.password } });
    const { accessToken } = (await login.json()) as { accessToken: string };
    return request.post(url, { data: line, headers: { Authorization: `Bearer ${accessToken}` } });
  };

  // Control: the same line from B's own encoder is accepted, so A's refusal is about the division.
  const own = await post("encoderB");
  expect(own.status(), await own.text()).toBeLessThan(300);

  const other = await post("encoderA");
  expect(other.status(), "encoder A wrote to division B's activity").toBeGreaterThanOrEqual(400);
  expect(other.status()).not.toBe(500);
});
