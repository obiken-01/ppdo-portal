/**
 * Login route metadata (RAL-202).
 *
 * page.tsx here is a client component ("use client" — auth-guard logic runs
 * on mount), and Next.js only reads `export const metadata` from Server
 * Components — so it lives in this layout instead, one level up.
 */

import type { Metadata } from "next";
import { APP_NAME } from "@/lib/branding";

export const metadata: Metadata = {
  title: "Login",
  description: `Sign in to ${APP_NAME}.`,
  alternates: { canonical: "/login" },
};

export default function LoginLayout({ children }: { children: React.ReactNode }) {
  return children;
}
