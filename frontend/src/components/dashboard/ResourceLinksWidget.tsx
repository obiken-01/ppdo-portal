"use client";

/**
 * Resource Links sidebar widget for the Main Dashboard.
 * Fetches GET /api/resource-links — returns ResourceLinkCategoryDto[]
 * (already grouped and sorted by the backend).
 * Gracefully shows an empty state when the endpoint is not yet available.
 */

import { useEffect, useState } from "react";
import api from "@/lib/api";

interface ResourceLinkItem {
  id: string;
  title: string;
  url: string;
  linkOrder: number;
}

interface ResourceLinkCategory {
  category: string;
  categoryOrder: number;
  links: ResourceLinkItem[];
}

export default function ResourceLinksWidget() {
  const [categories, setCategories] = useState<ResourceLinkCategory[]>([]);
  const [loading, setLoading]       = useState(true);
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    api.get<ResourceLinkCategory[]>("/resource-links")
      .then(({ data }) => setCategories(data))
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

        {!loading && !unavailable && categories.length === 0 && (
          <p className="text-xs text-slate-400 text-center py-4">
            No resource links found.
          </p>
        )}

        {!loading && !unavailable && categories.map((cat) => (
          <div key={cat.category}>
            <p className="text-xs font-semibold text-slate-400 uppercase tracking-wide mb-1 px-1">
              {cat.category}
            </p>
            <div className="space-y-0.5">
              {cat.links.map((link) => (
                <a
                  key={link.id}
                  href={link.url}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-2 px-2 py-1.5 rounded-lg text-xs text-slate-600 hover:bg-green-50 hover:text-green-700 transition-colors group"
                >
                  <span className="text-slate-300 group-hover:text-green-400 transition-colors shrink-0">↗</span>
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
