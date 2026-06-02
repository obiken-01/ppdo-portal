"use client";

/**
 * PPDO Portal — main navigation sidebar.
 * Always visible on desktop; collapses on mobile via the isOpen prop.
 * Uses PPDO green-700 (#196638) as background to match the Penpot design.
 */

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { MeResponse } from "@/types";

interface NavItem {
  label: string;
  href: string;
  icon: string;
  /** If set, only shown when the user has this permission. */
  requiredPermission?: keyof Pick<
    MeResponse,
    "canManageUsers" | "canAccessInventory" | "canManageResourceLinks"
  >;
}

const NAV_ITEMS: NavItem[] = [
  { label: "Dashboard",       href: "/dashboard",       icon: "🏠" },
  { label: "Inventory",       href: "/inventory",       icon: "📦", requiredPermission: "canAccessInventory" },
  { label: "Resource Links",  href: "/resource-links",  icon: "🔗" },
  { label: "User Management", href: "/admin/users",     icon: "👥", requiredPermission: "canManageUsers" },
];

interface SidebarProps {
  me: MeResponse | null;
}

export default function Sidebar({ me }: SidebarProps) {
  const pathname = usePathname();

  function isActive(href: string) {
    return pathname === href || pathname.startsWith(href + "/");
  }

  function isVisible(item: NavItem): boolean {
    if (!item.requiredPermission) return true;
    if (!me) return false;
    return me[item.requiredPermission] === true;
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
        {NAV_ITEMS.filter(isVisible).map((item) => (
          <Link
            key={item.href}
            href={item.href}
            className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
              isActive(item.href)
                ? "bg-green-800 text-white"
                : "text-green-100 hover:bg-green-600 hover:text-white"
            }`}
          >
            <span className="text-base leading-none w-5 text-center">{item.icon}</span>
            <span className="truncate">{item.label}</span>
          </Link>
        ))}
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
