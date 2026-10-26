import { expect, test } from "@playwright/test";
import { signIn, world } from "./session";
import { resetAipOffice } from "./reset";
import { addActivity, divisionFigure, openEntry, openReview, pickActivity, sendToPpdo, submitDivision } from "./entry";

// PPDO-259 — the AIP review round trip (v1.8.0 Phases 3–4), on the seeded E2E office:
//   encoders add activities and submit their divisions → the department head sends the office to
//   PPDO → the PPDO reviewer comments and returns it → the encoder sees the notice and the comment,
//   resubmits → the head resends → the reviewer accepts.
// Same flow as PPDO-195 T5, T9 and T10. Steps run in order and share the office's state.

test.describe.configure({ mode: "serial", timeout: 120_000 });

// Unique per run: comments on deleted rows are kept (PPDO-206), so a repeated text would match an old one.
const RUN = `E2E ${new Date().toISOString().slice(5, 19).replace("T", " ")} ${Math.random().toString(36).slice(2, 6)}`;
const ACTIVITY_A = `${RUN} office supplies`;
const ACTIVITY_B = `${RUN} collection drive`;
const COMMENT = `${RUN}: please split the supplies by quarter.`;
const REPLY = `${RUN}: split into four quarterly lines.`;

test.beforeAll(async ({ request }) => {
  await resetAipOffice(request);
});

test("encoder A adds an activity and submits division A", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);

  await addActivity(page, {
    program: /E2E PROGRAM ONE/, project: `${RUN} project A`, title: ACTIVITY_A, account: "Office Supplies", mooe: 150_000,
  });
  await expect(divisionFigure(page, /^Encoded \(MOOE \+ CO\)/)).toHaveText("150.00");
  await expect(divisionFigure(page, /^Remaining/)).toHaveText("1,350.00");

  await submitDivision(page, "A");
  await close();
});

test("encoder B's ₱250,500 counts as ₱251,000 against the ceiling; B submits", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderB", baseURL);
  await openEntry(page);

  await addActivity(page, {
    program: /E2E PROGRAM TWO/, project: `${RUN} project B`, title: ACTIVITY_B, account: "Office Supplies", mooe: 250_500,
  });
  // FY2028+ ceiling rule: MOOE + CO, each activity rounded UP to the thousand, PS exempt. The
  // figure is office-wide against the office's GF ceiling: A's 150 + B's 250.5 → 251 = 401.
  await expect(divisionFigure(page, /^Encoded \(MOOE \+ CO\)/)).toHaveText("401.00");
  await expect(divisionFigure(page, /^Remaining/)).toHaveText("1,099.00");

  await submitDivision(page, "B");
  await close();
});

test("the department head sees both divisions submitted and sends the office to PPDO", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "head", baseURL);
  await openEntry(page);
  await expect(page.getByText("2 of 2 submitted")).toBeVisible();
  await expect(divisionFigure(page, /^Encoded \(MOOE \+ CO\)/)).toHaveText("401.00");

  await sendToPpdo(page);
  await close();
});

test("the PPDO reviewer comments on an activity and sends the AIP back", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "reviewer", baseURL);
  await openReview(page);
  await expect(page.getByText("With PPDO — awaiting your decision")).toBeVisible();

  await pickActivity(page, /E2E PROGRAM ONE/, /project A/, /office supplies/);
  await page.getByRole("button", { name: /Comment$/ }).click();
  await page.getByPlaceholder("Ask for a change on this row…").fill(COMMENT);
  await page.getByRole("button", { name: "Comment", exact: true }).click();
  await expect(page.getByPlaceholder("Ask for a change on this row…")).toHaveValue("");
  await expect(page.locator("p", { hasText: COMMENT }).last()).toBeVisible();

  await page.getByRole("button", { name: "Send back" }).click();
  await page.getByRole("dialog").getByRole("button", { name: "Send back" }).click();
  await expect(page.getByRole("button", { name: "Accept" })).toBeHidden();
  await page.waitForLoadState("networkidle");
  await close();
});

test("encoder A sees the return and the comment, replies, and resubmits", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  await expect(page.getByRole("status").filter({ hasText: "PPDO sent this back for changes" })).toBeVisible();

  // The bell carries the comment itself (PPDO-221/222).
  await page.getByRole("button", { name: /^Notifications/ }).click();
  await expect(page.getByRole("region", { name: "Notifications" }).getByRole("link", { name: new RegExp(escape(COMMENT)) })).toBeVisible();
  await page.keyboard.press("Escape");

  await pickActivity(page, /E2E PROGRAM ONE/, /project A/, /office supplies/);
  // The activity's thread starts collapsed behind its "💬 N unresolved" chip.
  await page.getByRole("button", { name: /^💬\d+ unresolved$/ }).click();
  const thread = page.locator("p", { hasText: COMMENT }).last();
  await expect(thread).toBeVisible();
  await page.getByRole("button", { name: "Reply", exact: true }).click();
  await page.getByPlaceholder("Write a reply…").fill(REPLY);
  await page.getByRole("button", { name: "Reply", exact: true }).click();
  await expect(page.locator("p", { hasText: REPLY })).toBeVisible();

  await submitDivision(page, "A");
  await close();
});

test("encoder B resubmits division B (a PPDO return reopens every division)", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderB", baseURL);
  await openEntry(page);
  await submitDivision(page, "B");
  await close();
});

test("the department head sends it to PPDO again", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "head", baseURL);
  await openEntry(page);
  await expect(page.getByText("2 of 2 submitted")).toBeVisible();
  await sendToPpdo(page);
  await close();
});

test("the reviewer sees the office's reply and accepts the AIP", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "reviewer", baseURL);
  await openReview(page);
  await pickActivity(page, /E2E PROGRAM ONE/, /project A/, /office supplies/);
  await page.getByRole("button", { name: /^💬\d+ unresolved$/ }).click();
  await expect(page.locator("p", { hasText: REPLY })).toBeVisible();

  await page.getByRole("button", { name: "Accept" }).click();
  await page.getByRole("dialog", { name: "Accept into the consolidated AIP?" }).getByRole("button", { name: "Accept" }).click();
  await expect(page.getByText("With the consolidated AIP — already accepted", { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Re-open and send back" })).toBeVisible();
  await close();
});

function escape(text: string): string {
  return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
