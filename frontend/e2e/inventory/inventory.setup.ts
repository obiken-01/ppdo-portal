import { test as setup } from "@playwright/test";
import { assertSeedIsNotPersona } from "../aip/seed";
import { seedInventoryWorld } from "./seed";

// Runs before the inventory journey (project "inventory-setup"). See seed.ts for what it builds.
setup("seed the E2E inventory division", async ({ request }) => {
  setup.skip(!process.env.E2E_SEED_EMAIL || !process.env.E2E_SEED_PASSWORD,
    "E2E_SEED_EMAIL / E2E_SEED_PASSWORD not set");
  assertSeedIsNotPersona();
  setup.setTimeout(120_000);
  await seedInventoryWorld(request);
});
