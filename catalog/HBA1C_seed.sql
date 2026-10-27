-- =====================================================================
-- LIS catalog seed: HbA1c (Glycated Hemoglobin)
-- Safe to re-run (idempotent / upserts)
-- =====================================================================

BEGIN;

-- Ensure Department
INSERT INTO departments (name_en, name_ar, letter)
VALUES ('Biochemistry', 'الكيمياء السريرية والمناعة', 'B')
ON CONFLICT (name_en) DO NOTHING;

-- Ensure Specimen Type
INSERT INTO specimen_types (name_en, name_ar, color)
VALUES ('EDTA Whole Blood', 'دم كامل مع مانع تجلط EDTA', 'Purple')
ON CONFLICT (name_en) DO NOTHING;

-- Insert / Upsert Test
INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_HBA1C', 'Glycated Hemoglobin', 'السكر التراكمي', d.id, s.id, 10000.00, 30, TRUE
FROM departments d, specimen_types s
WHERE d.name_en = 'Biochemistry' AND s.name_en = 'EDTA Whole Blood'
ON CONFLICT (test_code) DO UPDATE
   SET name_en = EXCLUDED.name_en,
       name_ar = EXCLUDED.name_ar,
       department_id = EXCLUDED.department_id,
       specimen_type_id = EXCLUDED.specimen_type_id;

-- Remove any rogue / duplicate components under HbA1c (preserving only C_HBA1C)
DELETE FROM reference_ranges WHERE component_id IN (
    SELECT id FROM components WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_HBA1C') AND component_code != 'C_HBA1C'
);
DELETE FROM instrument_tests WHERE component_id IN (
    SELECT id FROM components WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_HBA1C') AND component_code != 'C_HBA1C'
);
DELETE FROM components 
WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_HBA1C') 
  AND component_code != 'C_HBA1C';

-- Insert / Upsert Component
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT t.id, 'C_HBA1C', 'HbA1c', 'السكر التراكمي', 'HbA1c', '%', 1, 1
FROM tests t
WHERE t.test_code = 'T_HBA1C'
ON CONFLICT (component_code) DO UPDATE
   SET name_en = EXCLUDED.name_en,
       name_ar = EXCLUDED.name_ar,
       display_code = EXCLUDED.display_code,
       unit = EXCLUDED.unit,
       decimals = EXCLUDED.decimals;

-- Insert / Upsert Reference Range (ADA / WHO standard)
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_unit, low, high, note)
SELECT 'R_HBA1C_STD', c.id, 'B', 0, 'Years', NULL, 5.7, 'طبيعي: أقل من 5.7% | ما قبل السكري: 5.7 - 6.4% | سكري: 6.5% فأعلى'
FROM components c
WHERE c.component_code = 'C_HBA1C'
ON CONFLICT (range_code) DO UPDATE
   SET high = EXCLUDED.high,
       note = EXCLUDED.note;

-- Ensure both instruments exist: Dimension EXL 200 & Roche Cobas c311
INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, active)
VALUES 
    ('EXL200', 'Dimension EXL 200', 'ASTM', 'TCP', 'Bidirectional', TRUE),
    ('COBAS_C311', 'Roche Cobas c311', 'ASTM', 'TCP', 'Bidirectional', TRUE)
ON CONFLICT (code) DO UPDATE 
    SET name = EXCLUDED.name, 
        active = TRUE;

-- Remove inappropriate mappings for non-HbA1c instruments (e.g. Sysmex XN-350 or ADVIA 360)
DELETE FROM instrument_tests 
WHERE test_id = (SELECT id FROM tests WHERE test_code = 'T_HBA1C')
  AND instrument_id NOT IN (SELECT id FROM instruments WHERE code IN ('EXL200', 'COBAS_C311'));

-- 1. Link Dimension EXL 200 with code 'HBA1C'
INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code)
SELECT i.id, t.id, c.id, 'HBA1C'
FROM instruments i, tests t, components c
WHERE i.code = 'EXL200' AND t.test_code = 'T_HBA1C' AND c.component_code = 'C_HBA1C'
ON CONFLICT (instrument_id, component_id) DO UPDATE
   SET instrument_test_code = EXCLUDED.instrument_test_code;

-- 2. Link Roche Cobas c311 with code 'A1C'
INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code)
SELECT i.id, t.id, c.id, 'A1C'
FROM instruments i, tests t, components c
WHERE i.code = 'COBAS_C311' AND t.test_code = 'T_HBA1C' AND c.component_code = 'C_HBA1C'
ON CONFLICT (instrument_id, component_id) DO UPDATE
   SET instrument_test_code = EXCLUDED.instrument_test_code;

COMMIT;
