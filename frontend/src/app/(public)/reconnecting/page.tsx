"use client";

/**
 * Reconnecting page — RAL-199.
 *
 * Landed on when a silent refresh fails with a network error (no HTTP response
 * at all — Azure Functions cold start ~5-20s, Azure SQL Free auto-pause resume
 * ~20-30s), as opposed to a definitive 401 (RAL-198, which goes straight to
 * /login with a reason). The refresh cookie was never proven invalid here, so
 * there is no reason to force a full re-login — we just need the backend to
 * finish waking up.
 *
 * Auto-retries POST /auth/refresh a few times with backoff. On success, sends
 * the user back to wherever they were headed (?next=). On a definitive 401
 * (the cold start resolves and the cookie turns out to be genuinely invalid),
 * falls through to the normal RAL-198 /login redirect. After auto-retries are
 * exhausted, shows manual Try Again / Cancel controls instead of retrying
 * silently forever.
 */

import { Suspense, useCallback, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import axios from "axios";
import { auth } from "@/lib/auth";
import { clearMeCache } from "@/lib/me-cache";
import {
  classifyRefreshFailure,
  loginUrlWithReason,
  REFRESH_TIMEOUT_MS,
} from "@/lib/auth-redirect";
import type { LoginResponse } from "@/types/auth";

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api";

// Auto-retry a few times with backoff before asking the user to take over —
// long enough to ride out a typical cold start without hammering the backend
// or retrying silently forever. In development, skip straight to manual
// controls — a restarted local backend should surface a Try Again button
// immediately, not make the developer sit through prod-length backoff delays.
const AUTO_RETRY_DELAYS_MS = process.env.NODE_ENV === "production" ? [4000, 8000] : [];

type Phase = "retrying" | "manual" | "redirecting";

function ReconnectingPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const next = searchParams.get("next") || "/dashboard";

  const [phase, setPhase] = useState<Phase>("retrying");
  const [attempt, setAttempt] = useState(1);
  // Guards against a stale timer/response resolving after unmount or after a
  // newer attempt has already started.
  const attemptToken = useRef(0);

  const runAttempt = useCallback(
    (attemptNumber: number) => {
      const token = ++attemptToken.current;

      axios
        .post<LoginResponse>(`${BASE_URL}/auth/refresh`, {}, { withCredentials: true, timeout: REFRESH_TIMEOUT_MS })
        .then(({ data }) => {
          if (token !== attemptToken.current) return;
          auth.login(data);
          setPhase("redirecting");
          router.replace(next);
        })
        .catch((err) => {
          if (token !== attemptToken.current) return;

          const failure = classifyRefreshFailure(err);
          if (failure.kind === "unauthorized") {
            clearMeCache();
            auth.logout();
            setPhase("redirecting");
            router.replace(loginUrlWithReason(failure.reason));
            return;
          }

          // Still unreachable — auto-retry with backoff, then hand control to the user.
          const delay = AUTO_RETRY_DELAYS_MS[attemptNumber - 1];
          if (delay !== undefined) {
            setTimeout(() => {
              if (token !== attemptToken.current) return;
              setAttempt(attemptNumber + 1);
              runAttempt(attemptNumber + 1);
            }, delay);
          } else {
            setPhase("manual");
          }
        });
    },
    [next, router]
  );

  useEffect(() => {
    runAttempt(1);
    // Auto-retry sequence should only ever start once per page load.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  function handleTryAgain() {
    setPhase("retrying");
    setAttempt(1);
    runAttempt(1);
  }

  function handleCancel() {
    attemptToken.current++; // invalidate any in-flight attempt/timer
    clearMeCache();
    auth.logout();
    router.replace("/login");
  }

  const totalAttempts = AUTO_RETRY_DELAYS_MS.length + 1;

  return (
    <div className="min-h-screen flex items-center justify-center bg-white px-4">
      <div className="w-full max-w-sm">
        <div className="bg-amber-50 border border-amber-200 rounded-xl px-8 py-8 shadow-sm text-center">
          <div className="w-10 h-10 border-4 border-amber-500 border-t-transparent rounded-full animate-spin mx-auto mb-5" />

          <h2 className="text-lg font-bold text-slate-800 mb-2">
            Reconnecting to the server…
          </h2>
          <p className="text-sm text-amber-800 mb-1">
            The PPDO Portal server may be waking up after a period of
            inactivity — this can take up to about a minute.
          </p>
          {phase === "retrying" && (
            <p className="text-xs text-amber-700 mb-6">
              Attempt {attempt} of {totalAttempts}…
            </p>
          )}
          {phase === "manual" && (
            <p className="text-xs text-amber-700 mb-6">
              Still unable to reach the server.
            </p>
          )}

          {phase === "manual" && (
            <div className="flex flex-col gap-2">
              <button
                type="button"
                onClick={handleTryAgain}
                className="w-full bg-green-600 text-white font-semibold py-2.5 rounded-lg text-sm
                           hover:bg-green-500 active:bg-green-700 transition-colors
                           focus:outline-none focus:ring-2 focus:ring-green-600 focus:ring-offset-2"
              >
                Try Again
              </button>
              <button
                type="button"
                onClick={handleCancel}
                className="w-full bg-white text-slate-800 font-medium py-2.5 rounded-lg text-sm
                           border border-slate-300 hover:bg-slate-50 transition-colors
                           focus:outline-none focus:ring-2 focus:ring-slate-400 focus:ring-offset-2"
              >
                Cancel — back to login
              </button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page export — wraps inner component in Suspense (required for useSearchParams)
// ---------------------------------------------------------------------------

function ReconnectingPageFallback() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-white">
      <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
    </div>
  );
}

export default function ReconnectingPage() {
  return (
    <Suspense fallback={<ReconnectingPageFallback />}>
      <ReconnectingPageInner />
    </Suspense>
  );
}
