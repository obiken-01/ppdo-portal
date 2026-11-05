import { readFileSync } from "node:fs";
import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";
import { meFrom, reportOfficesAnswer } from "./helpers";
import { hasAmount, readWorkbook } from "./xlsx";

// PPDO-271 item 3 — Excel exports on the Report page: the file arrives as an .xlsx and its grand
// total is the one the screen shows. Read-only (it previews and downloads, nothing is saved), so it
// runs on whatever the environment holds: the first office the picker offers.
//   • initial WFP (FY 2028, read off the consolidated AIP) · GRAND-TOTAL "Total Appropriation"
//   • PPMP (FY 2028) · every "TOTAL — <fund>" row
//   • WFP of an earlier year (FY 2027) · skipped where no office has entered one
// Runs for personas that may choose the office (SuperAdmin, Admin); the others skip. The PR report
// export is in e2e/inventory/ppdo-271-inventory-bad-paths.spec.ts.

interface Me { role: string; canManagePpdoAllocation?: boolean }

// A preview and an export each wait on the API, and the preview alone is allowed 30 s; the default
// 30 s test timeout ran out in a loaded full run (PPDO-300).
test.describe.configure({ timeout: 90_000 });

const AMOUNT = /\d{1,3}(?:,\d{3})*\.\d{2}/g;
const toNumber = (s: string) => Number(s.replace(/,/g, ""));

/** Opens the report for a type and year, picks the first office the picker offers, and previews it. */
async function preview(page: Page, type: "WFP" | "PPMP", fiscalYear: number): Promise<string | null> {
  const meResponse = page.waitForResponse((r) => /\/api\/auth\/me$/.test(r.url()) && r.ok());
  const officesAnswer = reportOfficesAnswer(page, fiscalYear);
  await page.goto(`/budget-planning/report?type=${type}&fiscalYear=${fiscalYear}`);
  // The page's own rule for who picks the office (report/page.tsx `canBypassDivision`). Everyone it
  // lets pick also gets the year's office list, so only they wait for it (PPDO-300: the picker is
  // empty and disabled until that list arrives, which read as "no office" or "cannot choose").
  // Decided before anything on the page is checked: a guest-office account may be sent elsewhere.
  const me = await meFrom<Me>(await meResponse);
  if (!(me.role === "SuperAdmin" || me.role === "Admin" || me.canManagePpdoAllocation === true))
    return "this account cannot choose the office";
  await expect(page.getByRole("heading", { level: 1, name: "Report" })).toBeVisible();
  await officesAnswer;
  await page.waitForLoadState("networkidle").catch(() => {});
  const office = page.locator("select").filter({ has: page.locator("option", { hasText: /select office|No office|Loading offices/ }) });
  await expect(office.locator("option", { hasText: "Loading offices" })).toHaveCount(0);
  if (!(await office.isEnabled())) return "this account cannot choose the office";
  if ((await office.locator("option:not([value=''])").count()) === 0) return `no office has a FY ${fiscalYear} ${type}`;
  await office.selectOption({ index: 1 });
  await page.getByRole("button", { name: "Generate Preview" }).click();
  await expect(page.getByRole("button", { name: "Export to Excel" })).toBeVisible({ timeout: 30_000 });
  await page.waitForLoadState("networkidle").catch(() => {});
  return null;
}

async function exported(page: Page) {
  const download = page.waitForEvent("download");
  await page.getByRole("button", { name: "Export to Excel" }).click();
  const file = await download;
  expect(file.suggestedFilename(), "the export is an Excel file").toMatch(/\.xlsx$/);
  return readWorkbook(readFileSync((await file.path())!));
}

/** Lines of the page's text that start with `label`, tab-separated cells as the table renders. */
const rowsStartingWith = async (page: Page, label: RegExp) =>
  (await page.getByRole("main").innerText()).split("\n").filter((l) => label.test(l.replace(/^\t+/, "")));

test.describe("PPDO-271 report exports", () => {
  test("initial WFP (FY 2028) exports an .xlsx holding the grand total on screen", async ({ page }) => {
    test.skip((await preview(page, "WFP", 2028)) !== null, "no office to export here");
    const grand = (await rowsStartingWith(page, /^GRAND-TOTAL\t/))[0];
    expect(grand, "the preview shows a GRAND-TOTAL row").toBeTruthy();
    const total = toNumber(grand.match(AMOUNT)![0]);
    expect(total).toBeGreaterThan(0);

    const sheets = await exported(page);
    expect(sheets.length).toBeGreaterThan(0);
    expect(hasAmount(sheets, total), `the workbook holds the grand total ${total}`).toBe(true);
  });

  test("PPMP (FY 2028) exports an .xlsx holding each fund total on screen", async ({ page }) => {
    test.skip((await preview(page, "PPMP", 2028)) !== null, "no office to export here");
    const totals = await rowsStartingWith(page, /^TOTAL — /);
    expect(totals.length, "the preview shows at least one fund total").toBeGreaterThan(0);

    const sheets = await exported(page);
    for (const row of totals) {
      const total = toNumber(row.match(AMOUNT)![0]);
      expect(hasAmount(sheets, total), `the workbook holds "${row.split("\t")[0]}" = ${total}`).toBe(true);
    }
  });

  test("WFP of an entered year (FY 2027) exports an .xlsx holding the grand total on screen", async ({ page }) => {
    test.skip((await preview(page, "WFP", 2027)) !== null, "no office to export here");
    const grand = (await rowsStartingWith(page, /^GRAND-TOTAL\t/))[0];
    test.skip(!grand, "the first FY 2027 office has no WFP lines");
    const total = toNumber(grand.match(AMOUNT)![0]);

    const sheets = await exported(page);
    expect(hasAmount(sheets, total), `the workbook holds the grand total ${total}`).toBe(true);
  });
});
