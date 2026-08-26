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

import { Suspense, useState, useEffect } from "react";
import Image from "next/image";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import axios from "axios";
import api from "@/lib/api";
import { auth } from "@/lib/auth";
import { APP_VERSION } from "@/lib/version";
import type {
  LoginResponse,
  RefreshErrorReason,
  ForgotPasswordResponse,
  VerifyRecoveryResponse,
} from "@/types/auth";
import { resolveLandingPath, LANDING_FALLBACK } from "@/lib/landing";
import { clearMeCache, fetchMe } from "@/lib/me-cache";

// ---------------------------------------------------------------------------
// Unexpected-logout explanation (RAL-198) — carried from a failed silent
// refresh via ?reason= on the /login redirect. See lib/auth-redirect.ts.
// ---------------------------------------------------------------------------

const LOGOUT_REASON_MESSAGES: Record<RefreshErrorReason, string> = {
  token_superseded:
    "You were signed out because someone signed into this account from another device or browser. If you both need access at the same time, ask an admin for a separate account.",
  token_expired: "Your session expired — please sign in again.",
};

function isRefreshErrorReason(value: string | null): value is RefreshErrorReason {
  return value === "token_superseded" || value === "token_expired";
}

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
// Forgot password (RAL-269) — self-service reset via a fixed recovery question.
//
// A generic failure message covers every failure mode the backend can return —
// unknown username, no recovery answer set, wrong answer, or locked out. Never show
// a different message per case; that would undo the RAL-265 enumeration guard.
// ---------------------------------------------------------------------------

const GENERIC_RECOVERY_FAILURE =
  "We couldn't verify that answer. Check your username and answer, or contact your administrator.";

const usernameSchema = z.object({
  username: z.string().min(1, "Username is required"),
});
type UsernameFormData = z.infer<typeof usernameSchema>;

const answerSchema = z.object({
  answer: z.string().min(1, "Answer is required"),
});
type AnswerFormData = z.infer<typeof answerSchema>;

type RecoveryStep = "username" | "answer" | "success";

function ForgotPasswordModal({ onClose }: { onClose: () => void }) {
  const [step, setStep] = useState<RecoveryStep>("username");
  const [username, setUsername] = useState("");
  const [questionText, setQuestionText] = useState("");
  const [temporaryPassword, setTemporaryPassword] = useState("");
  const [serverError, setServerError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  const usernameForm = useForm<UsernameFormData>({ resolver: zodResolver(usernameSchema) });
  const answerForm = useForm<AnswerFormData>({ resolver: zodResolver(answerSchema) });

  async function submitUsername(values: UsernameFormData) {
    setServerError(null);
    try {
      const { data } = await api.post<ForgotPasswordResponse>("/auth/forgot-password", {
        username: values.username,
      });
      setUsername(values.username);
      setQuestionText(data.questionText);
      setStep("answer");
    } catch {
      // /auth/forgot-password always returns 200 — a thrown error here means the
      // request itself failed (network/cold start), not a bad username.
      setServerError("Something went wrong. Please try again.");
    }
  }

  async function submitAnswer(values: AnswerFormData) {
    setServerError(null);
    try {
      const { data } = await api.post<VerifyRecoveryResponse>("/auth/verify-recovery", {
        username,
        answer: values.answer,
      });
      setTemporaryPassword(data.temporaryPassword);
      setStep("success");
    } catch {
      setServerError(GENERIC_RECOVERY_FAILURE);
    }
  }

  async function copyPassword() {
    try {
      await navigator.clipboard.writeText(temporaryPassword);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard API unavailable — the password is still shown on screen to copy by hand.
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/50 px-4">
      <div className="w-full max-w-sm bg-white rounded-xl shadow-lg px-6 py-6">
        {step === "username" && (
          <>
            <h3 className="text-lg font-bold text-slate-800 mb-1">Forgot password?</h3>
            <p className="text-sm text-slate-600 mb-5">
              Enter your username and we&apos;ll show your recovery question.
            </p>
            <form onSubmit={usernameForm.handleSubmit(submitUsername)} noValidate className="space-y-4">
              <div>
                <label htmlFor="fp-username" className="block text-sm font-medium text-slate-600 mb-1">
                  Username
                </label>
                <input
                  id="fp-username"
                  type="text"
                  autoComplete="username"
                  autoFocus
                  {...usernameForm.register("username")}
                  className="w-full px-3 py-2.5 rounded-lg text-sm text-slate-800 border border-slate-300
                             bg-white shadow-sm placeholder:text-slate-400 transition-colors
                             focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent"
                />
                {usernameForm.formState.errors.username && (
                  <p className="mt-1 text-xs text-red-600">
                    {usernameForm.formState.errors.username.message}
                  </p>
                )}
              </div>

              {serverError && (
                <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3">
                  <p className="text-sm text-red-700">{serverError}</p>
                </div>
              )}

              <div className="flex items-center justify-between gap-3 pt-1">
                <button
                  type="button"
                  onClick={onClose}
                  className="text-sm text-slate-600 hover:text-slate-800 transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={usernameForm.formState.isSubmitting}
                  className="bg-green-600 text-white font-semibold py-2 px-4 rounded-lg text-sm
                             hover:bg-green-500 active:bg-green-700 transition-colors
                             focus:outline-none focus:ring-2 focus:ring-green-600 focus:ring-offset-2
                             disabled:opacity-60 disabled:cursor-not-allowed"
                >
                  {usernameForm.formState.isSubmitting ? "Checking…" : "Continue"}
                </button>
              </div>
            </form>
          </>
        )}

        {step === "answer" && (
          <>
            <h3 className="text-lg font-bold text-slate-800 mb-1">Answer your recovery question</h3>
            <p className="text-sm text-slate-600 mb-5">{questionText}</p>
            <form onSubmit={answerForm.handleSubmit(submitAnswer)} noValidate className="space-y-4">
              <div>
                <label htmlFor="fp-answer" className="block text-sm font-medium text-slate-600 mb-1">
                  Answer
                </label>
                <input
                  id="fp-answer"
                  type="text"
                  autoFocus
                  {...answerForm.register("answer")}
                  className="w-full px-3 py-2.5 rounded-lg text-sm text-slate-800 border border-slate-300
                             bg-white shadow-sm placeholder:text-slate-400 transition-colors
                             focus:outline-none focus:ring-2 focus:ring-green-600 focus:border-transparent"
                />
                {answerForm.formState.errors.answer && (
                  <p className="mt-1 text-xs text-red-600">{answerForm.formState.errors.answer.message}</p>
                )}
              </div>

              {serverError && (
                <div className="rounded-lg bg-red-50 border border-red-200 px-4 py-3">
                  <p className="text-sm text-red-700">{serverError}</p>
                </div>
              )}

              <div className="flex items-center justify-between gap-3 pt-1">
                <button
                  type="button"
                  onClick={() => {
                    setServerError(null);
                    setStep("username");
                  }}
                  className="text-sm text-slate-600 hover:text-slate-800 transition-colors"
                >
                  ← Back
                </button>
                <button
                  type="submit"
                  disabled={answerForm.formState.isSubmitting}
                  className="bg-green-600 text-white font-semibold py-2 px-4 rounded-lg text-sm
                             hover:bg-green-500 active:bg-green-700 transition-colors
                             focus:outline-none focus:ring-2 focus:ring-green-600 focus:ring-offset-2
                             disabled:opacity-60 disabled:cursor-not-allowed"
                >
                  {answerForm.formState.isSubmitting ? "Verifying…" : "Verify"}
                </button>
              </div>
            </form>
          </>
        )}

        {step === "success" && (
          <>
            <h3 className="text-lg font-bold text-slate-800 mb-1">Your temporary password</h3>
            <p className="text-sm text-slate-600 mb-4">
              Use this to sign in. You&apos;ll be asked to set a new password right away.
            </p>
            <div className="flex items-center gap-2 mb-2">
              <code className="flex-1 px-3 py-2.5 rounded-lg text-sm font-mono text-slate-800 bg-slate-100 border border-slate-300 select-all">
                {temporaryPassword}
              </code>
              <button
                type="button"
                onClick={copyPassword}
                className="px-3 py-2.5 rounded-lg text-sm font-medium text-green-700 bg-green-50
                           border border-green-200 hover:bg-green-100 transition-colors whitespace-nowrap"
              >
                {copied ? "Copied!" : "Copy"}
              </button>
            </div>
            <p className="text-xs text-slate-500 mb-5">
              If you didn&apos;t request this, contact your administrator.
            </p>
            <div className="flex justify-end">
              <button
                type="button"
                onClick={onClose}
                className="bg-green-600 text-white font-semibold py-2 px-4 rounded-lg text-sm
                           hover:bg-green-500 active:bg-green-700 transition-colors
                           focus:outline-none focus:ring-2 focus:ring-green-600 focus:ring-offset-2"
              >
                Done
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

function LoginPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const [serverError, setServerError] = useState<string | null>(null);
  const [apiStatus, setApiStatus] = useState<ApiStatus>("checking");
  const [showForgotPassword, setShowForgotPassword] = useState(false);

  const reasonParam = searchParams.get("reason");
  const logoutReason = isRefreshErrorReason(reasonParam) ? LOGOUT_REASON_MESSAGES[reasonParam] : null;

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

      // A fresh login can be a DIFFERENT identity than whatever this browser tab had
      // cached before — e.g. testing a second account without logging out of the first.
      // The stale cache would otherwise carry over silently: the portal layout's own
      // fetchMe() short-circuits on a warm cache, so the wrong user's permissions AND
      // password/recovery gates (RAL-266/267) would apply until a hard reload.
      clearMeCache();

      // Where the user lands is resolved server-side and returned on /auth/me (RAL-261);
      // it already accounts for their preference, division, office and permissions.
      // Goes through fetchMe() (not a bare api.get) so this call also warms the cache the
      // portal layout is about to read — one request instead of two.
      try {
        const me = await fetchMe();
        router.replace(resolveLandingPath(me));
      } catch {
        // /auth/me failed — send them somewhere every account can reach rather than
        // guessing at a dashboard an office user cannot open.
        router.replace(LANDING_FALLBACK);
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
          src="/images/ppdo-logo.webp"
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
              src="/images/ppdo-logo.webp"
              alt="PPDO"
              width={36}
              height={36}
              priority
              className="rounded-full object-contain"
            />
            <span className="font-bold text-green-700 text-lg">PPDO Portal</span>
          </div>

          {/* Unexpected-logout explanation (RAL-198) */}
          {logoutReason && (
            <div className="mb-4 rounded-lg bg-amber-50 border border-amber-200 px-4 py-3">
              <p className="text-sm text-amber-800">{logoutReason}</p>
            </div>
          )}

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
                className="block text-sm font-medium text-slate-600 mb-1"
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
              <div className="flex items-center justify-between mb-1">
                <label
                  htmlFor="password"
                  className="block text-sm font-medium text-slate-600"
                >
                  Password
                </label>
                <button
                  type="button"
                  onClick={() => setShowForgotPassword(true)}
                  className="text-xs text-green-700 hover:text-green-800 hover:underline transition-colors"
                >
                  Forgot password?
                </button>
              </div>
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

      {showForgotPassword && (
        <ForgotPasswordModal onClose={() => setShowForgotPassword(false)} />
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page export — wraps inner component in Suspense (required for useSearchParams)
// ---------------------------------------------------------------------------

function LoginPageFallback() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-white">
      <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
    </div>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={<LoginPageFallback />}>
      <LoginPageInner />
    </Suspense>
  );
}
