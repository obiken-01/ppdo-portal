import type { Metadata, Viewport } from "next";
import "./globals.css";
import { NOINDEX, SITE_URL } from "@/lib/seo";
import ServiceWorkerRegistrar from "@/components/pwa/ServiceWorkerRegistrar";

export const metadata: Metadata = {
  metadataBase: new URL(SITE_URL),
  title: {
    default: "PPDO Portal",
    template: "%s | PPDO Portal",
  },
  description: "Provincial Planning and Development Office — Occidental Mindoro",
  // iOS ignores the manifest's icons for the home screen and needs this instead.
  // The file is flattened onto white — Safari composites transparency badly.
  icons: {
    apple: "/icons/apple-touch-icon.png",
  },
  appleWebApp: {
    capable: true,
    title: "PPDO Portal",
    statusBarStyle: "default",
  },
  // ⚠️ **Both spellings, deliberately** (PPDO-103). `appleWebApp.capable` emits
  // `apple-mobile-web-app-capable`, which Chromium now deprecates — it logs a warning on every page
  // load. The standard name is `mobile-web-app-capable`, and Next 14's Metadata API has no
  // first-class field for it, so it goes through `other`.
  //
  // ⚠️ The Apple tag STAYS. iOS Safari reads only that spelling, and add-to-home-screen on an iPhone
  // is a real usage path for this portal — dropping it to silence a console warning would trade a
  // working feature for a quieter log.
  other: {
    "mobile-web-app-capable": "yes",
  },
  // ⚠️ The half of the noindex story with actual teeth (PPDO-21 — UAT). `robots.txt` is a
  // request a crawler may ignore; this meta tag is an instruction Google documents as honoured, and
  // it is what keeps a UAT copy of a government site out of search results. Spread so production —
  // where NOINDEX is false — emits no `robots` key at all and RAL-202's behaviour is untouched.
  ...(NOINDEX ? { robots: { index: false, follow: false } } : {}),
};

// themeColor lives on the viewport export in Next 14, not on metadata.
// green-700 — matches the sidebar and login header, so the OS/browser chrome
// blends with the app frame instead of cutting against it.
export const viewport: Viewport = {
  themeColor: "#196638",
};

// Derive the API origin at build time so we can emit a preconnect hint.
// Establishing TCP+TLS to the backend before the user interacts saves ~200ms
// off the first API call in a cold browser session.
const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "";
let apiOrigin: string | null = null;
try { apiOrigin = new URL(API_BASE).origin; } catch { /* relative URL or unset — skip */ }

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en">
      <head>
        {apiOrigin && (
          <link rel="preconnect" href={apiOrigin} crossOrigin="use-credentials" />
        )}
      </head>
      <body className="antialiased">
        {children}
        <ServiceWorkerRegistrar />
      </body>
    </html>
  );
}
