"use client";

/**
 * Configuration → Investment Planning Settings (v1.8.0 — PPDO-144, spec
 * docs/v1.8/Default_Fiscal_Year_Spec.md §3.1 / §6.1).
 *
 * One setting today: the province-wide default fiscal year every Investment Planning page opens
 * on. Host-office config managers only (SuperAdmin from anywhere) — the server enforces it
 * (`CanManageInvestmentPlanningSettingsAsync`); the redirect and the hidden tile are courtesy.
 *
 *   GET / PUT /api/config/investment-planning/default-fiscal-year
 */

import { useCallback, useEffect, useState } from "react";
import { useMe } from "@/lib/me-cache";
import { configErrorMessage, getDefaultFiscalYear, updateDefaultFiscalYear } from "@/lib/config";
import ConfigPageHeader from "@/components/ui/ConfigPageHeader";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import type { DefaultFiscalYearResponse } from "@/types";

const MIN_FISCAL_YEAR = 2020;

/** Current Manila calendar year + 3 — mirrors the server's upper bound. */
function maxFiscalYear(): number {
  const manilaYear = Number(
    new Date().toLocaleDateString("en-CA", { timeZone: "Asia/Manila", year: "numeric" }),
  );
  return manilaYear + 3;
}

function formatManila(iso: string): string {
  return new Date(iso).toLocaleString("en-PH", {
    timeZone: "Asia/Manila",
    dateStyle: "medium",
    timeStyle: "short",
  });
}

export default function InvestmentPlanningSettingsPage() {
  const { toast } = useToast();
  const me = useMe(
    (m) => m.role === "SuperAdmin" || (m.canManageConfig && m.isHostOffice),
    (m) => (!m.isHostOffice ? "/budget-planning" : "/dashboard"),
  );

  const [setting, setSetting] = useState<DefaultFiscalYearResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState(false);

  const [input, setInput] = useState("");
  const [validation, setValidation] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [confirmClear, setConfirmClear] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setLoadError(false);
    try {
      const data = await getDefaultFiscalYear();
      setSetting(data);
      setInput(data.defaultFiscalYear?.toString() ?? "");
    } catch {
      setLoadError(true);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (me) void load();
  }, [me, load]);

  async function save(year: number | null) {
    setSaving(true);
    setValidation(null);
    try {
      const saved = await updateDefaultFiscalYear({ defaultFiscalYear: year });
      setSetting(saved);
      setInput(saved.defaultFiscalYear?.toString() ?? "");
      if (year === null) toast.success("Default cleared — pages use their own defaults.");
      else toast.success(`Default fiscal year set to FY ${year}.`);
    } catch (err) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      const message = configErrorMessage(err, "The default fiscal year could not be saved.");
      // A 400 is a validation message for the field; anything else is a save failure (typed value kept).
      if (status === 400) setValidation(message);
      else toast.error("Could not save", message);
    } finally {
      setSaving(false);
    }
  }

  function handleSave() {
    const max = maxFiscalYear();
    const trimmed = input.trim();
    const year = Number(trimmed);
    if (!/^\d+$/.test(trimmed) || year < MIN_FISCAL_YEAR || year > max) {
      setValidation(`Fiscal year must be between ${MIN_FISCAL_YEAR} and ${max}.`);
      return;
    }
    void save(year);
  }

  async function handleClear() {
    setConfirmClear(false);
    await save(null);
  }

  if (!me) return null;

  const isSet = setting?.defaultFiscalYear != null;

  return (
    <div className="min-h-full bg-slate-100 font-sans">
      <div className="max-w-6xl mx-auto px-6 py-6 space-y-5">
        <ConfigPageHeader
          title="Investment Planning Settings"
          description="Province-wide settings for the Investment Planning pages."
        />

        <section className="bg-white border border-slate-200 px-5 py-5 max-w-xl">
          <h2 className="text-base font-semibold text-slate-800">Default fiscal year</h2>
          <p className="mt-1 text-sm text-slate-600">
            Investment Planning pages open on this year. Users can still switch year on any page.
            Leave unset to let each page choose.
          </p>

          {loading ? (
            <div className="mt-4 space-y-3" aria-hidden="true">
              <div className="h-9 bg-slate-100 animate-pulse" />
              <div className="h-5 w-2/3 bg-slate-100 animate-pulse" />
            </div>
          ) : loadError ? (
            <div
              role="alert"
              className="mt-4 flex items-center justify-between gap-3 border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700"
            >
              <span>Could not load the setting.</span>
              <button
                type="button"
                onClick={() => void load()}
                className="font-medium underline hover:no-underline"
              >
                Retry
              </button>
            </div>
          ) : (
            <div className="mt-4">
              <label htmlFor="default-fiscal-year" className="block text-sm font-medium text-slate-800">
                Fiscal year
              </label>
              <div className="mt-1 flex flex-wrap items-center gap-2">
                <input
                  id="default-fiscal-year"
                  type="number"
                  inputMode="numeric"
                  value={input}
                  placeholder="e.g. 2028"
                  disabled={saving}
                  aria-invalid={validation ? true : undefined}
                  aria-describedby={validation ? "default-fiscal-year-error" : undefined}
                  onChange={(e) => {
                    setInput(e.target.value);
                    setValidation(null);
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter") handleSave();
                  }}
                  className="w-40 px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100"
                />
                <button
                  type="button"
                  onClick={handleSave}
                  disabled={saving}
                  className="px-4 py-2 text-sm font-medium bg-green-600 hover:bg-green-500 text-white disabled:opacity-50"
                >
                  {saving ? "Saving…" : "Save"}
                </button>
                {isSet && (
                  <button
                    type="button"
                    onClick={() => setConfirmClear(true)}
                    disabled={saving}
                    className="text-sm font-medium text-green-600 hover:text-green-700 underline-offset-2 hover:underline disabled:opacity-50"
                  >
                    Clear default
                  </button>
                )}
              </div>
              {validation && (
                <p id="default-fiscal-year-error" role="alert" className="mt-1 text-sm text-red-600">
                  {validation}
                </p>
              )}

              <p className="mt-3 text-sm text-slate-600">
                {isSet && setting?.updatedAt
                  ? `Last changed ${formatManila(setting.updatedAt)}${
                      setting.updatedByName ? ` by ${setting.updatedByName}` : ""
                    }`
                  : "Not set — each page uses its own default."}
              </p>
            </div>
          )}
        </section>
      </div>

      {confirmClear && (
        <ConfirmDialog
          title="Clear default fiscal year"
          message="Clear the default fiscal year? Every Investment Planning page goes back to choosing its own year."
          confirmLabel="Clear default"
          variant="warning"
          onConfirm={() => void handleClear()}
          onClose={() => setConfirmClear(false)}
        />
      )}
    </div>
  );
}
