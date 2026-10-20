---
id: PPDO-60
title: 'AipOffice.OfficeId is never set outside the importer — manually added offices are invisible to scoped reads'
status: Done
project: PPDO Portal
labels: 
milestone: techdebt
created: 2026-09-05
completed: 2026-09-14
parent: (none)
blocked_by: (none)
related: PPDO-38, PPDO-51, PPDO-61, PPDO-62
url: https://linear.app/ralphoksiprojects/issue/PPDO-60
uuid: 5a74dd9b-43c7-4c7a-92f7-4f6d853774d1
---

# PPDO-60 — AipOffice.OfficeId is never set outside the importer — manually added offices are invisible to scoped reads

**In one line:** `AddOfficeAsync` and `CopyOfficeFromPriorYearAsync` create an `AipOffice` without its ownership FK, so `AipReadScope` filters the row out and the owning office sees nothing.

## Found

While implementing [PPDO-51](https://linear.app/ralphoksiprojects/issue/PPDO-51/v18-41-aip-programs-sourced-from-a-valid-ldip-closed-list) (V18-41). Grepping `AipService` for where `AipOffice.OfficeId` is assigned returns **exactly one site** — `ConfirmImportAsync` line 453, via `AipOfficeOwnership.ResolveOfficeId`. The three other paths that create an `AipOffice` do not set it:

| Path | Sets `OfficeId`? |
| -- | -- |
| `ConfirmImportAsync` (.xlsm import) | ✅ yes |
| `AddOfficeAsync` (manual "+ Add Office") | ❌ no |
| `CopyOfficeFromPriorYearAsync` (carry forward) | ❌ no |
| `SeedProgramsFromLdipAsync` | ✅ **fixed in** [PPDO-51](https://linear.app/ralphoksiprojects/issue/PPDO-51/v18-41-aip-programs-sourced-from-a-valid-ldip-closed-list) |

## Why it matters

`AipReadScope` scopes on that column:

```csharp
offices.Where(o => o.OfficeId == _scope.Office.OfficeId).ToList()
```

A null means the row matches **no** office. A guest-office user who adds an office to their own AIP by hand then opens that AIP and **sees nothing** — no error, no empty-state explanation, just absence. This is the exact failure mode V18-32's migration comment warns unmatched rows cause, and it says such rows "must be found and resolved, not discovered later by a user seeing an empty page."

It has been survivable so far only because there are no production guest-office accounts — the same reason [PPDO-38](https://linear.app/ralphoksiprojects/issue/PPDO-38/v18-39-aip-scope-resolver-officescope-divisionscope) found AIP reads were unscoped entirely.

⚠️ **It gets worse in Phase 3, not better.** FY2028 records are office-owned and *will* be read by guest-office encoders. `AddOfficeAsync` already knows the answer — it looks up `dto.OfficeConfigId` to compute the ref code and then discards it.

## Suggested fix

Set `OfficeId = dto.OfficeConfigId` in `AddOfficeAsync`. For `CopyOfficeFromPriorYearAsync`, carry the source office's `OfficeId` across, or resolve it the same way `AipOfficeOwnership.ResolveOfficeId` does from the ref code.

⚠️ Also worth a **data check**: any `aip_offices` row created by hand since V18-32 shipped has a null here. The migration's reporting query finds them:

```sql
SELECT r.fiscal_year, COUNT(*) AS unmatched
FROM   aip_offices a JOIN aip_records r ON r.id = a.aip_record_id
WHERE  a.office_id IS NULL
GROUP  BY r.fiscal_year;
```

Rows under a **legacy** FY≤2027 record may legitimately be unmatched (an office that was never configured). Rows under an FY≥2028 office-owned record cannot be — that combination is the bug.

## Done when

* All four `AipOffice` creation paths set the ownership FK
* A test per path asserts it, so a fifth path added later fails rather than silently repeating this
* Existing null rows under office-owned records are found and resolved
