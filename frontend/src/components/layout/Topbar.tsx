"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { MeResponse } from "@/types";

interface TopbarProps {
  me: MeResponse | null;
  title: string;
}

// Inventory breadcrumb labels keyed by path prefix (longest match wins)
const INVENTORY_CRUMBS: { prefix: string; label: string }[] = [
  { prefix: "/inventory/create-pr",        label: "Create PR" },
  { prefix: "/inventory/receive-delivery", label: "Receive Delivery" },
  { prefix: "/inventory/items-master",     label: "Items Master" },
  { prefix: "/inventory/pr-report",        label: "PR Report" },
  { prefix: "/inventory/distribution",      label: "Distribution" },
  { prefix: "/inventory/item-ledger",      label: "Stock Overview" },
  { prefix: "/inventory/pr-register",      label: "PR List" },
  { prefix: "/inventory",                  label: "Dashboard" },
];

function InlineInventoryBreadcrumb() {
  const pathname = usePathname();
  const matched  = INVENTORY_CRUMBS.find(
    (m) => pathname === m.prefix || pathname.startsWith(m.prefix + "/")
  );
  const isSubPage = matched && matched.prefix !== "/inventory";

  return (
    <nav aria-label="breadcrumb" className="flex items-center gap-1.5 text-sm">
      {isSubPage ? (
        <>
          <Link
            href="/inventory"
            className="font-medium text-green-600 hover:text-green-700 hover:underline transition-colors"
          >
            Inventory
          </Link>
          <span className="text-slate-300">›</span>
          <span className="font-semibold text-slate-700">{matched!.label}</span>
        </>
      ) : (
        <span className="font-semibold text-slate-700">Inventory</span>
      )}
    </nav>
  );
}

export default function Topbar({ me, title }: TopbarProps) {
  const pathname = usePathname();
  const isInventory = pathname.startsWith("/inventory");

  return (
    <header className="h-14 bg-white border-b border-slate-200 flex items-center justify-between px-6 shrink-0 shadow-sm">
      {isInventory ? (
        <InlineInventoryBreadcrumb />
      ) : (
        <h1 className="text-sm font-semibold text-slate-700">{title}</h1>
      )}

      {me && (
        <span className="text-sm text-slate-500 hidden sm:block">
          {me.fullName}
          <span className="ml-1.5 text-xs text-slate-400">({me.role})</span>
        </span>
      )}
    </header>
  );
}
