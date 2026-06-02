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

// Module-level — shared across all instances and StrictMode double-runs.
// Ensures only one /auth/refresh request fires at a time; concurrent callers
// (e.g. the StrictMode second mount) await the same promise instead of sending
// a duplicate that would be rejected because the token was already rotated.
let _refreshInFlight: Promise<boolean> | null = null;

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
      // Access token already in memory — no round-trip needed.
      if (auth.isAuthenticated()) {
        if (!cancelled) setReady(true);
        return;
      }

      const refreshToken = auth.getRefreshToken();
      if (!refreshToken) {
        if (!cancelled) router.replace("/login");
        return;
      }

      // If a refresh is already in-flight (StrictMode second mount fires before
      // the first network call resolves), wait on the same promise instead of
      // sending a duplicate request that would be rejected as token-already-used.
      if (!_refreshInFlight) {
        _refreshInFlight = axios
          .post(`${BASE_URL}/auth/refresh`, { refreshToken })
          .then(({ data }) => { auth.login(data); return true; })
          .catch(() => { auth.logout(); return false; })
          .finally(() => { _refreshInFlight = null; });
      }

      const ok = await _refreshInFlight;
      if (!cancelled) {
        if (ok) setReady(true);
        else router.replace("/login");
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
