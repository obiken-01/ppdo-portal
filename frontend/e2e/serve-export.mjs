// PPDO-300 — serves the static export (`out/`, from `next build`) for a full local Playwright run.
//
// Why not `next dev`: it compiles each page on its first visit and drops pages it has not served
// for a while, then compiles them again. Under a full run (4 workers) those compiles held every
// request for seconds at a time, and tests failed on timeouts that had nothing to do with them.
// The export is what Azure Static Web Apps serves, so it is also closer to UAT.
//
// It copies the two SWA rules the app relies on (frontend/staticwebapp.config.json):
//   /path → path.html (or path/index.html), and anything else → index.html with 200.
// The production build registers a service worker (/sw.js). `page.route` cannot see requests that
// go through one, so playwright.config.ts sets `serviceWorkers: "block"`. Don't answer /sw.js with a
// 404 here instead: Chrome logs that as a console error, and the smoke specs fail on it.
//
// Usage (from frontend/):  node e2e/serve-export.mjs [port]     default port 3000
import { createReadStream, existsSync, statSync } from "node:fs";
import { createServer } from "node:http";
import { extname, join, normalize, resolve } from "node:path";

const root = resolve(process.cwd(), "out");
const port = Number(process.argv[2] ?? 3000);
if (!existsSync(root)) {
  console.error(`No ${root}. Run \`npx next build\` first (with NEXT_PUBLIC_API_BASE_URL set).`);
  process.exit(1);
}

const TYPES = {
  ".html": "text/html; charset=utf-8", ".txt": "text/plain; charset=utf-8", ".js": "text/javascript",
  ".css": "text/css", ".json": "application/json", ".webmanifest": "application/manifest+json",
  ".png": "image/png", ".jpg": "image/jpeg", ".svg": "image/svg+xml", ".ico": "image/x-icon",
  ".webp": "image/webp", ".woff2": "font/woff2", ".xml": "application/xml",
};

const isFile = (p) => existsSync(p) && statSync(p).isFile();

function resolveFile(pathname) {
  const rel = normalize(decodeURIComponent(pathname)).replace(/^([/\\])+/, "");
  const base = join(root, rel);
  if (!base.startsWith(root)) return null; // no ../ out of out/
  for (const candidate of [base, `${base}.html`, join(base, "index.html")])
    if (isFile(candidate)) return candidate;
  return null;
}

createServer((req, res) => {
  const { pathname } = new URL(req.url ?? "/", "http://localhost");
  const file = resolveFile(pathname) ?? join(root, "index.html");
  res.writeHead(200, { "Content-Type": TYPES[extname(file)] ?? "application/octet-stream" });
  createReadStream(file).pipe(res);
}).listen(port, () => console.log(`Serving ${root} on http://localhost:${port}`));
