-- =====================================================================
-- LIS catalog seed: CBC (Complete Blood Count) on Siemens ADVIA 360
-- Built from the lab's own files: tests.csv, components.csv,
-- reference_ranges.csv + the ADVIA 360 instrument mapping.
-- PostgreSQL. Safe to re-run (upserts).
-- CBC price: 1000 IQD (Iraqi dinar). It is only the starting value;
-- the price is edited later from the app's catalog screen.
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- Catalog tables (names as in the project plan). If the project already
-- created them through migrations, skip this section.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS departments (
    id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name_en   TEXT NOT NULL UNIQUE,
    name_ar   TEXT,
    letter    CHAR(1)
);

CREATE TABLE IF NOT EXISTS specimen_types (
    id        BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name_en   TEXT NOT NULL UNIQUE,
    name_ar   TEXT,
    color     TEXT
);

CREATE TABLE IF NOT EXISTS tests (
    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    test_code        TEXT NOT NULL UNIQUE,          -- Test_ID in tests.csv
    name_en          TEXT NOT NULL,
    name_ar          TEXT,
    department_id    BIGINT REFERENCES departments(id),
    specimen_type_id BIGINT REFERENCES specimen_types(id),
    is_profile       BOOLEAN NOT NULL DEFAULT FALSE,
    price            NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (price >= 0),  -- dinars
    sort_order       INT NOT NULL DEFAULT 0,
    active           BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS components (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    test_id         BIGINT NOT NULL REFERENCES tests(id),
    component_code  TEXT NOT NULL UNIQUE,           -- Component_ID in components.csv
    name_en         TEXT NOT NULL,                  -- Component_Name
    name_ar         TEXT,
    display_code    TEXT,                           -- Code (short label shown on the report)
    unit            TEXT,
    decimals        SMALLINT,                       -- Decimal_Places
    result_type     TEXT NOT NULL DEFAULT 'numeric'
                    CHECK (result_type IN ('numeric','text','option')),
    notes           TEXT,
    sort_order      INT NOT NULL DEFAULT 0
);

-- One table for all tests: one row per (component, sex, age band).
-- Ages are entered in natural units (as in the CSV) and converted to days
-- automatically; the lookup query compares the patient's age in days.
CREATE TABLE IF NOT EXISTS reference_ranges (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    range_code     TEXT UNIQUE,                     -- Range_ID in reference_ranges.csv
    component_id   BIGINT NOT NULL REFERENCES components(id),
    sex            CHAR(1) NOT NULL CHECK (sex IN ('M','F','B')),   -- B = both (CSV "All")
    age_min        NUMERIC(6,2) NOT NULL DEFAULT 0,
    age_max        NUMERIC(6,2),                    -- NULL = no upper limit; exclusive
    age_unit       TEXT NOT NULL DEFAULT 'Years' CHECK (age_unit IN ('Days','Months','Years')),
    age_from_days  INT GENERATED ALWAYS AS (
                       floor(age_min * CASE age_unit WHEN 'Days' THEN 1 WHEN 'Months' THEN 30.4375 ELSE 365.25 END)::int
                   ) STORED,
    age_to_days    INT GENERATED ALWAYS AS (
                       floor(age_max * CASE age_unit WHEN 'Days' THEN 1 WHEN 'Months' THEN 30.4375 ELSE 365.25 END)::int
                   ) STORED,
    low            NUMERIC,                         -- Min_Value
    high           NUMERIC,                         -- Max_Value
    critical_low   NUMERIC,
    critical_high  NUMERIC,
    note           TEXT
);

CREATE TABLE IF NOT EXISTS instruments (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code            TEXT NOT NULL UNIQUE,
    name            TEXT NOT NULL,
    protocol        TEXT CHECK (protocol IN ('ASTM','HL7')),
    connection_type TEXT CHECK (connection_type IN ('TCP','RS232')),
    operating_mode  TEXT CHECK (operating_mode IN ('Bidirectional','ResultsOnly')),
    active          BOOLEAN NOT NULL DEFAULT FALSE
);

CREATE TABLE IF NOT EXISTS instrument_tests (
    id                   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    instrument_id        BIGINT NOT NULL REFERENCES instruments(id),
    test_id              BIGINT NOT NULL REFERENCES tests(id),
    component_id         BIGINT NOT NULL REFERENCES components(id),
    instrument_test_code TEXT NOT NULL,             -- code the analyzer uses (e.g. LYM%)
    reportable_low       NUMERIC,                   -- measuring range of THIS instrument
    reportable_high      NUMERIC,
    UNIQUE (instrument_id, component_id)
);

-- ---------------------------------------------------------------------
-- Department, specimen type, instrument
-- ---------------------------------------------------------------------
INSERT INTO departments (name_en, name_ar, letter)
VALUES ('Hematology', 'امراض الدم', 'H')
ON CONFLICT (name_en) DO NOTHING;

INSERT INTO specimen_types (name_en, name_ar, color)
VALUES ('EDTA Whole Blood', NULL, 'Purple')
ON CONFLICT (name_en) DO NOTHING;

-- Connection settings are filled later in the Middleware settings screen.
INSERT INTO instruments (code, name, active)
VALUES ('ADVIA360', 'Siemens ADVIA 360', FALSE)
ON CONFLICT (code) DO NOTHING;

-- ---------------------------------------------------------------------
-- The orderable test: CBC, price 1000 IQD (changeable from the app)
-- ---------------------------------------------------------------------
INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order)
SELECT 'T_CBC_360', 'Complete Blood Count', 'صورة الدم الكاملة (CBC)', d.id, s.id, 1000.00, 20
FROM departments d, specimen_types s
WHERE d.name_en = 'Hematology' AND s.name_en = 'EDTA Whole Blood'
ON CONFLICT (test_code) DO UPDATE
   SET name_en = EXCLUDED.name_en,
       name_ar = EXCLUDED.name_ar,
       department_id = EXCLUDED.department_id,
       specimen_type_id = EXCLUDED.specimen_type_id;
-- (price is NOT overwritten on re-run, so a price changed in the app is kept)

-- ---------------------------------------------------------------------
-- Components: 22 parameters
-- (component_code, name_en, display_code, unit, decimals, sort_order)
-- ---------------------------------------------------------------------
INSERT INTO components (test_id, component_code, name_en, display_code, unit, decimals, sort_order)
SELECT t.id, v.code, v.name_en, v.display_code, v.unit, v.decimals, v.sort_order
FROM tests t,
(VALUES
    ('C_WBC', 'White Blood Cells', 'WBC', '10^9/L', 1, 1),
    ('C_LYM_abs', 'Lymphocytes Absolute', 'LYM#', '10^9/L', 1, 2),
    ('C_MID_abs', 'Mid Cells Absolute', 'MID#', '10^9/L', 1, 3),
    ('C_GRA_abs', 'Granulocytes Absolute', 'GRA#', '10^9/L', 1, 4),
    ('C_LYM_pct', 'Lymphocytes Percentage', 'LYM%', '%', 1, 5),
    ('C_MID_pct', 'Mid Cells Percentage', 'MID%', '%', 1, 6),
    ('C_GRA_pct', 'Granulocytes Percentage', 'GRA%', '%', 1, 7),
    ('C_RBC', 'Red Blood Cells', 'RBC', '10^12/L', 2, 8),
    ('C_HGB', 'Hemoglobin', 'HGB', 'g/dL', 1, 9),
    ('C_HCT', 'Hematocrit', 'HCT', '%', 1, 10),
    ('C_MCV', 'Mean Corpuscular Volume', 'MCV', 'fL', 1, 11),
    ('C_MCH', 'Mean Corpuscular Hemoglobin', 'MCH', 'pg', 1, 12),
    ('C_MCHC', 'Mean Corp. HGB Concentration', 'MCHC', 'g/dL', 1, 13),
    ('C_RDW_CV', 'Red Cell Dist. Width CV', 'RDW-CV', '%', 1, 14),
    ('C_RDW_SD', 'Red Cell Dist. Width SD', 'RDW-SD', 'fL', 1, 15),
    ('C_PLT', 'Platelets', 'PLT', '10^9/L', 0, 16),
    ('C_MPV', 'Mean Platelet Volume', 'MPV', 'fL', 1, 17),
    ('C_PCT', 'Plateletcrit', 'PCT', '%', 3, 18),
    ('C_PDW_CV', 'Platelet Dist. Width CV', 'PDW-CV', '%', 1, 19),
    ('C_PDW_SD', 'Platelet Dist. Width SD', 'PDW-SD', 'fL', 1, 20),
    ('C_PLCR', 'Platelet Large Cell Ratio', 'P-LCR', '%', 1, 21),
    ('C_PLCC', 'Platelet Large Cell Count', 'P-LCC', '10^9/L', 0, 22)
) AS v(code, name_en, display_code, unit, decimals, sort_order)
WHERE t.test_code = 'T_CBC_360'
ON CONFLICT (component_code) DO UPDATE
   SET name_en = EXCLUDED.name_en,
       display_code = EXCLUDED.display_code,
       unit = EXCLUDED.unit,
       decimals = EXCLUDED.decimals,
       sort_order = EXCLUDED.sort_order;

-- ---------------------------------------------------------------------
-- Reference ranges: 22 rows (adults 18 to 100 years).
-- (range_code, component_code, sex, age_min, age_max, age_unit, low, high)
-- Components with no row here (PDW-SD, P-LCR, P-LCC) get no H/L flag.
-- ---------------------------------------------------------------------
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high)
SELECT v.range_code, c.id, v.sex, v.age_min, v.age_max, v.age_unit, v.low, v.high
FROM (VALUES
    ('R01', 'C_WBC', 'B', 18, 100, 'Years', 4.0, 10.0),
    ('R02', 'C_LYM_abs', 'B', 18, 100, 'Years', 1.0, 3.0),
    ('R03', 'C_MID_abs', 'B', 18, 100, 'Years', 0.1, 1.5),
    ('R04', 'C_GRA_abs', 'B', 18, 100, 'Years', 2.0, 7.0),
    ('R05', 'C_LYM_pct', 'B', 18, 100, 'Years', 20.0, 40.0),
    ('R06', 'C_MID_pct', 'B', 18, 100, 'Years', 1.0, 15.0),
    ('R07', 'C_GRA_pct', 'B', 18, 100, 'Years', 50.0, 70.0),
    ('R08', 'C_RBC', 'M', 18, 100, 'Years', 4.5, 5.5),
    ('R09', 'C_RBC', 'F', 18, 100, 'Years', 4.0, 5.0),
    ('R10', 'C_HGB', 'M', 18, 100, 'Years', 13.0, 17.0),
    ('R11', 'C_HGB', 'F', 18, 100, 'Years', 12.0, 15.0),
    ('R12', 'C_HCT', 'M', 18, 100, 'Years', 40.0, 50.0),
    ('R13', 'C_HCT', 'F', 18, 100, 'Years', 36.0, 46.0),
    ('R14', 'C_MCV', 'B', 18, 100, 'Years', 80.0, 100.0),
    ('R15', 'C_MCH', 'B', 18, 100, 'Years', 27.0, 31.0),
    ('R16', 'C_MCHC', 'B', 18, 100, 'Years', 32.0, 36.0),
    ('R17', 'C_RDW_CV', 'B', 18, 100, 'Years', 11.5, 14.5),
    ('R18', 'C_RDW_SD', 'B', 18, 100, 'Years', 35.0, 56.0),
    ('R19', 'C_PLT', 'B', 18, 100, 'Years', 150.0, 400.0),
    ('R20', 'C_MPV', 'B', 18, 100, 'Years', 7.4, 10.4),
    ('R21', 'C_PCT', 'B', 18, 100, 'Years', 0.1, 0.28),
    ('R22', 'C_PDW_CV', 'B', 18, 100, 'Years', 15.0, 17.0)
) AS v(range_code, component_code, sex, age_min, age_max, age_unit, low, high)
JOIN components c ON c.component_code = v.component_code
ON CONFLICT (range_code) DO UPDATE
   SET component_id = EXCLUDED.component_id,
       sex = EXCLUDED.sex,
       age_min = EXCLUDED.age_min,
       age_max = EXCLUDED.age_max,
       age_unit = EXCLUDED.age_unit,
       low = EXCLUDED.low,
       high = EXCLUDED.high;

-- ---------------------------------------------------------------------
-- Instrument mapping: component -> code used by the ADVIA 360
-- (instrument_test_code, reportable_low, reportable_high)
-- ---------------------------------------------------------------------
INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code,
                              reportable_low, reportable_high)
SELECT i.id, t.id, c.id, v.analyzer_code, v.rlow, v.rhigh
FROM instruments i
JOIN tests t ON t.test_code = 'T_CBC_360'
JOIN (VALUES
    ('C_WBC', 'WBC', 0.95, 83.45),
    ('C_LYM_abs', 'LYM', NULL, NULL),
    ('C_LYM_pct', 'LYM%', NULL, NULL),
    ('C_MID_abs', 'MID', NULL, NULL),
    ('C_MID_pct', 'MID%', NULL, NULL),
    ('C_GRA_abs', 'GRA', NULL, NULL),
    ('C_GRA_pct', 'GRA%', NULL, NULL),
    ('C_RBC', 'RBC', 0.44, 7.74),
    ('C_HGB', 'HGB', 1.4, 23.7),
    ('C_HCT', 'HCT', NULL, NULL),
    ('C_MCV', 'MCV', NULL, NULL),
    ('C_MCH', 'MCH', NULL, NULL),
    ('C_MCHC', 'MCHC', NULL, NULL),
    ('C_RDW_CV', 'RDW-CV', NULL, NULL),
    ('C_RDW_SD', 'RDW-SD', NULL, NULL),
    ('C_PLT', 'PLT', 11, 975),
    ('C_MPV', 'MPV', NULL, NULL),
    ('C_PCT', 'PCT', NULL, NULL),
    ('C_PDW_CV', 'PDW-CV', NULL, NULL),
    ('C_PDW_SD', 'PDW-SD', NULL, NULL),
    ('C_PLCR', 'P-LCR', NULL, NULL),
    ('C_PLCC', 'P-LCC', NULL, NULL)
) AS v(component_code, analyzer_code, rlow, rhigh) ON TRUE
JOIN components c ON c.component_code = v.component_code
WHERE i.code = 'ADVIA360'
ON CONFLICT (instrument_id, component_id) DO UPDATE
   SET instrument_test_code = EXCLUDED.instrument_test_code,
       reportable_low = EXCLUDED.reportable_low,
       reportable_high = EXCLUDED.reportable_high;

COMMIT;

-- Quick checks (expected counts):
--   SELECT test_code, name_en, price FROM tests;                 -- 1 row, price 1000.00
--   SELECT count(*) FROM components;                             -- 22
--   SELECT count(*) FROM reference_ranges;                       -- 22
--   SELECT count(*) FROM instrument_tests;                       -- 22
--   SELECT range_code, age_from_days, age_to_days FROM reference_ranges LIMIT 3;  -- 6574, 36525
