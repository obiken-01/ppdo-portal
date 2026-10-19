---
id: PPDO-199
title: 'Security hardening: lower-risk findings from the 2026-10-08 review'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.4 — Minor fixes
created: 2026-10-08
completed: 2026-10-14
parent: (none)
blocked_by: (none)
related: PPDO-207
url: https://linear.app/ralphoksiprojects/issue/PPDO-199
uuid: ebe35ccf-4c4d-448f-af21-c09669ee66ff
---

# PPDO-199 — Security hardening: lower-risk findings from the 2026-10-08 review

From a 25-minute code-level security review (2026-10-08). The high-severity WFP issue is filed separately. Keep these off public GitHub.

## Findings

1. **Destructive dev-only endpoints are deployed to every environment (Medium, config-dependent).**
   * Affected: `DELETE inventory/cleanup`, `budget-planning/cleanup` and `budget-planning/wfp/cleanup` (`InventoryCleanupFunctions.cs`, `BudgetPlanningCleanupFunctions.cs`).
   * They wipe whole modules. The only guard is the `DevCleanupKey` app setting plus a header, and the key comparison is not constant-time.
   * They fail safe when the setting is unset. **Check that** `DevCleanupKey` **is not set on the UAT or production Function Apps.** The local value lives in the git-ignored `local.settings.json`, and the string is not in git history (checked).
   * Better fix: don't register these functions outside local dev, e.g. by excluding them from Release builds.
2. **No upload size cap in the app (Medium/Low).**
   * No explicit size limit was found on the Excel/PDF import endpoints (LDIP/AIP/PR/stock imports), so only the Functions host's default request limit applies.
   * Add a per-endpoint cap (a few MB), and test a zip-bomb `.xlsx` against the ClosedXML parsing.
3. **CSV formula injection in config exports (Low).**
   * `Csv.Escape` (`PPDO.Application/Common/Csv.cs:117`) quotes commas and quotes, but doesn't neutralise cells starting with `= + - @`.
   * A config manager could plant `=HYPERLINK(...)` that runs when someone opens the export in Excel.
   * Fix: prefix such cells with `'`.
   * The `.xlsx` exports go through ClosedXML values and are not affected unless they write formulas.
4. **No Content-Security-Policy (Low).** `staticwebapp.config.json` sets nosniff, frame DENY and a referrer policy, but no CSP and no HSTS header. Adding a CSP needs care with Next.js inline scripts.

## Checked and fine

* **Partner API keys:** hashed, constant-time compare (`FixedTimeHashEquals`), rate limiter, office scope check (`PartnerApiScope`).
* **Refresh cookie:** HttpOnly, Secure, SameSite=None (needed cross-site), path-scoped to `/api/auth/refresh`.
* **Function levels:** all 260 functions are Anonymous plus a manual JWT check. The 12 without a JWT check are the intended public ones (login/refresh/recovery, health, public announcements, CORS preflight, the external API with its own key auth) plus the 3 cleanup endpoints above.

## Not covered in 25 minutes

* Per-endpoint office/division scoping beyond WFP. An automated check flagged no other ID endpoint, but it only spots handlers that never pass the caller.
* Dependency audit (`npm audit`, `dotnet list package --vulnerable`), a gitleaks history scan, OWASP ZAP against UAT, and the manual role-matrix tests from the pentest plan.
