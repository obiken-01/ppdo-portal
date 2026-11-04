import { beforeEach, describe, expect, it, vi } from "vitest";

// PPDO-277 — the default-year read must say when it failed, so a page can show an error and Retry
// instead of silently falling back. A failure must not be cached, so Retry asks again.

const getFiscalYears = vi.fn();
vi.mock("./budget-planning", () => ({ getFiscalYears: () => getFiscalYears() }));

async function freshModule() {
  vi.resetModules();
  return import("./default-fiscal-year");
}

describe("loadDefaultFiscalYear", () => {
  // Braces matter: a returned function would run as a cleanup hook, calling the mock after each test.
  beforeEach(() => { getFiscalYears.mockReset(); });

  it("loadDefaultFiscalYear_WhenTheCallSucceeds_ReturnsTheYearNotFailed", async () => {
    getFiscalYears.mockResolvedValue({ defaultFiscalYear: 2029 });
    const { loadDefaultFiscalYear } = await freshModule();
    expect(await loadDefaultFiscalYear()).toEqual({ value: 2029, failed: false });
  });

  it("loadDefaultFiscalYear_WhenTheSettingIsUnset_ReturnsNullNotFailed", async () => {
    getFiscalYears.mockResolvedValue({ defaultFiscalYear: null });
    const { loadDefaultFiscalYear } = await freshModule();
    expect(await loadDefaultFiscalYear()).toEqual({ value: null, failed: false });
  });

  it("loadDefaultFiscalYear_WhenTheCallFails_ReturnsFailedAndNeverRejects", async () => {
    getFiscalYears.mockRejectedValue(new Error("500"));
    const { loadDefaultFiscalYear } = await freshModule();
    expect(await loadDefaultFiscalYear()).toEqual({ value: null, failed: true });
  });

  it("loadDefaultFiscalYear_AfterAFailure_AsksAgainOnTheNextCall", async () => {
    getFiscalYears.mockRejectedValueOnce(new Error("500")).mockResolvedValueOnce({ defaultFiscalYear: 2031 });
    const { loadDefaultFiscalYear } = await freshModule();
    await loadDefaultFiscalYear();
    expect(await loadDefaultFiscalYear()).toEqual({ value: 2031, failed: false });
    expect(getFiscalYears).toHaveBeenCalledTimes(2);
  });

  it("loadDefaultFiscalYear_AfterASuccess_ReadsTheCacheWithoutAnotherCall", async () => {
    getFiscalYears.mockResolvedValue({ defaultFiscalYear: 2030 });
    const { loadDefaultFiscalYear } = await freshModule();
    await loadDefaultFiscalYear();
    expect(await loadDefaultFiscalYear()).toEqual({ value: 2030, failed: false });
    expect(getFiscalYears).toHaveBeenCalledTimes(1);
  });

  it("fetchDefaultFiscalYear_WhenTheCallFails_StillResolvesNull", async () => {
    getFiscalYears.mockRejectedValue(new Error("500"));
    const { fetchDefaultFiscalYear } = await freshModule();
    expect(await fetchDefaultFiscalYear()).toBeNull();
  });
});
