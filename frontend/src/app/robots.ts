import type { MetadataRoute } from "next";
import { NOINDEX, SITE_URL } from "@/lib/seo";

// Static export quirk: without this, `next dev` treats robots.ts as a dynamic
// route and 500s asking for generateStaticParams(), even though `next build`
// (the config actually used for deployment) always emits it statically fine.
export const dynamic = "force-static";

/**
 * robots.txt (RAL-202) — static export, generated once at build time.
 *
 * Allows the public marketing pages; disallows the entire authenticated
 * portal tree. Those routes redirect anonymous visitors (and crawlers) to
 * /login anyway, so there's no real content there for a crawler to index —
 * this just saves crawl budget and keeps auth-gated URLs out of search
 * results outright.
 */
export default function robots(): MetadataRoute.Robots {
  // A non-production deployment asks crawlers for nothing at all (PPDO-21 — UAT).
  //
  // ⚠️ No `sitemap` line either. Advertising a sitemap while disallowing the site is contradictory,
  // and a crawler that reads the sitemap anyway gets handed the very URLs this is hiding. The
  // `noindex` meta tag in the root layout is the half with actual teeth — robots.txt is a request.
  if (NOINDEX) {
    return { rules: { userAgent: "*", disallow: ["/"] } };
  }

  return {
    rules: {
      userAgent: "*",
      allow: ["/", "/about", "/services", "/contact", "/login"],
      // Prefix match — "/inventory" also covers "/inventory/pr-register" etc.
      disallow: ["/dashboard", "/inventory", "/budget-planning", "/config", "/account", "/reconnecting"],
    },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}
