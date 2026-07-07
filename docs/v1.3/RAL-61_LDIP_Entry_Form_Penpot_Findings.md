# RAL-61 — LDIP Entry Form: Penpot Findings

_Extracted 2026-07-02 via the Penpot MCP plugin, before implementation starts. Written up per the
"extract Penpot designs upfront" convention (plugin access is session-dependent; capture findings
to a doc once, don't re-derive them every session)._

> **Per convention: this ticket's written field table (RAL-61 description / Claude Code Prompt)
> overrides the Penpot mockup wherever they conflict.** They conflict significantly here — see
> §3. Treat this doc as context, not as the source of truth.

## 1. What's actually in the Penpot file

The file has 4 pages. Two candidate "LDIP entry form" frames exist, from two different, unrelated
design passes:

| Frame | Page | Matches |
|---|---|---|
| `02 LDIP Entry Form` | Page 2 | An **older / superseded** design pass (see §2) |
| `14 LDIP Entry` | Page 4 | The **current, already-shipped stub form** (Title/EntryMode/FYStart/FYEnd/SourceLDIP only) |

The ticket description says "Mockup: Penpot Page 2 → `02 LDIP Entry Form`" — that pointer is now
stale. Page 4's cluster (`13 LDIP List` → `14 LDIP Entry` → `15 AIP List` → ... → `21 Planning
Dashboard (Visitor)`) is the newer pass and matches what's actually shipped today almost exactly
(e.g. `18 WFP Activity Grid` / `19 WFP Expenditure Popup` mirror the real WFP page, `13 LDIP List`
shows Draft/Final/Archived statuses matching the current schema). Page 2's cluster (`01 Office
Selection`, `02 LDIP Entry Form`, ...) is from an earlier pass — its `01 Office Selection` screen
was already explicitly dropped per the RAL-59 epic ("redundant with the existing dashboard,
RAL-80"), and `02 LDIP Entry Form` appears to be from that same abandoned generation (topbar shows
a `v1.0.1` version tag, "Submit Entry" — pre-dates the current sidebar/topbar and the Draft →
Final → Archived status model).

## 2. Content of each frame

### `14 LDIP Entry` (Page 4) — current stub, NOT the RAL-61 target

Matches today's `ldip/new/page.tsx` field-for-field:
- Breadcrumb: `Planning / LDIP / New LDIP`
- Title: "Create New LDIP"
- Fields: **Title**, **Entry Mode** (dropdown), **Fiscal Year Start** / **Fiscal Year End**,
  **Source LDIP** (shown as "N/A — Entry Mode is New" when Entry Mode = New)
- Ribbon: **Save Draft** / **Submit for Approval** / **Cancel**

⚠️ **"Submit for Approval" is stale copy** — per the RAL-59 alignment notes, the status workflow
is Draft → Final → Archived with no approval step; any "Submit" in a mockup = **Finalize**. Fix
this wording regardless of which field set ships.

This frame is useful as a **style/layout reference** (breadcrumb format, helper-text line under
the title, card-style section, ribbon button position) but has none of the RAL-61 fields.

### `02 LDIP Entry Form` (Page 2) — older pass, different field set entirely

Three numbered sections, but **not** the ones in the ticket's field table:

1. **PROGRAM INFORMATION** — AIP Reference Code (auto, "Auto-generated on save"), Sector (auto,
   read-only), Office (auto, read-only), PPA Name (required, with helper text "should match the
   LGU approved Development Investment Program nomenclature")
2. **FINANCIAL TARGETS (PHP)** — a **funding-source × fiscal-year matrix**: rows = General Fund /
   Special Education Fund / Trust Fund-Others; columns = FY2027 / FY2028 / FY2029 / 3-Year Total
   (all amounts, no PS/MOOE/CO split at all)
3. **PHYSICAL TARGETS** — Performance Indicator (required) + Target 2027 / Target 2028 / Target
   2029 — a concept that doesn't appear anywhere in the RAL-61 ticket's field table

Ribbon: **Save Draft** / **Cancel** / **Submit Entry** (same stale "Submit" wording).

## 3. The conflict — mockup vs. ticket field table

Neither Penpot frame matches the ticket's **Section 1 Program Information / Section 2 Budget
(PS/MOOE/CO via MoneyInput) / Section 3 Alignment & Tags (CC Adaptation/Mitigation/Typology,
PDP/RDP, SDGs, Sendai/NDRRM/NSP/PDPDFP)** field table:

- The **Program Information** fields overlap partially (AIP Ref Code / Sector / Office / PPA Name
  are common to both the mockup and the ticket), so that section's layout can reasonably borrow
  from `02 LDIP Entry Form`'s Section 1.
- The mockup's **Financial Targets** (fund-source × year matrix) and **Physical Targets**
  (performance indicators) sections have **no equivalent** in the ticket's Budget/Alignment
  sections, and vice versa — the ticket's PS/MOOE/CO split and CC/PDP-RDP/SDG tagging fields
  have **no visual precedent in Penpot at all**.

**Recommendation:** build Section 1 borrowing the mockup's read-only-field treatment (Ref Code /
Sector / Office as grey auto-filled fields, per the note-bar convention already documented in the
ticket: "yellow = user input, grey = auto-filled"). For Sections 2 (Budget) and 3 (Alignment &
Tags), there's no mockup to match — follow the ticket's field table directly and reuse the visual
conventions already established in the shipped AIP/WFP forms (`MoneyInput` rows, section card
style, `formatMoney`) rather than inventing new layout from scratch.

## 4. Related frames (context only, not this ticket's scope)

- **`13 LDIP List`** (Page 4) — current, matches the schema in production: columns REF CODE /
  TITLE / PERIOD / ENTRY MODE / STATUS (Draft/Final/Archived, matching today's status model) /
  CREATED / ACTIONS (Edit · View). Confirms the list page doesn't need to change for RAL-61.
- **`01 Office Selection`** (Page 2) — superseded, already dropped per RAL-59 epic notes.

## 5. Open item

The ticket itself says "confirm the final LDIP field set with PPDO" — this Penpot check doesn't
resolve that; if anything it adds a third candidate field set (Financial/Physical Targets) to rule
out or fold in. Worth a explicit confirmation pass before Section 2/3 implementation, not just a
schema-naming exercise.
