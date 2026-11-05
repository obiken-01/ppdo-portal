import { AxiosError, AxiosHeaders, type AxiosResponse } from "axios";
import { describe, expect, it } from "vitest";
import { apiErrorText } from "./api-error";

const FALLBACK = "Failed to save. Please try again.";

/** A rejected request as Axios hands it to a catch block, answered with `status` and `data`. */
function axiosError(status: number, data: unknown): AxiosError {
  const config = { headers: new AxiosHeaders() };
  const response = { status, statusText: "", headers: {}, config, data } as AxiosResponse;
  return new AxiosError("Request failed", "ERR_BAD_RESPONSE", config, {}, response);
}

describe("apiErrorText (PPDO-295…298)", () => {
  it("reads a plain-text body (Inventory, Users and the other older endpoints)", () => {
    expect(apiErrorText(axiosError(409, "Stock No. 'A-1' already exists."), FALLBACK))
      .toBe("Stock No. 'A-1' already exists.");
  });

  it("reads `{ error }` (Budget Planning, Config, and every 500 from the exception handler)", () => {
    const body = { success: false, data: null, error: "Something went wrong. Reference: abc123", message: null };
    expect(apiErrorText(axiosError(500, body), FALLBACK)).toBe("Something went wrong. Reference: abc123");
  });

  it("reads `{ message }` when there is no `error`", () => {
    expect(apiErrorText(axiosError(400, { message: "Description is required." }), FALLBACK))
      .toBe("Description is required.");
  });

  it("prefers `error` over `message` when both are set", () => {
    expect(apiErrorText(axiosError(400, { error: "the error", message: "the message" }), FALLBACK))
      .toBe("the error");
  });

  it("falls back on a null body", () => {
    expect(apiErrorText(axiosError(500, null), FALLBACK)).toBe(FALLBACK);
  });

  it("falls back on a blank string body", () => {
    expect(apiErrorText(axiosError(500, "   "), FALLBACK)).toBe(FALLBACK);
  });

  it("falls back on an object with neither field, never showing [object Object]", () => {
    expect(apiErrorText(axiosError(500, { title: "Internal Server Error" }), FALLBACK)).toBe(FALLBACK);
  });

  it("falls back when `error` is not a string", () => {
    expect(apiErrorText(axiosError(500, { error: { code: 1 } }), FALLBACK)).toBe(FALLBACK);
  });

  it("falls back on a network error (no response at all)", () => {
    const err = new AxiosError("Network Error", "ERR_NETWORK", { headers: new AxiosHeaders() });
    expect(apiErrorText(err, FALLBACK)).toBe(FALLBACK);
  });

  it("falls back on a non-Axios throw", () => {
    expect(apiErrorText(new Error("boom"), FALLBACK)).toBe(FALLBACK);
    expect(apiErrorText(undefined, FALLBACK)).toBe(FALLBACK);
  });
});
