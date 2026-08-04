import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/seo";

// Static export quirk: without this, `next dev` treats sitemap.ts as a dynamic
// route and 500s asking for generateStaticParams(), even though `next build`
// (the config actually used for deployment) always emits it statically fine.
export const dynamic = "force-static";

/**
 * sitemap.xml (RAL-202) — static export, generated once at build time.
 *
 * Only the public, content-bearing pages. /login is a real page but thin/
 * duplicate-purpose content — not worth a sitemap entry. Authenticated
 * portal routes are excluded entirely (see robots.ts).
 */
export default function sitemap(): MetadataRoute.Sitemap {
  const lastModified = new Date();

  return [
    { url: `${SITE_URL}/`, lastModified, changeFrequency: "weekly", priority: 1 },
    { url: `${SITE_URL}/about`, lastModified, changeFrequency: "monthly", priority: 0.6 },
    { url: `${SITE_URL}/services`, lastModified, changeFrequency: "monthly", priority: 0.6 },
    { url: `${SITE_URL}/contact`, lastModified, changeFrequency: "monthly", priority: 0.6 },
  ];
}
