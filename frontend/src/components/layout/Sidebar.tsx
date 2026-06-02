"use client";

/**
 * PPDO Portal — main navigation sidebar.
 * Uses PPDO green-700 (#196638) as background to match the Penpot design.
 *
 * Inventory group is collapsible. Visibility rules:
 *   - Inventory parent shown if canAccessInventory OR canAccessReports
 *   - "Inventory Dashboard" child shown if canAccessInventory
 *   - "Inventory Report"    child shown if canAccessReports
 *   - Parent auto-expands when the current path is under /inventory
 */

import { useState, useEffect } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import type { MeResponse } from "@/types";

interface SidebarProps {
  me: MeResponse | null;
}

export default function Sidebar({ me }: SidebarProps) {
  const pathname = usePathname();

  // Auto-expand inventory group when on an inventory route
  const [inventoryOpen, setInventoryOpen] = useState(
    () => pathname.startsWith("/inventory")
  );

  useEffect(() => {
    if (pathname.startsWith("/inventory")) setInventoryOpen(true);
  }, [pathname]);

  function isActive(href: string) {
    return pathname === href || pathname.startsWith(href + "/");
  }

  // ── Permission helpers ─────────────────────────────────────────────────────
  const hasInventory = me?.canAccessInventory === true;
  const hasReport    = me?.canAccessReports    === true;
  const showInventoryGroup = hasInventory || hasReport;
  const showManageUsers    = me?.canManageUsers === true;

  // ── Shared link class ──────────────────────────────────────────────────────
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
      {/* Logo / brand */}
      <div className="flex items-center gap-3 px-5 py-5 border-b border-green-600">
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
          <p className="text-green-300 text-xs leading-tight truncate">Occ. Mindoro</p>
        </div>
      </div>

      {/* Navigation */}
      <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto">

        {/* Dashboard */}
        <Link href="/dashboard" className={linkCls(isActive("/dashboard"))}>
          <span className="text-base leading-none w-5 text-center">🏠</span>
          <span className="truncate">Dashboard</span>
        </Link>

        {/* Inventory group — collapsible */}
        {showInventoryGroup && (
          <div>
            {/* Parent toggle */}
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
              <span className={`text-xs transition-transform ${inventoryOpen ? "rotate-90" : ""}`}>
                ›
              </span>
            </button>

            {/* Children */}
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

        {/* Resource Links */}
        <Link href="/resource-links" className={linkCls(isActive("/resource-links"))}>
          <span className="text-base leading-none w-5 text-center">🔗</span>
          <span className="truncate">Resource Links</span>
        </Link>

        {/* User Management */}
        {showManageUsers && (
          <Link href="/admin/users" className={linkCls(isActive("/admin/users"))}>
            <span className="text-base leading-none w-5 text-center">👥</span>
            <span className="truncate">User Management</span>
          </Link>
        )}

      </nav>

      {/* User info strip */}
      {me && (
        <div className="px-4 py-3 border-t border-green-600">
          <p className="text-green-200 text-xs truncate">{me.fullName}</p>
          <p className="text-green-400 text-xs truncate">{me.role}</p>
        </div>
      )}
    </aside>
  );
}
