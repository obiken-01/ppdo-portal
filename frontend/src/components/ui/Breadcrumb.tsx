"use client";

/**
 * Breadcrumb — compact section breadcrumb (v1.1 shared).
 *
 * Renders a "Parent › … › Current" trail above a page heading. Items with an
 * `href` are links (muted, hover-darken); the last item (typically without an
 * href) is the current page (non-link). Flat design, design tokens.
 *
 * Usage:
 *   <Breadcrumb
 *     items={[{ label: "Configuration", href: "/config" }, { label: "Accounts" }]}
 *   />
 */

import Link from "next/link";

export interface BreadcrumbItem {
  label: string;
  /** When set, the crumb is a link. Omit for the current (last) page. */
  href?: string;
}

export default function Breadcrumb({ items }: { items: BreadcrumbItem[] }) {
  return (
    <nav aria-label="Breadcrumb" className="flex items-center gap-1.5 text-xs text-slate-400 mb-1">
      {items.map((item, i) => {
        const isLast = i === items.length - 1;
        return (
          <span key={`${item.label}-${i}`} className="flex items-center gap-1.5">
            {item.href && !isLast ? (
              <Link href={item.href} className="hover:text-slate-600 transition-colors">
                {item.label}
              </Link>
            ) : (
              <span className={isLast ? "text-slate-500 font-medium" : ""}>{item.label}</span>
            )}
            {!isLast && <span className="text-slate-300">›</span>}
          </span>
        );
      })}
    </nav>
  );
}
