"use client";

/**
 * Resource Links page — RAL-36.
 *
 * Displays all active resource links grouped by category.
 * Permission-aware actions:
 *   Admin / SuperAdmin — Add, Edit, Delete
 *   Staff (canManageResourceLinks) — Add only
 *   Staff / Observer (no permission) — View only
 *
 * All links open in a new tab.
 * API endpoints: GET/POST/PUT/DELETE /api/resource-links
 */

import { useCallback, useEffect, useState } from "react";
import api from "@/lib/api";
import type {
  CreateResourceLinkRequest,
  MeResponse,
  ResourceLinkCategory,
  ResourceLinkItem,
  UpdateResourceLinkRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const CATEGORIES = [
  "Supply & Property Management",
  "Records Management",
  "Human Resource Management",
  "Financial Management",
  "General",
];

function isAdminOrSuper(role: string) {
  return role === "SuperAdmin" || role === "Admin";
}

// ---------------------------------------------------------------------------
// Modal — Add / Edit link
// ---------------------------------------------------------------------------

interface LinkFormModalProps {
  title: string;
  initial: Partial<CreateResourceLinkRequest>;
  categories: string[];
  saving: boolean;
  error: string | null;
  onSubmit: (values: CreateResourceLinkRequest) => void;
  onClose: () => void;
}

function LinkFormModal({
  title, initial, categories, saving, error, onSubmit, onClose,
}: LinkFormModalProps) {
  const [form, setForm] = useState<CreateResourceLinkRequest>({
    title: initial.title ?? "",
    url: initial.url ?? "",
    category: initial.category ?? categories[0] ?? "",
    categoryOrder: initial.categoryOrder ?? 1,
    linkOrder: initial.linkOrder ?? 99,
  });

  const backdropRef = (e: React.MouseEvent<HTMLDivElement>) => {
    if ((e.target as HTMLElement).dataset.backdrop) onClose();
  };

  return (
    <div
      data-backdrop="true"
      onClick={backdropRef}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
    >
      <div className="bg-white rounded-xl shadow-xl w-full max-w-md" onClick={(e) => e.stopPropagation()}>
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200">
          <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
          <button onClick={onClose} className="text-slate-400 hover:text-slate-600 text-xl leading-none">×</button>
        </div>

        <div className="px-6 py-5 space-y-4">
          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">Title *</label>
            <input
              value={form.title}
              onChange={(e) => setForm((f) => ({ ...f, title: e.target.value }))}
              placeholder="PR Monitoring"
              className="w-full px-3 py-2 text-sm border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-green-600"
            />
          </div>

          <div>
            <label className="block text-xs font-medium text-slate-600 mb-1">URL *</label>
            <input
              value={form.url}
              onChange={(e) => setForm((f) => ({ ...f, url: e.target.value }))}
              placeholder="https://docs.google.com/..."
              className="w-full px-3 py-2 text-sm border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-green-600"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Category *</label>
              <select
                value={form.category}
                onChange={(e) => setForm((f) => ({ ...f, category: e.target.value }))}
                className="w-full px-3 py-2 text-sm border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-green-600 bg-white"
              >
                {categories.map((c) => <option key={c} value={c}>{c}</option>)}
              </select>
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-600 mb-1">Link Order</label>
              <input
                type="number"
                min={1}
                value={form.linkOrder}
                onChange={(e) => setForm((f) => ({ ...f, linkOrder: parseInt(e.target.value) || 1 }))}
                className="w-full px-3 py-2 text-sm border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-green-600"
              />
            </div>
          </div>

          {error && (
            <div className="rounded-lg bg-danger-100 border border-danger-500/30 px-4 py-2">
              <p className="text-xs text-danger-500">{error}</p>
            </div>
          )}
        </div>

        <div className="flex justify-end gap-3 px-6 py-4 border-t border-slate-200">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm border border-slate-200 rounded-lg text-slate-600 hover:bg-slate-50 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={() => onSubmit(form)}
            disabled={saving}
            className="flex items-center gap-2 px-5 py-2 text-sm bg-green-600 text-white font-medium rounded-lg hover:bg-green-500 transition-colors disabled:opacity-60"
          >
            {saving && <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
            {saving ? "Saving…" : "Save"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Confirm delete dialog
// ---------------------------------------------------------------------------

function ConfirmDeleteModal({
  linkTitle, loading, onConfirm, onClose,
}: { linkTitle: string; loading: boolean; onConfirm: () => void; onClose: () => void }) {
  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
      onClick={onClose}
    >
      <div className="bg-white rounded-xl shadow-xl w-full max-w-sm p-6" onClick={(e) => e.stopPropagation()}>
        <h2 className="text-sm font-semibold text-slate-800 mb-2">Delete Link</h2>
        <p className="text-sm text-slate-600 mb-6">
          Delete <span className="font-medium">&ldquo;{linkTitle}&rdquo;</span>? This cannot be undone.
        </p>
        <div className="flex justify-end gap-3">
          <button onClick={onClose} className="px-4 py-2 text-sm border border-slate-200 rounded-lg text-slate-600 hover:bg-slate-50 transition-colors">
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={loading}
            className="flex items-center gap-2 px-5 py-2 text-sm bg-danger-500 text-white font-medium rounded-lg hover:bg-red-600 transition-colors disabled:opacity-60"
          >
            {loading && <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
            Delete
          </button>
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function ResourceLinksPage() {
  const [me, setMe]                     = useState<MeResponse | null>(null);
  const [categories, setCategories]     = useState<ResourceLinkCategory[]>([]);
  const [loading, setLoading]           = useState(true);
  const [fetchError, setFetchError]     = useState<string | null>(null);
  const [search, setSearch]             = useState("");

  // Modals
  const [showAdd, setShowAdd]           = useState(false);
  const [editTarget, setEditTarget]     = useState<ResourceLinkItem | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<ResourceLinkItem | null>(null);

  // Action state
  const [saving, setSaving]             = useState(false);
  const [deleting, setDeleting]         = useState(false);
  const [formError, setFormError]       = useState<string | null>(null);

  // Derive existing category names for the form dropdown
  const existingCategories = categories.map((c) => c.category);
  const allCategories = Array.from(new Set([...CATEGORIES, ...existingCategories]));

  // ---------------------------------------------------------------------------
  // Load data
  // ---------------------------------------------------------------------------

  const loadData = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      const [meRes, linksRes] = await Promise.all([
        api.get<MeResponse>("/auth/me"),
        api.get<ResourceLinkCategory[]>("/resource-links"),
      ]);
      setMe(meRes.data);
      setCategories(linksRes.data);
    } catch {
      setFetchError("Failed to load resource links. Please try again.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { loadData(); }, [loadData]);

  // ---------------------------------------------------------------------------
  // Filtered view
  // ---------------------------------------------------------------------------

  const q = search.toLowerCase().trim();
  const filtered: ResourceLinkCategory[] = q
    ? categories
        .map((cat) => ({
          ...cat,
          links: cat.links.filter(
            (l) => l.title.toLowerCase().includes(q) || l.category.toLowerCase().includes(q)
          ),
        }))
        .filter((cat) => cat.links.length > 0)
    : categories;

  // ---------------------------------------------------------------------------
  // Handlers — Add
  // ---------------------------------------------------------------------------

  async function handleAdd(values: CreateResourceLinkRequest) {
    setSaving(true);
    setFormError(null);
    try {
      await api.post("/resource-links", values);
      setShowAdd(false);
      await loadData();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: string } })?.response?.data;
      setFormError(typeof msg === "string" ? msg : "Failed to add link.");
    } finally {
      setSaving(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Handlers — Edit
  // ---------------------------------------------------------------------------

  async function handleEdit(values: UpdateResourceLinkRequest) {
    if (!editTarget) return;
    setSaving(true);
    setFormError(null);
    try {
      await api.put(`/resource-links/${editTarget.id}`, values);
      setEditTarget(null);
      await loadData();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: string } })?.response?.data;
      setFormError(typeof msg === "string" ? msg : "Failed to update link.");
    } finally {
      setSaving(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Handlers — Delete
  // ---------------------------------------------------------------------------

  async function handleDelete() {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await api.delete(`/resource-links/${deleteTarget.id}`);
      setDeleteTarget(null);
      await loadData();
    } catch {
      // keep modal open — user can retry
    } finally {
      setDeleting(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Permission helpers
  // ---------------------------------------------------------------------------

  const canAdd    = me ? (isAdminOrSuper(me.role) || me.canManageResourceLinks) : false;
  const canEdit   = me ? isAdminOrSuper(me.role) : false;
  const canDelete = me ? isAdminOrSuper(me.role) : false;

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className="p-5 space-y-4">
      {/* Toolbar */}
      <div className="flex items-center gap-3">
        <input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search links…"
          className="flex-1 px-4 py-2.5 text-sm border border-slate-200 bg-white rounded-lg shadow-sm focus:outline-none focus:ring-2 focus:ring-green-600"
        />
        {search && (
          <button onClick={() => setSearch("")} className="text-sm text-slate-400 hover:text-slate-600 px-2">
            Clear
          </button>
        )}
        {canAdd && (
          <button
            onClick={() => { setFormError(null); setShowAdd(true); }}
            className="flex items-center gap-1.5 bg-green-600 text-white text-sm font-semibold px-4 py-2.5 rounded-lg hover:bg-green-500 transition-colors shadow-sm shrink-0"
          >
            <span className="text-base leading-none">+</span>
            Add Link
          </button>
        )}
      </div>

      {/* Content */}
      {loading ? (
        <div className="flex justify-center py-20">
          <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
        </div>
      ) : fetchError ? (
        <div className="text-center py-16 space-y-2">
          <p className="text-sm text-danger-500">{fetchError}</p>
          <button onClick={loadData} className="text-sm text-green-600 hover:underline">Retry</button>
        </div>
      ) : filtered.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-20 gap-2 text-slate-400">
          <span className="text-3xl">🔗</span>
          <p className="text-sm">{search ? "No links match your search." : "No resource links found."}</p>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
          {filtered.map((cat) => (
            <div key={cat.category} className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
              {/* Category header */}
              <div className="px-4 py-3 bg-green-50 border-b border-green-100">
                <h3 className="text-xs font-semibold text-green-800 uppercase tracking-wide">
                  {cat.category}
                </h3>
                <p className="text-xs text-green-600 mt-0.5">{cat.links.length} link{cat.links.length !== 1 ? "s" : ""}</p>
              </div>

              {/* Links list */}
              <ul className="divide-y divide-slate-50">
                {cat.links.map((link) => (
                  <li key={link.id} className="flex items-center gap-2 px-4 py-2.5 hover:bg-slate-50 transition-colors group/row">
                    {/* Link */}
                    <a
                      href={link.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="flex-1 min-w-0 flex items-center gap-2 text-sm text-slate-700 hover:text-green-700 transition-colors"
                    >
                      <span className="text-slate-300 group-hover/row:text-green-400 shrink-0 transition-colors text-xs">↗</span>
                      <span className="truncate">{link.title}</span>
                      {!link.isAdminCreated && (
                        <span className="text-xs text-slate-400 shrink-0">(staff)</span>
                      )}
                    </a>

                    {/* Actions */}
                    <div className="flex items-center gap-1 opacity-0 group-hover/row:opacity-100 transition-opacity shrink-0">
                      {canEdit && (
                        <button
                          title="Edit"
                          onClick={() => { setFormError(null); setEditTarget(link); }}
                          className="p-1 rounded text-slate-400 hover:text-green-700 hover:bg-green-50 transition-colors"
                        >
                          ✏️
                        </button>
                      )}
                      {canDelete && (
                        <button
                          title="Delete"
                          onClick={() => setDeleteTarget(link)}
                          className="p-1 rounded text-slate-400 hover:text-danger-500 hover:bg-danger-100 transition-colors"
                        >
                          🗑️
                        </button>
                      )}
                    </div>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      )}

      {/* Add modal */}
      {showAdd && (
        <LinkFormModal
          title="Add Resource Link"
          initial={{ category: allCategories[0], categoryOrder: 1, linkOrder: 99 }}
          categories={allCategories}
          saving={saving}
          error={formError}
          onSubmit={handleAdd}
          onClose={() => setShowAdd(false)}
        />
      )}

      {/* Edit modal */}
      {editTarget && (
        <LinkFormModal
          title={`Edit — ${editTarget.title}`}
          initial={editTarget}
          categories={allCategories}
          saving={saving}
          error={formError}
          onSubmit={(values) => handleEdit(values)}
          onClose={() => setEditTarget(null)}
        />
      )}

      {/* Delete confirm */}
      {deleteTarget && (
        <ConfirmDeleteModal
          linkTitle={deleteTarget.title}
          loading={deleting}
          onConfirm={handleDelete}
          onClose={() => setDeleteTarget(null)}
        />
      )}
    </div>
  );
}
