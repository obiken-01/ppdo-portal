"use client";

/**
 * Resource Links compact widget for the Main Dashboard sidebar.
 *
 * Features:
 *  - Collapsible categories (all expanded by default)
 *  - Max 5 links shown per category — excess are hidden
 *  - "View All" link at the bottom → /resource-links full page
 *  - All links open in a new tab
 */

import { useEffect, useState } from "react";
import Link from "next/link";
import api from "@/lib/api";
import type { ResourceLinkCategory } from "@/types";

const MAX_LINKS_PER_CATEGORY = 5;

export default function ResourceLinksWidget() {
  const [categories, setCategories]     = useState<ResourceLinkCategory[]>([]);
  const [loading, setLoading]           = useState(true);
  const [unavailable, setUnavailable]   = useState(false);
  const [collapsed, setCollapsed]       = useState<Record<string, boolean>>({});

  useEffect(() => {
    api.get<ResourceLinkCategory[]>("/resource-links")
      .then(({ data }) => setCategories(data))
      .catch(() => setUnavailable(true))
      .finally(() => setLoading(false));
  }, []);

  function toggleCategory(category: string) {
    setCollapsed((prev) => ({ ...prev, [category]: !prev[category] }));
  }

  return (
    <div className="bg-white rounded-xl border border-slate-200 shadow-sm flex flex-col h-full">
      {/* Header */}
      <div className="px-4 py-3 border-b border-slate-100 shrink-0">
        <h2 className="text-sm font-semibold text-slate-700">🔗 Resource Links</h2>
      </div>

      {/* Body */}
      <div className="flex-1 overflow-y-auto px-3 py-2 space-y-2 min-h-0">
        {loading && (
          <div className="flex justify-center py-6">
            <div className="w-5 h-5 border-2 border-green-600 border-t-transparent rounded-full animate-spin" />
          </div>
        )}

        {!loading && unavailable && (
          <p className="text-xs text-slate-400 text-center py-4">
            Resource links unavailable.
          </p>
        )}

        {!loading && !unavailable && categories.length === 0 && (
          <p className="text-xs text-slate-400 text-center py-4">
            No resource links found.
          </p>
        )}

        {!loading && !unavailable && categories.map((cat) => {
          const isCollapsed  = collapsed[cat.category] ?? false;
          const visible      = cat.links.slice(0, MAX_LINKS_PER_CATEGORY);
          const hiddenCount  = cat.links.length - visible.length;

          return (
            <div key={cat.category}>
              {/* Category header — clickable to collapse */}
              <button
                onClick={() => toggleCategory(cat.category)}
                className="w-full flex items-center justify-between px-1 py-0.5 group"
              >
                <span className="text-xs font-semibold text-slate-400 uppercase tracking-wide group-hover:text-slate-600 transition-colors text-left">
                  {cat.category}
                </span>
                <span className={`text-xs text-slate-300 transition-transform duration-200 ${isCollapsed ? "" : "rotate-90"}`}>
                  ›
                </span>
              </button>

              {/* Links */}
              {!isCollapsed && (
                <div className="space-y-0.5 mt-0.5">
                  {visible.map((link) => (
                    <a
                      key={link.id}
                      href={link.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="flex items-center gap-2 px-2 py-1.5 rounded-lg text-xs text-slate-600 hover:bg-green-50 hover:text-green-700 transition-colors group/link"
                    >
                      <span className="text-slate-300 group-hover/link:text-green-400 shrink-0 transition-colors">↗</span>
                      <span className="truncate">{link.title}</span>
                    </a>
                  ))}
                  {hiddenCount > 0 && (
                    <Link
                      href="/resource-links"
                      className="block px-2 py-1 text-xs text-slate-400 hover:text-green-600 transition-colors"
                    >
                      +{hiddenCount} more…
                    </Link>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </div>

      {/* Footer — View All */}
      {!loading && !unavailable && categories.length > 0 && (
        <div className="px-4 py-2 border-t border-slate-100 shrink-0">
          <Link
            href="/resource-links"
            className="text-xs text-green-600 hover:text-green-700 font-medium transition-colors"
          >
            View all resource links →
          </Link>
        </div>
      )}
    </div>
  );
}
