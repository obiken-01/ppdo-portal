import { expect, test } from "@playwright/test";
import { Api, readWorld, type Json } from "./seed";
import { signIn } from "./session";

// PPDO-271 item 1 — the Funding Sources form on its bad paths. The E2E office's department head
// holds the office-setup grant, so the page shows its own-office view and every fund written here
// belongs to the E2E office only (unique E2E-<timestamp> codes and names; a fund can be hidden but
// not deleted, so a run leaves one inactive-able fund behind, in that office).
//   • empty form: "Code and name are required.", nothing sent;
//   • a code that already exists: the message under Code, nothing sent;
//   • a name that already exists under another code: refused, and still only one fund by that name.

test.describe.configure({ mode: "serial", timeout: 120_000 });

const STAMP = Date.now().toString(36).toUpperCase();
const SEEDED = { code: `E2E-${STAMP}`, name: `E2E-${STAMP} fund (automated)` };

async function head(request: Parameters<typeof Api.login>[0]): Promise<Api> {
  const a = readWorld().accounts.head;
  return (await Api.login(request, a.username, a.password))!;
}

const named = async (api: Api, needle: string) =>
  (await api.call<Json[]>("get", "/config/funding-sources", undefined, { search: needle, active: "all" }))
    .filter((f) => String(f.name).includes(needle) || String(f.code).includes(needle));

test.beforeAll(async ({ request }) => {
  const api = await head(request);
  if ((await named(api, SEEDED.code)).length === 0)
    await api.call("post", "/config/funding-sources", { ...SEEDED, description: null, color: null, isActive: true, aliases: null });
});

async function openForm(page: import("@playwright/test").Page) {
  await page.goto("/config/funding-sources");
  await expect(page.getByRole("heading", { level: 1, name: "Funding Sources" })).toBeVisible();
  await page.waitForLoadState("networkidle");
  await page.getByRole("button", { name: /Add Funding Source/ }).click();
  await expect(page.getByRole("heading", { name: "Add Funding Source" })).toBeVisible();
}

test("an empty form is refused next to the form and nothing is sent", async ({ browser, baseURL, request }) => {
  const api = await head(request);
  const before = (await api.call<Json[]>("get", "/config/funding-sources", undefined, { active: "all" })).length;
  const { page, close } = await signIn(browser, "head", baseURL);
  const posts: string[] = [];
  page.on("request", (r) => { if (r.method() === "POST" && /\/config\/funding-sources$/.test(r.url())) posts.push(r.url()); });

  await openForm(page);
  await page.getByRole("button", { name: "Create Funding Source" }).click();
  await expect(page.getByText("Code and name are required.")).toBeVisible();
  // Name alone is not enough.
  await page.getByPlaceholder("General Fund").fill(`E2E-${STAMP} only a name`);
  await page.getByRole("button", { name: "Create Funding Source" }).click();
  await expect(page.getByText("Code and name are required.")).toBeVisible();

  expect(posts, "an empty form must not be sent").toEqual([]);
  await close();
  expect((await api.call<Json[]>("get", "/config/funding-sources", undefined, { active: "all" })).length).toBe(before);
});

test("a code that already exists is refused under the Code field and nothing is sent", async ({ browser, baseURL, request }) => {
  const api = await head(request);
  const { page, close } = await signIn(browser, "head", baseURL);
  const posts: string[] = [];
  page.on("request", (r) => { if (r.method() === "POST" && /\/config\/funding-sources$/.test(r.url())) posts.push(r.url()); });

  await openForm(page);
  await page.getByPlaceholder("GF").fill(SEEDED.code);
  await page.getByPlaceholder("General Fund").fill(`E2E-${STAMP} second name`);
  await page.getByPlaceholder("General Fund").click(); // moves focus off Code: the check runs on blur
  await expect(page.getByText("A funding source with this code already exists.")).toBeVisible();
  await page.getByRole("button", { name: "Create Funding Source" }).click();
  await expect(page.getByRole("heading", { name: "Add Funding Source" })).toBeVisible();

  expect(posts, "a duplicate code must not be sent").toEqual([]);
  await close();
  expect(await named(api, SEEDED.code)).toHaveLength(1);
});

test("a name that already exists under a new code is refused, and only one fund keeps the name", async ({ browser, baseURL, request }) => {
  // PPDO-282: the app saves the second fund. Delete this line when that ticket is decided.
  test.fail();
  const api = await head(request);
  const { page, close } = await signIn(browser, "head", baseURL);

  await openForm(page);
  await page.getByPlaceholder("GF").fill(`E2E-${STAMP}B`);
  await page.getByPlaceholder("General Fund").fill(SEEDED.name);
  await page.getByRole("button", { name: "Create Funding Source" }).click();
  // Refused: the modal stays open with a reason. (If this fails because the fund was created, that
  // is a real finding: the server is not enforcing unique names.)
  await expect(page.getByRole("heading", { name: "Add Funding Source" })).toBeVisible();
  await expect(page.locator("p.text-red-600, p.text-red-700, [role=alert]").filter({ hasText: /\S/ }).first()).toBeVisible();
  await close();
  expect(await named(api, SEEDED.name)).toHaveLength(1);
});
