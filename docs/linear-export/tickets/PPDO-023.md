---
id: PPDO-23
title: 'Config: climate-change typology table + management page'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-02
parent: PPDO-28
blocked_by: (none)
related: PPDO-19
url: https://linear.app/ralphoksiprojects/issue/PPDO-23
uuid: 42096513-ce3d-4887-9c7d-5452aef8e194
---

# PPDO-23 — Config: climate-change typology table + management page

**V18-09** — plan: `docs/v1.8/Phase_Plan.md` §3.2; raised in `AIP_Requirements_Review.md` §5.5.

`AipActivity.CcTypologyCode` is a **free string** today, on a document that gets audited — and the v1.8.0 requirements anticipate a config page for these codes. Add `climate_change_typologies` as a proper config table plus a management page, following the existing config-page pattern (`config/funding-sources` is the closest template: entity + `IEntityTypeConfiguration` with snake_case mapping, repository, service, `/api/config/...` CRUD, `DataTable` page with `Modal`/`ConfirmDialog`/`useToast`, soft delete via `IsActive`).

Notes:

* Codes are referenced by AIP activities, so **soft-delete only** — never hard-delete a referenced row.
* `CcAdaptation` / `CcMitigation` amounts already exist on `AipActivity` and are not in scope here.
* Migrating the existing free-string values onto FK references belongs with the AIP data-model work (Phase 2), not here — but the table must be shaped so that migration is a straightforward backfill.

**Done when:** the codes are configurable, gated on `CanManageConfig`, and the page matches the design system (flat, no rounded corners).
