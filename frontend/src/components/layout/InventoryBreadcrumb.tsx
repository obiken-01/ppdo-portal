"use client";

/**
 * Breadcrumb strip shown at the top of every inventory page.
 * Rendered by the inventory nested layout.
 *
 * Examples:
 *   Inventory > Dashboard
 *   Inventory > Create PR
 *   Inventory > PR Report
 */

import Link from "next/link";
import { usePathname } from "next/navigation";

interface Crumb {
  label: string;
  href?: string;
}

// Map exact or prefix paths → human-readable label
const INVENTORY_LABELS: { prefix: string; label: string }[] = [
  { prefix: "/inventory/create-pr",          label: "Create PR" },
  { prefix: "/inventory/receive-delivery",   label: "Receive Delivery" },
  { prefix: "/inventory/items-master",       label: "Items Master" },
  { prefix: "/inventory/pr-report",          label: "PR Report" },
  { prefix: "/inventory/distribution",        label: "Distribution" },
  { prefix: "/inventory/item-ledger",        label: "Stock Overview" },
  { prefix: "/inventory/pr-register",        label: "PR List" },
  { prefix: "/inventory",                    label: "Dashboard" },
];

export default function InventoryBreadcrumb() {
  const pathname = usePathname();

  const crumbs: Crumb[] = [{ label: "Inventory", href: "/inventory" }];

  const matched = INVENTORY_LABELS.find((m) =>
    pathname === m.prefix || pathname.startsWith(m.prefix + "/")
  );

  if (matched && matched.prefix !== "/inventory") {
    crumbs.push({ label: matched.label });
  }

  return (
    <nav
      aria-label="breadcrumb"
      className="flex items-center gap-1.5 px-6 py-2.5 bg-white border-b border-slate-200 text-xs text-slate-500"
    >
      {crumbs.map((crumb, i) => {
        const isLast = i === crumbs.length - 1;
        return (
          <span key={i} className="flex items-center gap-1.5">
            {i > 0 && <span className="text-slate-300">›</span>}
            {!isLast && crumb.href ? (
              <Link
                href={crumb.href}
                className="font-medium text-green-600 hover:underline hover:text-green-700 transition-colors"
              >
                {crumb.label}
              </Link>
            ) : (
              <span className={isLast ? "font-semibold text-slate-700" : "font-medium text-green-600"}>
                {crumb.label}
              </span>
            )}
          </span>
        );
      })}
    </nav>
  );
}
