---
id: PPDO-41
title: 'V18-38 — Freeze .xlsm AIP upload to historical fiscal years'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: PPDO-40
related: PPDO-47, PPDO-49, PPDO-61
url: https://linear.app/ralphoksiprojects/issue/PPDO-41
uuid: a448e74f-4430-478f-a5b7-8b2431a1ff9b
---

# PPDO-41 — V18-38 — Freeze .xlsm AIP upload to historical fiscal years

**In one line:** the `.xlsm` importer can only produce the old record shape, so stop it accepting FY2028+ — from FY2028 the AIP is entered in the portal, not uploaded.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §3.1 · **Size:** S · **Blocked by:** V18-37

## Why

`AipXlsmParser` builds the v1.6 multi-office shape. After V18-40 and V18-37, an FY2028 upload would produce a record in a shape FY2028 does not use — silently, and discovered later by whoever opens it.

From FY2028 the AIP is **entered** (Phase 3), not imported. The importer keeps working for historical years, which is the only thing it is still needed for.

## Scope

Refuse `.xlsm` upload where the target fiscal year is ≥ 2028. FY≤2027 upload is **unchanged** — including LDIP-seeded and re-upload paths.

## UI

The upload control is **disabled with a reason**, not hidden. The user has permission; the *state* forbids it. That is the rule in `Budget_Planning_Dashboard_Requirements.md` §6.1: hide for permission, disable-with-reason for state.

The message must name the fiscal year and say what to do instead — "FY2028 AIPs are entered in the portal, not uploaded", not a generic validation error.

## Watch out

* ⚠️ **FY2027 rows in the database were imported by the pre-**[RAL-238](https://linear.app/ralphoksiprojects/issue/RAL-238/aip-import-detect-hierarchy-level-from-the-description-column-not-the) **parser** and are not a faithful copy of the province's file (Phase_Plan §4b). Freezing the importer means that stays true permanently — worth stating in the release notes rather than discovering later.
* Check the **re-upload-into-existing-record** path too (v1.6, [RAL-179](https://linear.app/ralphoksiprojects/issue/RAL-179/aip-inline-per-node-editing-for-existing-records-option-b-follow-up-to)) — not just first upload.
* Do not delete the parser. FY≤2027 still needs it.

## Out of scope

The Phase 3 entry flow this hands over to. Improving the parser. Re-importing FY2027 faithfully.

## Done when

* FY2028 upload refused, with a message naming the year and the alternative
* FY2027 upload and re-upload both still work
* The control is disabled with a visible reason, not hidden
