import type { PersonaKey } from "./personas";

// PPDO-258 — every portal route the smoke spec loads, and who should get in.
//
// Personas are the definitions in docs/v1.9/UAT_Polish_Plan.md §3.1, not particular accounts:
//   SA SuperAdmin · AD Admin with NO per-user grants · RV PPDO Staff + reviewer grants
//   ST PPDO Staff, plain · EN guest-office encoder (no CanReviewBudgetPlanning) · XD Staff, other division
// Point an E2E_<KEY>_* account at something else and the allow/deny lines stop meaning anything.
//
// access: "allow" → must render (stays on the path, marker visible).
//         "deny"  → must be redirected away (every guard in the portal redirects; none renders a refusal).
//         missing → depends on the account's division flags or grants; only "no crash" is checked.
// Only rules that hold for the persona's definition are written down. Sources: the page guards,
// lib/budget-planning-access.ts and docs/v1.8/Permission_Matrix.md.
//
// Routes that need an ID (aip/detail, ldip/edit, proposals/edit, the import previews,
// pr-report/[prNo]) are left out: a smoke run has no record to point them at.

export type Access = "allow" | "deny";

export interface PortalRoute {
  path: string;
  /** What proves the page rendered: its title heading, or a stable control where it has none. */
  marker: { role: "heading" | "button"; name: string | RegExp; level?: number };
  access: Partial<Record<PersonaKey, Access>>;
  /**
   * PPDO-272 — the page's main data GET, matched against the request's path (no query string).
   * The sweep blocks it (error state), holds it back (loading state) and expects the page to react.
   * Found by watching what each page requests on load, not guessed; the shell's own calls
   * (auth, notifications) never count. Omitted: the page makes no data call on load (a form, or
   * a page that loads only after the user picks something), so those two checks do not apply.
   */
  mainApi?: RegExp;
  /**
   * PPDO-272 — the page's primary action, when it has one visible while loading. The loading check
   * expects it to be disabled until the data arrives (plan §3.2 item 2). Omitted: no such control.
   */
  primary?: { name: string | RegExp };
}

export interface RedirectRoute {
  path: string;
  /** Where it should land. Omitted: anywhere signed in (e.g. the landing-page preference). */
  to?: RegExp;
}

const ALL: PersonaKey[] = ["SA", "AD", "RV", "ST", "EN", "XD"];

function rule(allow: PersonaKey[], deny: PersonaKey[] = []): Partial<Record<PersonaKey, Access>> {
  const access: Partial<Record<PersonaKey, Access>> = {};
  for (const p of allow) access[p] = "allow";
  for (const p of deny) access[p] = "deny";
  return access;
}

const h1 = (name: string | RegExp) => ({ role: "heading" as const, name, level: 1 });

export const PORTAL_ROUTES: PortalRoute[] = [
  // ── Shell ─────────────────────────────────────────────────────────────────
  { path: "/account",        marker: h1("My Account"),      access: rule(ALL) },
  // PPDO-262: EN is bounced off this page today (the office-user gate in the portal layout).
  { path: "/notifications",  marker: h1("Notifications"),   access: rule(ALL), mainApi: /\/notifications$/ },
  // Host-office pages: a guest-office user is sent to /budget-planning.
  { path: "/dashboard",      marker: h1("Main Dashboard"),  access: rule(["SA", "AD", "RV", "ST", "XD"], ["EN"]), mainApi: /\/dashboard\/events$/ },
  { path: "/resource-links", marker: h1("Resource Links"),  access: rule(["SA", "AD", "RV", "ST", "XD"], ["EN"]), mainApi: /\/resource-links$/ },
  // Role check, Admin or SuperAdmin.
  { path: "/announcements",  marker: h1(/Announcements/),   access: rule(["SA", "AD"], ["RV", "ST", "XD", "EN"]), mainApi: /\/announcements\/manage$/, primary: { name: /New Announcement/ } },

  // ── Admin ─────────────────────────────────────────────────────────────────
  // No title heading on this page (PPDO-256 finding); "+ Add User" is its first control.
  // canManageUsers: Admin by default, Staff only through a division flag.
  { path: "/admin/users", marker: { role: "button", name: /Add User$/ }, access: rule(["SA", "AD"], ["EN"]), mainApi: /\/users$/, primary: { name: /Add User$/ } },

  // ── Budget Planning ───────────────────────────────────────────────────────
  { path: "/budget-planning",            marker: h1("Investment Planning"),      access: rule(["SA", "AD", "RV", "EN"]), mainApi: /\/budget-planning\/dashboard(\/offices?)?$/ },
  // canOpenAipRecords: Admin role only.
  { path: "/budget-planning/aip",        marker: h1("Annual Investment Program"), access: rule(["SA", "AD"], ["RV", "ST", "XD", "EN"]), mainApi: /\/budget-planning\/aip$/ },
  { path: "/budget-planning/aip/new",    marker: h1("Create New AIP"),           access: rule(["SA", "AD"], ["RV", "ST", "XD", "EN"]) },
  { path: "/budget-planning/aip/entry",  marker: h1("AIP Entry"),                access: rule(["SA", "AD", "RV", "EN"]), mainApi: /\/budget-planning\/aip\/\d+$/ },
  // canOpenAipReview: a reviewer grant, never given to Admin by default.
  { path: "/budget-planning/aip/review/search", marker: h1("AIP Review"),        access: rule(["SA", "RV"], ["AD", "ST", "XD", "EN"]), mainApi: /\/budget-planning\/fiscal-years$/ },
  // The title follows the user (budget-planning-labels.ts): "Allocation", "Division Allocation" or "Budget Ceilings".
  { path: "/budget-planning/allocation", marker: h1(/^(Division )?Allocation$|^Budget Ceilings$/), access: rule(["SA"], ["EN"]), mainApi: /\/budget-planning\/allocation\/ceilings$/ },
  // canOpenLdip: host office.
  { path: "/budget-planning/ldip",       marker: h1("Local Development Investment Program"), access: rule(["SA", "AD"], ["EN"]), mainApi: /\/budget-planning\/ldip$/ },
  // No h1 on this page (PPDO-256 finding). Every user gets the Manual Entry tab; Upload is PPDO-only.
  { path: "/budget-planning/ldip/new",   marker: { role: "button", name: "Manual Entry" }, access: rule(["SA", "AD"], ["EN"]) },
  { path: "/budget-planning/office-ceilings", marker: h1("Office Ceilings"),     access: rule(["SA"], ["EN"]), mainApi: /\/budget-planning\/allocation\/ceilings$/ },
  { path: "/budget-planning/proposals",  marker: h1("Investment Proposals"),     access: rule(["SA", "AD", "RV", "EN"]), mainApi: /\/budget-planning\/proposals$/ },
  // PPDO-240: hidden from guest-office encoders.
  { path: "/budget-planning/report",     marker: h1("Report"),                   access: rule(["SA", "AD", "RV"], ["EN"]), mainApi: /\/budget-planning\/wfp\/report\/offices$/ },
  { path: "/budget-planning/wfp",        marker: h1(/^Work and Financial Plan/), access: rule(["SA", "AD", "RV"], ["EN"]), mainApi: /\/budget-planning\/wfp$/ },
  { path: "/budget-planning/wfp/entry",  marker: h1("WFP Entry Wizard"),         access: rule(["SA", "AD", "RV"], ["EN"]), mainApi: /\/budget-planning\/aip$/ },

  // ── Configuration (canManageConfig: Admin by default) ─────────────────────
  { path: "/config",                       marker: h1("Configuration"),                access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/accounts$/ },
  { path: "/config/accounts",              marker: h1("Chart of Accounts"),            access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/accounts$/, primary: { name: /^\+\s?Add / } },
  { path: "/config/cc-typologies",         marker: h1("Climate Change Typologies"),    access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/cc-typologies$/, primary: { name: /^\+\s?Add / } },
  { path: "/config/divisions",             marker: h1("Divisions"),                    access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/divisions$/, primary: { name: /^\+\s?Add / } },
  { path: "/config/esre-codes",            marker: h1("eSRE Codes"),                   access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/esre-codes$/, primary: { name: /^\+\s?Add / } },
  { path: "/config/funding-sources",       marker: h1("Funding Sources"),              access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/funding-sources$/, primary: { name: /^\+\s?Add / } },
  { path: "/config/investment-planning",   marker: h1("Investment Planning Settings"), access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/investment-planning\/signatory-defaults$/, primary: { name: "Save" } },
  { path: "/config/offices",               marker: h1("Offices"),                      access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/offices$/, primary: { name: /^\+\s?Add / } },
  { path: "/config/price-index",           marker: h1("Price Index"),                  access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/price-index\/paged$/, primary: { name: /^\+\s?Add / } },
  { path: "/config/procurement-presets",   marker: h1("Procurement Presets"),          access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/procurement-presets$/, primary: { name: /^\+\s?Add / } },
  // Per-user grants (CanManageApiKeys, CanManageOfficeSetup) that Admin is NOT given by default.
  { path: "/config/api-access",            marker: h1("API Access"),                   access: rule(["SA"], ["AD", "EN"]), mainApi: /\/config\/api-keys$/ },
  { path: "/config/office-users",          marker: h1("Staff Divisions"),              access: rule(["SA"], ["AD", "EN"]), mainApi: /\/office\/users$/ },
  { path: "/config/audit-log",             marker: h1("Audit Log"),                    access: rule(["SA"], ["AD", "RV", "ST", "XD", "EN"]), mainApi: /\/config\/audit-log$/ },

  // ── Inventory (canAccessInventory: Admin by default, Staff by division) ────
  { path: "/inventory",                  marker: h1(/Inventory Dashboard/), access: rule(["SA", "AD"], ["EN"]), mainApi: /\/inventory\/stats$/ },
  { path: "/inventory/create-pr",        marker: h1("Create PR"),           access: rule(["SA", "AD"], ["EN"]), mainApi: /\/config\/divisions$/, primary: { name: /Submit PR$/ } },
  { path: "/inventory/distribution",     marker: h1("Distribution"),        access: rule(["SA", "AD"], ["EN"]) },
  { path: "/inventory/item-ledger",      marker: h1("Stock Overview"),      access: rule(["SA", "AD"], ["EN"]), mainApi: /\/inventory\/ledger$/ },
  { path: "/inventory/items-master",     marker: h1("Items Master"),        access: rule(["SA", "AD"], ["EN"]), mainApi: /\/items\/master\/search$/, primary: { name: /Add Item/ } },
  { path: "/inventory/pr-register",      marker: h1("PR List"),             access: rule(["SA", "AD"], ["EN"]), mainApi: /\/purchase-requests\/search$/ },
  { path: "/inventory/pr-report",        marker: h1("Inventory Report"),    access: rule(["SA", "AD"], ["EN"]), mainApi: /\/purchase-requests$/ },
  { path: "/inventory/receive-delivery", marker: h1("Receive Delivery"),    access: rule(["SA", "AD"], ["EN"]), mainApi: /\/purchase-requests$/ },
  // No h1 on this page (PPDO-256 finding).
  { path: "/inventory/stock-balances",   marker: { role: "heading", name: "Record a physical count", level: 2 }, access: rule(["SA", "AD"], ["EN"]) },
];

export const REDIRECT_ROUTES: RedirectRoute[] = [
  { path: "/profile", to: /^\/account$/ },
  { path: "/home" },
  // The consolidated view moved onto the Report page.
  { path: "/budget-planning/aip/consolidated", to: /^\/budget-planning(\/report)?$/ },
  // Without ?id= the review screen sends a reviewer to the search, anyone else further on.
  { path: "/budget-planning/aip/review" },
];
