-- =====================================================================
-- LIS Catalog Seed: Clinical Chemistry & Siemens Dimension EXL 200 Setup
-- Includes:
--   1. Liver Function Tests (LFT)
--   2. Kidney / Renal Function Tests (RFT)
--   3. General Chemistry (Glucose, Calcium, Phosphorus)
--   4. Lipid Profile (Cholesterol, Triglycerides, HDL, LDL)
--   5. Electrolytes (Na, K, Cl)
--   6. Calculated Components (Globulin, Indirect Bilirubin, LDL)
--   7. Full Reference Ranges (Sex & Age Specific)
--   8. Mappings for Siemens Dimension EXL 200 (ASTM Bidirectional)
-- =====================================================================

BEGIN;

-- 1. Ensure Department
INSERT INTO departments (name_en, name_ar, letter)
VALUES ('Clinical Chemistry', 'الكيمياء السريرية', 'C')
ON CONFLICT (name_en) DO UPDATE SET name_ar = EXCLUDED.name_ar;

-- 2. Ensure Specimen Type: Serum
INSERT INTO specimen_types (name_en, name_ar, color)
VALUES ('Serum', 'مصل الدم', '#FBBF24')
ON CONFLICT (name_en) DO UPDATE SET name_ar = EXCLUDED.name_ar;

-- Helper variables via CTEs or direct subqueries
-- Dep ID:
-- (SELECT id FROM departments WHERE name_en IN ('Clinical Chemistry', 'Biochemistry') LIMIT 1)
-- Specimen ID:
-- (SELECT id FROM specimen_types WHERE name_en = 'Serum' LIMIT 1)

-- =====================================================================
-- 3. INSERT / UPSERT TESTS
-- =====================================================================

-- --- LIVER FUNCTION TESTS (LFT) ---
INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_ALT', 'Alanine Aminotransferase (ALT/SGPT)', 'انزيم الكبد (ALT / SGPT)', d.id, s.id, 5000.00, 10, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_AST', 'Aspartate Aminotransferase (AST/SGOT)', 'انزيم الكبد (AST / SGOT)', d.id, s.id, 5000.00, 11, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_ALP', 'Alkaline Phosphatase (ALP)', 'الفوسفاتاز القلوي (ALP)', d.id, s.id, 5000.00, 12, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_TBIL', 'Total Bilirubin', 'البيليروبين الكلي (أبو صفار)', d.id, s.id, 5000.00, 13, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_DBIL', 'Direct Bilirubin', 'البيليروبين المباشر', d.id, s.id, 5000.00, 14, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_IBIL', 'Indirect Bilirubin (Calculated)', 'البيليروبين غير المباشر (محسوب)', d.id, s.id, 0.00, 15, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_GGT', 'Gamma-Glutamyl Transferase (GGT)', 'انزيم غاما غلوتاميل (GGT)', d.id, s.id, 6000.00, 16, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

-- Update existing T_LFT_PROT
UPDATE tests
SET name_en = 'Total Protein & Albumin',
    name_ar = 'البروتين الكلي والألبومين',
    department_id = (SELECT id FROM departments WHERE name_en = 'Clinical Chemistry' LIMIT 1),
    specimen_type_id = (SELECT id FROM specimen_types WHERE name_en = 'Serum' LIMIT 1),
    price = 6000.00,
    sort_order = 17,
    active = TRUE
WHERE test_code = 'T_LFT_PROT';

-- --- KIDNEY / RENAL FUNCTION TESTS (RFT) ---
INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_UREA', 'Blood Urea', 'يوريا الدم', d.id, s.id, 5000.00, 20, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_CREA', 'Serum Creatinine', 'كرياتينين المصل', d.id, s.id, 5000.00, 21, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_URCA', 'Uric Acid', 'حمض اليوريك (داء النقرس)', d.id, s.id, 5000.00, 22, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_CA', 'Total Calcium', 'الكالسيوم الكلي', d.id, s.id, 5000.00, 23, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_PHOS', 'Inorganic Phosphorus', 'الفوسفور غير العضوي', d.id, s.id, 5000.00, 24, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

-- --- GENERAL CHEMISTRY & GLUCOSE ---
UPDATE tests
SET name_en = 'Fasting Blood Glucose',
    name_ar = 'سكر الدم الصائم',
    department_id = (SELECT id FROM departments WHERE name_en = 'Clinical Chemistry' LIMIT 1),
    specimen_type_id = (SELECT id FROM specimen_types WHERE name_en = 'Serum' LIMIT 1),
    price = 3000.00,
    sort_order = 5,
    active = TRUE
WHERE test_code = 'T_GLU';

-- --- LIPID PROFILE ---
INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_CHOL', 'Total Cholesterol', 'الكوليسترول الكلي', d.id, s.id, 5000.00, 30, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_TRIG', 'Triglycerides', 'الدهون الثلاثية', d.id, s.id, 5000.00, 31, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_HDL', 'HDL Cholesterol', 'الكوليسترول عالي الكثافة (النافع)', d.id, s.id, 5000.00, 32, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_LDL', 'LDL Cholesterol (Calculated)', 'الكوليسترول منخفض الكثافة (الضار)', d.id, s.id, 0.00, 33, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

-- --- ELECTROLYTES ---
INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_NA', 'Sodium (Na+)', 'الصوديوم', d.id, s.id, 5000.00, 40, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_K', 'Potassium (K+)', 'البوتاسيوم', d.id, s.id, 5000.00, 41, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;

INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 'T_CL', 'Chloride (Cl-)', 'الكلوريد', d.id, s.id, 5000.00, 42, TRUE
FROM departments d, specimen_types s WHERE d.name_en = 'Clinical Chemistry' AND s.name_en = 'Serum'
ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, department_id = EXCLUDED.department_id, specimen_type_id = EXCLUDED.specimen_type_id, active = TRUE;


-- =====================================================================
-- 4. INSERT / UPSERT COMPONENTS
-- =====================================================================

-- LFT Components
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_ALT', 'Alanine Aminotransferase', 'انزيم ناقلة الأمين الألانين', 'ALT', 'U/L', 0, 1 FROM tests WHERE test_code = 'T_ALT'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_AST', 'Aspartate Aminotransferase', 'انزيم ناقلة الأمين الأسبارتات', 'AST', 'U/L', 0, 1 FROM tests WHERE test_code = 'T_AST'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_ALP', 'Alkaline Phosphatase', 'الفوسفاتاز القلوي', 'ALP', 'U/L', 0, 1 FROM tests WHERE test_code = 'T_ALP'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_TBIL', 'Total Bilirubin', 'البيليروبين الكلي', 'T.Bilirubin', 'mg/dL', 2, 1 FROM tests WHERE test_code = 'T_TBIL'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_DBIL', 'Direct Bilirubin', 'البيليروبين المباشر', 'D.Bilirubin', 'mg/dL', 2, 1 FROM tests WHERE test_code = 'T_DBIL'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_IBIL', 'Indirect Bilirubin', 'البيليروبين غير المباشر', 'Ind.Bilirubin', 'mg/dL', 2, 1 FROM tests WHERE test_code = 'T_IBIL'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_GGT', 'Gamma-Glutamyl Transferase', 'انزيم غاما غلوتاميل', 'GGT', 'U/L', 0, 1 FROM tests WHERE test_code = 'T_GGT'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

-- Update Protein components under T_LFT_PROT
UPDATE components SET display_code = 'Total Protein', unit = 'g/dL', decimals = 1, sort_order = 1 WHERE component_code = 'C_TP_TEST';
UPDATE components SET display_code = 'Albumin', unit = 'g/dL', decimals = 1, sort_order = 2 WHERE component_code = 'C_ALB_TEST';
UPDATE components SET display_code = 'Globulin', unit = 'g/dL', decimals = 1, sort_order = 3 WHERE component_code = 'C_GLOB_TEST';

-- RFT Components
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_UREA', 'Blood Urea', 'يوريا الدم', 'Urea', 'mg/dL', 1, 1 FROM tests WHERE test_code = 'T_UREA'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_CREA', 'Serum Creatinine', 'كرياتينين المصل', 'Creatinine', 'mg/dL', 2, 1 FROM tests WHERE test_code = 'T_CREA'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_URCA', 'Uric Acid', 'حمض اليوريك', 'Uric Acid', 'mg/dL', 1, 1 FROM tests WHERE test_code = 'T_URCA'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_CA', 'Total Calcium', 'الكالسيوم الكلي', 'Calcium', 'mg/dL', 1, 1 FROM tests WHERE test_code = 'T_CA'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_PHOS', 'Inorganic Phosphorus', 'الفوسفور غير العضوي', 'Phosphorus', 'mg/dL', 1, 1 FROM tests WHERE test_code = 'T_PHOS'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

-- Glucose Component
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_GLU', 'Fasting Blood Glucose', 'سكر الدم الصائم', 'Glucose', 'mg/dL', 0, 1 FROM tests WHERE test_code = 'T_GLU'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

-- Lipid Components
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_CHOL', 'Total Cholesterol', 'الكوليسترول الكلي', 'Cholesterol', 'mg/dL', 0, 1 FROM tests WHERE test_code = 'T_CHOL'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_TRIG', 'Triglycerides', 'الدهون الثلاثية', 'Triglycerides', 'mg/dL', 0, 1 FROM tests WHERE test_code = 'T_TRIG'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_HDL', 'HDL Cholesterol', 'الكوليسترول عالي الكثافة (النافع)', 'HDL', 'mg/dL', 0, 1 FROM tests WHERE test_code = 'T_HDL'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_LDL', 'LDL Cholesterol', 'الكوليسترول منخفض الكثافة (الضار)', 'LDL', 'mg/dL', 0, 1 FROM tests WHERE test_code = 'T_LDL'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

-- Electrolyte Components
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_NA', 'Sodium (Na+)', 'الصوديوم', 'Na+', 'mmol/L', 1, 1 FROM tests WHERE test_code = 'T_NA'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_K', 'Potassium (K+)', 'البوتاسيوم', 'K+', 'mmol/L', 2, 1 FROM tests WHERE test_code = 'T_K'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;

INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT id, 'C_CL', 'Chloride (Cl-)', 'الكلوريد', 'Cl-', 'mmol/L', 1, 1 FROM tests WHERE test_code = 'T_CL'
ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en, name_ar = EXCLUDED.name_ar, display_code = EXCLUDED.display_code, unit = EXCLUDED.unit, decimals = EXCLUDED.decimals;


-- =====================================================================
-- 5. REFERENCE RANGES (Standard Clinical Guidelines: IFCC / WHO / ADA)
-- =====================================================================

-- ALT
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_ALT_M', id, 'M', 0, 120, 'Years', 0.0, 45.0, 500.0, 'المدى الطبيعي للذكور: حتى 45 U/L' FROM components WHERE component_code = 'C_ALT'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_ALT_F', id, 'F', 0, 120, 'Years', 0.0, 34.0, 500.0, 'المدى الطبيعي للإناث: حتى 34 U/L' FROM components WHERE component_code = 'C_ALT'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- AST
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_AST_M', id, 'M', 0, 120, 'Years', 0.0, 40.0, 500.0, 'المدى الطبيعي للذكور: حتى 40 U/L' FROM components WHERE component_code = 'C_AST'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_AST_F', id, 'F', 0, 120, 'Years', 0.0, 32.0, 500.0, 'المدى الطبيعي للإناث: حتى 32 U/L' FROM components WHERE component_code = 'C_AST'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- ALP
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_ALP_STD', id, 'B', 0, 120, 'Years', 40.0, 130.0, 'المدى الطبيعي للبالغين: 40 - 130 U/L' FROM components WHERE component_code = 'C_ALP'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- TBIL
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_TBIL_STD', id, 'B', 0, 120, 'Years', 0.20, 1.20, 15.0, 'المدى الطبيعي: 0.2 - 1.2 mg/dL' FROM components WHERE component_code = 'C_TBIL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- DBIL
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_DBIL_STD', id, 'B', 0, 120, 'Years', 0.00, 0.30, 'المدى الطبيعي: حتى 0.30 mg/dL' FROM components WHERE component_code = 'C_DBIL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- IBIL
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_IBIL_STD', id, 'B', 0, 120, 'Years', 0.20, 0.80, 'المدى الطبيعي: 0.20 - 0.80 mg/dL' FROM components WHERE component_code = 'C_IBIL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- GGT
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_GGT_M', id, 'M', 0, 120, 'Years', 10.0, 55.0, 'المدى الطبيعي للذكور: 10 - 55 U/L' FROM components WHERE component_code = 'C_GGT'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_GGT_F', id, 'F', 0, 120, 'Years', 7.0, 38.0, 'المدى الطبيعي للإناث: 7 - 38 U/L' FROM components WHERE component_code = 'C_GGT'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- Total Protein
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_TP_STD', id, 'B', 0, 120, 'Years', 6.4, 8.3, 'المدى الطبيعي للبروتين الكلي: 6.4 - 8.3 g/dL' FROM components WHERE component_code = 'C_TP_TEST'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- Albumin
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_ALB_STD', id, 'B', 0, 120, 'Years', 3.5, 5.2, 'المدى الطبيعي للألبومين: 3.5 - 5.2 g/dL' FROM components WHERE component_code = 'C_ALB_TEST'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- Urea
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_UREA_STD', id, 'B', 0, 120, 'Years', 15.0, 45.0, 100.0, 'المدى الطبيعي لليوريا: 15.0 - 45.0 mg/dL' FROM components WHERE component_code = 'C_UREA'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- Creatinine
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_CREA_M', id, 'M', 0, 120, 'Years', 0.70, 1.30, 4.0, 'المدى الطبيعي للذكور: 0.70 - 1.30 mg/dL' FROM components WHERE component_code = 'C_CREA'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_high, note)
SELECT 'R_CREA_F', id, 'F', 0, 120, 'Years', 0.50, 1.10, 4.0, 'المدى الطبيعي للإناث: 0.50 - 1.10 mg/dL' FROM components WHERE component_code = 'C_CREA'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- Uric Acid
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_URCA_M', id, 'M', 0, 120, 'Years', 3.5, 7.2, 'المدى الطبيعي للذكور: 3.5 - 7.2 mg/dL' FROM components WHERE component_code = 'C_URCA'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_URCA_F', id, 'F', 0, 120, 'Years', 2.6, 6.0, 'المدى الطبيعي للإناث: 2.6 - 6.0 mg/dL' FROM components WHERE component_code = 'C_URCA'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- Calcium
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_CA_STD', id, 'B', 0, 120, 'Years', 8.5, 10.5, 6.5, 13.0, 'المدى الطبيعي للكالسيوم: 8.5 - 10.5 mg/dL' FROM components WHERE component_code = 'C_CA'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_low = EXCLUDED.critical_low, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- Phosphorus
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_PHOS_STD', id, 'B', 0, 120, 'Years', 2.5, 4.5, 'المدى الطبيعي للفوسفور: 2.5 - 4.5 mg/dL' FROM components WHERE component_code = 'C_PHOS'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- Glucose
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_GLU_STD', id, 'B', 0, 120, 'Years', 70.0, 105.0, 45.0, 400.0, 'سكر الدم الصائم الطبيعي: 70 - 105 mg/dL' FROM components WHERE component_code = 'C_GLU'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_low = EXCLUDED.critical_low, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- Cholesterol
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_CHOL_STD', id, 'B', 0, 120, 'Years', 0.0, 200.0, 'المرغوب: أقل من 200 | الحدي: 200-239 | المرتفع: 240 فأعلى mg/dL' FROM components WHERE component_code = 'C_CHOL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- Triglycerides
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_TRIG_STD', id, 'B', 0, 120, 'Years', 0.0, 150.0, 'المرغوب: أقل من 150 | الحدي: 150-199 | المرتفع: 200 فأعلى mg/dL' FROM components WHERE component_code = 'C_TRIG'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- HDL
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_HDL_M', id, 'M', 0, 120, 'Years', 40.0, 60.0, 'الطبيعي للذكور: 40 - 60 mg/dL' FROM components WHERE component_code = 'C_HDL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_HDL_F', id, 'F', 0, 120, 'Years', 50.0, 60.0, 'الطبيعي للإناث: 50 - 60 mg/dL' FROM components WHERE component_code = 'C_HDL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- LDL
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_LDL_STD', id, 'B', 0, 120, 'Years', 0.0, 100.0, 'المرغوب للضار: أقل من 100 mg/dL' FROM components WHERE component_code = 'C_LDL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;

-- Sodium
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_NA_STD', id, 'B', 0, 120, 'Years', 135.0, 145.0, 120.0, 160.0, 'المدى الطبيعي للصوديوم: 135 - 145 mmol/L' FROM components WHERE component_code = 'C_NA'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_low = EXCLUDED.critical_low, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- Potassium
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_K_STD', id, 'B', 0, 120, 'Years', 3.50, 5.10, 2.80, 6.20, 'المدى الطبيعي للبوتاسيوم: 3.5 - 5.1 mmol/L' FROM components WHERE component_code = 'C_K'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, critical_low = EXCLUDED.critical_low, critical_high = EXCLUDED.critical_high, note = EXCLUDED.note;

-- Chloride
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, note)
SELECT 'R_CL_STD', id, 'B', 0, 120, 'Years', 98.0, 107.0, 'المدى الطبيعي للكلوريد: 98 - 107 mmol/L' FROM components WHERE component_code = 'C_CL'
ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high, note = EXCLUDED.note;


-- =====================================================================
-- 6. CALCULATED COMPONENTS FORMULAS
-- =====================================================================

-- Globulin = Total Protein - Albumin
INSERT INTO calculated_components (component_id, formula, input_component_ids, updated_at)
SELECT id, '[C_TP_TEST] - [C_ALB_TEST]', '["C_TP_TEST", "C_ALB_TEST"]'::jsonb, clock_timestamp()
FROM components WHERE component_code = 'C_GLOB_TEST'
ON CONFLICT (component_id) DO UPDATE SET formula = EXCLUDED.formula, input_component_ids = EXCLUDED.input_component_ids, updated_at = clock_timestamp();

-- Indirect Bilirubin = Total Bilirubin - Direct Bilirubin
INSERT INTO calculated_components (component_id, formula, input_component_ids, updated_at)
SELECT id, '[C_TBIL] - [C_DBIL]', '["C_TBIL", "C_DBIL"]'::jsonb, clock_timestamp()
FROM components WHERE component_code = 'C_IBIL'
ON CONFLICT (component_id) DO UPDATE SET formula = EXCLUDED.formula, input_component_ids = EXCLUDED.input_component_ids, updated_at = clock_timestamp();

-- LDL = Cholesterol - HDL - (Triglycerides / 5)
INSERT INTO calculated_components (component_id, formula, input_component_ids, updated_at)
SELECT id, '[C_CHOL] - [C_HDL] - ([C_TRIG] / 5)', '["C_CHOL", "C_HDL", "C_TRIG"]'::jsonb, clock_timestamp()
FROM components WHERE component_code = 'C_LDL'
ON CONFLICT (component_id) DO UPDATE SET formula = EXCLUDED.formula, input_component_ids = EXCLUDED.input_component_ids, updated_at = clock_timestamp();


-- =====================================================================
-- 7. CONFIGURE INSTRUMENT: Siemens Dimension EXL 200 (ASTM, Serial RS232, Bidirectional)
-- =====================================================================

INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, com_port, baud_rate, data_bits, parity, stop_bits, active)
VALUES ('EXL200', 'Dimension EXL 200', 'ASTM', 'RS232', 'Bidirectional', 'COM1', 9600, 8, 'None', 'One', TRUE)
ON CONFLICT (code) DO UPDATE
   SET protocol = 'ASTM',
       connection_type = 'RS232',
       operating_mode = 'Bidirectional',
       baud_rate = 9600,
       data_bits = 8,
       parity = 'None',
       stop_bits = 'One',
       active = TRUE;


-- =====================================================================
-- 8. MAP TESTS TO SIEMENS DIMENSION EXL 200 (ASTM Flex Cartridge Codes)
-- =====================================================================

-- Macro for mapping:
-- (EXL200, test_code, component_code, instrument_test_code)

INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code)
SELECT i.id, t.id, c.id, m.code
FROM instruments i
CROSS JOIN (VALUES
    ('T_ALT',      'C_ALT',     'ALT'),
    ('T_AST',      'C_AST',     'AST'),
    ('T_ALP',      'C_ALP',     'ALP'),
    ('T_TBIL',     'C_TBIL',    'TBIL'),
    ('T_DBIL',     'C_DBIL',    'DBIL'),
    ('T_GGT',      'C_GGT',     'GGT'),
    ('T_LFT_PROT', 'C_TP_TEST', 'TP'),
    ('T_LFT_PROT', 'C_ALB_TEST','ALB'),
    ('T_UREA',     'C_UREA',    'BUN'),
    ('T_CREA',     'C_CREA',    'CREA'),
    ('T_URCA',     'C_URCA',    'URCA'),
    ('T_CA',       'C_CA',      'CA'),
    ('T_PHOS',     'C_PHOS',    'PHOS'),
    ('T_GLU',      'C_GLU',     'GLU'),
    ('T_CHOL',     'C_CHOL',    'CHOL'),
    ('T_TRIG',     'C_TRIG',    'TRIG'),
    ('T_HDL',      'C_HDL',     'HDL'),
    ('T_NA',       'C_NA',      'NA'),
    ('T_K',        'C_K',       'K'),
    ('T_CL',       'C_CL',      'CL'),
    ('T_HBA1C',    'C_HBA1C',   'HBA1C')
) AS m(test_code, component_code, code)
JOIN tests t ON t.test_code = m.test_code
JOIN components c ON c.component_code = m.component_code
WHERE i.code = 'EXL200'
ON CONFLICT (instrument_id, component_id) DO UPDATE
   SET instrument_test_code = EXCLUDED.instrument_test_code,
       test_id = EXCLUDED.test_id;

COMMIT;
