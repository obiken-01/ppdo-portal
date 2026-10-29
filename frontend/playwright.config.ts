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
    // Off in CI: a trace keeps typed values (the login password) and request bodies and tokens, and
    // the E2E (UAT) workflow uploads the report as an artifact of a PUBLIC repo. Locally it stays useful.
    trace: isCI ? "off" : "on-first-retry",
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
      testIgnore: [/auth\.setup\.ts/, /public\.spec\.ts/, /[\\/]aip[\\/]/, /[\\/]inventory[\\/]/],
      dependencies: ["setup"],
      workers: 1,
      use: {
        ...devices["Desktop Chrome"],
        storageState: p.storageState,
        launchOptions: { executablePath },
      },
    })),
    // PPDO-259 — AIP journeys. They sign in as the accounts aip-setup creates in its own E2E
    // office (e2e/aip/seed.ts), not as the personas, and change data, so they run one at a time.
    {
      name: "aip-setup",
      testMatch: /aip[\\/]aip\.setup\.ts/,
      teardown: "aip-teardown",
    },
    {
      // Opt-in (E2E_AIP_CLEANUP=1): takes the E2E office back out of the AIP after the journeys.
      name: "aip-teardown",
      testMatch: /aip[\\/]aip\.teardown\.ts/,
    },
    {
      name: "aip",
      testMatch: /aip[\\/].*\.spec\.ts/,
      dependencies: ["aip-setup"],
      workers: 1,
      use: { ...devices["Desktop Chrome"], launchOptions: { executablePath } },
    },
    // PPDO-259 — inventory journey, in its own PPDO division "E2E Inventory (test)" (e2e/inventory/seed.ts).
    {
      name: "inventory-setup",
      testMatch: /inventory[\\/]inventory\.setup\.ts/,
    },
    {
      name: "inventory",
      testMatch: /inventory[\\/].*\.spec\.ts/,
      dependencies: ["inventory-setup"],
      workers: 1,
      use: { ...devices["Desktop Chrome"], launchOptions: { executablePath } },
    },
  ],
});
