import { AxiosError, AxiosHeaders } from "axios";
import { describe, expect, it } from "vitest";
import { ME_RECONNECT_COOLDOWN_MS, meFailureRedirect } from "./auth-redirect";

// PPDO-260 — a page whose fetchMe() fails must only send the user to /login when the
// server actually answered. No answer at all (cold start, restart, timeout) goes to
// /reconnecting, the same split the portal layout makes for a failed refresh (RAL-199).

function networkError(): AxiosError {
  return new AxiosError("Network Error", "ERR_NETWORK");
}

function httpError(status: number): AxiosError {
  const headers = new AxiosHeaders();
  return new AxiosError("Request failed", "ERR_BAD_RESPONSE", undefined, undefined, {
    status,
    statusText: "",
    headers,
    config: { headers },
    data: {},
  });
}

describe("meFailureRedirect", () => {
  it("sends a network error to /reconnecting with the current page as next", () => {
    expect(meFailureRedirect(networkError(), "/config/offices")).toBe(
      "/reconnecting?next=%2Fconfig%2Foffices",
    );
  });

  it("keeps the query string in next", () => {
    expect(meFailureRedirect(networkError(), "/inventory/pr-report?prNo=123")).toBe(
      "/reconnecting?next=%2Finventory%2Fpr-report%3FprNo%3D123",
    );
  });

  it("sends a timeout (no response) to /reconnecting", () => {
    const timeout = new AxiosError("timeout of 20000ms exceeded", "ECONNABORTED");
    expect(meFailureRedirect(timeout, "/inventory")).toBe("/reconnecting?next=%2Finventory");
  });

  it.each([401, 403, 500])("sends an HTTP %i to /login", (status) => {
    expect(meFailureRedirect(httpError(status), "/inventory")).toBe("/login");
  });

  it("sends a non-axios error to /login", () => {
    expect(meFailureRedirect(new Error("boom"), "/inventory")).toBe("/login");
  });

  // If /auth/refresh works but /auth/me keeps failing (e.g. a CORS misconfiguration on GET),
  // /reconnecting succeeds and sends the user straight back, and the page fails again. Without
  // a limit that loops hundreds of times a second (seen live in the PPDO-260 repro).
  it("falls back to /login on a second network failure inside the cooldown", () => {
    const now = 1_000_000;
    const last = now - (ME_RECONNECT_COOLDOWN_MS - 1);
    expect(meFailureRedirect(networkError(), "/config/offices", last, now)).toBe("/login");
  });

  it("goes to /reconnecting again once the cooldown has passed", () => {
    const now = 1_000_000;
    const last = now - ME_RECONNECT_COOLDOWN_MS;
    expect(meFailureRedirect(networkError(), "/config/offices", last, now)).toBe(
      "/reconnecting?next=%2Fconfig%2Foffices",
    );
  });
});
