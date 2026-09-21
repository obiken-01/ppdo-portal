"use client";

import { useCallback, useEffect, useState } from "react";
import {
  getMyProfile,
  updateMyProfile,
  type UpdateProfileRequest,
} from "@/lib/account";
import { useToast } from "@/components/ui/Toast";
import type { MeResponse, UserResponse } from "@/types";
import LandingPageSelect, { reachabilityFromMe } from "@/components/ui/LandingPageSelect";
import { fetchMe } from "@/lib/me-cache";
import ChangePasswordForm from "@/components/account/ChangePasswordForm";
import RecoveryAnswerForm from "@/components/account/RecoveryAnswerForm";

type Tab = "profile" | "security";

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function AccountPage() {
  const [activeTab, setActiveTab] = useState<Tab>("profile");
  const [profile, setProfile]     = useState<UserResponse | null>(null);

  const loadProfile = useCallback(async () => {
    try {
      const data = await getMyProfile();
      setProfile(data);
    } catch { /* layout auth guard handles session expiry */ }
  }, []);

  useEffect(() => { loadProfile(); }, [loadProfile]);

  function tabCls(tab: Tab) {
    return `px-5 py-2.5 text-sm font-medium border-b-2 transition-colors ${
      activeTab === tab
        ? "border-green-600 text-green-700"
        : "border-transparent text-slate-600 hover:text-slate-800"
    }`;
  }

  return (
    <div className="p-6 max-w-2xl">
      <h1 className="text-xl font-bold text-slate-800 mb-6">My Account</h1>

      {/* ── Tabs ─────────────────────────────────────────────────────────── */}
      <div className="flex border-b border-slate-200 mb-6">
        <button className={tabCls("profile")} onClick={() => setActiveTab("profile")}>
          Profile
        </button>
        <button className={tabCls("security")} onClick={() => setActiveTab("security")}>
          Security
        </button>
      </div>

      {/* ── Tab panels ───────────────────────────────────────────────────── */}
      {activeTab === "profile" && (
        <ProfileTab profile={profile} onSaved={loadProfile} />
      )}
      {activeTab === "security" && (
        <SecurityTab />
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Profile tab
// ---------------------------------------------------------------------------

function ProfileTab({
  profile,
  onSaved,
}: {
  profile: UserResponse | null;
  onSaved: () => void;
}) {
  const { toast } = useToast();

  const [form, setForm] = useState<UpdateProfileRequest>({
    fullName:  "",
    username:  "",
    email:     null,
    position:  null,
    contactNo: null,
    landingPage: null,
  });
  const [saving, setSaving] = useState(false);
  const [error, setError]   = useState<string | null>(null);
  // Resolved permission flags live on /auth/me; UserResponse only carries the raw overrides.
  const [me, setMe] = useState<MeResponse | null>(null);

  useEffect(() => { fetchMe().then(setMe).catch(() => setMe(null)); }, []);

  // Pre-fill once profile loads
  useEffect(() => {
    if (!profile) return;
    setForm({
      fullName:  profile.fullName,
      username:  profile.username,
      email:     profile.email ?? null,
      position:  profile.position ?? null,
      contactNo: profile.contactNo ?? null,
      landingPage: profile.landingPage ?? null,
    });
  }, [profile]);

  function patch(field: keyof UpdateProfileRequest, value: string) {
    setForm((f) => ({ ...f, [field]: value || null }));
    setError(null);
  }

  async function handleSave() {
    if (!form.fullName.trim()) { setError("Full name is required."); return; }
    if (!form.username.trim()) { setError("Username is required."); return; }

    setSaving(true);
    setError(null);
    try {
      await updateMyProfile({
        ...form,
        fullName: form.fullName.trim(),
        username: form.username.trim(),
      });
      toast.success("Profile updated successfully.");
      onSaved();
    } catch (err) {
      const msg =
        (err as { response?: { data?: string } })?.response?.data ??
        "Failed to update profile.";
      setError(msg);
    } finally {
      setSaving(false);
    }
  }

  const inputCls =
    "w-full px-3 py-2 text-sm border border-slate-200 bg-white text-slate-800 focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent";
  const readonlyCls =
    "w-full px-3 py-2 text-sm border border-slate-100 bg-slate-50 text-slate-400 cursor-not-allowed";

  return (
    <div className="bg-white border border-slate-200 shadow-sm p-6">
      <h2 className="text-sm font-semibold text-slate-800 mb-5">Profile Information</h2>

      <div className="space-y-4">
        <Field label="Full Name" required>
          <input
            className={inputCls}
            value={form.fullName}
            onChange={(e) => patch("fullName", e.target.value)}
            placeholder="Full legal name"
          />
        </Field>

        <Field label="Username" required>
          <input
            className={inputCls}
            value={form.username}
            onChange={(e) => patch("username", e.target.value)}
            placeholder="username"
            autoComplete="username"
          />
        </Field>

        <Field label="Email">
          <input
            type="email"
            className={inputCls}
            value={form.email ?? ""}
            onChange={(e) => patch("email", e.target.value)}
            placeholder="email@example.com (optional)"
          />
        </Field>

        <Field label="Position">
          <input
            className={inputCls}
            value={form.position ?? ""}
            onChange={(e) => patch("position", e.target.value)}
            placeholder="Job title or position"
          />
        </Field>

        <Field label="Contact Number">
          <input
            className={inputCls}
            value={form.contactNo ?? ""}
            onChange={(e) => patch("contactNo", e.target.value)}
            placeholder="e.g. 09171234567"
          />
        </Field>

        <Field label="Landing Page">
          <LandingPageSelect
            label=""
            value={form.landingPage}
            onChange={(landingPage) => setForm((f) => ({ ...f, landingPage }))}
            reachability={me ? reachabilityFromMe(me) : undefined}
            hint="The page you land on after signing in. Leave unset to follow your division or office default."
          />
        </Field>

        {/* Read-only fields — managed by User Management only */}
        <div className="pt-2 border-t border-slate-100">
          <p className="text-xs text-slate-600 mb-3">
            Role and division are managed by administrators.
          </p>
          <div className="grid grid-cols-2 gap-4">
            <Field label="Role">
              <input className={readonlyCls} value={profile?.role ?? "—"} readOnly />
            </Field>
            <Field label="Division">
              <input className={readonlyCls} value={profile?.division ?? "—"} readOnly />
            </Field>
          </div>
        </div>

        {error && (
          <p className="text-sm text-danger-500">{error}</p>
        )}

        <div className="flex justify-end pt-2">
          <button
            onClick={handleSave}
            disabled={saving}
            className="px-5 py-2 text-sm font-medium text-white bg-green-600 hover:bg-green-500 disabled:opacity-60 transition-colors flex items-center gap-2"
          >
            {saving && (
              <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
            )}
            {saving ? "Saving…" : "Save Changes"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Security tab
// ---------------------------------------------------------------------------

function SecurityTab() {
  const { toast } = useToast();

  return (
    <div className="space-y-6">
      <div className="bg-white border border-slate-200 shadow-sm p-6">
        <h2 className="text-sm font-semibold text-slate-800 mb-5">Change Password</h2>
        <ChangePasswordForm onSuccess={() => toast.success("Password updated successfully.")} />
      </div>

      <div className="bg-white border border-slate-200 shadow-sm p-6">
        <h2 className="text-sm font-semibold text-slate-800 mb-1">Account Recovery</h2>
        <p className="text-xs text-slate-600 mb-5">
          Used to reset your own password if you ever forget it. Saving here replaces
          whatever question and answer you set before.
        </p>
        <RecoveryAnswerForm
          onSuccess={() => toast.success("Recovery answer updated successfully.")}
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Label + input wrapper
// ---------------------------------------------------------------------------

function Field({
  label,
  required = false,
  children,
}: {
  label: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label className="block text-xs font-medium text-slate-600 mb-1">
        {label}
        {required && <span className="text-danger-500 ml-0.5">*</span>}
      </label>
      {children}
    </div>
  );
}
