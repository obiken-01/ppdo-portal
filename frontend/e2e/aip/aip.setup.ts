import { test as setup } from "@playwright/test";
import { seedAipWorld } from "./seed";

// Runs before the AIP journeys (project "aip-setup"). See seed.ts for what it builds.
setup("seed the E2E office for the AIP journeys", async ({ request }) => {
  setup.skip(!process.env.E2E_SEED_EMAIL || !process.env.E2E_SEED_PASSWORD,
    "E2E_SEED_EMAIL / E2E_SEED_PASSWORD not set");
  setup.setTimeout(120_000);
  await seedAipWorld(request);
});
