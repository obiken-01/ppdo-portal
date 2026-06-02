"use client";

/**
 * Resource Links sidebar widget for the Main Dashboard.
 * Fetches GET /api/resource-links and groups links by category.
 * Gracefully shows an empty state when the endpoint is not yet available.
 */

import { useEffect, useState } from "react";
import api from "@/lib/api";
import type { ResourceLink } from "@/types";

type GroupedLinks = Record<string, ResourceLink[]>;

export default function ResourceLinksWidget() {
  const [grouped, setGrouped]   = useState<GroupedLinks>({});
  const [loading, setLoading]   = useState(true);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    api.get<ResourceLink[]>("/resource-links")
      .then(({ data }) => {
        const map: GroupedLinks = {};
        data
          .sort((a, b) => a.categoryOrder - b.categoryOrder || a.linkOrder - b.linkOrder)
          .forEach((link) => {
            if (!map[link.category]) map[link.category] = [];
            map[link.category].push(link);
          });
        setGrouped(map);
      })
      .catch(() => setUnavailable(true))
      .finally(() => setLoading(false));
  }, []);

  return (
    <div className="bg-white rounded-xl border border-slate-200 shadow-sm flex flex-col">
      <div className="px-4 py-3 border-b border-slate-100">
        <h2 className="text-sm font-semibold text-slate-700 flex items-center gap-2">
          🔗 Resource Links
        </h2>
      </div>

      <div className="flex-1 overflow-y-auto px-3 py-2 space-y-3 max-h-72">
        {loading && (
          <div className="flex justify-center py-6">
            <div className="w-5 h-5 border-2 border-green-600 border-t-transparent rounded-full animate-spin" />
          </div>
        )}

        {!loading && unavailable && (
          <p className="text-xs text-slate-400 text-center py-4">
            Resource links are not yet available.
          </p>
        )}

        {!loading && !unavailable && Object.keys(grouped).length === 0 && (
          <p className="text-xs text-slate-400 text-center py-4">
            No resource links found.
          </p>
        )}

        {!loading && !unavailable && Object.entries(grouped).map(([category, links]) => (
          <div key={category}>
            <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-1 px-1">
              {category}
            </p>
            <div className="space-y-0.5">
              {links.map((link) => (
                <a
                  key={link.id}
                  href={link.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-2 px-2 py-1.5 rounded-lg text-xs text-slate-600 hover:bg-green-50 hover:text-green-700 transition-colors group"
                >
                  <span className="text-slate-300 group-hover:text-green-400 transition-colors">↗</span>
                  <span className="truncate">{link.title}</span>
                </a>
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
