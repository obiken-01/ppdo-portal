import type { Metadata, Viewport } from "next";
import "./globals.css";
import { SITE_URL } from "@/lib/seo";
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
