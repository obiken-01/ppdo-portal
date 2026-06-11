# PPDO Portal — Database Model
_Version 1.1 | Date: June 10, 2026 | Target: PPDO Portal v1.1_
_Covers: LDIP · AIP · WFP · Account Config · Office Config · Funding Source Config · Audit Log_

---

## 1. Config Tables

### `offices`

> CSV column order (seed & download): `office_code, office_name, is_active`

```sql
offices
  id              INT PK IDENTITY
  office_code     NVARCHAR(20)   NOT NULL UNIQUE   -- e.g. "PPDO", "PGO"
  office_name     NVARCHAR(200)  NOT NULL
  is_active       BIT            NOT NULL DEFAULT 1
  created_at      DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  updated_at      DATETIME2      NOT NULL DEFAULT GETUTCDATE()
```

---

### `funding_sources`

> CSV column order (seed & download): `code, name, description, is_active`

```sql
funding_sources
  id              INT PK IDENTITY
  code            NVARCHAR(20)   NOT NULL UNIQUE   -- e.g. "GF", "GAD", "LDRRMF"
  name            NVARCHAR(100)  NOT NULL
  description     NVARCHAR(MAX)  NULL
  is_active       BIT            NOT NULL DEFAULT 1
  created_at      DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  updated_at      DATETIME2      NOT NULL DEFAULT GETUTCDATE()
```

---

### `accounts` (Chart of Accounts)

> CSV column order (seed & download): `account_title, account_number, normal_balance, description, is_active`
>
> **Derived field (not stored):** expenditure type (PS / MOOE / CO) is inferred from `account_number` prefix at query time:
> `5-01-xx` = PS · `5-02-xx` = MOOE · `5-03-xx` = CO · Other `5-xx` = Other

```sql
accounts
  id               INT PK IDENTITY
  account_title    NVARCHAR(300)  NOT NULL           -- Object of Expenditure label
  account_number   NVARCHAR(20)   NOT NULL UNIQUE    -- e.g. "5-01-01-010"
  normal_balance   NVARCHAR(10)   NULL               -- "Debit" / "Credit"
  description      NVARCHAR(MAX)  NULL
  is_active        BIT            NOT NULL DEFAULT 1
  created_at       DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  updated_at       DATETIME2      NOT NULL DEFAULT GETUTCDATE()
```

---

## 2. LDIP

> Table created now; not actively used in batch 1. AIP has a nullable FK to it for future use.

### `ldip_records`
```sql
ldip_records
  id                  INT PK IDENTITY
  ref_code            NVARCHAR(50)   NOT NULL UNIQUE   -- system-generated
  title               NVARCHAR(500)  NOT NULL
  fiscal_year_start   INT            NOT NULL           -- e.g. 2027
  fiscal_year_end     INT            NOT NULL           -- e.g. 2029
  entry_mode          NVARCHAR(20)   NOT NULL           -- New / Amendment / Supplemental
  status              NVARCHAR(20)   NOT NULL DEFAULT 'Draft'
                                                        -- Draft / Final / Archived
  source_id           INT            NULL FK -> ldip_records  -- set when copied from another record
  created_by          INT            NOT NULL FK -> users
  created_at          DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  updated_at          DATETIME2      NOT NULL DEFAULT GETUTCDATE()
```

---

## 3. AIP

AIP is **independent from LDIP** (no required FK). The hierarchy is stored in 4 tables, each linked by parent FK. Level is determined by segment count in the ref code.

### `aip_records`
One record per AIP creation — whether from file upload or manual web UI entry.

```sql
aip_records
  id                INT PK IDENTITY
  fiscal_year       INT            NOT NULL           -- e.g. 2027
  entry_source      NVARCHAR(10)   NOT NULL           -- Upload / Manual
  original_filename NVARCHAR(500)  NULL               -- set only when entry_source = Upload
  uploaded_by       INT            NOT NULL FK -> users
  uploaded_at       DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  status            NVARCHAR(20)   NOT NULL DEFAULT 'Draft'
                                                      -- Draft / Final / Archived
  ldip_id           INT            NULL     FK -> ldip_records
  source_id         INT            NULL     FK -> aip_records  -- set when copied for amendment/supplemental
```

### `aip_offices` — Level 1 (5-segment ref code)
```sql
aip_offices
  id              INT PK IDENTITY
  aip_record_id   INT            NOT NULL FK -> aip_records
  ref_code        NVARCHAR(50)   NOT NULL
  name            NVARCHAR(500)  NOT NULL
  sector          NVARCHAR(20)   NOT NULL   -- General / Social / Economic / Others
  UNIQUE (aip_record_id, ref_code)
```

### `aip_programs` — Level 2 (6-segment ref code)
```sql
aip_programs
  id          INT PK IDENTITY
  office_id   INT            NOT NULL FK -> aip_offices
  ref_code    NVARCHAR(50)   NOT NULL
  name        NVARCHAR(500)  NOT NULL
  UNIQUE (office_id, ref_code)
```

### `aip_projects` — Level 3 (7-segment ref code)
```sql
aip_projects
  id          INT PK IDENTITY
  program_id  INT            NOT NULL FK -> aip_programs
  ref_code    NVARCHAR(50)   NOT NULL
  name        NVARCHAR(500)  NOT NULL
  UNIQUE (program_id, ref_code)
```

### `aip_activities` — Level 4 (8-segment ref code, leaf level)
```sql
aip_activities
  id                    INT PK IDENTITY
  project_id            INT            NOT NULL FK -> aip_projects
  ref_code              NVARCHAR(50)   NOT NULL
  name                  NVARCHAR(1000) NOT NULL
  esre_code             NVARCHAR(10)   NULL       -- SS / ES / ID / EN
  implementing_office   NVARCHAR(200)  NULL
  start_date            NVARCHAR(50)   NULL       -- stored as string (e.g. "January")
  end_date              NVARCHAR(50)   NULL
  expected_outputs      NVARCHAR(MAX)  NULL
  funding_source_id     INT            NULL FK -> funding_sources
  funding_source_snapshot NVARCHAR(20) NULL       -- snapshot of funding_sources.code at import time
  ps                    DECIMAL(18,2)  NULL
  mooe                  DECIMAL(18,2)  NULL
  co                    DECIMAL(18,2)  NULL
  total                 DECIMAL(18,2)  NULL       -- auto-computed: ps + mooe + co
  cc_adaptation         DECIMAL(18,2)  NULL
  cc_mitigation         DECIMAL(18,2)  NULL
  cc_typology_code      NVARCHAR(50)   NULL
  UNIQUE (project_id, ref_code)
```

> **Note:** `start_date` / `end_date` stored as strings because the source data uses month names ("January", "December"), not proper dates.

---

## 4. WFP

WFP is linked to one AIP upload and scoped to one office. A WFP record is created per AIP activity, and each activity has multiple expenditure lines.

### `wfp_records`
One record = one WFP for one office under one AIP record.

> **Scoping rule:** An AIP record contains activities for all offices. A WFP is scoped to exactly one office — only that office's programs/projects/activities from the AIP are loaded. Multiple WFPs for the same AIP record are possible (one per office).

```sql
wfp_records
  id             INT PK IDENTITY
  aip_record_id  INT            NOT NULL FK -> aip_records
  office_id      INT            NOT NULL FK -> offices
  fiscal_year    INT            NOT NULL
  status         NVARCHAR(20)   NOT NULL DEFAULT 'Draft'
                                                -- Draft / Final
  created_by     INT            NOT NULL FK -> users
  created_at     DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  updated_at     DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  finalized_at   DATETIME2      NULL
  source_id      INT            NULL FK -> wfp_records  -- set when copied for amendment/supplemental
  UNIQUE (aip_record_id, office_id)   -- 1 WFP per office per AIP record
```

### `wfp_activities`
One row per AIP activity included in the WFP.

```sql
wfp_activities
  id                INT PK IDENTITY
  wfp_id            INT            NOT NULL FK -> wfp_records
  aip_activity_id   INT            NOT NULL FK -> aip_activities
  UNIQUE (wfp_id, aip_activity_id)
```

### `wfp_expenditure_lines`
One row per expenditure line inside a WFP activity.

```sql
wfp_expenditure_lines
  id                       INT PK IDENTITY
  wfp_activity_id          INT            NOT NULL FK -> wfp_activities
  expenditure_type         NVARCHAR(10)   NOT NULL   -- PS / MOOE / CO
  resources_needed         NVARCHAR(MAX)  NULL        -- optional; typically on first line of activity
  responsible_unit         NVARCHAR(200)  NULL
  success_indicator        NVARCHAR(MAX)  NULL
  means_of_verification    NVARCHAR(MAX)  NULL
  account_id               INT            NULL FK -> accounts
  account_number_snapshot  NVARCHAR(20)   NULL        -- snapshot of account_number at save time
  account_title_snapshot   NVARCHAR(300)  NULL        -- snapshot of account_title at save time
  total_appropriation      DECIMAL(18,2)  NULL
  apply_reserve            BIT            NOT NULL DEFAULT 0
  reserve_amount           DECIMAL(18,2)  NULL        -- auto-computed: 10% of total_appropriation
  net_appropriation        DECIMAL(18,2)  NULL        -- auto-computed: total - reserve
  q1                       DECIMAL(18,2)  NULL
  q2                       DECIMAL(18,2)  NULL
  q3                       DECIMAL(18,2)  NULL
  q4                       DECIMAL(18,2)  NULL
  quarterly_total          DECIMAL(18,2)  NULL        -- auto-computed: q1+q2+q3+q4
  funding_source_id        INT            NULL FK -> funding_sources
  funding_source_snapshot  NVARCHAR(20)   NULL        -- snapshot of funding_sources.code at save time
  sort_order               INT            NOT NULL DEFAULT 0
```

---

## 5. Audit / History

One generic table covers all entities.

### `audit_log`
```sql
audit_log
  id            BIGINT PK IDENTITY
  table_name    NVARCHAR(100)  NOT NULL   -- e.g. "aip_activities", "wfp_expenditure_lines"
  record_id     INT            NOT NULL
  action        NVARCHAR(10)   NOT NULL   -- CREATE / UPDATE / DELETE
  changed_by    INT            NOT NULL FK -> users
  changed_at    DATETIME2      NOT NULL DEFAULT GETUTCDATE()
  old_values    NVARCHAR(MAX)  NULL       -- JSON snapshot of changed fields before
  new_values    NVARCHAR(MAX)  NULL       -- JSON snapshot of changed fields after
```

---

## 6. Entity Relationships (Summary)

```
ldip_records
    └── (optional future link)
            ↓
aip_records ──────────────────── wfp_records
    └── aip_offices                   └── wfp_activities
            └── aip_programs                  └── wfp_expenditure_lines
                    └── aip_projects                  ├── accounts (FK)
                            └── aip_activities ←──────┤  (aip_activity_id)
                                    └── funding_sources (FK)
                                                       └── funding_sources (FK)

offices ──────────────────────── wfp_records (FK)
funding_sources ──────────────── aip_activities (FK) + wfp_expenditure_lines (FK)

audit_log ←── all tables (application-level logging)
```

---

## 7. Recommended Indexes

```sql
-- AIP hierarchy traversal
CREATE INDEX IX_aip_offices_aip_record_id     ON aip_offices           (aip_record_id)
CREATE INDEX IX_aip_offices_ref_code          ON aip_offices           (ref_code)
CREATE INDEX IX_aip_programs_office_id        ON aip_programs          (office_id)
CREATE INDEX IX_aip_programs_ref_code         ON aip_programs          (ref_code)
CREATE INDEX IX_aip_projects_program_id       ON aip_projects          (program_id)
CREATE INDEX IX_aip_projects_ref_code         ON aip_projects          (ref_code)
CREATE INDEX IX_aip_activities_project_id     ON aip_activities        (project_id)
CREATE INDEX IX_aip_activities_ref_code       ON aip_activities        (ref_code)

-- WFP traversal
CREATE INDEX IX_wfp_records_aip_record_id     ON wfp_records           (aip_record_id)
CREATE INDEX IX_wfp_records_office_id         ON wfp_records           (office_id)
CREATE INDEX IX_wfp_activities_wfp_id         ON wfp_activities        (wfp_id)
CREATE INDEX IX_wfp_activities_aip_act_id     ON wfp_activities        (aip_activity_id)
CREATE INDEX IX_wfp_exp_wfp_activity_id       ON wfp_expenditure_lines (wfp_activity_id)

-- Account config search
CREATE INDEX IX_accounts_number               ON accounts              (account_number)
CREATE INDEX IX_accounts_title                ON accounts              (account_title)

-- Audit log lookup
CREATE INDEX IX_audit_log_table_record        ON audit_log             (table_name, record_id)
CREATE INDEX IX_audit_log_changed_at          ON audit_log             (changed_at)

-- Source chain (amendment/supplemental tracing)
CREATE INDEX IX_ldip_source_id                ON ldip_records          (source_id)
CREATE INDEX IX_aip_source_id                 ON aip_records           (source_id)
CREATE INDEX IX_wfp_source_id                 ON wfp_records           (source_id)
```

---

## 8. Notes & Decisions

| # | Note |
|---|---|
| 1 | `aip_activities.total` is stored (not only computed) to match source file and for faster queries |
| 2 | **Config value snapshotting:** whenever a field's value is sourced from a config table and that value matters for record accuracy or reporting, store both the FK (for relational integrity) *and* a `_snapshot` column of the display value at save time. This ensures historical records remain accurate even if config is updated later. Applied to: `wfp_expenditure_lines` (account_number, account_title, funding_source code) and `aip_activities` (funding_source code). Apply the same pattern to any future feature that reads from config tables |
| 3 | Expenditure type (PS/MOOE/CO) is derived from account_number prefix at query time — not stored as a column |
| 4 | LDIP table is created but has no required relationships in batch 1 |
| 5 | `start_date` / `end_date` on activities are strings — convert to proper DATE only if a standard format is enforced during entry |
| 6 | `sort_order` on expenditure lines preserves the user-defined row order in the popup |
| 7 | Soft delete (`is_active`) on all config tables; no hard delete if record is referenced |
| 8 | `users` and `roles/permissions` tables already exist in the portal — no need to create |
| 9 | LDIP, AIP, and WFP all share the same status pattern: Draft / Final / Archived. Draft = editable; Final = locked (read-only); Archived = superseded/inactive. Once Final, edits require an admin unlock request to revert to Draft |
| 10 | Access control: PPDO users (`users.office_id` = null) manage all offices; non-PPDO office users (`users.office_id` set) access only their own office. Office encoders = Staff role; office viewers = Observer role. `users.division` becomes nullable in v1.1 (null for office users). Full model: `docs/v1.1/User_Roles_Permissions.md` |
| 11 | Amendment / Supplemental flow: system copies the Final record into a new Draft (`source_id` points to original); on finalize, new record becomes Final and original is Archived |
| 12 | AIP can be created via file upload (xlsm) or manual web UI entry — `entry_source` field tracks which |
| 13 | After file upload, a summary page is shown before confirming import: grouped by sector, hierarchical layout, with import counts |
| 14 | **WFP business rule:** `quarterly_total` (Q1+Q2+Q3+Q4) must not exceed `net_appropriation` per expenditure line. Enforced on both frontend (warning) and backend (API validation). Backend rule covered by unit tests |
| 15 | **CSV download column order** matches the seed CSV files exactly. This is the contract for round-trip CSV editing (download → edit offline → re-upload). Do not change column order without updating the import parser. See column order notes on each config table above |
