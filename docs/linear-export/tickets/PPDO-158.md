---
id: PPDO-158
title: 'Demo 2.15 T5 — Word export: .docx template + Open XML renderer + export endpoint'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: PPDO-157
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-158
uuid: af82e8ba-a1f4-42a9-880b-8926c58fafeb
---

# PPDO-158 — Demo 2.15 T5 — Word export: .docx template + Open XML renderer + export endpoint

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` decision 27, §4 (export), §8 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

⚠️ **NEW DEPENDENCY (explicit pin only):** `DocumentFormat.OpenXml`, already resolved transitively via ClosedXML — no new download.

> **Not a manual-implementation candidate:** layout-heavy Open XML with a slow feedback loop, the same reasons `ExcelService.cs` is excluded.

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md decision 27, §4 (export) and §8 FULLY, and
docs/v1.8/Investment_Proposal_Findings.md §8.1 (measured page setup, fonts, letterhead).

Read these files before writing code:
- backend/PPDO.Application/Services/IAipFormExcelService.cs and
  backend/PPDO.Infrastructure/Services/AipFormExcelService.cs (interface in Application,
  implementation in Infrastructure — mirror exactly)
- backend/PPDO.Functions/Functions/AipConsolidatedFunctions.cs (the /export route: headers,
  Content-Disposition, file response)
- backend/PPDO.Infrastructure/PPDO.Infrastructure.csproj (package references)
- the ProposalDocument model from PPDO-157

Before changing anything, state in one line each:
- Current behaviour: proposals cannot be exported.
- Target behaviour: GET /api/budget-planning/proposals/{id}/export returns a valid .docx built
  from the template and the PPDO-157 model.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-158-proposal-word-export off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

TDD: InvestmentProposalWordServiceTests first (opens with WordprocessingDocument.Open, zero
OpenXmlValidator errors, contains the title and the expected H-1 rows).

1. Dependency: explicit PackageReference DocumentFormat.OpenXml in PPDO.Infrastructure, pinned to
   the version ClosedXML 0.104.2 already resolves (read obj/project.assets.json).
                                                                    ⚠️ NEW DEPENDENCY (explicit pin only)
   Verify: dotnet build; dotnet list package --include-transitive shows one OpenXml version.
2. Template: PPDO.Infrastructure/Templates/InvestmentProposalTemplate.docx (embedded resource) —
   A4, 1009-twip margins, Verdana 10 pt, header = seal + four letterhead lines + Bagong Pilipinas
   logo, footer "Page {PAGE} of {NUMPAGES}", empty body. Compress the seal image to < 200 KB first.
   Verify: open the template in Word; the file is < 400 KB.
3. InvestmentProposalWordService: render the model into the body — title, sections A–M in template
   order, tables with the template's column headings, H-1 with MOOE | PS | CO | Total | Source of
   Fund (decision 17, revised by PPDO-173), rich-text runs as paragraphs/lists, signature block
   (up to 4 slots, two per row, empty slots skipped). No guidance text.
   Verify: dotnet test --filter InvestmentProposalWordServiceTests
4. Endpoint: GET /api/budget-planning/proposals/{id}/export — same JWT/permission/scope/404 as
   GET; read-only (no write guard). Filename per spec §3.1, sanitized, ≤ 120 chars, RFC 5987
   filename*.
   Verify: func start; export a seeded FY2028 proposal; open in Word — no repair prompt; compare
   against spec §10 "Export" lines one by one.

Risks and rollback:
- Word "repair" prompt from malformed XML → OpenXmlValidator in the tests; do not merge with
  validator errors.
- Large embedded images bloat every export → step 2 size check.
- Revert is safe (no migration).

Stop and get my sign-off before: adding the package reference (new dependency in the csproj).

Do NOT: generate PDF; render guidance text; round amounts.

When done, commit with:
feat(investment-proposal): Word export of the investment proposal (PPDO-158)
```
