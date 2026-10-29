import fs from "node:fs";
import path from "node:path";
import { expect, type APIRequestContext } from "@playwright/test";
import { API_URL, Api, ensureAccount, hostOfficeId, stateFile, type AipAccount, type Json } from "../aip/seed";

// PPDO-259 — the inventory journey's own data, built through the app's API like the AIP seed:
//   a PPDO (host office) division "E2E Inventory (test)" with inventory access,
//   a Staff account in it, and one Items Master item to order.
// Inventory reads and writes are division-scoped, so the test's PRs and deliveries stay in this
// division and never show in a real division's lists. Find-or-create: a second run changes nothing.
// Runs as the same seed Admin (E2E_SEED_EMAIL / E2E_SEED_PASSWORD).

export const DIVISION = { code: "E2EINV", name: "E2E Inventory (test)" };
export const ITEM = { stockNo: "E2E-0001", description: "E2E test item (automated)", unit: "pc", unitCost: 100 };

export interface InventoryWorld {
  divisionId: number;
  divisionName: string;
  account: AipAccount;
}

// Per environment, like the AIP state (see stateFile in ../aip/seed.ts).
const ACCOUNT_FILE = stateFile("inventory-account.json");
export const INVENTORY_WORLD_FILE = stateFile("inventory-world.json");

export async function seedInventoryWorld(request: APIRequestContext): Promise<InventoryWorld> {
  const email = process.env.E2E_SEED_EMAIL;
  const password = process.env.E2E_SEED_PASSWORD;
  if (!email || !password) throw new Error("Set E2E_SEED_EMAIL / E2E_SEED_PASSWORD (an Admin) to seed the inventory test division.");
  const admin = await Api.login(request, email, password);
  expect(admin, "seed admin login failed").not.toBeNull();
  const api = admin!;

  // A host-office division with inventory access (and nothing else).
  const officeId = hostOfficeId(await api.call<Json[]>("get", "/config/offices"));
  let division = (await api.call<Json[]>("get", "/config/divisions"))
    .find((d) => d.officeId === officeId && d.code === DIVISION.code);
  if (!division) {
    division = await api.call<Json>("post", "/config/divisions", {
      officeId, code: DIVISION.code, name: DIVISION.name, isActive: true,
      canAccessBudgetPlanning: false, canAccessInventory: true, canAccessReports: false,
      canManageConfig: false, canUploadAip: false, canManageUsers: false, canManageResourceLinks: false,
      landingPage: null,
    });
  }

  // The item the PR orders. Created as a reviewed item (not "new"), so it doesn't sit in the
  // Items Master review queue.
  const found = await api.call<Json>("get", "/items/master/search", undefined, { stockNo: ITEM.stockNo, page: 1, pageSize: 5 });
  if (!(found.items as Json[]).some((i) => i.stockNo === ITEM.stockNo)) {
    await api.call("post", "/items/master", {
      ...ITEM, category: null, itemType: null, reorderQty: 0, remarks: "Automated test item (PPDO-259)", isNewItem: false,
    });
  }

  let saved: AipAccount | undefined;
  try { saved = JSON.parse(fs.readFileSync(ACCOUNT_FILE, "utf8")); } catch { saved = undefined; }
  const account = await ensureAccount(request, api, {
    role: "inventory", username: "e2e.inventory", fullName: "E2E Inventory Staff",
    officeId: null, divisionId: division.id as number, overrides: {},
  }, saved);
  fs.mkdirSync(path.dirname(ACCOUNT_FILE), { recursive: true });
  fs.writeFileSync(ACCOUNT_FILE, JSON.stringify(account, null, 2));

  const world: InventoryWorld = { divisionId: division.id as number, divisionName: division.name as string, account };
  fs.writeFileSync(INVENTORY_WORLD_FILE, JSON.stringify(world, null, 2));
  return world;
}

export function readInventoryWorld(): InventoryWorld {
  return JSON.parse(fs.readFileSync(INVENTORY_WORLD_FILE, "utf8")) as InventoryWorld;
}

export { API_URL };
