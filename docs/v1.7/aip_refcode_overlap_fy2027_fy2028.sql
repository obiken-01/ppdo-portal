-- Ref-code overlap check: FY2027 vs FY2028 AIP data.
-- Context: program_divisions (the PPA→Division assignment table) is keyed only by
-- (office_ref_code, program_ref_code) — no fiscal_year column. AipXlsmParser reads
-- ref codes verbatim from column A of the uploaded file (a standardized national
-- classification code), so the same office/program is likely to carry the SAME
-- ref code across fiscal years. That means assigning a division while testing
-- FY2028 could silently mutate what FY2027's Allocation page shows as assigned.
-- This script measures how much actual overlap exists before testing FY2028.
--
-- Adjust @FYOld / @FYNew below, then run in SSMS.

DECLARE @FYOld INT = 2027;
DECLARE @FYNew INT = 2028;

-- ── 1) Program-level ref-code overlap between the two years' active AIPs ────
;WITH OldPrograms AS (
    SELECT ao.ref_code AS office_ref_code, ap.ref_code AS program_ref_code,
           ao.name AS office_name, ap.name AS program_name
    FROM aip_programs ap
    JOIN aip_offices ao ON ao.id = ap.office_id
    JOIN aip_records ar ON ar.id = ao.aip_record_id
    WHERE ar.fiscal_year = @FYOld AND ar.status <> 'Archived'
),
NewPrograms AS (
    SELECT ao.ref_code AS office_ref_code, ap.ref_code AS program_ref_code,
           ao.name AS office_name, ap.name AS program_name
    FROM aip_programs ap
    JOIN aip_offices ao ON ao.id = ap.office_id
    JOIN aip_records ar ON ar.id = ao.aip_record_id
    WHERE ar.fiscal_year = @FYNew AND ar.status <> 'Archived'
)
SELECT
    o.office_ref_code,
    o.program_ref_code,
    o.office_name  AS office_name_fy_old,
    n.office_name  AS office_name_fy_new,
    o.program_name AS program_name_fy_old,
    n.program_name AS program_name_fy_new,
    CASE WHEN o.program_name = n.program_name THEN 'Same name' ELSE 'DIFFERENT NAME — same ref code, different program!' END AS name_check
FROM OldPrograms o
JOIN NewPrograms n
    ON n.office_ref_code = o.office_ref_code
   AND n.program_ref_code = o.program_ref_code
ORDER BY o.office_ref_code, o.program_ref_code;

-- ── 2) Summary counts ────────────────────────────────────────────────────────
;WITH OldPrograms AS (
    SELECT ao.ref_code AS office_ref_code, ap.ref_code AS program_ref_code
    FROM aip_programs ap
    JOIN aip_offices ao ON ao.id = ap.office_id
    JOIN aip_records ar ON ar.id = ao.aip_record_id
    WHERE ar.fiscal_year = @FYOld AND ar.status <> 'Archived'
),
NewPrograms AS (
    SELECT ao.ref_code AS office_ref_code, ap.ref_code AS program_ref_code
    FROM aip_programs ap
    JOIN aip_offices ao ON ao.id = ap.office_id
    JOIN aip_records ar ON ar.id = ao.aip_record_id
    WHERE ar.fiscal_year = @FYNew AND ar.status <> 'Archived'
)
SELECT
    (SELECT COUNT(*) FROM OldPrograms) AS total_programs_fy_old,
    (SELECT COUNT(*) FROM NewPrograms) AS total_programs_fy_new,
    (SELECT COUNT(*) FROM OldPrograms o JOIN NewPrograms n
        ON n.office_ref_code = o.office_ref_code AND n.program_ref_code = o.program_ref_code) AS overlapping_ref_codes;

-- ── 3) Of the overlapping ref codes, which already have a division assigned ──
-- for FY_old? These are the rows at real risk of being changed by FY_new testing.
;WITH OldPrograms AS (
    SELECT ao.ref_code AS office_ref_code, ap.ref_code AS program_ref_code, ap.name AS program_name
    FROM aip_programs ap
    JOIN aip_offices ao ON ao.id = ap.office_id
    JOIN aip_records ar ON ar.id = ao.aip_record_id
    WHERE ar.fiscal_year = @FYOld AND ar.status <> 'Archived'
),
NewPrograms AS (
    SELECT ao.ref_code AS office_ref_code, ap.ref_code AS program_ref_code
    FROM aip_programs ap
    JOIN aip_offices ao ON ao.id = ap.office_id
    JOIN aip_records ar ON ar.id = ao.aip_record_id
    WHERE ar.fiscal_year = @FYNew AND ar.status <> 'Archived'
)
SELECT
    o.office_ref_code,
    o.program_ref_code,
    o.program_name,
    d.name AS currently_assigned_division
FROM OldPrograms o
JOIN NewPrograms n
    ON n.office_ref_code = o.office_ref_code AND n.program_ref_code = o.program_ref_code
JOIN program_divisions pd
    ON pd.office_ref_code = o.office_ref_code AND pd.program_ref_code = o.program_ref_code
JOIN divisions d ON d.id = pd.division_id
ORDER BY o.office_ref_code, o.program_ref_code;
