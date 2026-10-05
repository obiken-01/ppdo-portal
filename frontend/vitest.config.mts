import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

// PPDO-111 — the frontend's first test runner. Pure logic only (no DOM): `src/**/*.test.ts`.
export default defineConfig({
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  test: { include: ["src/**/*.test.ts"], environment: "node" },
});
