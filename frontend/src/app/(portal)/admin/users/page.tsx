"use client";

/**
 * User Management page — RAL-43.
 *
 * Access guard: only users with canManageUsers = true may view this page.
 * Checks /api/auth/me on mount; redirects to /dashboard if permission is denied.
 *
 * Features:
 *   - Table listing all portal users (name, email, role, division, status)
 *   - Add User modal — create a new account; a one-time password is issued and shown once
 *   - Edit User modal — update profile + per-user permission override toggles
 *   - Reset Password — one-click reset; issues a new one-time password, shown once
 *   - Deactivate / Reactivate — toggle isActive without deleting the record
 *
 * API endpoints used (all from UserFunctions.cs):
 *   GET    /api/users                     → list all users
 *   POST   /api/users                     → create user
 *   PUT    /api/users/{id}                → update user
 *   PUT    /api/users/{id}/reset-password → issue a new one-time password
 *   DELETE /api/users/{id}               → deactivate
 *   PUT    /api/users/{id}/reactivate    → reactivate
 *   GET    /api/config/divisions          → list divisions for the dropdown (RAL-97)
 */

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { listDivisions, listOffices } from "@/lib/config";
import Modal from "@/components/ui/Modal";
import OfficeSelect from "@/components/ui/OfficeSelect";
import RowActions, { type RowAction } from "@/components/ui/RowActions";
import IssuedPasswordDialog from "@/components/ui/IssuedPasswordDialog";
import LandingPageSelect from "@/components/ui/LandingPageSelect";
import type {
  CreateUserRequest,
  DivisionResponse,
  MeResponse,
  OfficeResponse,
  UpdateUserRequest,
  UserCredentialResponse,
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

/** Tabs in the Add/Edit User modal (RAL-268). */
type FormTab = "details" | "permissions";

const FORM_TABS: { id: FormTab; label: string }[] = [
  { id: "details",     label: "Details" },
  { id: "permissions", label: "Permissions" },
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
  landingPage: null,
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
    { v: null,  label: "Inherit", cls: "bg-slate-100 text-slate-600" },
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
    <div className={`flex items-center justify-between py-2 px-3 border border-slate-200 ${disabled ? "opacity-40" : ""}`}>
      <span className="text-sm text-slate-600">{label}</span>
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

  // Split across tabs (RAL-268): the flat form already ran to ~11 permission rows below the
  // profile fields, so the flags people edit most were the ones furthest down the scroll. The
  // count is what makes the split safe — you can see a user carries overrides without opening
  // the tab, which a plain "Permissions" label would have hidden.
  const [tab, setTab] = useState<FormTab>("details");

  const visibleOverrideKeys =
    form.role === "Staff" ? OVERRIDE_KEYS :
    form.role === "Admin" ? adminOnlyKeys : [];

  // Only counts flags actually shown for this role, so the badge always matches the tab.
  const overrideCount = isEdit
    ? visibleOverrideKeys.filter(({ key }) => (form as UpdateUserRequest)[key] != null).length
    : 0;
  // Every user has an office since RAL-258, so "has an office" no longer distinguishes anyone.
  // A guest-office user is one whose office is NOT the host office; leaving the picker blank
  // means the host office, which is what an empty selection has always meant in practice.
  const hostOffice = offices.find((o) => o.isHostOffice) ?? null;
  const selectedOffice = offices.find((o) => o.id === form.officeId) ?? null;
  const isOfficeUser = form.officeId != null && !selectedOffice?.isHostOffice;
  // Division is required only for host-office Staff. Guest-office users are scoped by office_id.
  const isPpdoDivisionUser = form.role === "Staff" && !isOfficeUser;
  // Drives which landing pages can be offered — a Staff user inherits feature flags from here.
  const selectedDivision = divisions.find((d) => d.id === form.divisionId) ?? null;

  // Division options: filter to the selected office's divisions. A blank office means the host
  // office, whose divisions are the ones a host-office user can belong to.
  const divisionOptions = isOfficeUser
    ? divisions.filter((d) => d.officeId === form.officeId)
    : divisions.filter((d) => d.officeId === hostOffice?.id);

  // Selecting an office forces a non-admin role (office users are encoders).
  function handleOfficeChange(officeId: number | null) {
    const patch: Partial<CreateUserRequest & UpdateUserRequest> = { officeId, divisionId: null };
    // Only a GUEST office forces the Staff role — the host office still holds admins.
    const picked = offices.find((o) => o.id === officeId) ?? null;
    if (officeId != null && !picked?.isHostOffice
        && (form.role === "SuperAdmin" || form.role === "Admin")) patch.role = "Staff";
    onChange(patch);
  }

  return (
    <div className="space-y-4">
      <div
        role="tablist"
        aria-label="User settings"
        className="flex border-b border-slate-200"
        onKeyDown={(e) => {
          if (e.key !== "ArrowLeft" && e.key !== "ArrowRight") return;
          e.preventDefault();
          const order: FormTab[] = ["details", "permissions"];
          const next = order[(order.indexOf(tab) + (e.key === "ArrowRight" ? 1 : -1) + order.length) % order.length];
          setTab(next);
          document.getElementById(`user-form-tab-${next}`)?.focus();
        }}
      >
        {FORM_TABS.map(({ id, label }) => (
          <button
            key={id}
            id={`user-form-tab-${id}`}
            type="button"
            role="tab"
            aria-selected={tab === id}
            aria-controls={`user-form-panel-${id}`}
            tabIndex={tab === id ? 0 : -1}
            onClick={() => setTab(id)}
            className={`-mb-px flex items-center gap-2 border-b-2 px-4 py-2 text-sm font-medium transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-green-600 ${
              tab === id
                ? "border-green-600 text-green-700"
                : "border-transparent text-slate-600 hover:text-slate-800"
            }`}
          >
            {label}
            {id === "permissions" && overrideCount > 0 && (
              <span
                className="inline-flex h-[18px] min-w-[18px] items-center justify-center rounded-full bg-green-600 px-1 text-[10px] font-semibold text-white"
                title={`${overrideCount} permission${overrideCount === 1 ? "" : "s"} overridden for this user`}
              >
                {overrideCount}
              </span>
            )}
          </button>
        ))}
      </div>

      {/* Profile fields */}
      <div
        id="user-form-panel-details"
        role="tabpanel"
        aria-labelledby="user-form-tab-details"
        hidden={tab !== "details"}
        // The class must carry the hiding too. `hidden` works through the UA stylesheet's
        // [hidden] { display: none }, which LOSES to any class that sets display — `grid` here —
        // so the attribute alone left this panel fully visible on the Permissions tab.
        className={tab === "details" ? "grid grid-cols-2 gap-3" : "hidden"}
      >
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
            // Lower-cased as it is typed so the field always shows exactly what will be
            // saved — the backend normalises the same way (RAL-254).
            onChange={(e) => onChange({ username: e.target.value.toLowerCase() })}
            placeholder="juandelacruz"
            autoComplete="off"
            className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 font-mono"
          />
          <p className="mt-1 text-xs text-slate-600">
            Saved in lowercase — capitals are converted automatically. Signing in is not
            case-sensitive, so the user can type it any way they like.
          </p>
        </div>

        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Email
            <span className="ml-1 font-normal text-slate-600">(optional)</span>
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
            <p className="mt-1 text-[11px] text-slate-600">Office users are Staff (encoder).</p>
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
            <p className="mt-1 text-[11px] text-slate-600">SuperAdmin / Admin have no division.</p>
          )}
        </div>

        {/* Office (v1.1) — pick a guest office to create a Budget-Planning-only user. */}
        <div className="col-span-2">
          <label className="block text-xs font-medium text-slate-600 mb-1">
            Office
            <span className="ml-1 font-normal text-slate-600">
              (another office — clears Division)
            </span>
          </label>
          <OfficeSelect
            offices={offices}
            value={form.officeId ?? null}
            onChange={handleOfficeChange}
            // Blank means the host office, not "no office" — every user has one since RAL-258.
            // Named from the flagged row rather than the literal "PPDO" so a rename carries.
            allOptionLabel={`— ${hostOffice?.officeCode ?? "Host office"} (this office) —`}
          />
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

        <div className="col-span-2">
          <LandingPageSelect
            value={form.landingPage ?? null}
            onChange={(landingPage) => onChange({ landingPage })}
            reachability={{
              isOfficeUser,
              // Mirrors PermissionService: SuperAdmin/Admin hold every flag, so only Staff
              // depend on their division and overrides.
              canAccessInventory:
                form.role !== "Staff" ||
                ((form as UpdateUserRequest).overrideCanAccessInventory ??
                  selectedDivision?.canAccessInventory ??
                  false),
              canAccessBudgetPlanning:
                form.role !== "Staff" ||
                isOfficeUser ||
                ((form as UpdateUserRequest).overrideCanAccessBudgetPlanning ??
                  selectedDivision?.canAccessBudgetPlanning ??
                  false),
            }}
            hint="Where this user lands after signing in. Only pages they can open are listed; leave unset to use their division or office default."
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
            <span className="text-xs text-slate-600">
              {(form as UpdateUserRequest).isActive ? "Active" : "Inactive"}
            </span>
          </div>
        )}
      </div>

      {/* Permissions */}
      <div
        id="user-form-panel-permissions"
        role="tabpanel"
        aria-labelledby="user-form-tab-permissions"
        hidden={tab !== "permissions"}
        // Explicit for the same reason as the Details panel — space-y-4 sets no display, so this
        // one happened to work on the attribute alone. Not something to leave to luck.
        className={tab === "permissions" ? "space-y-4" : "hidden"}
      >

      {/* Create has no override fields — CreateUserDto does not carry them, so say where
          they live rather than showing an empty tab. */}
      {!isEdit && (
        <p className="bg-slate-50 px-3 py-2 text-xs text-slate-600">
          Permissions are set after the account exists. A new user inherits every flag from their
          division — reopen this tab from <span className="font-medium">Edit User</span> to grant
          or deny one individually.
        </p>
      )}

      {/* Permission overrides — Staff: all flags; Admin: adminOnly flags only */}
      {isEdit && showOverrides && (
        <div>
          <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-2">
            Permission Overrides
            <span className="ml-1 font-normal normal-case tracking-normal text-slate-600">
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
          <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide mb-1">
            Permission Overrides
          </p>
          <p className="text-xs text-slate-600 mb-2">
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
        <p className="text-xs text-slate-600 bg-slate-50 px-3 py-2">
          SuperAdmin always has full access — permission overrides do not apply.
        </p>
      )}

      {/* Admin full-access note — only when no adminOnly overrides exist */}
      {isEdit && showAdminOverrides && adminOnlyKeys.length === 0 && (
        <p className="text-xs text-slate-600 bg-slate-50 px-3 py-2">
          Admin always has full access — permission overrides do not apply.
        </p>
      )}

      </div>

      {/* Error — outside both panels on purpose: a save can fail on a field the user cannot
          currently see, and a message hidden behind an inactive tab reads as nothing happening. */}
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
      <p className="text-sm text-slate-600">{message}</p>
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
  /** One-time password awaiting acknowledgement — set after a create or a reset (RAL-254). */
  const [issued, setIssued] = useState<{
    fullName: string;
    username: string;
    password: string;
    context: "created" | "reset";
  } | null>(null);
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
        router.replace(!data.isHostOffice ? "/budget-planning" : "/dashboard");
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
        setOffices(await listOffices({ active: "true" }));
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
      const { data } = await api.post<UserCredentialResponse>("/users", addForm);
      setShowAdd(false);
      await loadData();
      // Shown once — the plaintext is not stored and cannot be fetched again.
      setIssued({
        fullName: data.user.fullName,
        username: data.user.username,
        password: data.temporaryPassword,
        context:  "created",
      });
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
    // Guest-office users: clear any stale host-office division — they're scoped by officeId.
    // Must test "is a GUEST office", not "has an office": since RAL-258 every user has one, so
    // the old null test would wipe the division of every host-office user opened for edit.
    const isGuestOffice =
      user.officeId != null && !offices.find((o) => o.id === user.officeId)?.isHostOffice;
    const divisionId = isGuestOffice ? null : user.divisionId;
    setEditForm({
      fullName:                      user.fullName,
      username:                      user.username,
      email:                         user.email,
      role:                          user.role,
      divisionId,
      officeId:                      user.officeId,
      position:                      user.position,
      contactNo:                     user.contactNo,
      landingPage:                   user.landingPage,
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
      const { data } = await api.put<UserCredentialResponse>(`/users/${resetTarget.id}/reset-password`);
      setResetTarget(null);
      setIssued({
        fullName: data.user.fullName,
        username: data.user.username,
        password: data.temporaryPassword,
        context:  "reset",
      });
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
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
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
              className="text-sm text-slate-600 hover:text-slate-600 transition-colors px-2"
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
            <div className="flex flex-col items-center justify-center py-16 gap-2 text-slate-600">
              <span className="text-3xl">👤</span>
              <p className="text-sm">
                {search ? "No users match your search." : "No users found."}
              </p>
            </div>
          ) : (
            <div className="overflow-x-auto overflow-y-hidden">
              <table className="w-full text-sm">
                <thead>
                  <tr className="bg-slate-50 border-b border-slate-200 text-xs text-slate-600 uppercase tracking-wide">
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
                          <div className="text-xs text-slate-600">{user.position}</div>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <div className="font-mono text-sm text-slate-600">{user.username}</div>
                        {user.email && (
                          <div className="text-xs text-slate-600">{user.email}</div>
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
                        <RowActions
                          btnPaddingX="px-1"
                          actions={[
                            { key: "edit", label: "Edit", onClick: () => openEdit(user) },
                            { key: "reset", label: "Reset", onClick: () => setResetTarget(user) },
                            user.isActive
                              ? { key: "deactivate", label: "Deactivate", onClick: () => setDeactivateTarget(user), variant: "danger" }
                              : { key: "activate", label: "Activate", onClick: () => setDeactivateTarget(user) },
                          ] satisfies RowAction[]}
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>

              {/* Row count */}
              <div className="px-4 py-2 border-t border-slate-100 text-xs text-slate-600">
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
          <p className="text-xs text-slate-600 mb-4">
            A one-time password is generated automatically and shown once after the account is
            created. Copy it then and give it to the user — it cannot be retrieved afterwards.
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

      {/* ── One-time password, shown once (RAL-254) ───────────────────────── */}
      {issued && (
        <IssuedPasswordDialog
          fullName={issued.fullName}
          username={issued.username}
          password={issued.password}
          context={issued.context}
          onClose={() => setIssued(null)}
        />
      )}

      {/* ── Reset Password confirm ─────────────────────────────────────────── */}
      {resetTarget && (
        <ConfirmDialog
          message={`Reset password for ${resetTarget.fullName}? A new one-time password will be issued and shown once, and any active session will be signed out.`}
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
