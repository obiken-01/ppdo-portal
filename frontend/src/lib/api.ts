/**
 * PPDO Portal — Axios instance with JWT interceptors.
 *
 * All API calls MUST go through this module — never use fetch() directly.
 *
 * Request interceptor:
 *   Attaches Authorization: Bearer <accessToken> to every outgoing request
 *   when an access token is held in memory.
 *
 * Response interceptor (silent refresh on 401):
 *   1. Intercepts 401 responses (expired / missing access token).
 *   2. Retrieves the refresh token from localStorage.
 *   3. POSTs to /auth/refresh using a bare axios call (not this instance,
 *      to prevent an infinite interceptor loop).
 *   4. On success → stores the new token pair and retries the original request.
 *   5. On failure → clears all tokens and redirects the user to /login.
 *
 * Concurrent requests that 401 while a refresh is in-flight are queued and
 * replayed once the new access token is available.
 */

import axios, { type AxiosError, type InternalAxiosRequestConfig } from "axios";
import { auth } from "./auth";

// ---------------------------------------------------------------------------
// Base URL — set NEXT_PUBLIC_API_BASE_URL in .env.local
// ---------------------------------------------------------------------------

const BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api";

// ---------------------------------------------------------------------------
// Axios instance
// ---------------------------------------------------------------------------

const api = axios.create({
  baseURL: BASE_URL,
  headers: { "Content-Type": "application/json" },
  withCredentials: true, // forward cookies — ready for httpOnly refresh-token cookie
});

// ---------------------------------------------------------------------------
// Request interceptor — attach Bearer token
// ---------------------------------------------------------------------------

api.interceptors.request.use((config) => {
  const token = auth.getToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

// ---------------------------------------------------------------------------
// Retry helper — handles Azure Functions cold starts (Consumption plan)
// ---------------------------------------------------------------------------

// In production, retry auth/refresh to handle Azure Functions cold starts
// (Consumption plan scales to zero; first request after inactivity can take 20–30s).
// In development, fail immediately so a restarted local server redirects to login
// without making the user wait through retry delays.
const COLD_START_RETRIES = process.env.NODE_ENV === "production" ? 2 : 0;

async function callWithRetry<T>(
  fn: () => Promise<T>,
  retries = COLD_START_RETRIES,
  delayMs = 3000
): Promise<T> {
  try {
    return await fn();
  } catch (err) {
    if (retries <= 0) throw err;
    // Only retry on network errors (no response), not on 4xx/5xx from the server
    if (axios.isAxiosError(err) && !err.response) {
      await new Promise((r) => setTimeout(r, delayMs));
      return callWithRetry(fn, retries - 1, delayMs);
    }
    throw err;
  }
}

// ---------------------------------------------------------------------------
// Response interceptor — silent refresh on 401
// ---------------------------------------------------------------------------

type QueueEntry = {
  resolve: (token: string) => void;
  reject: (error: unknown) => void;
};

let isRefreshing = false;
let failedQueue: QueueEntry[] = [];

function processQueue(error: unknown, token: string | null): void {
  failedQueue.forEach(({ resolve, reject }) => {
    if (error || token === null) reject(error);
    else resolve(token);
  });
  failedQueue = [];
}

interface RetryConfig extends InternalAxiosRequestConfig {
  _retry?: boolean;
}

api.interceptors.response.use(
  (response) => response,

  async (error: AxiosError) => {
    const original = error.config as RetryConfig | undefined;

    // Only attempt refresh on 401 and only once per request
    if (!original || error.response?.status !== 401 || original._retry) {
      return Promise.reject(error);
    }

    // Queue concurrent 401s while refresh is in-flight
    if (isRefreshing) {
      return new Promise<string>((resolve, reject) => {
        failedQueue.push({ resolve, reject });
      })
        .then((token) => {
          original.headers.Authorization = `Bearer ${token}`;
          return api(original);
        })
        .catch(Promise.reject.bind(Promise));
    }

    original._retry = true;
    isRefreshing = true;

    const refreshToken = auth.getRefreshToken();

    if (!refreshToken) {
      // No refresh token stored — send the user to login immediately
      isRefreshing = false;
      processQueue(new Error("No refresh token."), null);
      auth.logout();
      if (typeof window !== "undefined") window.location.href = "/login";
      return Promise.reject(error);
    }

    try {
      // Use a plain axios call (not `api`) to avoid triggering this interceptor again.
      // Retry up to 2 times with a 3-second delay to handle Azure Functions cold starts
      // (Consumption plan scales to zero after inactivity; first request can take 20-30s).
      const { data } = await callWithRetry(() =>
        axios.post(`${BASE_URL}/auth/refresh`, { refreshToken }, { withCredentials: true })
      );
      auth.login(data);
      processQueue(null, data.accessToken);
      original.headers.Authorization = `Bearer ${data.accessToken}`;
      return api(original);
    } catch (refreshError) {
      processQueue(refreshError, null);
      auth.logout();
      if (typeof window !== "undefined") window.location.href = "/login";
      return Promise.reject(refreshError);
    } finally {
      isRefreshing = false;
    }
  }
);

export default api;
