import { expect, test } from "@playwright/test";
import { signIn } from "./session";
import { resetAipOffice } from "./reset";
import {
  addActivity, divisionFigure, editFirstLineMooe, openEntry, openReview, pickActivity, sendToPpdo, submitDivision,
} from "./entry";

// PPDO-259 — v1.9 rules on the seeded E2E office (GF ceiling ₱1.5M; A allocated ₱900K, B ₱500K):
//   PPDO-237  a division over its allocation is warned, never blocked
//   PPDO-146  over the office ceiling: the encoder may still submit; the head may not send to PPDO
//   PPDO-214  a PPDO reviewer edits amounts on a submitted AIP, with a reason
//   PPDO-213  the office sees "PPDO made N changes" and can mark it seen

test.describe.configure({ mode: "serial", timeout: 120_000 });

const RUN = `E2E ${new Date().toISOString().slice(5, 19).replace("T", " ")} ${Math.random().toString(36).slice(2, 6)}`;
const REASON = `${RUN}: trimmed to division A's allocation.`;

test.beforeAll(async ({ request }) => {
  await resetAipOffice(request);
});

test("a division over its allocation is warned on submit, and can still submit (PPDO-237)", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  await addActivity(page, {
    program: /E2E PROGRAM ONE/, project: `${RUN} project A`, title: `${RUN} A over allocation`, account: "Office Supplies", mooe: 950_000,
  });
  await submitDivision(page, "A", ["E2EA is over its GF allocation by ₱50,000.00"]);
  await close();
});

test("over the office ceiling, the encoder is told it blocks the send, and may still submit (PPDO-146)", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderB", baseURL);
  await openEntry(page);
  await addActivity(page, {
    program: /E2E PROGRAM TWO/, project: `${RUN} project B`, title: `${RUN} B over ceiling`, account: "Office Supplies", mooe: 600_000,
  });
  await expect(divisionFigure(page, /^Encoded \(MOOE \+ CO\)/)).toHaveText("1,550.00");
  await expect(divisionFigure(page, /^Remaining/)).toHaveText("-50.00");
  await expect(page.getByText("Over ceiling — blocks sending to PPDO")).toBeVisible();

  await submitDivision(page, "B", [/₱50,000\.00 over the ceiling of ₱1,500,000\.00/, "E2EB is over its GF allocation by ₱100,000.00"]);
  await close();
});

test("the head cannot send while over the ceiling; fixing a line unblocks it", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "head", baseURL);
  await openEntry(page);
  await expect(page.getByRole("button", { name: "Send to PPDO" })).toBeDisabled();
  await expect(page.getByText("Over ceiling — blocks sending to PPDO")).toBeVisible();
  // The allocation overage is listed for the head too, and does not stop the send.
  await expect(page.getByText("This does not stop the send", { exact: false })).toBeVisible();

  // During department review the head may fix any division's work (AIP_Review_Spec decision 4).
  await pickActivity(page, /E2E PROGRAM TWO/, /project B/, /B over ceiling/);
  await editFirstLineMooe(page, 100_000);
  await expect(divisionFigure(page, /^Encoded \(MOOE \+ CO\)/)).toHaveText("1,050.00");
  await expect(page.getByRole("button", { name: "Send to PPDO" })).toBeEnabled();

  await sendToPpdo(page);
  await close();
});

test("the PPDO reviewer edits an amount, with a reason (PPDO-214)", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "reviewer", baseURL);
  await openReview(page);
  await pickActivity(page, /E2E PROGRAM ONE/, /project A/, /A over allocation/);

  await page.getByRole("button", { name: "Edit amounts" }).click();
  const dialog = page.getByRole("dialog", { name: "Edit amounts" });
  // Save needs a reason: it is sent with every line saved.
  await dialog.getByRole("textbox", { name: "Reason for the change *" }).fill(REASON);
  await editFirstLineMooe(dialog, 900_000);
  await expect(dialog.getByRole("cell", { name: "900.00" }).first()).toBeVisible();
  await dialog.getByRole("button", { name: "Close" }).last().click();

  // The activity shows the change once the dialog closes, and carries a "PPDO edit" comment.
  await expect(page.getByRole("row", { name: /^Office Supplies Expenses/ }).getByRole("cell").nth(2)).toHaveText("900.00");
  await expect(page.getByRole("button", { name: /PPDO edit/ })).toBeVisible();
  await close();
});

test("the office sees PPDO's change and its reason, and marks it seen (PPDO-213)", async ({ browser, baseURL }) => {
  const { page, close } = await signIn(browser, "encoderA", baseURL);
  await openEntry(page);
  const notice = page.getByRole("status").filter({ hasText: /PPDO made \d+ changes? to your AIP/ });
  await expect(notice).toBeVisible();
  await expect(divisionFigure(page, /^Encoded \(MOOE \+ CO\)/)).toHaveText("1,000.00");

  await pickActivity(page, /E2E PROGRAM ONE/, /project A/, /A over allocation/);
  await page.getByRole("button", { name: /PPDO edit/ }).click();
  await expect(page.getByText(REASON).first()).toBeVisible();

  await notice.getByRole("button", { name: "Mark as seen" }).click();
  await expect(notice).toBeHidden();
  await close();
});
