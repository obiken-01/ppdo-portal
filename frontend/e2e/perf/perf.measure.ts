import { mkdirSync, writeFileSync } from "node:fs";
import { test } from "@playwright/test";
import { measureApis } from "./api-measure";
import { pageTargets, measurePages } from "./page-measure";
import { toMarkdown, type PerfResult } from "./report";
import { signIn } from "./session";

// PPDO-257 §3 + §4 — a measuring tool, not an assertion suite. It records numbers and never fails
// because a page is slow. Run it with `npm run e2e:perf` (see e2e/README.md, "Perf").
// Output: e2e-results/perf/perf-<timestamp>.md and .json (git-ignored), and the markdown on stdout.

const OUT_DIR = "e2e-results/perf";

test("PPDO-257 perf measurement", async ({ browser }, testInfo) => {
  test.setTimeout(25 * 60_000);
  const baseURL = testInfo.project.use.baseURL;
  const host = baseURL ? new URL(baseURL).hostname : "localhost";
  const env = process.env.E2E_PERF_ENV_LABEL || (["localhost", "127.0.0.1", "[::1]"].includes(host) ? "local" : "UAT");

  const session = await signIn(browser, baseURL);
  try {
    const { rows: apis, proposalId } = await test.step("§3 API responses", () => measureApis(session));

    const pageRuns = Math.max(1, Number(process.env.E2E_PERF_PAGE_RUNS) || 1);
    const pages = await test.step("§4 throttled pass", () => measurePages(session, pageTargets(proposalId), pageRuns));

    const result: PerfResult = {
      generatedAt: new Date().toISOString().slice(0, 16).replace("T", " ") + " UTC",
      env,
      persona: session.persona.key,
      personaNote: session.personaNote,
      apis,
      pages,
      pageRuns,
    };
    const markdown = toMarkdown(result);

    mkdirSync(OUT_DIR, { recursive: true });
    const stamp = new Date().toISOString().replace(/[:.]/g, "-").slice(0, 19);
    writeFileSync(`${OUT_DIR}/perf-${stamp}.md`, markdown);
    writeFileSync(`${OUT_DIR}/perf-${stamp}.json`, JSON.stringify(result, null, 2));
    console.log(`\n${markdown}\n(written to frontend/${OUT_DIR}/perf-${stamp}.md and .json)`);
  } finally {
    await session.context.close();
  }
});
