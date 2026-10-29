import { expect, test as setup } from "@playwright/test";
import { PERSONAS } from "./personas";

// Setup project (runs before the signed-in projects): log in through the real form, once per
// configured persona, and save storageState. The httpOnly refresh cookie and the localStorage
// session flag are captured with it; the access token is in memory only, so every test starts
// with the same silent refresh a real reload does.

for (const persona of PERSONAS) {
  setup(`authenticate ${persona.key}`, async ({ page }) => {
    setup.skip(!persona.configured, `${persona.emailVar} / ${persona.passwordVar} not set`);

    await page.goto("/login");
    await page.getByLabel("Username").fill(persona.email!);
    await page.getByLabel("Password").fill(persona.password!);
    await page.getByRole("button", { name: "Sign In" }).click();

    // A password login ends in router.replace() away from /login (landing path depends on role).
    await expect(page).not.toHaveURL(/\/login/, { timeout: 60_000 });

    await page.context().storageState({ path: persona.storageState });
  });
}
