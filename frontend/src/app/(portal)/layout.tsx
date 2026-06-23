"use client";

/**
 * Portal layout — RAL-42 (auth guard) + RAL-45 (sidebar shell).
 *
 * Auth guard: every route inside (portal)/ requires a valid JWT.
 * On mount:
 *   1. If an access token is already in memory → allow immediately.
 *   2. If not, look for a stored refresh token in localStorage.
 *      - Found  → POST /auth/refresh → store new tokens → allow.
 *      - Missing / expired → clear tokens → redirect to /login.
 *
 * After auth passes, renders the full portal shell:
 *   Sidebar (left, fixed) + Topbar (top) + main content area.
 * The shell fetches /auth/me to populate the sidebar and topbar with
 * the current user's name, role, and permission-based nav items.
 */

import { useEffect, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import axios from "axios";
import api from "@/lib/api";
import { auth } from "@/lib/auth";
import Sidebar from "@/components/layout/Sidebar";
import Topbar from "@/components/layout/Topbar";
import { ToastProvider } from "@/components/ui/Toast";
import type { MeResponse } from "@/types";

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api";

// Page title map — keyed by pathname prefix.
const PAGE_TITLES: Record<string, string> = {
  "/dashboard":     "Main Dashboard",
  "/inventory":     "Inventory",
  "/budget-planning":       "Budget Planning",
  "/config":                "Configuration",
  "/resource-links":        "Resource Links",
  "/profile":               "My Profile",
  "/account":               "My Account",
  "/inventory/create-pr":       "Create Purchase Request",
  "/inventory/receive-delivery": "Receive Delivery",
  "/inventory/items-master": "Items Master",
  "/inventory/pr-report":    "PR Report",
  "/admin/users":           "User Management",
};

function getPageTitle(pathname: string): string {
  for (const [prefix, title] of Object.entries(PAGE_TITLES)) {
    if (pathname === prefix || pathname.startsWith(prefix + "/")) return title;
  }
  return "PPDO Portal";
}

// Module-level — deduplicates the silent refresh across StrictMode double-mounts.
let _refreshInFlight: Promise<boolean> | null = null;

export default function PortalLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const router   = useRouter();
  const pathname = usePathname();
  const [ready, setReady] = useState(false);
  const [me, setMe]       = useState<MeResponse | null>(null);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    let cancelled = false;

    async function checkAuth() {
      if (auth.isAuthenticated()) {
        if (!cancelled) setReady(true);
        return;
      }

      const refreshToken = auth.getRefreshToken();
      if (!refreshToken) {
        if (!cancelled) router.replace("/login");
        return;
      }

      if (!_refreshInFlight) {
        // Production: retry up to 2× with a 3-second delay to handle Azure Functions
        // cold starts (Consumption plan). Development: 0 retries — fail fast so a
        // restarted local server redirects to /login immediately instead of making
        // the user wait 6 seconds through retry delays.
        const maxRetries = process.env.NODE_ENV === "production" ? 2 : 0;

        const tryRefresh = (retries: number): Promise<boolean> =>
          axios
            .post(`${BASE_URL}/auth/refresh`, { refreshToken }, { withCredentials: true })
            .then(({ data }) => { auth.login(data); return true; })
            .catch((err) => {
              if (retries > 0 && axios.isAxiosError(err) && !err.response) {
                return new Promise<boolean>((resolve) => setTimeout(resolve, 3000))
                  .then(() => tryRefresh(retries - 1));
              }
              auth.logout();
              return false;
            });

        _refreshInFlight = tryRefresh(maxRetries).finally(() => { _refreshInFlight = null; });
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

  // ── Fetch current user for sidebar / topbar ────────────────────────────────

  useEffect(() => {
    if (!ready) return;
    api.get<MeResponse>("/auth/me").then(({ data }) => setMe(data)).catch(() => {});
  }, [ready]);

  // ── Prefetch nav routes after permissions are known ─────────────────────────
  // Sidebar links are permission-gated so <Link> elements don't exist in the DOM
  // until auth/me resolves — by then the user may already click before Next.js
  // auto-prefetch runs. Calling router.prefetch() here warms the chunk cache
  // proactively so first navigation to any section is instant.
  useEffect(() => {
    if (!me) return;
    const isOfficeUser = me.officeId != null;
    const routes: string[] = [];
    if (!isOfficeUser) {
      routes.push("/dashboard", "/resource-links");
      if (me.canAccessInventory || me.canAccessReports) routes.push("/inventory");
      if (me.canManageConfig)  routes.push("/config");
      if (me.canManageUsers)   routes.push("/admin/users");
      if (me.role === "Admin" || me.role === "SuperAdmin") routes.push("/announcements");
    }
    if (me.canAccessBudgetPlanning) routes.push("/budget-planning");
    routes.push("/account");
    routes.forEach((r) => router.prefetch(r));
  }, [me, router]);

  // ── Office-user gate ────────────────────────────────────────────────────────
  // Non-PPDO office users have Budget Planning as their only feature. If they land
  // anywhere else (e.g. via a stale link or the home dashboard), send them back.
  useEffect(() => {
    if (!me || me.officeId == null) return;
    const allowed =
      pathname.startsWith("/budget-planning") ||
      pathname.startsWith("/profile") ||
      pathname.startsWith("/account");
    if (!allowed) router.replace("/budget-planning");
  }, [me, pathname, router]);

  // ── Loading state ──────────────────────────────────────────────────────────

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

  // ── Portal shell ───────────────────────────────────────────────────────────

  return (
    <ToastProvider>
      <div className="flex h-screen bg-slate-100 font-sans overflow-hidden">
        <Sidebar me={me} />
        <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
          <Topbar me={me} title={getPageTitle(pathname)} />
          <main className="flex-1 overflow-auto">
            {children}
          </main>
        </div>
      </div>
    </ToastProvider>
  );
}
