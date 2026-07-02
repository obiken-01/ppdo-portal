# RAL-61 — LDIP Entry Form: Implemented Design

_Written 2026-07-02 alongside the implementation. **This supersedes the field table in the
RAL-61 ticket description** — the form was redesigned in the RAP-01 Penpot frame
("RalphPage" → `RAP-01| LDIP Entry Form`) and confirmed interactively via chat prototypes
before implementation. See `RAL-61_LDIP_Entry_Form_Penpot_Findings.md` for why the older
mockups were unusable._

## 1. Data model (replaces the ticket's flat-column plan)

The ticket's original plan (PS/MOOE/CO + funding + CC/alignment columns directly on
`ldip_records`) was superseded. Instead, LDIP mirrors the AIP hierarchy:

```
ldip_records                 -- one multi-year DOCUMENT per office
  + office_id INT NULL FK -> offices (Restrict)
    -- nullable in DB for pre-v1.3 rows; REQUIRED by the service for new records

ldip_offices                 -- one row per SECTOR GROUP under a document (new)
  id, ldip_record_id FK (Cascade), ref_code NVARCHAR(50),
  name NVARCHAR(500), sector NVARCHAR(20)
  UNIQUE (ldip_record_id, ref_code)

ldip_programs                -- program rows under a sector group (new)
  id, ldip_office_id FK (Cascade), ref_code NVARCHAR(50),
  name NVARCHAR(500), budget DECIMAL(18,2)
  UNIQUE (ldip_office_id, ref_code)
```

Migration: `20260702071330_AddLdipOfficeScopeAndPrograms` (applied to local dev;
**must be applied to Azure SQL at deploy** per the standard procedure).

### Key decisions

| # | Decision |
|---|---|
| 1 | **One office per LDIP document** (Section 1 field). The office is the config `offices` row; office users are locked to their own, PPDO picks any. |
| 2 | **Sector ≡ group.** Because the office is fixed, each `ldip_offices` row corresponds to one sector choice (General/Social/Economic/Others, max 4 per document). |
| 3 | **AIP ref codes are server-authoritative.** Group = `{sector prefix}-000-1-{offices.office_ref_code}` (e.g. General + "01-010" → `1000-000-1-01-010`); program = group + `-NNN`, contiguous 001-based in submitted order. The client shows previews only. |
| 4 | **Updates full-replace the hierarchy** (WFP SaveAsync pattern, two SaveChanges rounds so the unique index never sees old+new side by side) → removing a program renumbers the rest of its group with **no gaps**. Correct ref codes are a hard project requirement. |
| 5 | **Office/sub-office display name is per sector group** and may differ from the config office name while sharing the office ref-code suffix — matching the real AIP-file quirk (e.g. `1000-…-010` "PPDO" vs `8000-…-010` "PPDO - SPECIAL PROJECTS"). In the form the name is editable only while its group has no programs; once the group exists the field locks (one ref code → one name). |
| 6 | **Budget = one amount per program for the document's whole year range**, entered and stored in **thousands (₱000)** like AIP totals. Per PPDO discussion this may still change (e.g. per-FY split) — the column is isolated on `ldip_programs` so a later split is additive. |
| 7 | **Title auto-generated** when blank: `LDIP {start}-{end} — {office name}`. The form doesn't expose Title; EntryMode defaults to "New" (amendments = RAL-78). |
| 8 | Draft saves are permissive; **Finalize validates completeness** (office set, year start ≤ year end, ≥1 program) — the WFP finalize pattern. |
| 9 | **Office scoping in Functions**: office users are forced to their own `office_id` on List/Create/Update and blocked (403) from touching other offices' records on Get/Update/Archive/Finalize; PPDO sees all and may filter `?officeId=`. |
| 10 | The RAL-60 dashboard LDIP panel is **un-stubbed**: per-office document count (year range covering the selected FY) + status breakdown. |

### Dropped from the original ticket scope (deliberately)

- PS/MOOE/CO split, funding-source FK, expected outputs, implementing office, CC
  adaptation/mitigation/typology, PDP/RDP/SDG/Sendai tags — none of these are in the
  confirmed RAP-01 design. If PPDO later confirms any of them, they are additive
  columns on `ldip_programs`/`ldip_records`.

## 2. The form (frontend)

Shared `LdipForm.tsx` used by `ldip/new` and `ldip/[id]/edit`:

1. **LDIP Information** — Year Start / Year End, Office (`OfficeSelect`; locked label for
   office users and read-only records).
2. **Program Information** (hidden when read-only) — Sector select → live office ref-code
   preview; Office/Sub-office Name (lock behavior per decision 5, with an
   "existing group, name locked" / "new group, name it" hint); Program Name;
   program ref-code preview; Budget (`MoneyInput`, ₱000); **+ Add Program**.
3. **Created Programs** — AIP-detail-style grouped table (green header per sector group
   with ref code + name), rows renumber on Remove.

Ribbon: Save Draft / Finalize / Cancel (+ admin Unlock on Final records). Yellow =
user input, grey = auto-filled, per the ticket's note-bar convention. First Save Draft
on the create page routes to the edit page of the new record.

LDIP list gains OFFICE + PROGRAMS columns and honors `?officeId=` carried from the
dashboard nav.

## 3. Verified live (2026-07-02, local SQL Express)

- Create: 2 sectors/3 programs → `1000-000-1-01-010-001/002` + `8000-000-1-01-010-001`,
  auto-title, per-sector sub-office names ✓
- Update removing a group's first program → survivor renumbered to `-001` ✓
- Duplicate sector → 400; finalize-with-programs → Final; unlock → Draft ✓
- List returns officeName + programCount; office dashboard LDIP panel returns
  `scopingSupported: true` with real counts ✓
- 428/428 backend tests; tsc + eslint clean.
