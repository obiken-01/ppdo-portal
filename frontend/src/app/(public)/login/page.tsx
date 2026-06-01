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

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import api from "@/lib/api";
import { auth } from "@/lib/auth";
import type { LoginResponse } from "@/types/auth";

// ---------------------------------------------------------------------------
// Validation schema
// ---------------------------------------------------------------------------

const schema = z.object({
  email: z
    .string()
    .min(1, "Email is required")
    .email("Enter a valid email address"),
  password: z.string().min(1, "Password is required"),
});

type FormData = z.infer<typeof schema>;

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function LoginPage() {
  const router = useRouter();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormData>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormData) {
    setServerError(null);
    try {
      const { data } = await api.post<LoginResponse>("/auth/login", {
        email: values.email,
        password: values.password,
      });
      auth.login(data);
      router.replace("/dashboard");
    } catch {
      setServerError("Invalid email or password. Please try again.");
    }
  }

  return (
    <div className="min-h-screen flex font-sans">
      {/* ── Left panel — branding (desktop only) ────────────────────────── */}
      <aside className="hidden md:flex md:w-1/4 bg-green-700 flex-col items-center justify-center px-8 py-14 text-white">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src="/images/ppdo-logo-placeholder.png"
          alt="PPDO Logo"
          width={88}
          height={88}
          className="rounded-full mb-6 object-contain"
        />
        <h1 className="text-2xl font-bold text-center mb-1">PPDO Portal</h1>
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
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/images/Ph_seal_occidental_mindoro.png"
            alt="Province Seal"
            width={40}
            height={40}
            className="object-contain"
          />
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/images/Bagong_Pilipinas_logo.png"
            alt="Bagong Pilipinas"
            width={40}
            height={40}
            className="object-contain"
          />
        </div>
      </aside>

      {/* ── Right panel — form ───────────────────────────────────────────── */}
      <main className="flex-1 flex flex-col items-center justify-center px-8 py-12 bg-white">
        <div className="w-full max-w-sm">
          {/* Mobile logo (visible only on small screens) */}
          <div className="flex md:hidden items-center justify-center gap-2 mb-8">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src="/images/ppdo-logo-placeholder.png"
              alt="PPDO"
              width={36}
              height={36}
              className="rounded-full object-contain"
            />
            <span className="font-bold text-green-700 text-lg">PPDO Portal</span>
          </div>

          <h2 className="text-2xl font-bold text-slate-800 mb-1">
            Welcome back
          </h2>
          <p className="text-slate-500 text-sm mb-8">
            Sign in to your PPDO staff account
          </p>

          <form onSubmit={handleSubmit(onSubmit)} noValidate className="space-y-5">
            {/* Email */}
            <div>
              <label
                htmlFor="email"
                className="block text-sm font-medium text-slate-700 mb-1"
              >
                Email address
              </label>
              <input
                id="email"
                type="email"
                autoComplete="email"
                placeholder="your.name@ppdo.gov.ph"
                {...register("email")}
                className={`w-full px-3 py-2.5 rounded-lg text-sm text-slate-800 border
                  placeholder:text-slate-400 transition-colors
                  focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent
                  ${errors.email
                    ? "border-red-400 bg-red-50"
                    : "border-slate-200 bg-white hover:border-slate-300"
                  }`}
              />
              {errors.email && (
                <p className="mt-1 text-xs text-red-600">{errors.email.message}</p>
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
                    : "border-slate-200 bg-white hover:border-slate-300"
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

          {/* Back link */}
          <div className="mt-6 text-center">
            <Link
              href="/"
              className="text-sm text-slate-400 hover:text-green-700 transition-colors"
            >
              ← Back to home
            </Link>
          </div>
        </div>
      </main>
    </div>
  );
}
