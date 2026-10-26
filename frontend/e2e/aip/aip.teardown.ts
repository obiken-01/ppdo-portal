import { test as teardown } from "@playwright/test";
import { removeAipOffice } from "./reset";

// Runs after the AIP journeys (project "aip-teardown"), only with E2E_AIP_CLEANUP=1. Meant for UAT,
// where an accepted E2E office would otherwise show in the consolidated AIP until the next run.
teardown("take the E2E office back out of the AIP", async ({ request }) => {
  teardown.skip(process.env.E2E_AIP_CLEANUP !== "1", "E2E_AIP_CLEANUP is not 1");
  await removeAipOffice(request);
});
