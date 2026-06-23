import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "PPDO Portal",
  description: "Provincial Planning and Development Office — Occidental Mindoro",
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
      <body className="antialiased">{children}</body>
    </html>
  );
}
