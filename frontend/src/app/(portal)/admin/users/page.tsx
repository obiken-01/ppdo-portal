"use client";

/**
 * User Management page — RAL-43.
 *
 * Access guard: only users with canManageUsers = true may view this page.
 * Checks /api/auth/me on mount; redirects to /dashboard if permission is denied.
 *
 * Features:
 *   - Table listing all portal users (name, email, role, division, status)
 *   - Add User modal — create a new account with default password TamarawUser2026!
 *   - Edit User modal — update profile + per-user permission override toggles
 *   - Reset Password — one-click reset back to TamarawUser2026!
 *   - Deactivate / Reactivate — toggle isActive without deleting the record
 *
 * API endpoints used (all from UserFunctions.cs):
 *   GET    /api/users                     → list all users
 *   POST   /api/users                     → create user
 *   PUT    /api/users/{id}                → update user
 *   PUT    /api/users/{id}/reset-password → reset to default password
 *   DELETE /api/users/{id}               → deactivate
 *   PUT    /api/users/{id}/reactivate    → reactivate
 *   GET    /api/config/divisions          → list divisions for the dropdown (RAL-97)
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { listDivisions } from "@/lib/config";
import Modal from "@/components/ui/Modal";
import { useToast } from "@/components/ui/Toast";
import type {
  CreateUserRequest,
  DivisionResponse,
  MeResponse,
  OfficeResponse,
  UpdateUserRequest,
  UserResponse,
  UserRole,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const ROLES: UserRole[] = ["SuperAdmin", "Admin", "Staff"];

const ROLE_BADGE: Record<UserRole, string> = {
  SuperAdmin: "bg-green-100 text-green-800",
  Admin:      "bg-info-100 text-info-500",
  Staff:      "bg-slate-100 text-slate-600",
};

// Permission override descriptors.
// adminOnly: true  — Admin does NOT auto-inherit this flag; the toggle is shown for Admin too.
// (All flags are shown for Staff.)
const OVERRIDE_KEYS: {
  key: keyof UpdateUserRequest & `override${string}`;
  label: string;
  adminOnly?: boolean;
}[] = [
  { key: "overrideCanAccessInventory",      label: "Access Inventory" },
  { key: "overrideCanAccessReports",        label: "Inventory Report" },
  { key: "overrideCanManageUsers",          label: "Manage Users" },
  { key: "overrideCanManageResourceLinks",  label: "Manage Resource Links" },
  { key: "overrideCanAccessBudgetPlanning", label: "Access Budget Planning" },
  { key: "overrideCanUploadAip",            label: "Upload AIP" },
  { key: "overrideCanManageConfig",         label: "Manage Configuration" },
  { key: "overrideCanManageAllocation",     label: "Manage Allocation (finance officer)", adminOnly: true },
];

// ---------------------------------------------------------------------------
// Blank form state
// ---------------------------------------------------------------------------

const blankForm = (): CreateUserRequest => ({
  fullName: "",
  username: "",
  email: undefined,
  role: "Staff",
  divisionId: null,
  officeId: null,
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
// User form (shared between Add and Edit)
// ---------------------------------------------------------------------------

type UserFormProps = {
  form: CreateUserRequest | UpdateUserRequest;
  divisions: DivisionResponse[];
  offices: OfficeResponse[];
  isEdit: boolean;  // when false, overrides are hidden
  error: string | null;
  onChange: (patch: Partial<CreateUserRequest & UpdateUserRequest>) => void;
};

function UserForm({ form, divisions, offices, isEdit, error, onChange }: UserFormProps) {
  const showOverrides      = form.role === "Staff";
  const showAdminOverrides = form.role === "Admin";
  const adminOnlyKeys      = OVERRIDE_KEYS.filter((o) => o.adminOnly);
  // A non-PPDO office user has an office assigned. Their division must belong to that office.
  const isOfficeUser = form.officeId != null;
  // Division is required only for PPDO-internal Staff. Office users are scoped by office_id, not division.
  const isPpdoDivisionUser = form.role === "Staff" && !isOfficeUser;

  // Division options: filter to the selected office's divisions.
  // No office selected = PPDO-internal user → show only PPDO divisions (officeCode === "PPDO").
  const divisionOptions = isOfficeUser
    ? divisions.filter((d) => d.officeId === form.officeId)
    : divisions.filter((d) => d.officeCode === "PPDO");

  // Selecting an office forces a non-admin role (office users are encoders).
  function handleOfficeChange(value: string) {
    const officeId = value ? Number(value) : null;
    const patch: Partial<CreateUserRequest & UpdateUserRequest> = { officeId, divisionId: null };
    if (officeId != null && (form.role === "SuperAdmin" || form.role === "Admin")) patch.role = "Staff";
    onChange(patch);
  }

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
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">Username *</label>
          <input
            value={form.username}
            onChange={(e) => onChange({ username: e.target.value })}
            placeholder="juandelacruz"
            autoComplete="off"
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 font-mono"
          />
        </div>

        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Email
            <span className="ml-1 font-normal text-slate-400">(optional)</span>
          </label>
          <input
            type="email"
            value={form.email ?? ""}
            onChange={(e) => onChange({ email: e.target.value || undefined })}
            placeholder="user@ppdo.gov.ph"
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Role *</label>
          <select
            value={form.role}
            onChange={(e) => onChange({ role: e.target.value as UserRole })}
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 bg-white"
          >
            {(isOfficeUser ? (["Staff"] as UserRole[]) : ROLES).map((r) => (
              <option key={r} value={r}>{r}</option>
            ))}
          </select>
          {isOfficeUser && (
            <p className="mt-1 text-[11px] text-slate-400">Office users are Staff (encoder).</p>
          )}
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Division{isPpdoDivisionUser ? " *" : ""}
          </label>
          <select
            value={form.divisionId ?? ""}
            onChange={(e) => onChange({ divisionId: e.target.value ? Number(e.target.value) : null })}
            disabled={!isPpdoDivisionUser}
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 bg-white disabled:bg-slate-100 disabled:text-slate-400"
          >
            <option value="">— None —</option>
            {divisionOptions.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name}{!isOfficeUser && d.officeName ? ` (${d.officeName})` : ""}
              </option>
            ))}
          </select>
          {!isPpdoDivisionUser && (
            <p className="mt-1 text-[11px] text-slate-400">SuperAdmin / Admin have no division.</p>
          )}
        </div>

        {/* Office (v1.1) — set to create a non-PPDO office user (Budget Planning only). */}
        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Office
            <span className="ml-1 font-normal text-slate-400">(non-PPDO user — clears Division)</span>
          </label>
          <select
            value={form.officeId ?? ""}
            onChange={(e) => handleOfficeChange(e.target.value)}
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 bg-white"
          >
            <option value="">— None (PPDO-internal user) —</option>
            {offices.map((o) => (
              <option key={o.id} value={o.id}>{o.officeName} ({o.officeCode})</option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Position</label>
          <input
            value={form.position ?? ""}
            onChange={(e) => onChange({ position: e.target.value || null })}
            placeholder="Planning Officer II"
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

        <div>
          <label className="block text-xs font-medium text-slate-600 mb-1">Contact No.</label>
          <input
            value={form.contactNo ?? ""}
            onChange={(e) => onChange({ contactNo: e.target.value || null })}
            placeholder="09XX-XXX-XXXX"
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600"
          />
        </div>

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

      {/* Permission overrides — Staff: all flags; Admin: adminOnly flags only */}
      {isEdit && showOverrides && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-2">
            Permission Overrides
            <span className="ml-1 font-normal normal-case tracking-normal text-slate-400">
              (inherits from division unless overridden)
            </span>
          </p>
          <div className="space-y-2">
            {OVERRIDE_KEYS.map(({ key, label }) => (
              <OverrideToggle
                key={key}
                label={label}
                value={(form as UpdateUserRequest)[key]}
                onChange={(v) => onChange({ [key]: v })}
              />
            ))}
          </div>
        </div>
      )}

      {isEdit && showAdminOverrides && adminOnlyKeys.length > 0 && (
        <div>
          <p className="text-xs font-semibold text-slate-500 uppercase tracking-wide mb-1">
            Permission Overrides
          </p>
          <p className="text-xs text-slate-400 mb-2">
            Admin has full access to all features except the flags below — these must be granted explicitly.
          </p>
          <div className="space-y-2">
            {adminOnlyKeys.map(({ key, label }) => (
              <OverrideToggle
                key={key}
                label={label}
                value={(form as UpdateUserRequest)[key]}
                onChange={(v) => onChange({ [key]: v })}
              />
            ))}
          </div>
        </div>
      )}

      {/* SuperAdmin note — Edit only */}
      {isEdit && form.role === "SuperAdmin" && (
        <p className="text-xs text-slate-400 bg-slate-50 px-3 py-2">
          SuperAdmin always has full access — permission overrides do not apply.
        </p>
      )}

      {/* Admin full-access note — only when no adminOnly overrides exist */}
      {isEdit && showAdminOverrides && adminOnlyKeys.length === 0 && (
        <p className="text-xs text-slate-400 bg-slate-50 px-3 py-2">
          Admin always has full access — permission overrides do not apply.
        </p>
      )}

      {/* Error */}
      {error && (
        <div className="bg-danger-100 border border-danger-500/30 px-4 py-3">
          <p className="text-sm text-danger-500">{error}</p>
        </div>
      )}

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
    <Modal
      title="Confirm Action"
      size="sm"
      onClose={onCancel}
      footer={
        <>
          <button
            onClick={onCancel}
            className="px-4 py-2 text-sm border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
          >
            Cancel
          </button>
          <button
            onClick={onConfirm}
            disabled={loading}
            className={`px-5 py-2 text-sm font-medium text-white transition-colors disabled:opacity-60 flex items-center gap-2 ${
              danger ? "bg-danger-500 hover:bg-red-600" : "bg-green-600 hover:bg-green-500"
            }`}
          >
            {loading && <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />}
            {loading ? "Processing…" : confirmLabel}
          </button>
        </>
      }
    >
      <p className="text-sm text-slate-700">{message}</p>
    </Modal>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function UsersPage() {
  const router = useRouter();

  // Auth / permission guard
  const { toast } = useToast();
  const [authChecked, setAuthChecked] = useState(false);

  // Data
  const [users, setUsers] = useState<UserResponse[]>([]);
  const [divisions, setDivisions] = useState<DivisionResponse[]>([]);
  const [offices, setOffices] = useState<OfficeResponse[]>([]);
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
        router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
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
      // Fetch users — required. Divisions/offices drive the form dropdowns.
      const usersRes = await api.get<UserResponse[]>("/users");
      setUsers(usersRes.data);

      try {
        setDivisions(await listDivisions({ active: "true" }));
      } catch {
        // divisions endpoint unavailable — division dropdown stays empty
        setDivisions([]);
      }

      try {
        // /api/config/offices returns the { data, error, message } envelope (RAL-70).
        const officesRes = await api.get<{ data: OfficeResponse[] }>("/config/offices?active=true");
        setOffices(officesRes.data.data ?? []);
      } catch {
        // offices endpoint unavailable — office dropdown stays empty
        setOffices([]);
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
      u.username.toLowerCase().includes(q) ||
      (u.email ?? "").toLowerCase().includes(q) ||
      u.role.toLowerCase().includes(q) ||
      (u.division ?? "").toLowerCase().includes(q) ||
      (u.officeName ?? "").toLowerCase().includes(q)
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
    if (!addForm.fullName.trim() || !addForm.username.trim()) {
      setFormError("Full name and username are required.");
      return;
    }
    setSaving(true);
    setFormError(null);
    try {
      await api.post("/users", addForm);
      setShowAdd(false);
      await loadData();
      toast.success("User created", `${addForm.fullName} has been added. Default password: TamarawUser2026!`);
    } catch (e: unknown) {
      const data = (e as { response?: { data?: unknown } })?.response?.data;
      const msg = typeof data === "string" ? data : (data as { message?: string } | undefined)?.message;
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
    // Office users (non-PPDO): clear any stale PPDO division — they're scoped by officeId.
    const divisionId = user.officeId != null ? null : user.divisionId;
    setEditForm({
      fullName:                      user.fullName,
      username:                      user.username,
      email:                         user.email,
      role:                          user.role,
      divisionId,
      officeId:                      user.officeId,
      position:                      user.position,
      contactNo:                     user.contactNo,
      isActive:                      user.isActive,
      overrideCanAccessInventory:    user.overrideCanAccessInventory,
      overrideCanAccessReports:      user.overrideCanAccessReports,
      overrideCanManageUsers:        user.overrideCanManageUsers,
      overrideCanManageResourceLinks: user.overrideCanManageResourceLinks,
      overrideCanAccessBudgetPlanning: user.overrideCanAccessBudgetPlanning,
      overrideCanUploadAip:            user.overrideCanUploadAip,
      overrideCanManageConfig:         user.overrideCanManageConfig,
      overrideCanManageAllocation:     user.overrideCanManageAllocation,
    });
    setFormError(null);
  }

  async function handleEdit() {
    if (!editTarget || !editForm) return;
    if (!editForm.fullName.trim() || !editForm.username.trim()) {
      setFormError("Full name and username are required.");
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
      const data = (e as { response?: { data?: unknown } })?.response?.data;
      const msg = typeof data === "string" ? data : (data as { message?: string } | undefined)?.message;
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
      toast.success("Password reset", `Password reset to TamarawUser2026! for ${resetTarget.fullName}.`);
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
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-4">
        {/* Toolbar: search + add button */}
        <div className="flex items-center gap-3">
          <input
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by name, username, email, role, or division…"
            className="flex-1 px-4 py-2.5 text-sm border border-slate-200 bg-white shadow-sm focus:outline-none focus:ring-2 focus:ring-green-600"
          />
          {search && (
            <button
              onClick={() => setSearch("")}
              className="text-sm text-slate-400 hover:text-slate-600 transition-colors px-2"
            >
              Clear
            </button>
          )}
          <button
            onClick={openAdd}
            className="flex items-center gap-1.5 bg-green-600 text-white font-semibold text-sm px-4 py-2.5 hover:bg-green-500 transition-colors shadow-sm shrink-0"
          >
            <span className="text-base leading-none">+</span>
            Add User
          </button>
        </div>

        {/* Table card */}
        <div className="bg-white shadow-sm border border-slate-200 overflow-hidden">
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
            <div className="overflow-x-auto overflow-y-hidden">
              <table className="w-full text-sm">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 uppercase tracking-wide">
                    <th className="text-left px-4 py-3 font-medium">Name</th>
                    <th className="text-left px-4 py-3 font-medium">Username / Email</th>
                    <th className="text-left px-4 py-3 font-medium">Role</th>
                    <th className="text-left px-4 py-3 font-medium">Division / Office</th>
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
                      <td className="px-4 py-3">
                        <div className="font-mono text-sm text-slate-700">{user.username}</div>
                        {user.email && (
                          <div className="text-xs text-slate-400">{user.email}</div>
                        )}
                      </td>
                      <td className="px-4 py-3">{roleBadge(user.role)}</td>
                      <td className="px-4 py-3 text-slate-600">
                        {user.officeName
                          ? <span className="inline-flex items-center gap-1"><span className="text-xs">🏛️</span>{user.officeName}</span>
                          : (user.division ?? "—")}
                      </td>
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
        <Modal
          title="Add New User"
          onClose={() => setShowAdd(false)}
          footer={
            <>
              <Modal.SecondaryButton onClick={() => setShowAdd(false)} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={handleAdd} loading={saving} disabled={saving}>
                Create User
              </Modal.PrimaryButton>
            </>
          }
        >
          <p className="text-xs text-slate-400 mb-4">
            Default password <span className="font-mono bg-slate-100 px-1">TamarawUser2026!</span> is set automatically. The user must change it on first login.
          </p>
          <UserForm
            form={addForm}
            divisions={divisions}
            offices={offices}
            isEdit={false}
            error={formError}
            onChange={(patch) => setAddForm((f) => ({ ...f, ...patch }))}
          />
        </Modal>
      )}

      {/* ── Edit User modal ────────────────────────────────────────────────── */}
      {editTarget && editForm && (
        <Modal
          title={`Edit User — ${editTarget.fullName}`}
          onClose={() => { setEditTarget(null); setEditForm(null); }}
          footer={
            <>
              <Modal.SecondaryButton onClick={() => { setEditTarget(null); setEditForm(null); }} disabled={saving}>
                Cancel
              </Modal.SecondaryButton>
              <Modal.PrimaryButton onClick={handleEdit} loading={saving} disabled={saving}>
                Save Changes
              </Modal.PrimaryButton>
            </>
          }
        >
          <UserForm
            form={editForm}
            divisions={divisions}
            offices={offices}
            isEdit
            error={formError}
            onChange={(patch) => setEditForm((f) => f ? { ...f, ...patch } : f)}
          />
        </Modal>
      )}

      {/* ── Reset Password confirm ─────────────────────────────────────────── */}
      {resetTarget && (
        <ConfirmDialog
          message={`Reset password for ${resetTarget.fullName}? Their password will be set back to the default: TamarawUser2026!`}
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
      className={`p-1.5 text-sm transition-colors ${
        danger
          ? "hover:bg-danger-100 text-slate-400 hover:text-danger-500"
          : "hover:bg-green-50 text-slate-400 hover:text-green-700"
      }`}
    >
      {icon}
    </button>
  );
}
