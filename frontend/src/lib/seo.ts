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

/**
 * Keep this deployment out of search results entirely (PPDO-21 — UAT).
 *
 * Baked in at build time via `NEXT_PUBLIC_NOINDEX=true`, set only by the UAT deploy workflow.
 * **Production never sets it**, so `NOINDEX` is `false` there and RAL-202's SEO behaviour is
 * untouched.
 *
 * ⚠️ RAL-202 made the public site deliberately indexable, and did so *unconditionally*. A second
 * public deployment without this flag gets Google indexing a duplicate copy of a government site,
 * or emits canonicals and a sitemap pointing at production. Both are public-trust problems, not
 * just SEO ones.
 *
 * ⚠️ **Four surfaces have to agree**: `robots.ts`, `sitemap.ts`, the root layout's `robots`
 * metadata, and `metadataBase`. A `robots.txt` disallow is a *request*; the `noindex` meta tag is
 * the part with teeth; an emitted sitemap works against both by advertising the URLs anyway.
 *
 * ⚠️ **Set `NEXT_PUBLIC_SITE_URL` alongside this.** `SITE_URL` above falls back to the
 * *production* host, so a deployment that sets `NOINDEX` but forgets the site URL still emits
 * prod-pointing canonicals. The two env vars are a pair.
 */
export const NOINDEX = process.env.NEXT_PUBLIC_NOINDEX === "true";
