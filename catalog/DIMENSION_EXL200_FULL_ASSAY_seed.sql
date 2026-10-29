-- =====================================================================
-- SIEMENS DIMENSION EXL 200 - COMPLETE ASSAY MENU & LIS CODE MAPPINGS
-- Comprehensive Catalog Seed:
--   1. General Chemistries (Metabolic, Liver, Renal)
--   2. Electrolytes & IMT (Na, K, Cl, ECO2)
--   3. Lipid Profile (CHOL, HDL, LDL, TG)
--   4. Cardiac Markers (hsCRP, CK, CKMB, TNI, MYO, PBNP)
--   5. Endocrinology, Thyroid, Tumor Markers (CEA, FT3, FT4, HCG, TPSA, T3, T4, TSH)
--   6. Specific Proteins & Diabetes (RCRP, A1C, MALB, PCT, TRFR)
--   7. Therapeutic Drug Monitoring - TDM (13 Drugs)
--   8. Toxicology / Drugs of Abuse - DAT (12 Drugs)
--   9. Immunosuppressant Drugs (CSA, MPA, TACR)
-- =====================================================================

BEGIN;

-- ---------------------------------------------------------------------
-- 1. Ensure Departments
-- ---------------------------------------------------------------------
INSERT INTO departments (name_en, name_ar, letter) VALUES
    ('Clinical Chemistry',    'الكيمياء السريرية',          'C'),
    ('Hormones',              'الهرمونات ودلالات الأورام',   'H'),
    ('Immunology & Serology', 'المناعة والمصول',             'I'),
    ('Toxicology & TDM',      'السموم ومراقبة الأدوية العلاجية', 'T')
ON CONFLICT (name_en) DO UPDATE SET name_ar = EXCLUDED.name_ar;

-- ---------------------------------------------------------------------
-- 2. Ensure Specimen Types
-- ---------------------------------------------------------------------
INSERT INTO specimen_types (name_en, name_ar, color) VALUES
    ('Serum',            'مصل الدم',                 '#FBBF24'),
    ('EDTA Whole Blood', 'دم كامل (EDTA)',           '#A855F7'),
    ('Urine',            'عينة ادرار',               '#EAB308'),
    ('Heparin Plasma',   'بلازما هيبارين',           '#22C55E')
ON CONFLICT (name_en) DO UPDATE SET name_ar = EXCLUDED.name_ar;

-- ---------------------------------------------------------------------
-- 3. Upsert Tests & Components & Reference Ranges & EXL200 Mappings
-- ---------------------------------------------------------------------

-- Macro helper via temporary table or repeatable function
CREATE TEMP TABLE tmp_assay_def (
    test_code         text PRIMARY KEY,
    comp_code         text NOT NULL,
    lis_code          text NOT NULL,
    name_en           text NOT NULL,
    name_ar           text NOT NULL,
    dept_name         text NOT NULL,
    spec_type         text NOT NULL,
    unit              text NOT NULL,
    decimals          smallint NOT NULL,
    price             numeric(12,2) NOT NULL,
    sort_order        integer NOT NULL,
    ref_low           numeric,
    ref_high          numeric,
    ref_crit_low      numeric,
    ref_crit_high     numeric,
    ref_sex           character(1) DEFAULT 'B',
    ref_note          text
);

-- =====================================================================
-- POPULATE tmp_assay_def
-- =====================================================================

-- 1. GENERAL CHEMISTRIES
INSERT INTO tmp_assay_def VALUES
('T_ALB',   'C_ALB',   'ALB',  'Albumin',                              'الألبومين',                      'Clinical Chemistry', 'Serum', 'g/dL',   1, 4000.00,  10, 3.5,   5.2,   NULL, NULL, 'B', 'المدى الطبيعي: 3.5 - 5.2 g/dL'),
('T_ALT',   'C_ALT',   'ALT',  'Alanine Aminotransferase',             'انزيم الكبد (ALT/SGPT)',         'Clinical Chemistry', 'Serum', 'U/L',    0, 5000.00,  11, 0.0,   45.0,  NULL, 500.0,'B', 'المدى الطبيعي: حتى 45 U/L'),
('T_ALKP',  'C_ALKP',  'ALKP', 'Alkaline Phosphatase',                 'الفوسفاتاز القلوي (ALP)',        'Clinical Chemistry', 'Serum', 'U/L',    0, 5000.00,  12, 40.0,  130.0, NULL, NULL, 'B', 'المدى الطبيعي للبالغين: 40 - 130 U/L'),
('T_AMON',  'C_AMON',  'AMON', 'Ammonia',                              'الأمونيا',                       'Clinical Chemistry', 'Serum', 'umol/L', 0, 10000.00, 13, 11.0,  35.0,  NULL, 100.0,'B', 'المدى الطبيعي: 11 - 35 umol/L'),
('T_AMY',   'C_AMY',   'AMY',  'Amylase',                              'انزيم الأميليز',                 'Clinical Chemistry', 'Serum', 'U/L',    0, 6000.00,  14, 28.0,  100.0, NULL, 300.0,'B', 'المدى الطبيعي: 28 - 100 U/L'),
('T_AST',   'C_AST',   'AST',  'Aspartate Aminotransferase',           'انزيم الكبد (AST/SGOT)',         'Clinical Chemistry', 'Serum', 'U/L',    0, 5000.00,  15, 0.0,   40.0,  NULL, 500.0,'B', 'المدى الطبيعي: حتى 40 U/L'),
('T_DBIL',  'C_DBIL',  'DBIL', 'Direct Bilirubin',                     'البيليروبين المباشر',            'Clinical Chemistry', 'Serum', 'mg/dL',  2, 5000.00,  16, 0.00,  0.30,  NULL, NULL, 'B', 'المدى الطبيعي: حتى 0.30 mg/dL'),
('T_TBIL',  'C_TBIL',  'TBIL', 'Total Bilirubin',                      'البيليروبين الكلي',              'Clinical Chemistry', 'Serum', 'mg/dL',  2, 5000.00,  17, 0.20,  1.20,  NULL, 15.0, 'B', 'المدى الطبيعي: 0.20 - 1.20 mg/dL'),
('T_CA',    'C_CA',    'CA',   'Calcium (Total)',                      'الكالسيوم الكلي',                'Clinical Chemistry', 'Serum', 'mg/dL',  1, 5000.00,  18, 8.5,   10.5,  6.5,  13.0, 'B', 'المدى الطبيعي: 8.5 - 10.5 mg/dL'),
('T_CREA',  'C_CREA',  'CREA', 'Creatinine (Jaffe)',                   'كرياتينين المصل (Jaffe)',        'Clinical Chemistry', 'Serum', 'mg/dL',  2, 5000.00,  19, 0.60,  1.20,  NULL, 4.0,  'B', 'المدى الطبيعي: 0.60 - 1.20 mg/dL'),
('T_ECRE',  'C_ECRE',  'ECRE', 'Creatinine (Enzymatic)',               'كرياتينين إنزيمي عالي الدقة',   'Clinical Chemistry', 'Serum', 'mg/dL',  2, 7000.00,  20, 0.60,  1.20,  NULL, 4.0,  'B', 'المدى الطبيعي: 0.60 - 1.20 mg/dL'),
('T_GLU',   'C_GLU',   'GLU',  'Glucose',                              'سكر الدم (الجلوكوز)',            'Clinical Chemistry', 'Serum', 'mg/dL',  0, 3000.00,  21, 70.0,  105.0, 45.0, 400.0,'B', 'المدى الطبيعي الصائم: 70 - 105 mg/dL'),
('T_IRON',  'C_IRON',  'IRON', 'Serum Iron',                           'حديد المصل',                     'Clinical Chemistry', 'Serum', 'ug/dL',  0, 7000.00,  22, 50.0,  170.0, NULL, NULL, 'B', 'المدى الطبيعي: 50 - 170 ug/dL'),
('T_LACT',  'C_LACT',  'LACT', 'Lactate',                              'حمض اللاكتيك',                   'Clinical Chemistry', 'Serum', 'mmol/L', 1, 8000.00,  23, 0.5,   2.2,   NULL, 4.0,  'B', 'المدى الطبيعي: 0.5 - 2.2 mmol/L'),
('T_LDH',   'C_LDH',   'LDH',  'Lactate Dehydrogenase',                'انزيم نازعة هيدروجين اللاكتات',  'Clinical Chemistry', 'Serum', 'U/L',    0, 6000.00,  24, 125.0, 220.0, NULL, NULL, 'B', 'المدى الطبيعي: 125 - 220 U/L'),
('T_LIP',   'C_LIP',   'LIP',  'Lipase',                               'انزيم الليبيز',                  'Clinical Chemistry', 'Serum', 'U/L',    0, 8000.00,  25, 13.0,  60.0,  NULL, 180.0,'B', 'المدى الطبيعي: 13 - 60 U/L'),
('T_MG',    'C_MG',    'MG',   'Magnesium',                            'المغنيسيوم',                     'Clinical Chemistry', 'Serum', 'mg/dL',  1, 5000.00,  26, 1.7,   2.4,   1.0,  4.5,  'B', 'المدى الطبيعي: 1.7 - 2.4 mg/dL'),
('T_PHOS',  'C_PHOS',  'PHOS', 'Phosphorus (Inorganic)',               'الفوسفور غير العضوي',            'Clinical Chemistry', 'Serum', 'mg/dL',  1, 5000.00,  27, 2.5,   4.5,   NULL, NULL, 'B', 'المدى الطبيعي: 2.5 - 4.5 mg/dL'),
('T_TP',    'C_TP',    'TP',   'Total Protein',                        'البروتين الكلي',                 'Clinical Chemistry', 'Serum', 'g/dL',   1, 4000.00,  28, 6.4,   8.3,   NULL, NULL, 'B', 'المدى الطبيعي: 6.4 - 8.3 g/dL'),
('T_TIBC',  'C_TIBC',  'TIBC', 'Total Iron Binding Capacity',          'السعة الكلية لربط الحديد',       'Clinical Chemistry', 'Serum', 'ug/dL',  0, 8000.00,  29, 250.0, 450.0, NULL, NULL, 'B', 'المدى الطبيعي: 250 - 450 ug/dL'),
('T_UREA',  'C_UREA',  'BUN',  'Urea Nitrogen (BUN)',                  'يوريا الدم (BUN)',               'Clinical Chemistry', 'Serum', 'mg/dL',  1, 5000.00,  30, 7.0,   20.0,  NULL, 100.0,'B', 'BUN الطبيعي: 7 - 20 mg/dL (اليوريا 15 - 45)'),
('T_URCA',  'C_URCA',  'URCA', 'Uric Acid',                            'حمض اليوريك (داء النقرس)',       'Clinical Chemistry', 'Serum', 'mg/dL',  1, 5000.00,  31, 2.6,   7.2,   NULL, NULL, 'B', 'المدى الطبيعي: 2.6 - 7.2 mg/dL'),

-- 2. ELECTROLYTES & IMT
('T_CL',    'C_CL',    'CL',   'Chloride',                             'الكلوريد',                       'Clinical Chemistry', 'Serum', 'mmol/L', 1, 5000.00,  40, 98.0,  107.0, NULL, NULL, 'B', 'المدى الطبيعي: 98 - 107 mmol/L'),
('T_K',     'C_K',     'K',    'Potassium',                            'البوتاسيوم',                     'Clinical Chemistry', 'Serum', 'mmol/L', 2, 5000.00,  41, 3.50,  5.10,  2.80, 6.20, 'B', 'المدى الطبيعي: 3.5 - 5.1 mmol/L'),
('T_NA',    'C_NA',    'NA',   'Sodium',                               'الصوديوم',                       'Clinical Chemistry', 'Serum', 'mmol/L', 1, 5000.00,  42, 135.0, 145.0, 120.0,160.0,'B', 'المدى الطبيعي: 135 - 145 mmol/L'),
('T_ECO2',  'C_ECO2',  'ECO2', 'Carbon Dioxide (Enzymatic CO2/HCO3)',  'ثاني أكسيد الكربون / بيكربونات', 'Clinical Chemistry', 'Serum', 'mmol/L', 1, 6000.00,  43, 22.0,  29.0,  10.0, 40.0, 'B', 'المدى الطبيعي: 22 - 29 mmol/L'),

-- 3. LIPID PROFILE
('T_CHOL',  'C_CHOL',  'CHOL', 'Total Cholesterol',                    'الكوليسترول الكلي',              'Clinical Chemistry', 'Serum', 'mg/dL',  0, 5000.00,  50, 0.0,   200.0, NULL, 300.0,'B', 'المرغوب: أقل من 200 mg/dL'),
('T_HDL',   'C_HDL',   'HDL',  'HDL Cholesterol (Direct)',             'الكوليسترول عالي الكثافة (النافع)','Clinical Chemistry','Serum', 'mg/dL',  0, 6000.00,  51, 40.0,  60.0,  NULL, NULL, 'B', 'المرغوب: 40 - 60 mg/dL'),
('T_LDL',   'C_LDL',   'LDL',  'LDL Cholesterol (Direct)',             'الكوليسترول منخفض الكثافة (الضار)','Clinical Chemistry','Serum', 'mg/dL',  0, 6000.00,  52, 0.0,   100.0, NULL, 190.0,'B', 'المرغوب: أقل من 100 mg/dL'),
('T_TRIG',  'C_TRIG',  'TG',   'Triglycerides',                        'الدهون الثلاثية',                'Clinical Chemistry', 'Serum', 'mg/dL',  0, 5000.00,  53, 0.0,   150.0, NULL, 500.0,'B', 'المرغوب: أقل من 150 mg/dL'),

-- 4. CARDIAC MARKERS
('T_HSCRP', 'C_HSCRP', 'HSCRP','Cardiophase hsCRP',                   'بروتين سي التفاعلي عالي الحساسية للقلب','Clinical Chemistry','Serum','mg/L',  2, 10000.00, 60, 0.00,  3.00,  NULL, 10.0, 'B', 'منخفض الخطورة: < 1.0 | متوسط: 1.0 - 3.0 | مرتفع: > 3.0'),
('T_CK',    'C_CK',    'CK',   'Creatine Kinase (Total)',              'انزيم كرياتين كينيز الكلي',      'Clinical Chemistry', 'Serum', 'U/L',    0, 7000.00,  61, 26.0,  174.0, NULL, NULL, 'B', 'المدى الطبيعي: 26 - 174 U/L'),
('T_CKMB',  'C_CKMB',  'CKMB', 'CK-MB Isoenzyme',                      'نظير إنزيم CK-MB القلبي',        'Clinical Chemistry', 'Serum', 'ng/mL',  1, 12000.00, 62, 0.0,   5.0,   NULL, 10.0, 'B', 'المدى الطبيعي: أقل من 5.0 ng/mL'),
('T_TNI',   'C_TNI',   'TNI',  'High-Sensitivity Troponin I',          'تروبونين I عالي الحساسية',       'Clinical Chemistry', 'Serum', 'ng/mL',  3, 18000.00, 63, 0.000, 0.040, NULL, 0.100,'B', 'الحد الفاصل لإصابة عضلة القلب: > 0.040 ng/mL'),
('T_MYO',   'C_MYO',   'MYO',  'Myoglobin',                            'الميوغلوبين القلبي والعضلي',     'Clinical Chemistry', 'Serum', 'ng/mL',  1, 14000.00, 64, 28.0,  72.0,  NULL, 150.0,'B', 'المدى الطبيعي: 28 - 72 ng/mL'),
('T_PBNP',  'C_PBNP',  'PBNP', 'NT-proBNP',                            'مؤشر قصور القلب NT-proBNP',      'Clinical Chemistry', 'Serum', 'pg/mL',  0, 25000.00, 65, 0.0,   125.0, NULL, 900.0,'B', 'المدى الطبيعي للبالغين (<75 سنة): أقل من 125 pg/mL'),

-- 5. ENDOCRINOLOGY, THYROID, & TUMOR MARKERS
('T_CEA',   'C_CEA',   'CEA',  'Carcinoembryonic Antigen (CEA)',       'مستضد السرطان المضغي (CEA)',     'Hormones',           'Serum', 'ng/mL',  2, 15000.00, 70, 0.00,  3.00,  NULL, 10.0, 'B', 'غير المدخنين: < 3.0 | المدخنون: حتى 5.0 ng/mL'),
('T_FT3',   'C_FT3',   'FT3',  'Free T3 (Triiodothyronine)',           'هرمون الغدة الدرقية الحر FT3',   'Hormones',           'Serum', 'pg/mL',  2, 12000.00, 71, 2.30,  4.20,  NULL, NULL, 'B', 'المدى الطبيعي: 2.30 - 4.20 pg/mL'),
('T_FT4',   'C_FT4',   'FT4',  'Free T4 (Thyroxine)',                  'هرمون الغدة الدرقية الحر FT4',   'Hormones',           'Serum', 'ng/dL',  2, 12000.00, 72, 0.80,  1.80,  NULL, NULL, 'B', 'المدى الطبيعي: 0.80 - 1.80 ng/dL'),
('T_HCG',   'C_HCG',   'HCG',  'Total hCG (Pregnancy/Tumor)',          'هرمون الحمل الكلي (Beta-hCG)',   'Hormones',           'Serum', 'mIU/mL', 1, 12000.00, 73, 0.0,   5.0,   NULL, NULL, 'B', 'غير الحوامل / الذكور: أقل من 5.0 mIU/mL'),
('T_TPSA',  'C_TPSA',  'TPSA', 'Total PSA (Prostate Specific Ag)',     'مستضد البروستاتا النوعي الكلي',  'Hormones',           'Serum', 'ng/mL',  2, 15000.00, 74, 0.00,  4.00,  NULL, 10.0, 'M', 'المدى الطبيعي للذكور: أقل من 4.00 ng/mL'),
('T_T3',    'C_T3',    'T3',   'Total T3',                             'هرمون الغدة الدرقية الكلي T3',   'Hormones',           'Serum', 'ng/dL',  1, 10000.00, 75, 80.0,  200.0, NULL, NULL, 'B', 'المدى الطبيعي: 80 - 200 ng/dL'),
('T_T4',    'C_T4',    'T4',   'Total T4',                             'هرمون الغدة الدرقية الكلي T4',   'Hormones',           'Serum', 'ug/dL',  1, 10000.00, 76, 5.0,   12.0,  NULL, NULL, 'B', 'المدى الطبيعي: 5.0 - 12.0 ug/dL'),
('T_TSH',   'C_TSH',   'TSH',  'Thyroid Stimulating Hormone (TSH)',    'الهرمون المنبه للدرقية (TSH)',    'Hormones',           'Serum', 'uIU/mL', 2, 12000.00, 77, 0.35,  4.94,  NULL, 20.0, 'B', 'المدى الطبيعي للبالغين: 0.35 - 4.94 uIU/mL'),

-- 6. SPECIFIC PROTEINS & DIABETES
('T_RCRP',  'C_RCRP',  'RCRP', 'C-Reactive Protein (Wide Range)',      'بروتين سي التفاعلي الروتيني CRP','Clinical Chemistry', 'Serum', 'mg/L',   1, 6000.00,  80, 0.0,   5.0,   NULL, 50.0, 'B', 'طبيعي: أقل من 5.0 mg/L'),
('T_HBA1C', 'C_HBA1C', 'A1C',  'Hemoglobin A1c',                       'السكر التراكمي (HbA1c)',         'Clinical Chemistry', 'EDTA Whole Blood','%',1,10000.00,81, NULL,  5.7,   NULL, 12.0, 'B', 'طبيعي: < 5.7% | ما قبل سكري: 5.7 - 6.4% | سكري: >= 6.5%'),
('T_MALB',  'C_MALB',  'MALB', 'Microalbumin (Urine)',                 'الزلال الدقيق في الإدرار',       'Clinical Chemistry', 'Urine', 'mg/L',   1, 12000.00, 82, 0.0,   20.0,  NULL, 200.0,'B', 'طبيعي: أقل من 20.0 mg/L'),
('T_PCT',   'C_PCT',   'PCT',  'Procalcitonin',                        'البروكالسيتونين (مؤشر التسمم الدموي)','Immunology & Serology','Serum','ng/mL',2,25000.00, 83, 0.00,  0.50,  NULL, 2.00, 'B', 'طبيعي: < 0.50 | تسمم دموي محتمل: 0.50 - 2.00 | إنتان شديد: > 2.00'),
('T_TRFR',  'C_TRFR',  'TRFR', 'Transferrin',                          'الترانسفيرين (ناقل الحديد)',     'Clinical Chemistry', 'Serum', 'mg/dL',  0, 10000.00, 84, 200.0, 360.0, NULL, NULL, 'B', 'المدى الطبيعي: 200 - 360 mg/dL'),

-- 7. THERAPEUTIC DRUG MONITORING (TDM)
('T_ACTM',  'C_ACTM',  'ACTM', 'Acetaminophen (Paracetamol)',          'مستوى الباراسيتامول في الدم',    'Toxicology & TDM',   'Serum', 'ug/mL',  1, 12000.00, 90, 10.0,  30.0,  NULL, 150.0,'B', 'المدى العلاجي: 10 - 30 ug/mL | سمي: > 150'),
('T_CARB',  'C_CARB',  'CARB', 'Carbamazepine (Tegretol)',             'مستوى التجريتول (كاربامازيبين)', 'Toxicology & TDM',   'Serum', 'ug/mL',  1, 15000.00, 91, 4.0,   12.0,  NULL, 15.0, 'B', 'المدى العلاجي: 4.0 - 12.0 ug/mL'),
('T_DIG',   'C_DIG',   'DIG',  'Digoxin',                              'مستوى الديجوكسين القلبي',        'Toxicology & TDM',   'Serum', 'ng/mL',  2, 15000.00, 92, 0.80,  2.00,  NULL, 2.40, 'B', 'المدى العلاجي: 0.80 - 2.00 ng/mL | سمي: > 2.40'),
('T_DGNA',  'C_DGNA',  'DGNA', 'Digitoxin',                            'مستوى الديجيتوكسين',             'Toxicology & TDM',   'Serum', 'ng/mL',  1, 15000.00, 93, 10.0,  30.0,  NULL, 35.0, 'B', 'المدى العلاجي: 10.0 - 30.0 ng/mL'),
('T_GENT',  'C_GENT',  'GENT', 'Gentamicin',                           'مستوى الجنتامايسين في الدم',     'Toxicology & TDM',   'Serum', 'ug/mL',  1, 15000.00, 94, 0.5,   10.0,  NULL, 12.0, 'B', 'الذروة (Peak): 5 - 10 | القاع (Trough): < 2 ug/mL'),
('T_LITH',  'C_LITH',  'LITH', 'Lithium',                              'مستوى الليثيوم في الدم',         'Toxicology & TDM',   'Serum', 'mmol/L', 2, 12000.00, 95, 0.60,  1.20,  NULL, 1.50, 'B', 'المدى العلاجي: 0.60 - 1.20 mmol/L | سمي: > 1.50'),
('T_PBAR',  'C_PBAR',  'PBAR', 'Phenobarbital',                        'مستوى الفينوباربيتال',           'Toxicology & TDM',   'Serum', 'ug/mL',  1, 15000.00, 96, 15.0,  40.0,  NULL, 50.0, 'B', 'المدى العلاجي: 15.0 - 40.0 ug/mL'),
('T_PHEN',  'C_PHEN',  'PHEN', 'Phenytoin (Dilantin)',                 'مستوى الفينيتوين (ديلانتين)',    'Toxicology & TDM',   'Serum', 'ug/mL',  1, 15000.00, 97, 10.0,  20.0,  NULL, 25.0, 'B', 'المدى العلاجي: 10.0 - 20.0 ug/mL | سمي: > 25.0'),
('T_SAL',   'C_SAL',   'SAL',  'Salicylate (Aspirin)',                 'مستوى الساليسيلات (الأسبرين)',   'Toxicology & TDM',   'Serum', 'mg/dL',  1, 12000.00, 98, 15.0,  30.0,  NULL, 35.0, 'B', 'المدى العلاجي: 15.0 - 30.0 mg/dL'),
('T_THEO',  'C_THEO',  'THEO', 'Theophylline',                         'مستوى الثيوفيلين الموسع للقصبات','Toxicology & TDM',   'Serum', 'ug/mL',  1, 15000.00, 99, 10.0,  20.0,  NULL, 20.0, 'B', 'المدى العلاجي: 10.0 - 20.0 ug/mL'),
('T_TOBR',  'C_TOBR',  'TOBR', 'Tobramycin',                           'مستوى التوبرامايسين',            'Toxicology & TDM',   'Serum', 'ug/mL',  1, 15000.00, 100,0.5,   10.0,  NULL, 12.0, 'B', 'الذروة (Peak): 5 - 10 | القاع (Trough): < 2 ug/mL'),
('T_VALP',  'C_VALP',  'VALP', 'Valproic Acid (Depakene)',             'مستوى حمض الفالبرويك (ديباكين)', 'Toxicology & TDM',   'Serum', 'ug/mL',  1, 15000.00, 101,50.0,  100.0, NULL, 100.0,'B', 'المدى العلاجي: 50.0 - 100.0 ug/mL'),
('T_VANC',  'C_VANC',  'VANC', 'Vancomycin',                           'مستوى الفانكومايسين',            'Toxicology & TDM',   'Serum', 'ug/mL',  1, 18000.00, 102,10.0,  20.0,  NULL, 40.0, 'B', 'المدى العلاجي للقاع (Trough): 10 - 20 ug/mL | سمي: > 40'),

-- 8. TOXICOLOGY / DRUGS OF ABUSE (DAT)
('T_AMPH',  'C_AMPH',  'AMPH', 'Amphetamines Screen (Urine)',          'فحص الأمفيتامينات (المنشطات)',   'Toxicology & TDM',   'Urine', 'ng/mL',  0, 10000.00, 110,NULL,  500.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 500 ng/mL | إيجابي: 500 فأعلى'),
('T_BARB',  'C_BARB',  'BARB', 'Barbiturates Screen (Urine)',          'فحص الباربيتورات المهدئة',       'Toxicology & TDM',   'Urine', 'ng/mL',  0, 10000.00, 111,NULL,  200.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 200 ng/mL | إيجابي: 200 فأعلى'),
('T_BENZ',  'C_BENZ',  'BENZ', 'Benzodiazepines Screen (Urine)',       'فحص البنزوديازيبينات (المهدئات)', 'Toxicology & TDM',   'Urine', 'ng/mL',  0, 10000.00, 112,NULL,  200.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 200 ng/mL | إيجابي: 200 فأعلى'),
('T_THC',   'C_THC',   'THC',  'Cannabinoids / THC Screen (Urine)',    'فحص الحشيش والقنب (THC)',        'Toxicology & TDM',   'Urine', 'ng/mL',  0, 10000.00, 113,NULL,  50.0,  NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 50 ng/mL | إيجابي: 50 فأعلى'),
('T_COMC',  'C_COMC',  'COMC', 'Cocaine Metabolite Screen (Urine)',    'فحص مشتقات الكوكايين',           'Toxicology & TDM',   'Urine', 'ng/mL',  0, 10000.00, 114,NULL,  150.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 150 ng/mL | إيجابي: 150 فأعلى'),
('T_MDMA',  'C_MDMA',  'MDMA', 'Ecstasy (MDMA) Screen (Urine)',        'فحص حبوب النشوة (إكستاسي MDMA)', 'Toxicology & TDM',   'Urine', 'ng/mL',  0, 12000.00, 115,NULL,  500.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 500 ng/mL | إيجابي: 500 فأعلى'),
('T_ETOH',  'C_ETOH',  'ETOH', 'Ethyl Alcohol (Ethanol)',              'فحص الكحول الإيثيلي',            'Toxicology & TDM',   'Serum', 'mg/dL',  1, 10000.00, 116,NULL,  10.0,  NULL, 80.0, 'B', 'سلبي (طبيعي): أقل من 10.0 mg/dL | سكر قانوني: >= 80.0'),
('T_METH',  'C_METH',  'METH', 'Methadone Screen (Urine)',             'فحص الميثادون في الإدرار',       'Toxicology & TDM',   'Urine', 'ng/mL',  0, 10000.00, 117,NULL,  300.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 300 ng/mL | إيجابي: 300 فأعلى'),
('T_OPI',   'C_OPI',   'OPI',  'Opiates Screen (Urine)',               'فحص الأفيونات والمورفين',        'Toxicology & TDM',   'Urine', 'ng/mL',  0, 10000.00, 118,NULL,  300.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 300 ng/mL | إيجابي: 300 فأعلى'),
('T_OXYC',  'C_OXYC',  'OXYC', 'Oxycodone Screen (Urine)',             'فحص الأوكسيكودون المسكن',        'Toxicology & TDM',   'Urine', 'ng/mL',  0, 12000.00, 119,NULL,  100.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 100 ng/mL | إيجابي: 100 فأعلى'),
('T_PCP',   'C_PCP',   'PCP',  'Phencyclidine Screen (Urine)',         'فحص فينسيكليدين (غبار الملاك)',  'Toxicology & TDM',   'Urine', 'ng/mL',  0, 12000.00, 120,NULL,  25.0,  NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 25 ng/mL | إيجابي: 25 فأعلى'),
('T_PPX',   'C_PPX',   'PPX',  'Propoxyphene Screen (Urine)',          'فحص البروبوكسيفين المسكن',       'Toxicology & TDM',   'Urine', 'ng/mL',  0, 12000.00, 121,NULL,  300.0, NULL, NULL, 'B', 'سلبي (طبيعي): أقل من 300 ng/mL | إيجابي: 300 فأعلى'),

-- 9. IMMUNOSUPPRESSANT DRUGS
('T_CSA',   'C_CSA',   'CSA',  'Cyclosporine',                         'مستوى السيكلوسبورين المثبط للمناعة','Toxicology & TDM', 'EDTA Whole Blood','ng/mL',0,25000.00,130,100.0, 300.0, NULL, 400.0,'B', 'المدى العلاجي للقاع (Trough): 100 - 300 ng/mL'),
('T_MPA',   'C_MPA',   'MPA',  'Mycophenolic Acid (CellCept)',         'مستوى حمض الميكوفينوليك (سيلسيبت)','Toxicology & TDM', 'Serum', 'ug/mL',  1, 25000.00, 131,1.0,   3.5,   NULL, 5.0,  'B', 'المدى العلاجي: 1.0 - 3.5 ug/mL'),
('T_TACR',  'C_TACR',  'TACR', 'Tacrolimus (Prograf / FK506)',         'مستوى التاكروليموس (بروجراف)',   'Toxicology & TDM',   'EDTA Whole Blood','ng/mL',1,25000.00,132,5.0,  15.0,  NULL, 20.0, 'B', 'المدى العلاجي للقاع (Trough): 5.0 - 15.0 ng/mL');


-- =====================================================================
-- STEP A: INSERT OR UPDATE TESTS
-- =====================================================================
INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, sort_order, active)
SELECT 
    d.test_code,
    d.name_en,
    d.name_ar,
    dept.id,
    spec.id,
    d.price,
    d.sort_order,
    TRUE
FROM tmp_assay_def d
JOIN departments dept ON dept.name_en = d.dept_name
JOIN specimen_types spec ON spec.name_en = d.spec_type
ON CONFLICT (test_code) DO UPDATE
   SET name_en = EXCLUDED.name_en,
       name_ar = EXCLUDED.name_ar,
       department_id = EXCLUDED.department_id,
       specimen_type_id = EXCLUDED.specimen_type_id,
       price = EXCLUDED.price,
       sort_order = EXCLUDED.sort_order,
       active = TRUE;

-- =====================================================================
-- STEP B: INSERT OR UPDATE COMPONENTS
-- =====================================================================
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, sort_order)
SELECT 
    t.id,
    d.comp_code,
    d.name_en,
    d.name_ar,
    d.lis_code,
    d.unit,
    d.decimals,
    1
FROM tmp_assay_def d
JOIN tests t ON t.test_code = d.test_code
ON CONFLICT (component_code) DO UPDATE
   SET name_en = EXCLUDED.name_en,
       name_ar = EXCLUDED.name_ar,
       display_code = EXCLUDED.display_code,
       unit = EXCLUDED.unit,
       decimals = EXCLUDED.decimals;

-- =====================================================================
-- STEP C: INSERT OR UPDATE REFERENCE RANGES
-- =====================================================================
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 
    'R_' || d.comp_code || '_STD',
    c.id,
    d.ref_sex,
    0,
    120,
    'Years',
    d.ref_low,
    d.ref_high,
    d.ref_crit_low,
    d.ref_crit_high,
    d.ref_note
FROM tmp_assay_def d
JOIN components c ON c.component_code = d.comp_code
ON CONFLICT (range_code) DO UPDATE
   SET low = EXCLUDED.low,
       high = EXCLUDED.high,
       critical_low = EXCLUDED.critical_low,
       critical_high = EXCLUDED.critical_high,
       note = EXCLUDED.note;

-- Gender specific range overrides for Creatinine and Uric Acid and PSA
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_C_CREA_M', c.id, 'M', 0, 120, 'Years', 0.70, 1.30, NULL, 4.0, 'المدى الطبيعي للذكور: 0.70 - 1.30 mg/dL'
FROM components c WHERE c.component_code = 'C_CREA'
ON CONFLICT (range_code) DO UPDATE SET low = 0.70, high = 1.30, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_C_CREA_F', c.id, 'F', 0, 120, 'Years', 0.50, 1.10, NULL, 4.0, 'المدى الطبيعي للإناث: 0.50 - 1.10 mg/dL'
FROM components c WHERE c.component_code = 'C_CREA'
ON CONFLICT (range_code) DO UPDATE SET low = 0.50, high = 1.10, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_C_URCA_M', c.id, 'M', 0, 120, 'Years', 3.5, 7.2, NULL, NULL, 'المدى الطبيعي للذكور: 3.5 - 7.2 mg/dL'
FROM components c WHERE c.component_code = 'C_URCA'
ON CONFLICT (range_code) DO UPDATE SET low = 3.5, high = 7.2, note = EXCLUDED.note;

INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note)
SELECT 'R_C_URCA_F', c.id, 'F', 0, 120, 'Years', 2.6, 6.0, NULL, NULL, 'المدى الطبيعي للإناث: 2.6 - 6.0 mg/dL'
FROM components c WHERE c.component_code = 'C_URCA'
ON CONFLICT (range_code) DO UPDATE SET low = 2.6, high = 6.0, note = EXCLUDED.note;

-- =====================================================================
-- STEP D: MAP ALL ASSAYS TO SIEMENS DIMENSION EXL 200 (ASTM)
-- =====================================================================
-- Ensure instrument exists
INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, com_port, baud_rate, data_bits, parity, stop_bits, active)
VALUES ('EXL200', 'Dimension EXL 200', 'ASTM', 'RS232', 'Bidirectional', 'COM1', 9600, 8, 'None', 'One', TRUE)
ON CONFLICT (code) DO UPDATE
   SET protocol = 'ASTM',
       connection_type = 'RS232',
       operating_mode = 'Bidirectional',
       active = TRUE;

-- Insert or update instrument test mappings
INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code)
SELECT 
    i.id,
    t.id,
    c.id,
    d.lis_code
FROM tmp_assay_def d
JOIN tests t ON t.test_code = d.test_code
JOIN components c ON c.component_code = d.comp_code
CROSS JOIN instruments i
WHERE i.code = 'EXL200'
ON CONFLICT (instrument_id, component_id) DO UPDATE
   SET instrument_test_code = EXCLUDED.instrument_test_code,
       test_id = EXCLUDED.test_id;

DROP TABLE tmp_assay_def;

COMMIT;

