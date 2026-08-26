"use client";

/**
 * Portal layout — RAL-42 (auth guard) + RAL-45 (sidebar shell).
 *
 * Auth guard: every route inside (portal)/ requires a valid JWT.
 * On mount:
 *   1. If an access token is already in memory → allow immediately.
 *   2. If not, attempt POST /auth/refresh (httpOnly cookie sent automatically).
 *      - Success → store new tokens → allow.
 *      - Definitive failure (401 — token superseded or expired) → clear tokens →
 *        redirect to /login, carrying the reason as a query param so /login can
 *        explain the logout instead of bouncing silently (RAL-198).
 *      - Network error (no response at all — cold start) → the refresh cookie was
 *        never proven invalid, so don't clear anything; redirect to /reconnecting
 *        to retry instead of forcing a logout (RAL-199).
 *
 * After auth passes, renders the full portal shell:
 *   Sidebar (left, fixed) + Topbar (top) + main content area.
 * The shell fetches /auth/me to populate the sidebar and topbar with
 * the current user's name, role, and permission-based nav items.
 */

import { useEffect, useRef, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import axios from "axios";
import { auth } from "@/lib/auth";
import { classifyRefreshFailure, loginUrlWithReason, reconnectingUrl, REFRESH_TIMEOUT_MS } from "@/lib/auth-redirect";
import { clearMeCache, fetchMe } from "@/lib/me-cache";
import Sidebar from "@/components/layout/Sidebar";
import Topbar from "@/components/layout/Topbar";
import MustChangePasswordGate from "@/components/layout/MustChangePasswordGate";
import RecoverySetupGate from "@/components/layout/RecoverySetupGate";
import PasswordResetNotice from "@/components/layout/PasswordResetNotice";
import { ToastProvider } from "@/components/ui/Toast";
import type { MeResponse, RefreshErrorReason } from "@/types";
import { resolveLandingPath } from "@/lib/landing";

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api";

// Page title map — keyed by pathname prefix.
const PAGE_TITLES: Record<string, string> = {
  "/home":          "Opening…",   // transient — /home redirects on mount (RAL-264)
  "/dashboard":     "Main Dashboard",
  "/inventory":     "Inventory",
  "/budget-planning":       "Budget Planning",
  "/config":                "Configuration",
  "/resource-links":        "Resource Links",
  "/profile":               "My Account",  // transient — /profile redirects to /account (RAL-252)
  "/account":               "My Account",
  "/inventory/create-pr":       "Create Purchase Request",
  "/inventory/receive-delivery": "Receive Delivery",
  "/inventory/items-master": "Items Master",
  "/inventory/stock-balances": "Warehouse Stock Input",
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
// Resolves with enough detail (RAL-198/RAL-199) so every awaiter — not just the
// one that kicked off the request — can redirect with the right explanation.
type RefreshOutcome =
  | { ok: true }
  | { ok: false; kind: "unreachable" }
  | { ok: false; kind: "unauthorized"; reason: RefreshErrorReason | null };
let _refreshInFlight: Promise<RefreshOutcome> | null = null;

export default function PortalLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const router   = useRouter();
  const pathname = usePathname();
  // Read inside the auth-guard effect below without adding pathname as a
  // dependency — that effect should only ever run once per unauthenticated
  // mount, not on every in-portal navigation.
  const pathnameRef = useRef(pathname);
  pathnameRef.current = pathname;
  const [ready, setReady] = useState(false);
  const [me, setMe]       = useState<MeResponse | null>(null);
  // True once the FIRST /auth/me attempt has settled (success or failure) — gates
  // the mustChangePassword/needsRecoverySetup checks below so they never fall through
  // to the normal shell just because `me` hasn't loaded yet (RAL-266/267).
  const [meLoaded, setMeLoaded] = useState(false);
  // Sidebar drawer (below lg — RAL-187). No effect at lg+, where the sidebar
  // is always visible regardless of this flag.
  const [sidebarOpen, setSidebarOpen] = useState(false);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    let cancelled = false;

    async function checkAuth() {
      if (auth.isAuthenticated()) {
        if (!cancelled) setReady(true);
        return;
      }

      if (!_refreshInFlight) {
        // No JS-readable refresh token: just attempt the refresh. The httpOnly cookie
        // is sent automatically via withCredentials; a missing/invalid cookie yields a
        // 401 and we redirect to /login below.
        //
        // Production: retry up to 2× with a 3-second delay to handle Azure Functions
        // cold starts (Consumption plan). Development: 0 retries — fail fast so a
        // restarted local server redirects immediately instead of making the
        // developer wait through retry delays.
        const maxRetries = process.env.NODE_ENV === "production" ? 2 : 0;

        const tryRefresh = (retries: number): Promise<RefreshOutcome> =>
          axios
            .post(`${BASE_URL}/auth/refresh`, {}, { withCredentials: true, timeout: REFRESH_TIMEOUT_MS })
            .then(({ data }) => { auth.login(data); return { ok: true } as const; })
            .catch((err) => {
              if (retries > 0 && axios.isAxiosError(err) && !err.response) {
                return new Promise<RefreshOutcome>((resolve) => setTimeout(resolve, 3000))
                  .then(() => tryRefresh(retries - 1));
              }

              const failure = classifyRefreshFailure(err);
              if (failure.kind === "unreachable") {
                // Never proven invalid — leave tokens/cache alone, RAL-199's
                // /reconnecting page will retry the same call.
                return { ok: false, kind: "unreachable" } as const;
              }
              clearMeCache();
              auth.logout();
              return { ok: false, kind: "unauthorized", reason: failure.reason } as const;
            });

        _refreshInFlight = tryRefresh(maxRetries).finally(() => { _refreshInFlight = null; });
      }

      const result = await _refreshInFlight;
      if (!cancelled) {
        if (result.ok) setReady(true);
        else if (result.kind === "unreachable") router.replace(reconnectingUrl(pathnameRef.current));
        else router.replace(loginUrlWithReason(result.reason));
      }
    }

    checkAuth();
    return () => { cancelled = true; };
  }, [router]);

  // ── Fetch current user for sidebar / topbar ────────────────────────────────

  useEffect(() => {
    if (!ready) return;
    fetchMe().then(setMe).catch(() => {}).finally(() => setMeLoaded(true));
  }, [ready]);

  // Re-fetches /auth/me after a gate action (change password, set recovery answer,
  // dismiss the reset notice) changes something the cached value no longer reflects.
  // The module-level cache never refetches on its own, so it must be cleared first.
  async function refreshMe() {
    clearMeCache();
    try {
      setMe(await fetchMe());
    } catch {
      // Transient failure — leave the current (stale) me in place rather than booting
      // the user out of a gate they just cleared; the next natural refetch will catch up.
    }
  }

  // ── Prefetch nav routes after permissions are known ─────────────────────────
  // Sidebar links are permission-gated so <Link> elements don't exist in the DOM
  // until auth/me resolves — by then the user may already click before Next.js
  // auto-prefetch runs. Prefetch every reachable route (including sub-pages) so
  // first navigation to any section is instant.
  useEffect(() => {
    if (!me) return;
    const isOfficeUser = !me.isHostOffice;
    const routes: string[] = ["/account"];
    if (!isOfficeUser) {
      routes.push("/dashboard", "/resource-links");
      if (me.canAccessInventory || me.canAccessReports) {
        routes.push(
          "/inventory",
          "/inventory/create-pr",
          "/inventory/receive-delivery",
          "/inventory/items-master",
          "/inventory/distribution",
          "/inventory/stock-balances",
          "/inventory/item-ledger",
          "/inventory/pr-register",
          "/inventory/pr-report",
        );
      }
      if (me.canManageConfig) {
        routes.push(
          "/config",
          "/config/accounts",
          "/config/offices",
          "/config/funding-sources",
        );
      }
      if (me.canManageUsers)   routes.push("/admin/users");
      if (me.role === "Admin" || me.role === "SuperAdmin") routes.push("/announcements");
    }
    if (me.canAccessBudgetPlanning) {
      routes.push(
        "/budget-planning",
        "/budget-planning/ldip",
        "/budget-planning/aip",
        "/budget-planning/wfp",
      );
    }
    routes.forEach((r) => router.prefetch(r));
  }, [me, router]);

  // ── Office-user gate ────────────────────────────────────────────────────────
  // Non-PPDO office users have Budget Planning as their only feature. If they land
  // anywhere else (e.g. via a stale link or the home dashboard), send them back.
  //
  // Guard against a redirect loop: an office user WITHOUT budget-planning access
  // (e.g. no division assigned yet) must not be sent to /budget-planning — that
  // page would eject them to /dashboard, which this gate would bounce back here,
  // looping forever. Send those users to /account (a terminal page they can
  // always reach) instead.
  useEffect(() => {
    if (!me || me.isHostOffice) return;
    const allowed =
      pathname.startsWith("/budget-planning") ||
      // /profile only redirects to /account (RAL-252); allowing it lets that redirect land
      // instead of the gate racing it to landingPath.
      pathname.startsWith("/profile") ||
      pathname.startsWith("/account") ||
      // /home resolves the destination itself; letting the gate fire here too would
      // race it to the same place (RAL-264).
      pathname === "/home";
    if (!allowed) {
      // landingPath is resolved server-side and is guaranteed reachable, so this cannot
      // bounce them somewhere that ejects them straight back here (RAL-261).
      router.replace(resolveLandingPath(me));
    }
  }, [me, pathname, router]);

  // ── Loading state ──────────────────────────────────────────────────────────
  // Waits for the first /auth/me attempt too, not just the token check — otherwise the
  // mustChangePassword/needsRecoverySetup gates below would read a still-null `me` on the
  // very first render and fall through to the real shell for a frame before correcting.
  if (!ready || !meLoaded) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
          <p className="text-sm text-slate-600">Loading…</p>
        </div>
      </div>
    );
  }

  // ── Password / recovery gates (RAL-266/RAL-267) ─────────────────────────────
  // Structural takeovers, not route redirects — neither renders Sidebar/Topbar/children,
  // so there is nothing to navigate to and no allowlist to maintain. me starts null while
  // /auth/me is still in flight, so there is a brief window (same as the office-user gate
  // above) where neither condition is true yet; that's an accepted, already-established
  // trade-off in this file, not new here.
  //
  // Order matters: a temporary password takes priority over recovery setup, so a freshly
  // reset account isn't asked to set up recovery using a password it's about to change.
  if (me?.mustChangePassword) {
    return <MustChangePasswordGate onComplete={refreshMe} />;
  }
  if (me?.needsRecoverySetup) {
    return <RecoverySetupGate onComplete={refreshMe} />;
  }

  // ── Portal shell ───────────────────────────────────────────────────────────

  return (
    <ToastProvider>
      <div className="flex h-screen bg-slate-100 font-sans overflow-hidden print:h-auto print:overflow-visible">
        <Sidebar me={me} open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
        <div className="flex-1 flex flex-col min-w-0 overflow-hidden print:overflow-visible">
          <Topbar me={me} title={getPageTitle(pathname)} onMenuClick={() => setSidebarOpen(true)} />
          {me?.unacknowledgedPasswordResetAt && (
            <PasswordResetNotice
              resetAt={me.unacknowledgedPasswordResetAt}
              onDismissed={() =>
                setMe((prev) => (prev ? { ...prev, unacknowledgedPasswordResetAt: null } : prev))
              }
            />
          )}
          <main className="flex-1 overflow-auto print:overflow-visible print:h-auto">
            {children}
          </main>
        </div>
      </div>
    </ToastProvider>
  );
}
