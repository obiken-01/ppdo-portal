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
 */

import { useState, useEffect, useRef } from "react";
import Link from "next/link";
import { useRouter, usePathname } from "next/navigation";
import api from "@/lib/api";
import { auth } from "@/lib/auth";
import type { MeResponse } from "@/types";

const APP_VERSION = "v1.1.0";

interface SidebarProps {
  me: MeResponse | null;
}

export default function Sidebar({ me }: SidebarProps) {
  const pathname  = usePathname();
  const router    = useRouter();
  const menuRef   = useRef<HTMLDivElement>(null);

  const [inventoryOpen, setInventoryOpen] = useState(
    () => pathname.startsWith("/inventory")
  );
  const [configOpen, setConfigOpen] = useState(
    () => pathname.startsWith("/config")
  );
  const [budgetPlanningOpen, setBudgetPlanningOpen] = useState(
    () => pathname.startsWith("/budget-planning")
  );
  const [userMenuOpen, setUserMenuOpen] = useState(false);

  // Auto-expand inventory when navigating to an inventory route
  useEffect(() => {
    if (pathname.startsWith("/inventory")) setInventoryOpen(true);
  }, [pathname]);

  // Auto-expand configuration when navigating to a config route
  useEffect(() => {
    if (pathname.startsWith("/config")) setConfigOpen(true);
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

  async function handleLogout() {
    setUserMenuOpen(false);
    try { await api.post("/auth/logout"); } catch { /* ignore */ }
    auth.logout();
    router.replace("/login");
  }

  // ── Helpers ────────────────────────────────────────────────────────────────

  function isActive(href: string) {
    return pathname === href || pathname.startsWith(href + "/");
  }

  // Non-PPDO office users (officeId set) get Budget Planning ONLY — no Dashboard,
  // Inventory, Resource Links (PPDO-internal), Configuration, or User Management.
  const isOfficeUser       = me != null && me.officeId != null;

  const hasInventory       = me?.canAccessInventory      === true;
  const hasReport          = me?.canAccessReports         === true;
  const showInventoryGroup = !isOfficeUser && (hasInventory || hasReport);
  const showManageUsers    = !isOfficeUser && me?.canManageUsers === true;
  const showBudgetPlanning = me?.canAccessBudgetPlanning === true;
  const showConfig         = !isOfficeUser && me?.canManageConfig === true;
  const showResourceLinks  = !isOfficeUser;
  const showDashboard      = !isOfficeUser;

  function linkCls(active: boolean) {
    return `flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
      active
        ? "bg-green-800 text-white"
        : "text-green-100 hover:bg-green-600 hover:text-white"
    }`;
  }

  function childLinkCls(active: boolean) {
    return `flex items-center gap-2 pl-9 pr-3 py-2 rounded-lg text-sm transition-colors ${
      active
        ? "bg-green-800 text-white font-medium"
        : "text-green-200 hover:bg-green-600 hover:text-white"
    }`;
  }

  return (
    <aside className="w-56 shrink-0 bg-green-700 flex flex-col h-full">

      {/* ── Logo / brand — click to go to Dashboard ─────────────────────── */}
      <Link
        href="/dashboard"
        className="flex items-center gap-3 px-5 py-5 border-b border-green-600 hover:bg-green-600 transition-colors group"
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src="/images/ppdo-logo-placeholder.png"
          alt="PPDO"
          width={32}
          height={32}
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

        {/* Inventory group — collapsible */}
        {showInventoryGroup && (
          <div>
            <button
              onClick={() => setInventoryOpen((o) => !o)}
              className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
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
              className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
                isActive("/budget-planning")
                  ? "bg-green-800 text-white"
                  : "text-green-100 hover:bg-green-600 hover:text-white"
              }`}
            >
              <span className="text-base leading-none w-5 text-center">💰</span>
              <span className="flex-1 text-left truncate">Budget Planning</span>
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
                <Link href="/budget-planning/ldip" className={childLinkCls(isActive("/budget-planning/ldip"))}>
                  <span className="text-xs">•</span>
                  <span className="truncate">LDIP</span>
                </Link>
                <Link href="/budget-planning/aip" className={childLinkCls(isActive("/budget-planning/aip"))}>
                  <span className="text-xs">•</span>
                  <span className="truncate">AIP</span>
                </Link>
                <Link href="/budget-planning/wfp" className={childLinkCls(isActive("/budget-planning/wfp"))}>
                  <span className="text-xs">•</span>
                  <span className="truncate">WFP</span>
                </Link>
              </div>
            )}
          </div>
        )}

        {/* Configuration — collapsible group; PPDO users with CanManageConfig */}
        {showConfig && (
          <div>
            <button
              onClick={() => setConfigOpen((o) => !o)}
              className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
                isActive("/config")
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
              </div>
            )}
          </div>
        )}

        {/* Resource Links — PPDO-internal; hidden for office users */}
        {showResourceLinks && (
          <Link href="/resource-links" className={linkCls(isActive("/resource-links"))}>
            <span className="text-base leading-none w-5 text-center">🔗</span>
            <span className="truncate">Resource Links</span>
          </Link>
        )}

        {/* User Management */}
        {showManageUsers && (
          <Link href="/admin/users" className={linkCls(isActive("/admin/users"))}>
            <span className="text-base leading-none w-5 text-center">👥</span>
            <span className="truncate">User Management</span>
          </Link>
        )}

      </nav>

      {/* ── User strip — click to toggle popup menu ──────────────────────── */}
      {me && (
        <div ref={menuRef} className="relative border-t border-green-600">

          {/* Popup menu — renders above the strip */}
          {userMenuOpen && (
            <div className="absolute bottom-full left-3 right-3 mb-1 bg-white rounded-xl shadow-xl border border-slate-100 overflow-hidden z-50">
              <div className="px-4 py-2.5 border-b border-slate-100">
                <p className="text-xs font-semibold text-slate-700 truncate">{me.fullName}</p>
                <p className="text-xs text-slate-400 truncate">{me.username}</p>
              </div>
              <Link
                href="/profile"
                onClick={() => setUserMenuOpen(false)}
                className="flex items-center gap-2.5 px-4 py-2.5 text-sm text-slate-600 hover:bg-slate-50 transition-colors"
              >
                <span>👤</span>
                <span>My Profile</span>
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
  );
}
