/**
 * PPDO Portal — Axios instance
 *
 * All API calls MUST go through this module. Never use fetch() directly.
 * The interceptor handles:
 *  - Attaching the Bearer token from memory on every request
 *  - Automatic silent refresh on 401 (calls POST /api/auth/refresh via cookie)
 *  - Redirect to /login if refresh fails
 *
 * TODO RAL-xx: Implement auth interceptors once AuthService + token store are ready.
 */
import axios from "axios";

const api = axios.create({
  baseURL: process.env.NEXT_PUBLIC_API_BASE_URL ?? "/api",
  headers: {
    "Content-Type": "application/json",
  },
  withCredentials: true, // sends httpOnly refresh-token cookie
});

// TODO RAL-xx: Request interceptor — attach access token from memory
// api.interceptors.request.use(...)

// TODO RAL-xx: Response interceptor — silent refresh on 401
// api.interceptors.response.use(...)

export default api;
