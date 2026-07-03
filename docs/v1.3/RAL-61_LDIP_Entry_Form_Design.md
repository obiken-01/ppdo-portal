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

ldip_offices                 -- one row per OFFICE/SUB-OFFICE GROUP under a document (new)
  id, ldip_record_id FK (Cascade), ref_code NVARCHAR(50),
  name NVARCHAR(500), sector NVARCHAR(20)
  -- NOT unique on (ldip_record_id, ref_code): several groups may share one ref
  -- code within a sector, distinguished by name (same as aip_offices)

ldip_programs                -- program rows under a sector group (new)
  id, ldip_office_id FK (Cascade), ref_code NVARCHAR(50),
  name NVARCHAR(500), budget DECIMAL(18,2)
  UNIQUE (ldip_office_id, ref_code)
```

Migration: `20260702081724_AddLdipOfficeScopeAndPrograms` (applied to local dev;
**must be applied to Azure SQL at deploy** per the standard procedure).

### Key decisions

| # | Decision |
|---|---|
| 1 | **One office per LDIP document** (Section 1 field). The office is the config `offices` row; office users are locked to their own, PPDO picks any. |
| 2 | **Group = (sector, sub-office name).** A sector may hold MULTIPLE groups sharing one ref code — the real AIP-file shape (PGO's SOCIAL sheet has `3000-000-1-01-001` three times: "…- WARDEN" / "…- AKAP-HUB" / "…- HOUSING"). Duplicate (sector, name) pairs are rejected instead. |
| 3 | **AIP ref codes are server-authoritative.** Group = `{sector prefix}-000-1-{offices.office_ref_code}` (e.g. General + "01-010" → `1000-000-1-01-010`); program = group + `-NNN`, numbered **continuously per ref code across all its groups** in submitted order (WARDEN gets -001, AKAP-HUB continues -002, …) so codes never collide. The client shows previews only. |
| 4 | **Updates full-replace the hierarchy** (WFP SaveAsync pattern, two SaveChanges rounds so the unique index never sees old+new side by side) → removing a program renumbers the rest of its group with **no gaps**. Correct ref codes are a hard project requirement. |
| 5 | **Office/sub-office name field is always editable** with a datalist of the current sector's existing group names — typing/picking an existing name (case-insensitive) appends to that group; a new name starts a new group. A hint under the label says which will happen. |
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

Shared `LdipForm.tsx` used by `ldip/new` and `ldip/edit`:

1. **LDIP Information** — Year Start / Year End, Office (`OfficeSelect`; locked label for
   office users and read-only records).
2. **Program Information** (hidden when read-only) — Sector select → live office ref-code
   preview; Office/Sub-office Name (always editable + datalist per decision 5, with an
   "adds to this existing group" / "starts a new group" hint); Program Name;
   program ref-code preview (continuous per ref code); Budget (`MoneyInput`, ₱000);
   **+ Add Program**.
3. **Created Programs** — AIP-detail-style grouped table (green header per sector group
   with ref code + name), rows renumber on Remove.

Ribbon: Save Draft / Finalize / Cancel (+ admin Unlock on Final records). Yellow =
user input, grey = auto-filled, per the ticket's note-bar convention. First Save Draft
on the create page routes to the edit page of the new record.

LDIP list gains OFFICE + PROGRAMS columns and honors `?officeId=` carried from the
dashboard nav.

**Edit route is query-param** (`/budget-planning/ldip/edit?id=N`, the AIP-detail
pattern) — the app uses `output: "export"`, so the old `[id]/edit` dynamic segment
500'd in dev for any id not in `generateStaticParams` and was removed.

## 3. Verified live (2026-07-02, local SQL Express)

- Create: 2 sectors/3 programs → `1000-000-1-01-010-001/002` + `8000-000-1-01-010-001`,
  auto-title, per-sector sub-office names ✓
- **The PGO screenshot case**: 3 sub-office groups under Social (WARDEN / AKAP-HUB /
  HOUSING) all `3000-000-1-01-001`, programs numbered continuously `-001/-002/-003`
  across the groups ✓
- Update removing a group's first program → survivor renumbered to `-001` ✓
- Duplicate (sector, name) → 400; finalize-with-programs → Final; unlock → Draft ✓
- List returns officeName + programCount; office dashboard LDIP panel returns
  `scopingSupported: true` with real counts ✓
- `/budget-planning/ldip/edit?id=N` renders 200 in dev (the old `[id]/edit` dynamic
  route 500'd under `output: export`); full `npm run build` static export passes ✓
- 429/429 backend tests; tsc + eslint clean.
