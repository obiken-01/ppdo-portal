"use client";

/**
 * Login page — RAL-42.
 * Matches Penpot frame "02 Login".
 *
 * Layout:
 *   Left panel  (green, hidden on mobile) — PPDO branding
 *   Right panel (white)                   — email + password form
 *
 * On success: stores access + refresh tokens via auth.login() then
 * navigates to /dashboard.
 */

import { useState, useEffect } from "react";
import Image from "next/image";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import axios from "axios";
import api from "@/lib/api";
import { auth } from "@/lib/auth";
import type { LoginResponse, MeResponse } from "@/types/auth";

// ---------------------------------------------------------------------------
// API status indicator
// ---------------------------------------------------------------------------

type ApiStatus = "checking" | "ok" | "unavailable";

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api";

function StatusDot({ status }: { status: ApiStatus }) {
  if (status === "checking") {
    return (
      <span className="flex items-center gap-1.5 text-xs text-slate-600">
        <span className="w-2 h-2 rounded-full bg-yellow-400 animate-pulse" />
        Connecting to server…
      </span>
    );
  }
  if (status === "ok") {
    return (
      <span className="flex items-center gap-1.5 text-xs text-green-600">
        <span className="w-2 h-2 rounded-full bg-green-500" />
        Server available
      </span>
    );
  }
  return (
    <span className="flex items-center gap-1.5 text-xs text-red-500">
      <span className="w-2 h-2 rounded-full bg-red-500" />
      Server unavailable — login may be slow
    </span>
  );
}

// ---------------------------------------------------------------------------
// Validation schema
// ---------------------------------------------------------------------------

const schema = z.object({
  username: z.string().min(1, "Username is required"),
  password: z.string().min(1, "Password is required"),
});

type FormData = z.infer<typeof schema>;

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

const APP_VERSION = "v1.4.8";

export default function LoginPage() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);
  const [apiStatus, setApiStatus] = useState<ApiStatus>("checking");

  // Fire a health check on mount — wakes up Azure Functions + Azure SQL
  // (both auto-sleep after inactivity on the free tier).
  useEffect(() => {
    let cancelled = false;

    async function checkHealth() {
      try {
        const res = await axios.get(`${BASE_URL}/health`, { timeout: 30_000 });
        if (!cancelled) setApiStatus(res.data?.database === "ok" ? "ok" : "unavailable");
      } catch {
        if (!cancelled) setApiStatus("unavailable");
      }
    }

    checkHealth();
    return () => { cancelled = true; };
  }, []);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormData>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormData) {
    setServerError(null);
    try {
      const { data } = await api.post<LoginResponse>("/auth/login", {
        username: values.username,
        password: values.password,
      });
      auth.login(data);

      // Non-PPDO office users go straight to Budget Planning — it's their only feature.
      // PPDO users land on the main Dashboard.
      try {
        const { data: me } = await api.get<MeResponse>("/auth/me");
        router.replace(me.officeId != null ? "/budget-planning" : "/dashboard");
      } catch {
        router.replace("/dashboard");
      }
    } catch {
      setServerError("Invalid username or password. Please try again.");
    }
  }

  return (
    <div className="min-h-screen flex font-sans">
      {/* ── Left panel — branding (desktop only) ────────────────────────── */}
      <aside className="hidden md:flex md:w-1/4 bg-green-700 flex-col items-center justify-center px-8 py-14 text-white">
        <Image
          src="/images/ppdo-logo-placeholder.webp"
          alt="PPDO Logo"
          width={88}
          height={88}
          priority
          className="rounded-full mb-6 object-contain"
        />
        <h1 className="text-2xl font-bold text-center mb-1">
          PPDO Portal <span className="text-sm font-normal text-green-300">{APP_VERSION}</span>
        </h1>
        <p className="text-green-200 text-sm text-center mb-2">
          Provincial Planning and Development Office
        </p>
        <p className="text-green-300 text-xs text-center mb-10">
          Province of Occidental Mindoro, Philippines
        </p>

        {/* Divider */}
        <div className="w-12 h-px bg-green-500 mb-10" />

        {/* Tagline */}
        <p className="text-green-100 text-sm text-center max-w-xs leading-relaxed">
          One portal for inventory monitoring, records management, and office
          coordination — for all PPDO divisions.
        </p>

        {/* Footer logos */}
        <div className="flex items-center gap-4 mt-auto pt-10 opacity-70">
          <Image
            src="/images/Ph_seal_occidental_mindoro.webp"
            alt="Province Seal"
            width={40}
            height={40}
            priority
            className="object-contain"
          />
          <Image
            src="/images/Bagong_Pilipinas_logo.webp"
            alt="Bagong Pilipinas"
            width={40}
            height={40}
            priority
            className="object-contain"
          />
        </div>
      </aside>

      {/* ── Right panel — form ───────────────────────────────────────────── */}
      <main className="flex-1 flex flex-col items-center justify-center px-8 py-12 bg-white">
        <div className="w-full max-w-sm">
          {/* Mobile logo (visible only on small screens) */}
          <div className="flex md:hidden items-center justify-center gap-2 mb-8">
            <Image
              src="/images/ppdo-logo-placeholder.webp"
              alt="PPDO"
              width={36}
              height={36}
              priority
              className="rounded-full object-contain"
            />
            <span className="font-bold text-green-700 text-lg">PPDO Portal</span>
          </div>

          {/* ── Login card ──────────────────────────────────────────────── */}
          <div className="bg-green-50 border border-green-200 rounded-xl px-8 py-8 shadow-sm">

          <h2 className="text-2xl font-bold text-slate-800 mb-1">
            Welcome back
          </h2>
          <p className="text-slate-600 text-sm mb-8">
            Sign in to your PPDO staff account
          </p>

          <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-5">
            {/* Username */}
            <div>
              <label
                htmlFor="username"
                className="block text-sm font-medium text-slate-700 mb-1"
              >
                Username
              </label>
              <input
                id="username"
                type="text"
                autoComplete="username"
                placeholder="Enter your username"
                {...register("username")}
                className={`w-full px-3 py-2.5 rounded-lg text-sm text-slate-800 border
                  placeholder:text-slate-400 transition-colors
                  focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent
                  ${errors.username
                    ? "border-red-400 bg-red-50"
                    : "border-slate-300 bg-white shadow-sm hover:border-slate-400"
                  }`}
              />
              {errors.username && (
                <p className="mt-1 text-xs text-red-600">{errors.username.message}</p>
              )}
            </div>

            {/* Password */}
            <div>
              <label
                htmlFor="password"
                className="block text-sm font-medium text-slate-700 mb-1"
              >
                Password
              </label>
              <input
                id="password"
                type="password"
                autoComplete="current-password"
                placeholder="Enter your password"
                {...register("password")}
                className={`w-full px-3 py-2.5 rounded-lg text-sm text-slate-800 border
                  placeholder:text-slate-400 transition-colors
                  focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent
                  ${errors.password
                    ? "border-red-400 bg-red-50"
                    : "border-slate-300 bg-white shadow-sm hover:border-slate-400"
                  }`}
              />
              {errors.password && (
                <p className="mt-1 text-xs text-red-600">{errors.password.message}</p>
              )}
            </div>

            {/* Server error banner */}
            {serverError && (
              <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3">
                <p className="text-sm text-red-700">{serverError}</p>
              </div>
            )}

            {/* Submit button */}
            <button
              type="submit"
              disabled={isSubmitting}
              className="w-full bg-green-600 text-white font-semibold py-2.5 rounded-lg text-sm
                         hover:bg-green-500 active:bg-green-700 transition-colors
                         focus:outline-none focus:ring-2 focus:ring-green-600 focus:ring-offset-2
                         disabled:opacity-60 disabled:cursor-not-allowed flex items-center justify-center gap-2"
            >
              {isSubmitting && (
                <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              )}
              {isSubmitting ? "Signing in…" : "Sign In"}
            </button>
          </form>

          {/* API status indicator */}
          <div className="mt-6 flex items-center justify-between">
            <Link
              href="/"
              className="text-sm text-slate-600 hover:text-green-700 transition-colors"
            >
              ← Back to home
            </Link>
            <StatusDot status={apiStatus} />
          </div>

          </div>{/* end login card */}
        </div>
      </main>
    </div>
  );
}
