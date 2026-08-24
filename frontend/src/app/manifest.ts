import type { MetadataRoute } from "next";
import { SITE_NAME } from "@/lib/seo";

// Static export quirk — same as robots.ts/sitemap.ts: without this, `next dev`
// treats a metadata route as dynamic and 500s asking for generateStaticParams(),
// even though `next build` (the config actually used for deployment) always
// emits it statically fine.
export const dynamic = "force-static";

/**
 * Web app manifest — makes the portal installable (RAL-234 follow-up, PWA Phase 1).
 *
 * Emitted at build time as /manifest.webmanifest; Next injects the
 * <link rel="manifest"> into every page automatically. Verified working under
 * `output: "export"` — see docs/PWA_Feasibility_Study.md §10.
 *
 * Icons are generated from the official PPDO logo by
 * `scripts/generate-pwa-icons.js`. Both a plain and a `maskable` 512 are
 * declared: Android crops icons to a device-chosen shape and would clip this
 * logo's outer ring text, so the maskable variant is pre-padded to the safe zone.
 *
 * start_url is /home rather than /login: the access token is in-memory only, so every
 * launch runs a silent refresh anyway — a returning user with a valid session lands
 * straight in the app, and an expired one is redirected to /login by the portal auth
 * guard.
 *
 * It points at /home, not a real page: start_url is a SINGLE FIXED VALUE baked into the
 * installed app and shared by every user, so it cannot be per-user. /home resolves the
 * user's actual landing page and replaces itself (RAL-264). It previously pointed at
 * /dashboard, which office users cannot open — they were launched into a page the
 * office gate ejected them from on arrival.
 */
export default function manifest(): MetadataRoute.Manifest {
  return {
    id: "/",
    name: SITE_NAME,
    short_name: "PPDO Portal",
    description:
      "Portal of the Provincial Planning and Development Office, Occidental Mindoro — budget planning, inventory monitoring, and office resources.",
    start_url: "/home",
    scope: "/",
    display: "standalone",
    orientation: "any",
    background_color: "#ffffff",
    // green-700 — the sidebar and login header colour, so the browser/OS chrome
    // blends with the app's own frame rather than cutting against it.
    theme_color: "#196638",
    categories: ["government", "productivity", "business"],
    icons: [
      { src: "/icons/icon-192.png", sizes: "192x192", type: "image/png", purpose: "any" },
      { src: "/icons/icon-512.png", sizes: "512x512", type: "image/png", purpose: "any" },
      { src: "/icons/icon-maskable-512.png", sizes: "512x512", type: "image/png", purpose: "maskable" },
    ],
  };
}
