-- AIP activities + budget ceiling/allocation for a given division
-- Adjust @DivisionName and @FiscalYear below, then run in SSMS.
--
-- How the pieces connect (no direct FK from aip_activities to a division):
--   aip_activities -> aip_projects -> aip_programs -> aip_offices
--   aip_offices.ref_code ENDS WITH offices.office_ref_code   (office match, same rule as AllocationService)
--   program_divisions (office_ref_code, program_ref_code, division_id)  <- assigns a program to a division
--   budget_ceilings   is OFFICE + fiscal_year + funding_source scoped
--   division_allocations is DIVISION + fiscal_year + funding_source scoped

DECLARE @DivisionName NVARCHAR(200) = N'Admin';   -- matches divisions.name (adjust as needed)
DECLARE @FiscalYear   INT           = 2027;        -- adjust to the AIP fiscal year you're checking

-- ── Resolve the division and its owning office ──────────────────────────────
DECLARE @DivisionId INT, @OfficeId INT, @OfficeRefCode NVARCHAR(50);

SELECT
    @DivisionId    = d.id,
    @OfficeId      = d.office_id,
    @OfficeRefCode = o.office_ref_code
FROM divisions d
JOIN offices o ON o.id = d.office_id
WHERE d.name = @DivisionName;

IF @DivisionId IS NULL
BEGIN
    RAISERROR('No division found matching @DivisionName.', 16, 1);
    RETURN;
END

IF @OfficeRefCode IS NULL
BEGIN
    RAISERROR('Division''s office has no office_ref_code set — cannot match AIP hierarchy.', 16, 1);
    RETURN;
END

-- ── 1) Budget ceiling (office-level) for this FY, one row per funding source ─
SELECT
    'Ceiling' AS RowType,
    o.office_name,
    bc.fiscal_year,
    fs.code AS funding_source_code,
    fs.name AS funding_source_name,
    bc.amount
FROM budget_ceilings bc
JOIN offices o        ON o.id = bc.office_id
JOIN funding_sources fs ON fs.id = bc.funding_source_id
WHERE bc.office_id = @OfficeId
  AND bc.fiscal_year = @FiscalYear;

-- ── 2) Division allocation for this FY, one row per funding source ──────────
SELECT
    'Allocation' AS RowType,
    d.name AS division_name,
    da.fiscal_year,
    fs.code AS funding_source_code,
    fs.name AS funding_source_name,
    da.amount
FROM division_allocations da
JOIN divisions d        ON d.id = da.division_id
JOIN funding_sources fs ON fs.id = da.funding_source_id
WHERE da.division_id = @DivisionId
  AND da.fiscal_year = @FiscalYear;

-- ── 3) AIP activities under programs assigned to this division ──────────────
;WITH MatchedOffices AS (
    SELECT ao.id, ao.ref_code, ao.name, ao.sector
    FROM aip_offices ao
    JOIN aip_records ar ON ar.id = ao.aip_record_id
    WHERE ar.fiscal_year = @FiscalYear
      AND ar.status <> 'Archived'
      AND ao.ref_code LIKE '%' + @OfficeRefCode      -- suffix match, same rule as AllocationService
),
AssignedPrograms AS (
    SELECT ap.id, ap.office_id, ap.ref_code, ap.name, ap.function_band
    FROM aip_programs ap
    JOIN MatchedOffices mo ON mo.id = ap.office_id
    JOIN program_divisions pd
        ON pd.office_ref_code  = mo.ref_code
       AND pd.program_ref_code = ap.ref_code
       AND pd.division_id      = @DivisionId
)
SELECT
    mo.name  AS office_name,
    apg.name AS program_name,
    apr.ref_code AS project_ref_code,
    apr.name AS project_name,
    aa.ref_code AS activity_ref_code,
    aa.name  AS activity_name,
    aa.esre_code,
    aa.implementing_office,
    aa.start_date,
    aa.end_date,
    fs.code  AS funding_source_code,
    aa.ps,
    aa.mooe,
    aa.co,
    aa.total,
    aa.is_creation,
    aa.is_synthetic
FROM AssignedPrograms apg
JOIN MatchedOffices mo   ON mo.id = apg.office_id
JOIN aip_projects apr    ON apr.program_id = apg.id
JOIN aip_activities aa   ON aa.project_id = apr.id
LEFT JOIN funding_sources fs ON fs.id = aa.funding_source_id
ORDER BY apg.name, apr.ref_code, aa.ref_code;
