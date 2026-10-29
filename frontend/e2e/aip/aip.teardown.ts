import { test as teardown } from "@playwright/test";
import { removeAipOffice } from "./reset";
import { hasWorld } from "./seed";

// Runs after the AIP journeys (project "aip-teardown"), only with E2E_AIP_CLEANUP=1. Meant for UAT,
// where an accepted E2E office would otherwise show in the consolidated AIP until the next run.
teardown("take the E2E office back out of the AIP", async ({ request }) => {
  teardown.skip(process.env.E2E_AIP_CLEANUP !== "1", "E2E_AIP_CLEANUP is not 1");
  // The teardown runs even when aip-setup failed. With no world for this environment there is no
  // E2E office here to remove.
  teardown.skip(!hasWorld(), "aip-setup has not finished for this environment");
  await removeAipOffice(request);
});
