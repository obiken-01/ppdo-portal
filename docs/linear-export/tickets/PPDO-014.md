---
id: PPDO-14
title: 'External AIP API #3 — external AIP read service (schema 1.0.0: pesos as strings, amounts + printedAmounts, pending offices)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-06-30
completed: 2026-09-15
parent: PPDO-16
blocked_by: (none)
related: PPDO-15, PPDO-83, RAL-108
url: https://linear.app/ralphoksiprojects/issue/PPDO-14
uuid: e0c495c9-10e9-4d44-b0af-73781216f949
---

# PPDO-14 — External AIP API #3 — external AIP read service (schema 1.0.0: pesos as strings, amounts + printedAmounts, pending offices)

**In one line:** build the `data` object of `docs/external-api/aip-response.schema.json` for a fiscal year, optionally one office — the same figures as the Consolidated AIP page and the Annex B Excel.

**Spec:** `docs/v1.8/External_AIP_API_Spec.md` §2 decisions 1, 2, 12, 13 and §3.1 data rows · **Payload:** `docs/external-api/README.md` + schema + samples · **Epic:** [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-partner-api-keys-v180-phase-5-step-2) · **Can start in parallel with** [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-issuing-service) · **Size:** L

> ⚠️ Rewritten 2026-09-14. The June draft said "convert thousands to pesos (×1000)", "Final-only", "sectors[]". All obsolete: amounts are stored in pesos, FY2028+ releases by workflow status, and the shape is `groups[]`.

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/External_AIP_API_Spec.md FULLY — authoritative for the rules.
Read docs/external-api/README.md FULLY, then aip-response.schema.json and the three samples —
authoritative for the payload shape. Read docs/v1.8/AIP_Amount_States.md §4 and docs/PERFORMANCE_GUIDELINES.md.

Read these files before writing code:
- backend/PPDO.Application/Services/AipConsolidatedService.cs (LoadTreeAsync: one set-based tree load for many groups; IsWithPpdo; CountOffices)
- backend/PPDO.Application/Common/AipPrintedFigures.cs (the ONLY source of printedAmounts — never re-derive the +30%/rounding)
- backend/PPDO.Application/Common/AipFormRowBuilder.cs + AipTreeMapper.cs (tree ordering by ref code, synthetic projects, fund-code grouping)
- backend/PPDO.Application/Common/AipSector.cs (sector codes/prefixes), AuditAction.cs (AcceptByPpdo = "ACCEPT_PPD", logged on "aip_offices")
- backend/PPDO.Domain/Interfaces/IAipRepository.cs, IAipExpenditureRepository.cs, IAuditRepository.cs (GetLatestActionAsync — releasedAt)
- backend/PPDO.Domain/Entities/AipRecord.cs (Status Draft/Final/Archived — legacy rule), AipOffice.cs (WorkflowStatus, OfficeId, Sector), AipActivity.cs, AipExpenditure.cs (account/fund snapshots), AipProcurementItem.cs, Office.cs (OfficeCode)
- backend/PPDO.Tests/Application/AipConsolidatedServiceTests.cs (fixture with offices in every state — copy that discipline)

Before changing anything, state in one line each:
- Current behaviour: no external read; the Consolidated service builds Annex B rows for reviewers only.
- Target behaviour: IExternalAipReadService returns the schema 1.0.0 data object for a year (and optionally one office) with no caller/permission logic — scope is PPDO-13's.

Working branch: release/1.8.0. Create feature/v1.8.0-ppdo-14-external-aip-read-service off release/1.8.0
and open the PR against release/1.8.0 (NOT main).

TDD: ExternalAipReadServiceTests failing first, then implement.

Steps:
0. Resolve the README §6 "verify before implementation" list against local data and write the answers
   into the spec's open follow-ups: legacy blank/"None" ref codes, FundingSourceId on FY2028 activities,
   CC typology split, releasedAt source, unlinked legacy groups (spec: exclude), whole-year size.
   Verify: note the findings in the PR.
1. DTOs in Application/DTOs/ExternalApi/ mirroring the schema exactly (camelCase JSON; money as string
   via one formatter "0.00" InvariantCulture). Verify: a serializer test against one sample fragment.
2. Released/pending rule (FY2028+): an office is released only when EVERY one of its groups is
   Consolidated; otherwise pending if it has any group. Legacy (FY≤2027): record Status == "Final".
   Verify: tests for all-consolidated, one-not, none, sent-back-after-accept.
3. Tree load: one query per level for all released groups (reuse/extend the repo methods behind
   LoadTreeAsync), expenditures + procurement items by activity ids — no per-office loop.
   Verify: a test asserting repository calls do not grow with office count.
4. Mapping: groups keyed by (sector, name); programs/projects/activities by ref code; isSynthetic;
   amounts from stored pesos; printedAmounts via AipPrintedFigures (FY2028+ only); totals,
   printedTotals, totalsByFundingSource at office and top level (released offices only);
   releasedAt = latest ACCEPT_PPD across the office's groups.
   Verify: ₱1,000,400 MOOE → amounts "1000400.00", printedAmounts "1301000.00"; totals equal sums.
5. Schema check: serialize a fixture response and validate it against aip-response.schema.json in a
   test (JsonSchema.Net or equivalent — ⚠️ NEW DEPENDENCY if a package is needed; test project only).
   Verify: dotnet test PPDO.Tests --filter ExternalAipReadService.

Risks / sign-off:
- A figure disagreeing with the Excel is the worst outcome — printedAmounts must come from AipPrintedFigures, tested against it.
- Office scope is NOT decided here; this service must never be callable from an internal JWT route without PPDO-13's checks.
- Response size: measure FY2027 whole-year locally; report bytes and time in the PR.

Do NOT: add endpoints (PPDO-12), auth (PPDO-13), pagination, releasedSince, or expose ids/divisions/users/comments.

When done, commit with:
feat(external-api): external AIP read service for schema 1.0.0 (PPDO-14)
```

## Manual-coding candidate?

Partly. Step 1 (DTOs + money formatter test) is a good hand-coding piece: pure, testable with `dotnet test`, the samples are the spec. The released/pending rule and the tree load are not — correctness and performance traps.
