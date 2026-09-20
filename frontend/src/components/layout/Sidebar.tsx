"use client";

/**
 * PPDO Portal — main navigation sidebar.
 * Uses PPDO green-700 (#196638) as background to match the Penpot design.
 *
 * Logo strip (top)  — clicking navigates to /dashboard.
 * User strip (bottom) — clicking toggles a popup submenu (Profile, Logout).
 *
 * Inventory group is collapsible. Visibility rules:
 *   - Inventory parent shown if canAccessInventory OR canAccessReports
 *   - "Inventory Dashboard" child shown if canAccessInventory
 *   - "Inventory Report"    child shown if canAccessReports
 *   - Parent auto-expands when the current path is under /inventory
 *
 * Responsive (RAL-187): below `lg`, the aside is a fixed off-canvas drawer
 * driven by the `open`/`onClose` props (owned by the portal layout) — hidden
 * off-screen via a transform, sliding in over a backdrop when open. At `lg`
 * and above it reverts to the original static column; `open`/`onClose` have
 * no visual effect there, since the drawer classes are only active below `lg`.
 */

import { useState, useEffect, useRef } from "react";
import Image from "next/image";
import Link from "next/link";
import { useRouter, usePathname } from "next/navigation";
import api from "@/lib/api";
import { allocationLabels } from "@/lib/budget-planning-labels";
import {
  canOpenAipRecords, canOpenAipReview, canOpenBudgetPlanningReport, canOpenLdip,
  canOpenOfficeCeilings,
} from "@/lib/budget-planning-access";
import { auth } from "@/lib/auth";
import { clearMeCache } from "@/lib/me-cache";
import { pendingLink, useAipNotifications } from "@/lib/aip-notifications";
import { APP_VERSION } from "@/lib/version";
import type { MeResponse } from "@/types";
import { resolveLandingPath } from "@/lib/landing";

/** A count or status pill on a nav row (PPDO-75) — sits over the row's right edge. */
const SIDEBAR_PILL =
  "absolute right-3 top-1/2 -translate-y-1/2 rounded-full bg-white px-2 py-0.5 text-[11px] font-semibold leading-4 text-green-800 hover:bg-green-100";

interface SidebarProps {
  me: MeResponse | null;
  /** Drawer open state below `lg`. Ignored (always visible) at `lg` and above. */
  open: boolean;
  /** Called on backdrop click, Escape, a nav link click, or route change. */
  onClose: () => void;
}

export default function Sidebar({ me, open, onClose }: SidebarProps) {
  const pathname  = usePathname();
  const router    = useRouter();
  const menuRef   = useRef<HTMLDivElement>(null);

  const [inventoryOpen, setInventoryOpen] = useState(
    () => pathname.startsWith("/inventory")
  );
  const [configOpen, setConfigOpen] = useState(
    () => pathname.startsWith("/config") || pathname.startsWith("/admin/users")
  );
  const [budgetPlanningOpen, setBudgetPlanningOpen] = useState(
    () => pathname.startsWith("/budget-planning")
  );
  const [userMenuOpen, setUserMenuOpen] = useState(false);

  // PPDO-75 — the pending count and the returned notice, from the shared store (never a fetch here).
  const notifications = useAipNotifications(me);
  const pending = notifications ? pendingLink(notifications) : null;
  const returnedNotice = notifications?.returned[0] ?? null;

  // Auto-expand inventory when navigating to an inventory route
  useEffect(() => {
    if (pathname.startsWith("/inventory")) setInventoryOpen(true);
  }, [pathname]);

  // Auto-expand configuration when navigating to a config route or user management
  useEffect(() => {
    if (pathname.startsWith("/config") || pathname.startsWith("/admin/users")) setConfigOpen(true);
  }, [pathname]);

  // Auto-expand budget planning when navigating to a budget-planning route
  useEffect(() => {
    if (pathname.startsWith("/budget-planning")) setBudgetPlanningOpen(true);
  }, [pathname]);

  // Close user menu when clicking outside
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
    }
    if (userMenuOpen) document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, [userMenuOpen]);

  // Drawer (below lg): close on Escape while open
  useEffect(() => {
    if (!open) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === "Escape") onClose();
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [open, onClose]);

  // Drawer (below lg): close on route change. No-op at lg+ — `open` doesn't
  // affect rendering there — so this is safe to run unconditionally.
  useEffect(() => {
    onClose();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pathname]);

  async function handleLogout() {
    setUserMenuOpen(false);
    try { await api.post("/auth/logout"); } catch { /* ignore */ }
    clearMeCache();
    auth.logout();
    router.replace("/login");
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  function isActive(href: string) {
    return pathname === href || pathname.startsWith(href + "/");
  }

  // Non-PPDO office users (officeId set) get Budget Planning ONLY — no Dashboard,
  // Inventory, Resource Links (PPDO-internal), Configuration, or User Management.
  const isOfficeUser       = me != null && !me.isHostOffice;

  const hasInventory       = me?.canAccessInventory      === true;
  const hasReport          = me?.canAccessReports         === true;
  const showInventoryGroup = !isOfficeUser && (hasInventory || hasReport);
  const isAdmin            = me?.role === "Admin" || me?.role === "SuperAdmin";
  const showManageUsers    = !isOfficeUser && me?.canManageUsers === true;
  const showAuditLog       = !isOfficeUser && me?.role === "SuperAdmin";
  // PPDO-86 — unlike showConfig, not gated on !isOfficeUser: a guest-office Staff holding
  // CanManageApiKeys must still see the page (build spec §6.1 "office and division not read").
  const showApiAccess      = me?.canManageApiKeys === true;
  const showBudgetPlanning = me?.canAccessBudgetPlanning === true;
  // WFP — and the Report page, which renders a WFP — are PPDO-internal (PPDO-20).
  // A guest office plans against its ceiling in the AIP and submits that; it has no
  // division split to build a WFP from, and its users have never been shown the
  // feature. PBO is a guest office too, so its cross-office ceiling grant does not
  // widen this. Revisit when WFP is reworked after v1.8.0.
  const showWfp            = !isOfficeUser && showBudgetPlanning;
  // The allocation page is reachable two ways, and only one of them is cross-office.
  // `CanManagePpdoAllocation` is host-office-exclusive (`docs/v1.8/Permission_Matrix.md`
  // §4) — both its endpoints refuse a guest-office caller outright — so pairing it with
  // isOfficeUser here stops the nav offering a page whose writes will 403. A guest office
  // reaches it only through `CanManageOfficeCeilings`, which is deliberately cross-office.
  const showAllocation     = (me?.canManagePpdoAllocation === true && !isOfficeUser)
                          || me?.canManageOfficeCeilings === true;
  // PPDO-106 — the ceilings page is the ceiling grant's own surface, and that grant is deliberately
  // cross-office, so this is NOT paired with isOfficeUser the way showAllocation is above.
  const showOfficeCeilings = me != null && canOpenOfficeCeilings(me);
  const showConfig         = !isOfficeUser && me?.canManageConfig === true;
  // PPDO-81 — the AIP record list is where the base record is created, finalized and archived, and
  // all three are Admin actions. An encoder's surface is AIP Entry below. LDIP is hidden from a
  // GUEST office rather than from non-admins: PPDO planning staff work in it, but a guest office
  // that finds a program missing would come here to add it and every write control is Admin-only.
  // Both rules live in lib/budget-planning-access so the nav and the route guard cannot drift.
  const showAipRecords     = me != null && canOpenAipRecords(me);
  const showLdip           = me != null && canOpenLdip(me);
  // PPDO-79 — the review surface, for EITHER reviewer: the search is where both the PPDO reviewer
  // and a department head find work (the server clamps a department head to their own office).
  // ⚠️ Not `isHostOffice` — a PPDO division encoder holds no review work. Same file as the two above,
  // for the same reason: the nav and the route guard read one rule.
  const showAipReview      = me != null && canOpenAipReview(me);
  // ↩️ **The Consolidated AIP item is gone** (PPDO-92) — it is a type on the Report page now.
  //
  // ⚠️ Report is no longer inside the WFP block. It carries three types with different audiences:
  // WFP and PPMP are host-office only, AIP is either reviewer, so a guest-office department head has
  // something to read there while having no WFP at all. `canOpenBudgetPlanningReport` is the union,
  // derived from the two rules rather than restated, so the nav and the page cannot disagree.
  const showReport         = me != null && canOpenBudgetPlanningReport(me);
  const showResourceLinks  = !isOfficeUser;
  const showDashboard      = !isOfficeUser;
  const showAnnouncements  = !isOfficeUser && isAdmin;

  function linkCls(active: boolean) {
    // py-3 (not py-2.5) so the row clears a 44px touch target in the mobile drawer.
    return `flex items-center gap-3 px-3 py-3 text-sm font-medium transition-colors ${
      active
        ? "bg-green-800 text-white"
        : "text-green-100 hover:bg-green-600 hover:text-white"
    }`;
  }

  function childLinkCls(active: boolean) {
    return `flex items-center gap-2 pl-9 pr-3 py-2 text-sm transition-colors ${
      active
        ? "bg-green-800 text-white font-medium"
        : "text-green-200 hover:bg-green-600 hover:text-white"
    }`;
  }

  return (
    <>
      {/* Backdrop — below lg only, shown while the drawer is open */}
      {open && (
        <div
          className="fixed inset-0 z-40 bg-slate-900/40 backdrop-blur-sm lg:hidden print:hidden"
          onClick={onClose}
          aria-hidden="true"
        />
      )}

      <aside
        className={`fixed inset-y-0 left-0 z-50 w-56 bg-green-700 flex flex-col transition-transform duration-200 ease-in-out
          lg:static lg:z-auto lg:h-full lg:shrink-0 lg:transition-none
          ${open ? "translate-x-0" : "-translate-x-full"} lg:translate-x-0
          print:hidden`}
      >

      {/* ── Logo / brand — click to go to this user's landing page ──────── */}
      {/* Not hardcoded to /dashboard: office users cannot open it, so the brand
          link used to eject them the moment they clicked it (RAL-263). */}
      <Link
        href={resolveLandingPath(me)}
        className="flex items-center gap-3 px-5 py-3 border-b border-green-600 hover:bg-green-600 transition-colors group"
      >
        <Image
          src="/images/ppdo-logo.webp"
          alt="PPDO"
          width={48}
          height={48}
          className="rounded-full object-contain shrink-0"
        />
        <div className="min-w-0">
          <p className="text-white font-bold text-sm leading-tight truncate">PPDO Portal</p>
          <p className="text-green-300 text-xs leading-tight truncate group-hover:text-green-200">
            Occ. Mindoro &middot; {APP_VERSION}
          </p>
        </div>
      </Link>

      {/* ── Navigation ───────────────────────────────────────────────────── */}
      <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto">

        {/* Dashboard — PPDO users only */}
        {showDashboard && (
          <Link href="/dashboard" className={linkCls(isActive("/dashboard"))}>
            <span className="text-base leading-none w-5 text-center">🏠</span>
            <span className="truncate">Dashboard</span>
          </Link>
        )}

        {/* Resource Links — PPDO-internal; hidden for office users */}
        {showResourceLinks && (
          <Link href="/resource-links" className={linkCls(isActive("/resource-links"))}>
            <span className="text-base leading-none w-5 text-center">🔗</span>
            <span className="truncate">Resource Links</span>
          </Link>
        )}

        {/* Announcements — Admin / SuperAdmin only */}
        {showAnnouncements && (
          <Link href="/announcements" className={linkCls(isActive("/announcements"))}>
            <span className="text-base leading-none w-5 text-center">📢</span>
            <span className="truncate">Announcements</span>
          </Link>
        )}

        {/* Inventory group — collapsible */}
        {showInventoryGroup && (
          <div>
            <button
              onClick={() => setInventoryOpen((o) => !o)}
              className={`w-full flex items-center gap-3 px-3 py-3 text-sm font-medium transition-colors ${
                isActive("/inventory")
                  ? "bg-green-800 text-white"
                  : "text-green-100 hover:bg-green-600 hover:text-white"
              }`}
            >
              <span className="text-base leading-none w-5 text-center">📦</span>
              <span className="flex-1 text-left truncate">Inventory</span>
              <span className={`text-base leading-none transition-transform duration-200 ${inventoryOpen ? "rotate-90" : ""}`}>
                ›
              </span>
            </button>

            {inventoryOpen && (
              <div className="mt-0.5 space-y-0.5">
                {hasInventory && (
                  <Link
                    href="/inventory"
                    className={childLinkCls(pathname === "/inventory")}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Inventory Dashboard</span>
                  </Link>
                )}
                {hasInventory && (
                  <Link
                    href="/inventory/create-pr"
                    className={childLinkCls(isActive("/inventory/create-pr"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Create PR</span>
                  </Link>
                )}
                {hasInventory && (
                  <Link
                    href="/inventory/receive-delivery"
                    className={childLinkCls(isActive("/inventory/receive-delivery"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Receive Delivery</span>
                  </Link>
                )}
                {hasInventory && (
                  <Link
                    href="/inventory/items-master"
                    className={childLinkCls(isActive("/inventory/items-master"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Items Master</span>
                  </Link>
                )}
                {hasInventory && (
                  <Link
                    href="/inventory/distribution"
                    className={childLinkCls(isActive("/inventory/distribution"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Distribution</span>
                  </Link>
                )}
                {hasInventory && (
                  <Link
                    href="/inventory/stock-balances"
                    className={childLinkCls(isActive("/inventory/stock-balances"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Warehouse Stock Input</span>
                  </Link>
                )}
                {hasInventory && (
                  <Link
                    href="/inventory/item-ledger"
                    className={childLinkCls(isActive("/inventory/item-ledger"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Stock Overview</span>
                  </Link>
                )}
                {hasInventory && (
                  <Link
                    href="/inventory/pr-register"
                    className={childLinkCls(isActive("/inventory/pr-register"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">PR List</span>
                  </Link>
                )}
                {hasReport && (
                  <Link
                    href="/inventory/pr-report"
                    className={childLinkCls(isActive("/inventory/pr-report"))}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">Inventory Report</span>
                  </Link>
                )}
              </div>
            )}
          </div>
        )}

        {/* Budget Planning — v1.1. Office users see only this; PPDO users gated by flag. */}
        {showBudgetPlanning && (
          <div>
            <button
              onClick={() => setBudgetPlanningOpen((o) => !o)}
              className={`w-full flex items-center gap-3 px-3 py-3 text-sm font-medium transition-colors ${
                isActive("/budget-planning")
                  ? "bg-green-800 text-white"
                  : "text-green-100 hover:bg-green-600 hover:text-white"
              }`}
            >
              <span className="text-base leading-none w-5 text-center">💰</span>
              <span className="flex-1 text-left truncate">Investment Planning</span>
              <span className={`text-base leading-none transition-transform duration-200 ${budgetPlanningOpen ? "rotate-90" : ""}`}>
                ›
              </span>
            </button>

            {budgetPlanningOpen && (
              <div className="mt-0.5 space-y-0.5">
                <Link href="/budget-planning" className={childLinkCls(pathname === "/budget-planning")}>
                  <span className="text-xs">•</span>
                  <span className="truncate">Dashboard</span>
                </Link>
                {showLdip && (
                  <Link href="/budget-planning/ldip" className={childLinkCls(isActive("/budget-planning/ldip"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">LDIP</span>
                  </Link>
                )}
                {/* ↩️ Labelled "AIP Records", not "AIP" (PPDO-79). With Entry and Review beside it,
                    three items all beginning "AIP" is one more than a reader will parse — and
                    "Records" is what this page actually is: the record list and detail, Admin-only.
                    The route is unchanged; only the label moved. */}
                {showAipRecords && (
                  <Link href="/budget-planning/aip" className={childLinkCls(pathname === "/budget-planning/aip" || isActive("/budget-planning/aip/detail") || isActive("/budget-planning/aip/new") || isActive("/budget-planning/aip/import-preview"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">AIP Records</span>
                  </Link>
                )}
                {/* PPDO-52 — the encoder's own tab, separate from the AIP list/detail above.
                    ⚠️ Ungated on purpose, unlike the two items around it: this is the ONE
                    budget-planning page every office has, and since PPDO-81 hid the record list it
                    is the only AIP surface most users see. */}
                {/* PPDO-75 — a Returned pill while the office is handed back to this reader. A
                    sibling link, not nested: a link inside a link is invalid and clicks the outer. */}
                <div className="relative">
                  <Link
                    href="/budget-planning/aip/entry"
                    className={`${childLinkCls(isActive("/budget-planning/aip/entry"))}${returnedNotice ? " pr-24" : ""}`}
                  >
                    <span className="text-xs">•</span>
                    <span className="truncate">AIP Entry</span>
                  </Link>
                  {returnedNotice && (
                    <Link
                      href={`/budget-planning/aip/entry?fiscalYear=${returnedNotice.fiscalYear}`}
                      title={`FY ${returnedNotice.fiscalYear} was returned to you`}
                      className={SIDEBAR_PILL}
                    >
                      Returned
                    </Link>
                  )}
                </div>
                {/* PPDO-79 — the PPDO consolidated reviewer's home.
                    ⚠️ Points at the SEARCH, not at `/aip/review`: the search is the landing, and the
                    one-office screen is reached from a result rather than typed.
                    ⚠️ Active on the whole `/aip/review` subtree, so the item stays lit when a
                    reviewer follows a result into an office — otherwise the nav appears to lose
                    its place on the click it exists to enable.
                    PPDO-75 — the pending count beside it: offices waiting on this reader. Nothing at
                    zero, and its own link, since where it goes depends on who is reading. */}
                {showAipReview && (
                  <div className="relative">
                    <Link
                      href="/budget-planning/aip/review/search"
                      className={`${childLinkCls(isActive("/budget-planning/aip/review"))}${pending ? " pr-14" : ""}`}
                    >
                      <span className="text-xs">•</span>
                      <span className="truncate">AIP Review</span>
                    </Link>
                    {pending && (
                      <Link
                        href={pending.href}
                        title={`${pending.count} ${pending.count === 1 ? "office is" : "offices are"} waiting on you`}
                        aria-label={`${pending.count} waiting on you`}
                        className={`${SIDEBAR_PILL} min-w-[1.5rem] text-center tabular-nums`}
                      >
                        {pending.count}
                      </Link>
                    )}
                  </div>
                )}
                {showOfficeCeilings && (
                  <Link href="/budget-planning/office-ceilings" className={childLinkCls(isActive("/budget-planning/office-ceilings"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">Office Ceilings</span>
                  </Link>
                )}
                {showAllocation && (
                  <Link href="/budget-planning/allocation" className={childLinkCls(isActive("/budget-planning/allocation"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">{allocationLabels(me).nav}</span>
                  </Link>
                )}
                {showWfp && (
                  <Link href="/budget-planning/wfp/entry" className={childLinkCls(isActive("/budget-planning/wfp"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">WFP</span>
                  </Link>
                )}
                {showReport && (
                  <Link href="/budget-planning/report" className={childLinkCls(isActive("/budget-planning/report"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">Report</span>
                  </Link>
                )}
              </div>
            )}
          </div>
        )}

        {/* Configuration — collapsible group; PPDO users with CanManageConfig or CanManageUsers */}
        {(showConfig || showManageUsers || showAuditLog || showApiAccess) && (
          <div>
            <button
              onClick={() => setConfigOpen((o) => !o)}
              className={`w-full flex items-center gap-3 px-3 py-3 text-sm font-medium transition-colors ${
                (isActive("/config") || isActive("/admin/users"))
                  ? "bg-green-800 text-white"
                  : "text-green-100 hover:bg-green-600 hover:text-white"
              }`}
            >
              <span className="text-base leading-none w-5 text-center">⚙️</span>
              <span className="flex-1 text-left truncate">Configuration</span>
              <span className={`text-base leading-none transition-transform duration-200 ${configOpen ? "rotate-90" : ""}`}>
                ›
              </span>
            </button>

            {configOpen && (
              <div className="mt-0.5 space-y-0.5">
                {showConfig && (
                  <>
                    <Link href="/config" className={childLinkCls(pathname === "/config")}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Dashboard</span>
                    </Link>
                    <Link href="/config/accounts" className={childLinkCls(isActive("/config/accounts"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Accounts</span>
                    </Link>
                    <Link href="/config/offices" className={childLinkCls(isActive("/config/offices"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Offices</span>
                    </Link>
                    <Link href="/config/funding-sources" className={childLinkCls(isActive("/config/funding-sources"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Funding Sources</span>
                    </Link>
                    <Link href="/config/cc-typologies" className={childLinkCls(isActive("/config/cc-typologies"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Climate Change Typologies</span>
                    </Link>
                    <Link href="/config/esre-codes" className={childLinkCls(isActive("/config/esre-codes"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">eSRE Codes</span>
                    </Link>
                    <Link href="/config/price-index" className={childLinkCls(isActive("/config/price-index"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Price Index</span>
                    </Link>
                    <Link href="/config/divisions" className={childLinkCls(isActive("/config/divisions"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Divisions</span>
                    </Link>
                    <Link href="/config/procurement-presets" className={childLinkCls(isActive("/config/procurement-presets"))}>
                      <span className="text-xs">•</span>
                      <span className="truncate">Procurement Presets</span>
                    </Link>
                  </>
                )}
                {showManageUsers && (
                  <Link href="/admin/users" className={childLinkCls(isActive("/admin/users"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">User Management</span>
                  </Link>
                )}
                {showAuditLog && (
                  <Link href="/config/audit-log" className={childLinkCls(isActive("/config/audit-log"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">Audit Log</span>
                  </Link>
                )}
                {showApiAccess && (
                  <Link href="/config/api-access" className={childLinkCls(isActive("/config/api-access"))}>
                    <span className="text-xs">•</span>
                    <span className="truncate">API Access</span>
                  </Link>
                )}
              </div>
            )}
          </div>
        )}

      </nav>

      {/* ── User strip — click to toggle popup menu ──────────────────────── */}
      {me && (
        <div ref={menuRef} className="relative border-t border-green-600">

          {/* Popup menu — renders above the strip */}
          {userMenuOpen && (
            <div className="absolute bottom-full left-3 right-3 mb-1 bg-white shadow-xl border border-slate-100 overflow-hidden z-50">
              <div className="px-4 py-2.5 border-b border-slate-100">
                <p className="text-xs font-semibold text-slate-800 truncate">{me.fullName}</p>
                <p className="text-xs text-slate-600 truncate">{me.username}</p>
              </div>
              <Link
                href="/account"
                onClick={() => setUserMenuOpen(false)}
                className="flex items-center gap-2.5 px-4 py-2.5 text-sm text-slate-600 hover:bg-slate-50 transition-colors"
              >
                <span>👤</span>
                <span>My Account</span>
              </Link>
              <button
                onClick={handleLogout}
                className="w-full flex items-center gap-2.5 px-4 py-2.5 text-sm text-danger-500 hover:bg-danger-100 transition-colors"
              >
                <span>🚪</span>
                <span>Log out</span>
              </button>
            </div>
          )}

          {/* Strip button */}
          <button
            onClick={() => setUserMenuOpen((o) => !o)}
            className="w-full flex items-center gap-3 px-4 py-3 hover:bg-green-600 transition-colors text-left"
          >
            <div className="w-7 h-7 rounded-full bg-green-600 flex items-center justify-center shrink-0 text-white text-xs font-bold">
              {me.fullName.charAt(0).toUpperCase()}
            </div>
            <div className="min-w-0 flex-1">
              <p className="text-green-200 text-xs font-medium truncate">{me.fullName}</p>
              <p className="text-green-400 text-xs truncate">{me.role}</p>
            </div>
            <span className={`text-green-400 text-base leading-none transition-transform duration-200 ${userMenuOpen ? "rotate-180" : ""}`}>
              ‹
            </span>
          </button>
        </div>
      )}
    </aside>
    </>
  );
}
