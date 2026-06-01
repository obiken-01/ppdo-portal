"use client";

/**
 * Portal layout — RAL-42.
 *
 * Auth guard: every route inside (portal)/ requires a valid JWT.
 *
 * On mount:
 *   1. If an access token is already in memory → allow immediately.
 *   2. If not, look for a stored refresh token in localStorage.
 *      - Found  → POST /auth/refresh → store new tokens → allow.
 *      - Missing / expired → clear tokens → redirect to /login.
 *
 * A full-screen loading spinner is shown while the auth check runs to
 * prevent a flash of portal content before the redirect fires.
 *
 * The sidebar and top-nav shell will be added in a future RAL once the
 * dashboard design is implemented.
 */

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import axios from "axios";
import { auth } from "@/lib/auth";

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api";

export default function PortalLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const router = useRouter();
  const [ready, setReady] = useState(false);

  useEffect(() => {
    let cancelled = false;

    async function checkAuth() {
      // Access token already in memory — no round-trip needed
      if (auth.isAuthenticated()) {
        if (!cancelled) setReady(true);
        return;
      }

      // Try silent refresh with the stored refresh token
      const refreshToken = auth.getRefreshToken();
      if (!refreshToken) {
        router.replace("/login");
        return;
      }

      try {
        // Use plain axios to bypass the api.ts interceptor and avoid loops
        const { data } = await axios.post(`${BASE_URL}/auth/refresh`, {
          refreshToken,
        });
        auth.login(data);
        if (!cancelled) setReady(true);
      } catch {
        auth.logout();
        router.replace("/login");
      }
    }

    checkAuth();
    return () => { cancelled = true; };
  }, [router]);

  if (!ready) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-slate-500">Loading…</p>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
