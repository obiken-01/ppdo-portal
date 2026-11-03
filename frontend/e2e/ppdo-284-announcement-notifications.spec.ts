import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";

// PPDO-284: an announcement entry in the bell opens a modal with the announcement instead of
// navigating, and marks the entry read like any other.
//
// Read-only. The feed and the public announcement list are answered by the test, so no
// announcement is published and no notification row is written; every persona may run it.

const ID = "0e2e0284-0000-4000-8000-000000000284";
const TITLE = "E2E: office closed on Friday";

async function stubFeed(page: Page, published: boolean) {
  await page.route(/\/api\/notifications\/unread-count$/, (route) =>
    route.fulfill({ json: { data: { count: 1 }, error: null, message: null } }));
  await page.route(/\/api\/notifications\?/, (route) =>
    route.fulfill({
      json: {
        data: {
          items: [{
            id: 9_000_284, type: "Announcement", text: `New announcement: ${TITLE}`,
            link: `/notifications?announcement=${ID}`, createdAt: new Date().toISOString(), readAt: null,
          }],
          total: 1,
        },
        error: null, message: null,
      },
    }));
  await page.route(/\/api\/notifications\/9000284\/read$/, (route) => route.fulfill({ status: 204 }));
  await page.route(/\/api\/announcements$/, (route) =>
    route.request().method() === "GET"
      ? route.fulfill({
          json: published
            ? [{ id: ID, title: TITLE, content: "<p>Back on <strong>Monday</strong>.</p>", publishedAt: "2026-11-03T01:00:00Z" }]
            : [],
        })
      : route.fallback());
}

test.describe("PPDO-284 announcement in the notification bell", () => {
  test("opens in a modal, stays on the page, and is marked read", async ({ page }) => {
    await stubFeed(page, true);
    // Every persona may open My Account, so no guard redirects mid-test. Not /profile, which
    // redirects here and raced the URL read below.
    await page.goto("/account");
    await page.waitForLoadState("networkidle");
    const before = new URL(page.url()).pathname;

    await page.getByRole("button", { name: /^Notifications/ }).click();
    const markRead = page.waitForRequest(/\/api\/notifications\/9000284\/read$/);
    await page.getByRole("link", { name: new RegExp(TITLE) }).click();
    await markRead;

    const dialog = page.getByRole("dialog", { name: TITLE });
    await expect(dialog).toBeVisible();
    await expect(dialog).toContainText("Back on Monday.");
    await expect(dialog.locator("strong")).toHaveText("Monday");
    expect(new URL(page.url()).pathname).toBe(before);

    await page.keyboard.press("Escape");
    await expect(dialog).toBeHidden();
  });

  test("the entry's link opens the same modal on the full page", async ({ page }) => {
    await stubFeed(page, true);
    await page.goto(`/notifications?announcement=${ID}`);

    await expect(page.getByRole("dialog", { name: TITLE })).toContainText("Back on Monday.");
    await page.getByRole("button", { name: "Close" }).click();
    await expect(page).toHaveURL(/\/notifications$/);
  });

  test("an announcement no longer published says so, not a stale copy", async ({ page }) => {
    await stubFeed(page, false);
    await page.goto(`/notifications?announcement=${ID}`);

    await expect(page.getByRole("dialog")).toContainText("This announcement is no longer available.");
  });
});
