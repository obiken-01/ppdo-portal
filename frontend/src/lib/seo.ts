/**
 * Shared SEO config (RAL-202).
 *
 * SITE_URL is the single source of truth for every absolute URL used in
 * metadata — sitemap.xml, robots.txt, Open Graph, canonical tags. Swapping to
 * a custom domain later is a one-line env var change here, not a per-file edit.
 *
 * Baked in at build time via NEXT_PUBLIC_SITE_URL (see .github/workflows/deploy.yml
 * for production; .env.local for local dev). Falls back to the current production
 * Azure Static Web Apps URL so this still works if the env var is ever unset.
 */
export const SITE_URL =
  process.env.NEXT_PUBLIC_SITE_URL ?? "https://jolly-sky-0e3a2e310.7.azurestaticapps.net";

export const SITE_NAME = "PPDO Portal — Occidental Mindoro";
