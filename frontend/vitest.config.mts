import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

// PPDO-111 — the frontend's first test runner. Pure logic by default (no DOM): `src/**/*.test.ts`.
// PPDO-235: a test file can opt into a DOM with `// @vitest-environment jsdom`, and may import a
// .tsx component — Next's tsconfig keeps `jsx: preserve`, so tell esbuild to emit the React runtime.
export default defineConfig({
  resolve: { alias: { "@": fileURLToPath(new URL("./src", import.meta.url)) } },
  esbuild: { jsx: "automatic" },
  test: { include: ["src/**/*.test.ts"], environment: "node" },
});
