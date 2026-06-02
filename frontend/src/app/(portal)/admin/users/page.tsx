"use client";

/**
 * User Management page — RAL-43.
 *
 * Access guard: only users with canManageUsers = true may view this page.
 * Checks /api/auth/me on mount; redirects to /dashboard if permission is denied.
 *
 * Features:
 *   - Table listing all portal users (name, email, role, division, status)
 *   - Add User modal — create a new account with default password PPDOUser2026!
 *   - Edit User modal — update profile + per-user permission override toggles
 *   - Reset Password — one-click reset back to PPDOUser2026!
 *   - Deactivate / Reactivate — toggle isActive without deleting the record
 *
 * API endpoints used (all from UserFunctions.cs):
 *   GET    /api/users                     → list all users
 *   POST   /api/users                     → create user
 *   PUT    /api/users/{id}                → update user
 *   PUT    /api/users/{id}/reset-password → reset to default password
 *   DELETE /api/users/{id}               → deactivate
 *   PUT    /api/users/{id}/reactivate    → reactivate
 *   GET    /api/permission-groups         → list groups for dropdown
 */

import { useCallback, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import type {
  CreateUserRequest,
  Division,
  MeResponse,
  PermissionGroupResponse,
  UpdateUserRequest,
  UserResponse,
  UserRole,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const ROLES: UserRole[] = ["SuperAdmin", "Admin", "Staff", "Observer"];
const DIVISIONS: Division[] = ["Admin", "Planning", "RM", "MIS", "SPD"];

const ROLE_BADGE: Record<UserRole, string> = {
  SuperAdmin: "bg-green-100 text-green-800",
  Admin:      "bg-info-100 text-info-500",
  Staff:      "bg-slate-100 text-slate-600",
  Observer:   "bg-amber-100 text-amber-500",
};

// Overrides that are meaningful only for Staff / Observer
const OVERRIDE_KEYS = [
  { key: "overrideCanAccessInventory",     label: "Access Inventory" },
  { key: "overrideCanAccessReports",       label: "Access Reports" },
  { key: "overrideCanManageUsers",         label: "Manage Users" },
  { key: "overrideCanManageResourceLinks", label: "Manage Resource Links" },
] as const;

type OverrideKey = typeof OVERRIDE_KEYS[number]["key"];

// ---------------------------------------------------------------------------
// Blank form state
// ---------------------------------------------------------------------------

const blankForm = (): CreateUserRequest => ({
  fullName: "",
  email: "",
  role: "Staff",
  division: "Admin",
  position: null,
  contactNo: null,
});

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function roleBadge(role: UserRole) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${ROLE_BADGE[role]}`}>
      {role}
    </span>
  );
}

function statusBadge(isActive: boolean) {
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium ${
      isActive
        ? "bg-green-100 text-green-700"
        : "bg-danger-100 text-danger-500"
    }`}>
      {isActive ? "Active" : "Inactive"}
    </span>
  );
}

// ---------------------------------------------------------------------------
// Override toggle — three-state: null (inherit) | true | false
// ---------------------------------------------------------------------------

function OverrideToggle({
  label,
  value,
  onChange,
  disabled,
}: {
  label: string;
  value: boolean | null;
  onChange: (v: boolean | null) => void;
  disabled?: boolean;
}) {
  const states: Array<{ v: boolean | null; label: string; cls: string }> = [
    { v: null,  label: "Inherit", cls: "bg-slate-100 text-slate-500" },
    { v: true,  label: "Grant",   cls: "bg-green-600 text-white" },
    { v: false, label: "Deny",    cls: "bg-danger-500 text-white" },
  ];
  const current = states.find((s) => s.v === value) ?? states[0];

  function cycle() {
    if (disabled) return;
    const idx = states.findIndex((s) => s.v === value);
    onChange(states[(idx + 1) % states.length].v);
  }

  return (
    <div className={`flex items-center justify-between py-2 px-3 rounded-lg border border-slate-200 ${disabled ? "opacity-40" : ""}`}>
      <span className="text-sm text-slate-700">{label}</span>
      <button
        type="button"
        onClick={cycle}
        disabled={disabled}
        className={`min-w-[72px] text-xs font-medium px-3 py-1 rounded-full transition-colors ${current.cls}`}
      >
        {current.label}
      </button>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Modal wrapper
// ---------------------------------------------------------------------------

function Modal({ title, onClose, children }: { title: string; onClose: () => void; children: React.ReactNode }) {
  const backdropRef = useRef<HTMLDivElement>(null);

  function handleBackdrop(e: React.MouseEvent) {
    if (e.target === backdropRef.current) onClose();
  }

  return (
    <div
      ref={backdropRef}
      onClick={handleBackdrop}
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4"
    >
      <div className="w-full max-w-lg bg-white rounded-xl shadow-2xl flex flex-col max-h-[90vh]">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200 shrink-0">
          <h2 className="text-base font-semibold text-slate-800">{title}</h2>
          <button
            onClick={onClose}
            className="text-slate-400 hover:text-slate-600 transition-colors text-xl leading-none"
            aria-label="Close"
          >
            ×
          </button>
        </div>
        {/* Body — scrollable */}
        <div className="overflow-y-auto flex-1 px-6 py-5">
          {children}
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// User form (shared between Add and Edit)
// ---------------------------------------------------------------------------

type UserFormProps = {
  form: CreateUserRequest | UpdateUserRequest;
  groups: PermissionGroupResponse[];
  isEdit: boolean;  // when false, group dropdown and overrides are hidden
  saving: boolean;
  error: string | null;
  onChange: (patch: Partial<CreateUserRequest & UpdateUserRequest>) => void;
  onSubmit: () => void;
  onCancel: () => void;
};

function UserForm({ form, groups, isEdit, saving, error, onChange, onSubmit, onCancel }: UserFormProps) {
  const showOverrides = form.role === "Staff" || form.role === "Observer";

  return (
    <div className="space-y-4">
      {/* Profile fields */}
      <div className="grid grid-cols-2 gap-3">
        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">Full Name *</label>
          <input
            value={form.fullName}
            onChange={(e) => onChange({ fullName: e.target.value })}
            placeholder="Juan dela Cruz"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">Email *</label>
          <input
            type="email"
            value={form.email}
            onChange={(e) => onChange({ email: e.target.value })}
            placeholder="user@ppdo.gov.ph"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Role *</label>
          <select
            value={form.role}
            onChange={(e) => onChange({ role: e.target.value as UserRole })}
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 bg-white"
          >
            {ROLES.map((r) => (
              <option key={r} value={r}>{r}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Division</label>
          <select
            value={form.division ?? ""}
            onChange={(e) => onChange({ division: (e.target.value as Division) || null })}
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 bg-white"
          >
            <option value="">— None —</option>
            {DIVISIONS.map((d) => (
              <option key={d} value={d}>{d}</option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Position</label>
          <input
            value={form.position ?? ""}
            onChange={(e) => onChange({ position: e.target.value || null })}
            placeholder="Planning Officer II"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Contact No.</label>
          <input
            value={form.contactNo ?? ""}
            onChange={(e) => onChange({ contactNo: e.target.value || null })}
            placeholder="09XX-XXX-XXXX"
            className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        {/* Group dropdown — Edit only. Add User auto-assigns group from Role + Division. */}
        {isEdit && groups.length > 0 && (
          <div className="col-span-2">
            <label className="block text-xs font-medium text-slate-600 mb-1">
              Permission Group
              <span className="ml-1 font-normal text-slate-400">(auto-assigned from Role + Division)</span>
            </label>
            <select
              value={(form as UpdateUserRequest).groupId ?? ""}
              onChange={(e) => onChange({ groupId: e.target.value || null })}
              className="w-full px-3 py-2 rounded-lg text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 bg-white"
            >
              <option value="">— No group —</option>
              {groups.map((g) => (
                <option key={g.id} value={g.id}>{g.name}</option>
              ))}
            </select>
          </div>
        )}

        {/* isActive toggle — edit only */}
        {isEdit && "isActive" in form && (
          <div className="col-span-2 flex items-center gap-3 py-1">
            <span className="text-xs font-medium text-slate-600">Account Status</span>
            <button
              type="button"
              onClick={() => onChange({ isActive: !(form as UpdateUserRequest).isActive })}
              className={`relative inline-flex h-6 w-11 items-center rounded-full transition-colors focus:outline-none focus:ring-2 focus:ring-green-600 ${
                (form as UpdateUserRequest).isActive ? "bg-green-600" : "bg-slate-300"
              }`}
            >
              <span className={`inline-block h-4 w-4 rounded-full bg-white shadow transform transition-transform ${
                (form as UpdateUserRequest).isActive ? "translate-x-6" : "translate-x-1"
              }`} />
            </button>
            <span className="text-xs text-slate-500">
              {(form as UpdateUserRequest).isActive ? "Active" : "Inactive"}
            </span>
          </div>
        )}
      </div>

      {/* Permission overrides — Edit only, Staff / Observer only */}
      {isEdit && showOverrides && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">
            Permission Overrides
            <span className="ml-1 font-normal normal-case tracking-normal text-slate-400">
              (inherits from group unless overridden)
            </span>
          </p>
          <div className="space-y-2">
            {OVERRIDE_KEYS.map(({ key, label }) => (
              <OverrideToggle
                key={key}
                label={label}
                value={form[key as OverrideKey]}
                onChange={(v) => onChange({ [key]: v })}
              />
            ))}
          </div>
        </div>
      )}

      {/* SuperAdmin / Admin note — Edit only */}
      {isEdit && !showOverrides && (
        <p className="text-xs text-slate-400 bg-slate-50 rounded-lg px-3 py-2">
          SuperAdmin and Admin roles always have full access — permission overrides do not apply.
        </p>
      )}

      {/* Error */}
      {error && (
        <div className="rounded-lg bg-danger-100 border border-danger-500/30 px-4 py-3">
          <p className="text-sm text-danger-500">{error}</p>
        </div>
      )}

      {/* Actions */}
      <div className="flex justify-end gap-3 pt-2">
        <button
          type="button"
          onClick={onCancel}
          className="px-4 py-2 text-sm rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={onSubmit}
          disabled={saving}
          className="px-5 py-2 text-sm rounded-lg bg-green-600 text-white font-medium hover:bg-green-500 transition-colors disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-2"
        >
          {saving && <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
          {saving ? "Saving…" : isEdit ? "Save Changes" : "Create User"}
        </button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Confirm dialog
// ---------------------------------------------------------------------------

function ConfirmDialog({
  message,
  confirmLabel,
  danger,
  loading,
  onConfirm,
  onCancel,
}: {
  message: string;
  confirmLabel: string;
  danger?: boolean;
  loading: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <Modal title="Confirm Action" onClose={onCancel}>
      <p className="text-sm text-slate-700 mb-6">{message}</p>
      <div className="flex justify-end gap-3">
        <button
          onClick={onCancel}
          className="px-4 py-2 text-sm rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
        >
          Cancel
        </button>
        <button
          onClick={onConfirm}
          disabled={loading}
          className={`px-5 py-2 text-sm rounded-lg font-medium text-white transition-colors disabled:opacity-60 flex items-center gap-2 ${
            danger ? "bg-danger-500 hover:bg-red-600" : "bg-green-600 hover:bg-green-500"
          }`}
        >
          {loading && <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
          {loading ? "Processing…" : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function UsersPage() {
  const router = useRouter();

  // Auth / permission guard
  const [authChecked, setAuthChecked] = useState(false);

  // Data
  const [users, setUsers] = useState<UserResponse[]>([]);
  const [groups, setGroups] = useState<PermissionGroupResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [fetchError, setFetchError] = useState<string | null>(null);

  // Search
  const [search, setSearch] = useState("");

  // Modals
  const [showAdd, setShowAdd] = useState(false);
  const [editTarget, setEditTarget] = useState<UserResponse | null>(null);
  const [resetTarget, setResetTarget] = useState<UserResponse | null>(null);
  const [deactivateTarget, setDeactivateTarget] = useState<UserResponse | null>(null);

  // Form state
  const [addForm, setAddForm] = useState<CreateUserRequest>(blankForm());
  const [editForm, setEditForm] = useState<UpdateUserRequest | null>(null);

  // Action state
  const [saving, setSaving] = useState(false);
  const [actionLoading, setActionLoading] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  // ---------------------------------------------------------------------------
  // Auth check — redirect if not canManageUsers
  // ---------------------------------------------------------------------------

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canManageUsers) {
        router.replace("/dashboard");
      } else {
        setAuthChecked(true);
      }
    }).catch(() => {
      router.replace("/login");
    });
  }, [router]);

  // ---------------------------------------------------------------------------
  // Load data
  // ---------------------------------------------------------------------------

  const loadData = useCallback(async () => {
    setLoading(true);
    setFetchError(null);
    try {
      // Fetch users — required. Groups are optional (endpoint may not exist yet).
      const usersRes = await api.get<UserResponse[]>("/users");
      setUsers(usersRes.data);

      try {
        const groupsRes = await api.get<PermissionGroupResponse[]>("/permission-groups");
        setGroups(groupsRes.data);
      } catch {
        // permission-groups endpoint not yet implemented — group dropdown stays empty
        setGroups([]);
      }
    } catch {
      setFetchError("Failed to load user data. Please try again.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (authChecked) loadData();
  }, [authChecked, loadData]);

  // ---------------------------------------------------------------------------
  // Filtered users
  // ---------------------------------------------------------------------------

  const filteredUsers = users.filter((u) => {
    const q = search.toLowerCase();
    return (
      u.fullName.toLowerCase().includes(q) ||
      u.email.toLowerCase().includes(q) ||
      u.role.toLowerCase().includes(q) ||
      (u.division ?? "").toLowerCase().includes(q)
    );
  });

  // ---------------------------------------------------------------------------
  // Handlers — Add
  // ---------------------------------------------------------------------------

  function openAdd() {
    setAddForm(blankForm());
    setFormError(null);
    setShowAdd(true);
  }

  async function handleAdd() {
    if (!addForm.fullName.trim() || !addForm.email.trim()) {
      setFormError("Full name and email are required.");
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      await api.post("/users", addForm);
      setShowAdd(false);
      await loadData();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? "Failed to create user. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Handlers — Edit
  // ---------------------------------------------------------------------------

  function openEdit(user: UserResponse) {
    setEditTarget(user);
    setEditForm({
      fullName:                      user.fullName,
      email:                         user.email,
      role:                          user.role,
      division:                      user.division,
      groupId:                       user.groupId,
      position:                      user.position,
      contactNo:                     user.contactNo,
      isActive:                      user.isActive,
      overrideCanAccessInventory:    user.overrideCanAccessInventory,
      overrideCanAccessReports:      user.overrideCanAccessReports,
      overrideCanManageUsers:        user.overrideCanManageUsers,
      overrideCanManageResourceLinks: user.overrideCanManageResourceLinks,
    });
    setFormError(null);
  }

  async function handleEdit() {
    if (!editTarget || !editForm) return;
    if (!editForm.fullName.trim() || !editForm.email.trim()) {
      setFormError("Full name and email are required.");
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      await api.put(`/users/${editTarget.id}`, editForm);
      setEditTarget(null);
      setEditForm(null);
      await loadData();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? "Failed to update user. Please try again.");
    } finally {
      setSaving(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Handlers — Reset Password
  // ---------------------------------------------------------------------------

  async function handleResetPassword() {
    if (!resetTarget) return;
    setActionLoading(true);
    try {
      await api.put(`/users/${resetTarget.id}/reset-password`);
      setResetTarget(null);
    } catch {
      // keep modal open — user can retry
    } finally {
      setActionLoading(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Handlers — Deactivate / Reactivate
  // ---------------------------------------------------------------------------

  async function handleToggleActive() {
    if (!deactivateTarget) return;
    setActionLoading(true);
    try {
      if (deactivateTarget.isActive) {
        await api.delete(`/users/${deactivateTarget.id}`);
      } else {
        await api.put(`/users/${deactivateTarget.id}/reactivate`);
      }
      setDeactivateTarget(null);
      await loadData();
    } catch {
      // keep modal open — user can retry
    } finally {
      setActionLoading(false);
    }
  }

  // ---------------------------------------------------------------------------
  // Loading / auth states
  // ---------------------------------------------------------------------------

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ---------------------------------------------------------------------------
  // Render
  // ---------------------------------------------------------------------------

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      {/* Page header */}
      <div className="bg-green-700 text-white px-6 py-4 shadow-sm">
        <div className="max-w-6xl mx-auto flex items-center justify-between">
          <div>
            <h1 className="text-lg font-bold">User Management</h1>
            <p className="text-green-200 text-xs mt-0.5">Manage portal accounts and permission overrides</p>
          </div>
          <button
            onClick={openAdd}
            className="flex items-center gap-2 bg-white text-green-700 font-semibold text-sm px-4 py-2 rounded-lg hover:bg-green-50 transition-colors shadow-sm"
          >
            <span className="text-lg leading-none">+</span>
            Add User
          </button>
        </div>
      </div>

      <div className="max-w-6xl mx-auto px-6 py-6 space-y-4">
        {/* Search bar */}
        <div className="flex items-center gap-3">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, email, role, or division…"
            className="flex-1 px-4 py-2.5 rounded-lg text-sm border border-slate-200 bg-white shadow-sm focus:outline-none focus:ring-2 focus:ring-green-600"
          />
          {search && (
            <button
              onClick={() => setSearch("")}
              className="text-sm text-slate-400 hover:text-slate-600 transition-colors px-2"
            >
              Clear
            </button>
          )}
        </div>

        {/* Table card */}
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
          {loading ? (
            <div className="flex items-center justify-center py-16">
              <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
            </div>
          ) : fetchError ? (
            <div className="flex flex-col items-center justify-center py-16 gap-3">
              <p className="text-sm text-danger-500">{fetchError}</p>
              <button
                onClick={loadData}
                className="text-sm text-green-600 hover:underline"
              >
                Retry
              </button>
            </div>
          ) : filteredUsers.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-16 gap-2 text-slate-400">
              <span className="text-3xl">👤</span>
              <p className="text-sm">
                {search ? "No users match your search." : "No users found."}
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto">
              <table className="w-full text-sm">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 uppercase tracking-wide">
                    <th className="text-left px-4 py-3 font-medium">Name</th>
                    <th className="text-left px-4 py-3 font-medium">Email</th>
                    <th className="text-left px-4 py-3 font-medium">Role</th>
                    <th className="text-left px-4 py-3 font-medium">Division</th>
                    <th className="text-left px-4 py-3 font-medium">Status</th>
                    <th className="text-right px-4 py-3 font-medium">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {filteredUsers.map((user, i) => (
                    <tr
                      key={user.id}
                      className={`transition-colors hover:bg-green-50 ${i % 2 === 1 ? "bg-slate-50" : "bg-white"}`}
                    >
                      <td className="px-4 py-3">
                        <div className="font-medium text-slate-800">{user.fullName}</div>
                        {user.position && (
                          <div className="text-xs text-slate-400">{user.position}</div>
                        )}
                      </td>
                      <td className="px-4 py-3 text-slate-600">{user.email}</td>
                      <td className="px-4 py-3">{roleBadge(user.role)}</td>
                      <td className="px-4 py-3 text-slate-600">{user.division ?? "—"}</td>
                      <td className="px-4 py-3">{statusBadge(user.isActive)}</td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-1">
                          {/* Edit */}
                          <ActionButton
                            title="Edit user"
                            onClick={() => openEdit(user)}
                            icon="✏️"
                          />
                          {/* Reset password */}
                          <ActionButton
                            title="Reset password"
                            onClick={() => setResetTarget(user)}
                            icon="🔑"
                          />
                          {/* Deactivate / Reactivate */}
                          <ActionButton
                            title={user.isActive ? "Deactivate user" : "Reactivate user"}
                            onClick={() => setDeactivateTarget(user)}
                            icon={user.isActive ? "🚫" : "✅"}
                            danger={user.isActive}
                          />
                        </div>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>

              {/* Row count */}
              <div className="px-4 py-2 border-t border-slate-100 text-xs text-slate-400">
                {filteredUsers.length} {filteredUsers.length === 1 ? "user" : "users"}
                {search && ` matching "${search}"`}
              </div>
            </div>
          )}
        </div>
      </div>

      {/* ── Add User modal ─────────────────────────────────────────────────── */}
      {showAdd && (
        <Modal title="Add New User" onClose={() => setShowAdd(false)}>
          <p className="text-xs text-slate-400 mb-4">
            Default password <span className="font-mono bg-slate-100 px-1 rounded">PPDOUser2026!</span> is set automatically. The user must change it on first login.
          </p>
          <UserForm
            form={addForm}
            groups={groups}
            isEdit={false}
            saving={saving}
            error={formError}
            onChange={(patch) => setAddForm((f) => ({ ...f, ...patch }))}
            onSubmit={handleAdd}
            onCancel={() => setShowAdd(false)}
          />
        </Modal>
      )}

      {/* ── Edit User modal ────────────────────────────────────────────────── */}
      {editTarget && editForm && (
        <Modal title={`Edit User — ${editTarget.fullName}`} onClose={() => { setEditTarget(null); setEditForm(null); }}>
          <UserForm
            form={editForm}
            groups={groups}
            isEdit
            saving={saving}
            error={formError}
            onChange={(patch) => setEditForm((f) => f ? { ...f, ...patch } : f)}
            onSubmit={handleEdit}
            onCancel={() => { setEditTarget(null); setEditForm(null); }}
          />
        </Modal>
      )}

      {/* ── Reset Password confirm ─────────────────────────────────────────── */}
      {resetTarget && (
        <ConfirmDialog
          message={`Reset password for ${resetTarget.fullName}? Their password will be set back to the default: PPDOUser2026!`}
          confirmLabel="Reset Password"
          loading={actionLoading}
          onConfirm={handleResetPassword}
          onCancel={() => setResetTarget(null)}
        />
      )}

      {/* ── Deactivate / Reactivate confirm ───────────────────────────────── */}
      {deactivateTarget && (
        <ConfirmDialog
          message={
            deactivateTarget.isActive
              ? `Deactivate ${deactivateTarget.fullName}? They will no longer be able to log in.`
              : `Reactivate ${deactivateTarget.fullName}? They will be able to log in again.`
          }
          confirmLabel={deactivateTarget.isActive ? "Deactivate" : "Reactivate"}
          danger={deactivateTarget.isActive}
          loading={actionLoading}
          onConfirm={handleToggleActive}
          onCancel={() => setDeactivateTarget(null)}
        />
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Small action button
// ---------------------------------------------------------------------------

function ActionButton({
  title,
  onClick,
  icon,
  danger,
}: {
  title: string;
  onClick: () => void;
  icon: string;
  danger?: boolean;
}) {
  return (
    <button
      title={title}
      onClick={onClick}
      className={`p-1.5 rounded-lg text-sm transition-colors ${
        danger
          ? "hover:bg-danger-100 text-slate-400 hover:text-danger-500"
          : "hover:bg-green-50 text-slate-400 hover:text-green-700"
      }`}
    >
      {icon}
    </button>
  );
}
