import { expect, test } from "@playwright/test";

// Needs no backend and no credentials — proves the Playwright wiring works. Not the smoke suite.

test("landing page renders its hero", async ({ page }) => {
  await page.goto("/");
  await expect(
    page.getByRole("heading", { level: 1, name: "Provincial Planning and Development Office" }),
  ).toBeVisible();
});

test("login page renders the form", async ({ page }) => {
  await page.goto("/login");
  await expect(page.getByLabel("Username")).toBeVisible();
  await expect(page.getByLabel("Password")).toBeVisible();
  await expect(page.getByRole("button", { name: /Sign In|Loading/ })).toBeVisible();
});
