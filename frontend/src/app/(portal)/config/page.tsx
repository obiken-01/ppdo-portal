"use client";

/**
 * Config Dashboard — RAL-71.
 *
 * Landing page for the Configuration section (replaces the interim RAL-72
 * redirect to /config/accounts). Shows one stat/link tile per config type with
 * its active record count, fetched from the RAL-70 config endpoints via the
 * lib/config.ts helpers ({ data, error, message } envelope; count = data.length).
 *
 * Access guard: only users with canManageConfig may view this page.
 */

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { fetchMe } from "@/lib/me-cache";
import { listAccounts, listDivisions, listFundingSources, listOffices, listPriceIndex } from "@/lib/config";

// ---------------------------------------------------------------------------
// Tile definitions
// ---------------------------------------------------------------------------

interface TileDef {
  key: string;
  icon: string;
  name: string;
  caption: string;
  href: string;
  load: () => Promise<number>;
}

const TILES: TileDef[] = [
  {
    key: "accounts",
    icon: "🧾",
    name: "Accounts",
    caption: "Chart of accounts for WFP expenditure entry.",
    href: "/config/accounts",
    load: async () => (await listAccounts({ active: "true" })).length,
  },
  {
    key: "offices",
    icon: "🏛️",
    name: "Offices",
    caption: "Provincial government offices for planning scope.",
    href: "/config/offices",
    load: async () => (await listOffices({ active: "true" })).length,
  },
  {
    key: "funding",
    icon: "💰",
    name: "Funding Sources",
    caption: "Budget funding sources used across AIP entries.",
    href: "/config/funding-sources",
    load: async () => (await listFundingSources({ active: "true" })).length,
  },
  {
    key: "priceIndex",
    icon: "🏷️",
    name: "Price Index",
    caption: "Procurement item catalogue searched from WFP line-item entry.",
    href: "/config/price-index",
    load: async () => (await listPriceIndex({ active: "true" })).length,
  },
  {
    key: "divisions",
    icon: "🏢",
    name: "Divisions",
    caption: "Per-office divisions that carry data scope and feature-permission flags.",
    href: "/config/divisions",
    load: async () => (await listDivisions({ active: "true" })).length,
  },
];

const USER_TILE: TileDef = {
  key: "users",
  icon: "👥",
  name: "User Management",
  caption: "Manage portal users, roles, and access permissions.",
  href: "/admin/users",
  load: async () => {
    const { data } = await api.get<unknown[]>("/users");
    return data.length;
  },
};

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function ConfigDashboardPage() {
  const router = useRouter();

  const [authChecked] = useState(true);
  const [canManageUsers, setCanManageUsers] = useState(false);
  // null = loading, number = count, "error" = failed to load this tile's count
  const [counts, setCounts] = useState<Record<string, number | "error" | null>>({
    accounts: null,
    offices: null,
    funding: null,
    priceIndex: null,
    divisions: null,
    users: null,
  });

  // ── Auth check ────────────────────────────────────────────────────────────────

  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canManageConfig) router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
        else setCanManageUsers(data.canManageUsers === true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load counts (each tile independently) ──────────────────────────────────────

  useEffect(() => {
    if (!authChecked) return;
    const tiles = canManageUsers ? [...TILES, USER_TILE] : TILES;
    for (const tile of tiles) {
      tile
        .load()
        .then((n) => setCounts((c) => ({ ...c, [tile.key]: n })))
        .catch(() => setCounts((c) => ({ ...c, [tile.key]: "error" })));
    }
  }, [authChecked, canManageUsers]);

  // ── Auth gate ─────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-full flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ── Render ────────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-5">
        {/* Header */}
        <div>
          <h1 className="text-lg font-bold text-slate-800">Configuration</h1>
          <p className="text-sm text-slate-500">
            Manage reference data used across AIP and WFP planning entries.
          </p>
        </div>

        {/* Tiles */}
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
          {[...TILES, ...(canManageUsers ? [USER_TILE] : [])].map((tile) => (
            <Link
              key={tile.key}
              href={tile.href}
              className="group bg-white border border-slate-200 hover:border-green-300 hover:shadow-sm transition-colors flex flex-col"
            >
              <div className="px-5 py-5 flex-1">
                <div className="flex items-start justify-between">
                  <span className="w-10 h-10 flex items-center justify-center text-xl bg-green-50 text-green-700">
                    {tile.icon}
                  </span>
                  <CountBadge value={counts[tile.key]} />
                </div>
                <h2 className="mt-4 text-base font-semibold text-slate-800">{tile.name}</h2>
                <p className="mt-1 text-sm text-slate-500 leading-relaxed">{tile.caption}</p>
              </div>
              <div className="px-5 py-3 border-t border-slate-100 flex items-center justify-end">
                <span className="text-sm font-medium text-green-600 group-hover:text-green-700 inline-flex items-center gap-1">
                  Manage
                  <span className="transition-transform group-hover:translate-x-0.5">→</span>
                </span>
              </div>
            </Link>
          ))}
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Count badge — loading spinner / number / dash on error
// ---------------------------------------------------------------------------

function CountBadge({ value }: { value: number | "error" | null }) {
  if (value === null) {
    return <span className="w-5 h-5 border-2 border-slate-300 border-t-transparent rounded-full animate-spin" />;
  }
  if (value === "error") {
    return <span className="text-2xl font-bold text-slate-300" title="Could not load count">—</span>;
  }
  return <span className="text-2xl font-bold text-slate-800 tabular-nums">{value}</span>;
}
