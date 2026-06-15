"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import type { MeResponse } from "@/types";

interface TopbarProps {
  me: MeResponse | null;
  title: string;
}

// A breadcrumb section: a root page plus its sub-pages (longest prefix wins).
// On the root path the breadcrumb shows just the root label; on a sub-page it
// shows "Root › Sub-page" with the root as a link back to the section root.
interface Section {
  root: string;
  rootLabel: string;
  crumbs: { prefix: string; label: string }[];
}

const SECTIONS: Section[] = [
  {
    root: "/inventory",
    rootLabel: "Inventory",
    crumbs: [
      { prefix: "/inventory/create-pr",        label: "Create PR" },
      { prefix: "/inventory/receive-delivery", label: "Receive Delivery" },
      { prefix: "/inventory/items-master",     label: "Items Master" },
      { prefix: "/inventory/pr-report",        label: "PR Report" },
      { prefix: "/inventory/distribution",     label: "Distribution" },
      { prefix: "/inventory/item-ledger",      label: "Stock Overview" },
      { prefix: "/inventory/pr-register",      label: "PR List" },
    ],
  },
  {
    root: "/config",
    rootLabel: "Configuration",
    crumbs: [
      { prefix: "/config/accounts",        label: "Accounts" },
      { prefix: "/config/offices",         label: "Offices" },
      { prefix: "/config/funding-sources", label: "Funding Sources" },
    ],
  },
];

function matchesPrefix(pathname: string, prefix: string) {
  return pathname === prefix || pathname.startsWith(prefix + "/");
}

function SectionBreadcrumb({ section, pathname }: { section: Section; pathname: string }) {
  const sub = section.crumbs.find((c) => matchesPrefix(pathname, c.prefix));

  return (
    <nav aria-label="breadcrumb" className="flex items-center gap-1.5 text-sm">
      {sub ? (
        <>
          <Link
            href={section.root}
            className="font-medium text-green-600 hover:text-green-700 hover:underline transition-colors"
          >
            {section.rootLabel}
          </Link>
          <span className="text-slate-300">›</span>
          <span className="font-semibold text-slate-700">{sub.label}</span>
        </>
      ) : (
        <span className="font-semibold text-slate-700">{section.rootLabel}</span>
      )}
    </nav>
  );
}

export default function Topbar({ me, title }: TopbarProps) {
  const pathname = usePathname();
  const section  = SECTIONS.find((s) => matchesPrefix(pathname, s.root));

  return (
    <header className="h-14 bg-white border-b border-slate-200 flex items-center justify-between px-6 shrink-0 shadow-sm">
      {section ? (
        <SectionBreadcrumb section={section} pathname={pathname} />
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
