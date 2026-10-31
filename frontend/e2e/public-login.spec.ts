import { expect, test } from "@playwright/test";

// PPDO-271 item 2 — a failed sign-in shows the error and stays on /login.
//
// Needs no persona: it tries a username that does not exist. The server answers it with the same
// message as a wrong password for a real account, so the screen is the same, and no real account
// ever collects a failed attempt toward the login rate limit. One attempt per run.

test("a wrong sign-in shows the error and stays on /login", async ({ page }) => {
  const logins: number[] = [];
  page.on("response", (r) => {
    if (r.request().method() === "POST" && /\/auth\/login$/.test(r.url())) logins.push(r.status());
  });

  await page.goto("/login");
  await page.getByLabel("Username").fill(`e2e.nobody.${Date.now()}`);
  await page.getByLabel("Password").fill("not-the-password-1A!");
  await page.getByRole("button", { name: "Sign In" }).click();

  await expect(page.getByText("Invalid username or password. Please try again.")).toBeVisible({ timeout: 20_000 });
  expect(new URL(page.url()).pathname).toBe("/login");
  expect(logins, "exactly one login attempt, answered 401").toEqual([401]);
  // The form is still there for another try, and the typed password is not echoed anywhere.
  await expect(page.getByRole("button", { name: "Sign In" })).toBeEnabled();
  await expect(page.getByLabel("Password")).toHaveAttribute("type", "password");
});
