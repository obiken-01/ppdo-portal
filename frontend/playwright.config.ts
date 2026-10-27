import { defineConfig, devices } from "@playwright/test";
import { PERSONAS } from "./e2e/personas";

// PPDO-258 — Playwright foundation. Chromium only; read-only specs until the suite proves stable.
// Locally: backend `func start` + `npm run dev`, then `npm run e2e`. Against UAT: `npm run e2e:uat`.
const isCI = !!process.env.CI;

// Local escape hatch for a preinstalled browser whose build doesn't match this Playwright version.
// Never set in CI, where `playwright install` provides the matching one.
const executablePath = process.env.E2E_CHROMIUM_PATH || undefined;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: isCI,
  retries: isCI ? 1 : 0,
  reporter: [["html", { open: "never" }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? "http://localhost:3000",
    trace: "on-first-retry",
  },
  projects: [
    {
      name: "setup",
      testMatch: /auth\.setup\.ts/,
      use: { ...devices["Desktop Chrome"], launchOptions: { executablePath } },
    },
    {
      name: "public",
      testMatch: /public\.spec\.ts/,
      use: { ...devices["Desktop Chrome"], launchOptions: { executablePath } },
    },
    // Signed-in specs (smoke, journeys) belong in per-persona projects that depend on "setup".
    // Each persona with credentials gets its own storageState; unconfigured ones are left out.
    // workers: 1 — a user has one live refresh token, so one persona's tests can't run side by
    // side. Different personas still run in parallel. See e2e/fixtures.ts.
    ...PERSONAS.filter((p) => p.configured).map((p) => ({
      name: `as-${p.key}`,
      testIgnore: [/auth\.setup\.ts/, /public\.spec\.ts/],
      dependencies: ["setup"],
      workers: 1,
      use: {
        ...devices["Desktop Chrome"],
        storageState: p.storageState,
        launchOptions: { executablePath },
      },
    })),
  ],
});
