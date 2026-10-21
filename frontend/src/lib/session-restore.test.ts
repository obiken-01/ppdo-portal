import { afterEach, beforeEach, describe, expect, it } from "vitest";
import type { LoginResponse } from "@/types/auth";
import { auth } from "./auth";
import {
  SESSION_FLAG_KEY,
  clearSessionFlag,
  hasSessionFlag,
  setSessionFlag,
  shouldTryRestore,
} from "./session-restore";

// The suite runs in node (no DOM) — stand in a minimal localStorage per test.
function memoryStorage(): Storage {
  const store = new Map<string, string>();
  return {
    get length() { return store.size; },
    clear: () => store.clear(),
    getItem: (k: string) => store.get(k) ?? null,
    key: (i: number) => Array.from(store.keys())[i] ?? null,
    removeItem: (k: string) => { store.delete(k); },
    setItem: (k: string, v: string) => { store.set(k, v); },
  };
}

// A private window that refuses storage: every call throws, like Safari/old Edge InPrivate.
function throwingStorage(): Storage {
  const fail = () => { throw new Error("SecurityError"); };
  return { length: 0, clear: fail, getItem: fail, key: fail, removeItem: fail, setItem: fail };
}

function useStorage(s: Storage | undefined) {
  Object.defineProperty(globalThis, "localStorage", { value: s, configurable: true, writable: true });
}

const LOGIN: LoginResponse = { accessToken: "a.b.c" } as LoginResponse;

describe("session flag (PPDO-238)", () => {
  beforeEach(() => useStorage(memoryStorage()));
  afterEach(() => useStorage(undefined));

  it("is absent on a browser that never signed in", () => {
    expect(hasSessionFlag()).toBe(false);
  });

  it("reads back after set, and is gone after clear", () => {
    setSessionFlag();
    expect(hasSessionFlag()).toBe(true);
    expect(localStorage.getItem(SESSION_FLAG_KEY)).not.toBeNull();

    clearSessionFlag();
    expect(hasSessionFlag()).toBe(false);
  });

  it("is set by auth.login and cleared by auth.logout — the one place each", () => {
    auth.login(LOGIN);
    expect(hasSessionFlag()).toBe(true);

    auth.logout();
    expect(hasSessionFlag()).toBe(false);
  });

  it("never stores the token itself", () => {
    auth.login(LOGIN);
    expect(localStorage.getItem(SESSION_FLAG_KEY)).not.toContain(LOGIN.accessToken);
    auth.logout();
  });

  it("reads a throwing storage as no flag, and set/clear do not throw", () => {
    useStorage(throwingStorage());
    expect(hasSessionFlag()).toBe(false);
    expect(() => setSessionFlag()).not.toThrow();
    expect(() => clearSessionFlag()).not.toThrow();
    expect(() => auth.login(LOGIN)).not.toThrow();
    expect(auth.getToken()).toBe(LOGIN.accessToken);
    expect(() => auth.logout()).not.toThrow();
  });

  it("reads missing storage (server render) as no flag", () => {
    useStorage(undefined);
    expect(hasSessionFlag()).toBe(false);
    expect(() => setSessionFlag()).not.toThrow();
  });
});

describe("shouldTryRestore (PPDO-238)", () => {
  it("is false when this browser never signed in", () => {
    expect(shouldTryRestore({ hasFlag: false, reasonParam: null })).toBe(false);
  });

  it("is true when the flag is set and no reason is shown", () => {
    expect(shouldTryRestore({ hasFlag: true, reasonParam: null })).toBe(true);
  });

  it("is false when /login carries a ?reason= banner", () => {
    expect(shouldTryRestore({ hasFlag: true, reasonParam: "token_superseded" })).toBe(false);
    expect(shouldTryRestore({ hasFlag: true, reasonParam: "token_expired" })).toBe(false);
  });

  it("treats an unknown or empty reason as present too (never fight a banner)", () => {
    expect(shouldTryRestore({ hasFlag: true, reasonParam: "something_else" })).toBe(false);
    expect(shouldTryRestore({ hasFlag: true, reasonParam: "" })).toBe(false);
  });
});
