import { randomBytes } from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { expect, type APIRequestContext } from "@playwright/test";

// PPDO-259 — builds a self-contained "E2E" office for the AIP journeys, through the app's own
// API, the way an Admin would by hand. Same shape as PPDO-195's PTO test (docs on that ticket):
//   office E2E · its own LDIP (2 programs, General sector) · seeded into the open FY AIP ·
//   divisions A and B · GF ceiling + division split + program → division assignment ·
//   accounts: department head, encoder A, encoder B, and a PPDO cross-office reviewer.
//
// Every step is find-or-create, so running it again changes nothing. It never edits another
// office's data. Passwords are generated here and kept only in e2e/.auth/aip-accounts.json
// (git-ignored); an account whose saved password no longer works gets a fresh one.
//
// ⚠️ Runs as an Admin (E2E_SEED_EMAIL / E2E_SEED_PASSWORD). Point it at UAT only on purpose:
// it creates an office, an LDIP, divisions and four users there.

export const FISCAL_YEAR = 2028;
export const OFFICE = { code: "E2E", name: "E2E Test Office (automated)", refCode: "01-099" };
export const DIVISIONS = {
  A: { code: "E2EA", name: "E2E Division A (test)" },
  B: { code: "E2EB", name: "E2E Division B (test)" },
} as const;
export const PROGRAMS = ["E2E Program One (test)", "E2E Program Two (test)"] as const;
export const GF_CEILING = 1_500_000;
export const ALLOCATION = { A: 900_000, B: 500_000 } as const; // ₱100,000 left unallocated

export type AipRole = "head" | "encoderA" | "encoderB" | "reviewer";

export interface AipAccount {
  role: AipRole;
  username: string;
  fullName: string;
  password: string;
}

export interface AipWorld {
  apiUrl: string;
  aipId: number;
  officeId: number;
  officeRefCode: string;
  divisionIds: { A: number; B: number };
  gfId: number;
  programRefCodes: string[];
  /** Another guest office, for the scope-leak checks: its name and ref code must never reach E2E users. */
  foreignOffice: { id: number; name: string; refCode: string };
  accounts: Record<AipRole, AipAccount>;
}

const ACCOUNTS_FILE = "e2e/.auth/aip-accounts.json";
export const WORLD_FILE = "e2e/.auth/aip-world.json";

export const API_URL = (process.env.E2E_API_URL ?? "http://localhost:7071/api").replace(/\/$/, "");

// ── HTTP helpers ────────────────────────────────────────────────────────────

type Json = Record<string, unknown>;

const ENVELOPE_KEYS = new Set(["data", "error", "message", "success"]);

/** Unwraps the `ApiResponse` envelope ({ data, error?, message? }; nulls are omitted); plain bodies pass through. */
function unwrap<T>(json: unknown): T {
  if (json && typeof json === "object" && !Array.isArray(json) && "data" in json
      && Object.keys(json).every((k) => ENVELOPE_KEYS.has(k)))
    return (json as { data: T }).data;
  return json as T;
}

export class Api {
  constructor(private readonly request: APIRequestContext, private token: string) {}

  static async login(request: APIRequestContext, username: string, password: string): Promise<Api | null> {
    const res = await request.post(`${API_URL}/auth/login`, { data: { username, password } });
    if (!res.ok()) return null;
    const { accessToken } = (await res.json()) as { accessToken: string };
    return new Api(request, accessToken);
  }

  async call<T>(method: "get" | "post" | "put" | "delete", url: string, data?: unknown, params?: Record<string, string | number>): Promise<T> {
    const res = await this.request.fetch(`${API_URL}${url}`, {
      method: method.toUpperCase(),
      headers: { Authorization: `Bearer ${this.token}` },
      data,
      params,
    });
    if (!res.ok()) throw new Error(`${method.toUpperCase()} ${url} → ${res.status()}: ${(await res.text()).slice(0, 400)}`);
    const text = await res.text();
    return unwrap<T>(text ? JSON.parse(text) : null);
  }
}

// ── Seed ────────────────────────────────────────────────────────────────────

export async function seedAipWorld(request: APIRequestContext): Promise<AipWorld> {
  const email = process.env.E2E_SEED_EMAIL;
  const password = process.env.E2E_SEED_PASSWORD;
  if (!email || !password) throw new Error("Set E2E_SEED_EMAIL / E2E_SEED_PASSWORD (an Admin) to seed the AIP test office.");
  const admin = await Api.login(request, email, password);
  expect(admin, "seed admin login failed").not.toBeNull();
  const api = admin!;

  // Office
  const offices = await api.call<Json[]>("get", "/config/offices");
  let office = offices.find((o) => o.officeCode === OFFICE.code);
  if (!office) {
    office = await api.call<Json>("post", "/config/offices", {
      officeCode: OFFICE.code, officeName: OFFICE.name, officeRefCode: OFFICE.refCode, isActive: true, landingPage: null,
    });
  }
  const officeId = office.id as number;
  const officeRefCode = (office.officeRefCode as string) ?? OFFICE.refCode;

  // Divisions
  const divisions = await api.call<Json[]>("get", "/config/divisions");
  const divisionIds = { A: 0, B: 0 };
  for (const key of ["A", "B"] as const) {
    const spec = DIVISIONS[key];
    let d = divisions.find((x) => x.officeId === officeId && x.code === spec.code);
    if (!d) {
      d = await api.call<Json>("post", "/config/divisions", {
        officeId, code: spec.code, name: spec.name, isActive: true,
        canAccessBudgetPlanning: true, canAccessInventory: false, canAccessReports: false,
        canManageConfig: false, canUploadAip: false, canManageUsers: false, canManageResourceLinks: false,
        landingPage: null,
      });
    }
    divisionIds[key] = d.id as number;
  }

  // The office's own LDIP, covering the fiscal year
  const ldips = await api.call<Json[]>("get", "/budget-planning/ldip");
  let ldipSummary = ldips.find((l) => l.officeId === officeId
    && (l.fiscalYearStart as number) <= FISCAL_YEAR && (l.fiscalYearEnd as number) >= FISCAL_YEAR
    && l.status !== "Archived");
  if (!ldipSummary) {
    ldipSummary = await api.call<Json>("post", "/budget-planning/ldip", {
      title: "", fiscalYearStart: FISCAL_YEAR - 2, fiscalYearEnd: FISCAL_YEAR + 1, entryMode: "New", officeId,
      groups: [{
        sector: "General", name: OFFICE.name.toUpperCase(),
        programs: PROGRAMS.map((name) => ({ name, budget: 0, mooe: 0, co: 0 })),
      }],
    });
  }
  const ldip = await api.call<Json>("get", `/budget-planning/ldip/${ldipSummary.id}`);
  const ldipPrograms = ((ldip.offices ?? ldip.groups) as Json[]).flatMap((g) => g.programs as Json[]);
  const programRefCodes = ldipPrograms.map((p) => p.refCode as string);

  // The open AIP for the fiscal year, with this office's programs in it
  const records = await api.call<Json[]>("get", "/budget-planning/aip", undefined, { fiscalYear: FISCAL_YEAR });
  const record = records.find((r) => r.fiscalYear === FISCAL_YEAR && r.status === "Draft");
  if (!record) throw new Error(`FY${FISCAL_YEAR} is not open locally. Open it from Investment Planning first.`);
  const aipId = record.id as number;
  let detail = await api.call<Json>("get", `/budget-planning/aip/${aipId}`);
  const aipOffice = (detail.offices as Json[]).find((o) => o.officeId === officeId);
  const present = new Set(((aipOffice?.programs as Json[] | undefined) ?? []).map((p) => p.refCode as string));
  const missing = ldipPrograms.filter((p) => !present.has(p.refCode as string)).map((p) => p.id as number);
  if (missing.length > 0) {
    await api.call("post", "/budget-planning/aip/seed-programs-from-ldip", {
      targetFiscalYear: FISCAL_YEAR, officeConfigId: officeId, sector: "General", ldipProgramIds: missing,
    });
    detail = await api.call<Json>("get", `/budget-planning/aip/${aipId}`); // now holds the office row
  }
  const aipOfficeRef = aipOfficeRefCode(detail, officeId);
  if (!aipOfficeRef) throw new Error("The E2E office is not in the AIP after seeding its programs.");

  // GF ceiling (set by PPDO; the division split below is the department head's job)
  const funds = await api.call<Json[]>("get", "/config/funding-sources");
  const gf = funds.find((f) => f.code === "GF");
  if (!gf) throw new Error("No funding source with code GF.");
  const gfId = gf.id as number;
  await api.call("put", "/budget-planning/allocation/ceiling", { officeId, fiscalYear: FISCAL_YEAR, fundingSourceId: gfId, amount: GF_CEILING });
  // Accounts
  const ppdoDivision = (await api.call<Json[]>("get", "/config/divisions"))
    .find((d) => (d.officeId as number) === hostOfficeId(offices) && d.isActive);
  if (!ppdoDivision) throw new Error("No active PPDO division for the reviewer account.");

  const saved = readAccounts();
  const accounts = {} as Record<AipRole, AipAccount>;
  const specs: Array<{ role: AipRole; username: string; fullName: string; officeId: number | null; divisionId: number | null; overrides: Json }> = [
    { role: "head", username: "e2e.head", fullName: "E2E Department Head", officeId, divisionId: null,
      overrides: { overrideCanReviewBudgetPlanning: true, overrideCanManageOfficeSetup: true } },
    { role: "encoderA", username: "e2e.encoder.a", fullName: "E2E Encoder A", officeId, divisionId: divisionIds.A, overrides: {} },
    { role: "encoderB", username: "e2e.encoder.b", fullName: "E2E Encoder B", officeId, divisionId: divisionIds.B, overrides: {} },
    { role: "reviewer", username: "e2e.reviewer", fullName: "E2E PPDO Reviewer", officeId: null, divisionId: ppdoDivision.id as number,
      overrides: { overrideCanReviewAllOffices: true, overrideCanManageOfficeCeilings: true } },
  ];
  for (const spec of specs) {
    accounts[spec.role] = await ensureAccount(request, api, spec, saved[spec.role]);
  }
  writeAccounts(accounts);

  // Division split and program → division assignment (program 1 → A, program 2 → B), as the
  // department head: for a guest office the host-office Admin may not write these (PPDO-18).
  const headApi = await Api.login(request, accounts.head.username, accounts.head.password);
  expect(headApi, "department head login failed").not.toBeNull();
  // Written only when different: once the office's AIP has left the office (a journey can stop
  // there), the server refuses allocation changes, and they would be no-ops anyway.
  const wanted: Array<[number, number]> = [[divisionIds.A, ALLOCATION.A], [divisionIds.B, ALLOCATION.B]];
  const current = await headApi!.call<Json[]>("get", "/budget-planning/allocation/divisions", undefined,
    { officeId, fiscalYear: FISCAL_YEAR, fundingSourceId: gfId });
  const allocationOk = wanted.every(([id, amount]) =>
    current.some((c) => c.divisionId === id && Number(c.amount) === amount));
  if (!allocationOk) {
    await headApi!.call("put", "/budget-planning/allocation/divisions", {
      officeId, fiscalYear: FISCAL_YEAR, fundingSourceId: gfId,
      allocations: wanted.map(([divisionId, amount]) => ({ divisionId, amount })),
    });
  }
  const assigned = await headApi!.call<Json[]>("get", "/budget-planning/allocation/programs", undefined,
    { officeId, fiscalYear: FISCAL_YEAR });
  for (let i = 0; i < programRefCodes.length; i++) {
    const refCode = programRefCodes[i];
    const divisionId = i === 0 ? divisionIds.A : divisionIds.B;
    const row = assigned.find((a) => a.programRefCode === refCode);
    const ids = (row?.divisionIds as number[] | undefined) ?? [];
    if (ids.length === 1 && ids[0] === divisionId) continue;
    await headApi!.call("put", "/budget-planning/allocation/programs", {
      officeRefCode: aipOfficeRef, programRefCode: refCode, divisionIds: [divisionId],
    });
  }

  const foreign = offices.find((o) => o.id !== officeId && o.isHostOffice !== true && o.isActive === true && o.officeRefCode);
  if (!foreign) throw new Error("Need one other active guest office for the scope checks.");
  const foreignOffice = { id: foreign.id as number, name: foreign.officeName as string, refCode: foreign.officeRefCode as string };

  const world: AipWorld = { apiUrl: API_URL, aipId, officeId, officeRefCode, divisionIds, gfId, programRefCodes, foreignOffice, accounts };
  fs.mkdirSync(path.dirname(WORLD_FILE), { recursive: true });
  fs.writeFileSync(WORLD_FILE, JSON.stringify(world, null, 2));
  return world;
}

function hostOfficeId(offices: Json[]): number {
  const host = offices.find((o) => o.isHostOffice === true);
  if (!host) throw new Error("No host office (PPDO) found.");
  return host.id as number;
}

/** The AIP office row's own ref code (sector prefix + office ref code), which program assignment keys on. */
function aipOfficeRefCode(detail: Json, officeId: number): string | undefined {
  const o = (detail.offices as Json[]).find((x) => x.officeId === officeId);
  return o?.refCode as string | undefined;
}

// ── Accounts ────────────────────────────────────────────────────────────────

function newPassword(): string {
  // Policy (UserService.ChangePasswordAsync): 8+ characters, one uppercase letter, one digit.
  return `E2e-${randomBytes(9).toString("base64url")}9`;
}

function readAccounts(): Partial<Record<AipRole, AipAccount>> {
  try { return JSON.parse(fs.readFileSync(ACCOUNTS_FILE, "utf8")); } catch { return {}; }
}

function writeAccounts(accounts: Record<AipRole, AipAccount>) {
  fs.mkdirSync(path.dirname(ACCOUNTS_FILE), { recursive: true });
  fs.writeFileSync(ACCOUNTS_FILE, JSON.stringify(accounts, null, 2));
}

async function ensureAccount(
  request: APIRequestContext,
  admin: Api,
  spec: { role: AipRole; username: string; fullName: string; officeId: number | null; divisionId: number | null; overrides: Json },
  saved: AipAccount | undefined,
): Promise<AipAccount> {
  const users = await admin.call<Json[]>("get", "/users");
  let user = users.find((u) => (u.username as string)?.toLowerCase() === spec.username);
  let temporary: string | null = null;

  const base = {
    fullName: spec.fullName, username: spec.username, email: null, role: "Staff",
    divisionId: spec.divisionId, officeId: spec.officeId, position: "Automated test account", contactNo: null, landingPage: null,
  };
  if (!user) {
    const created = await admin.call<Json>("post", "/users", base);
    user = (created.user as Json) ?? created;
    temporary = created.temporaryPassword as string;
  }
  await admin.call("put", `/users/${user.id}`, {
    ...base, isActive: true,
    overrideCanAccessInventory: null, overrideCanAccessReports: null, overrideCanManageUsers: null,
    overrideCanManageResourceLinks: null, overrideCanAccessBudgetPlanning: null, overrideCanUploadAip: null,
    overrideCanManageConfig: null, overrideCanManagePpdoAllocation: null, overrideCanManageOfficeCeilings: null,
    overrideCanReviewBudgetPlanning: null, overrideCanReviewAllOffices: null, overrideCanManageApiKeys: null,
    overrideCanManageOfficeSetup: null,
    ...spec.overrides,
  });

  // Reuse the saved password when it still signs in and nothing is pending on the account.
  if (!temporary && saved && (await isReady(request, spec.username, saved.password)))
    return { role: spec.role, username: spec.username, fullName: spec.fullName, password: saved.password };

  if (!temporary) {
    const reset = await admin.call<Json>("put", `/users/${user.id}/reset-password`);
    temporary = reset.temporaryPassword as string;
  }

  // First sign-in: replace the one-time password and answer a recovery question, so the UI
  // never stops on the forced-change or recovery-setup screens.
  const password = newPassword();
  const self = await Api.login(request, spec.username, temporary);
  expect(self, `first login for ${spec.username} failed`).not.toBeNull();
  await self!.call("put", "/users/me/password", { currentPassword: temporary, newPassword: password, confirmPassword: password });
  const relogged = await Api.login(request, spec.username, password);
  const questions = await relogged!.call<Json[]>("get", "/auth/recovery-questions");
  await relogged!.call("put", "/users/me/recovery-answer", { questionKey: questions[0].key, answer: "e2e test answer" });
  try { await relogged!.call("post", "/users/me/acknowledge-password-reset"); } catch { /* nothing pending */ }

  return { role: spec.role, username: spec.username, fullName: spec.fullName, password };
}

async function isReady(request: APIRequestContext, username: string, password: string): Promise<boolean> {
  const api = await Api.login(request, username, password);
  if (!api) return false;
  const me = await api.call<Json>("get", "/auth/me");
  return !me.mustChangePassword && !me.needsRecoverySetup;
}

export function readWorld(): AipWorld {
  return JSON.parse(fs.readFileSync(WORLD_FILE, "utf8")) as AipWorld;
}
