-- =====================================================================
-- LIS Catalog Seed: Sysmex XN-350 Automated CBC Integration
-- Complete Blood Count (CBC + 5-Part Diff + IG)
-- Protocol: ASTM E1394 / E1381 over TCP/IP (Server Mode, Port 5100)
-- Mode: Bidirectional
-- PostgreSQL. Safe to re-run (upserts).
-- =====================================================================

BEGIN;

-- 1. Ensure Instrument SYSMEX_XN350 is configured
INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, tcp_mode, com_port, baud_rate, data_bits, parity, stop_bits, flow_control, active, updated_at)
VALUES ('SYSMEX_XN350', 'Sysmex XN-350', 'ASTM', 'RS232', 'Bidirectional', 'Server', 'COM8', 9600, 8, 'None', 'One', 'None', TRUE, clock_timestamp())
ON CONFLICT (code) DO UPDATE
SET name = 'Sysmex XN-350',
    protocol = 'ASTM',
    connection_type = 'RS232',
    operating_mode = 'Bidirectional',
    com_port = 'COM8',
    baud_rate = 9600,
    data_bits = 8,
    parity = 'None',
    stop_bits = 'One',
    flow_control = 'None',
    active = TRUE,
    updated_at = clock_timestamp();

-- 2. Ensure 5-Part Diff and Sysmex Advanced Markers exist in components for CBC
INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, result_type, sort_order)
SELECT t.id, v.code, v.name_en, v.name_ar, v.display_code, v.unit, v.decimals, 'numeric', v.sort_order
FROM tests t
CROSS JOIN (VALUES
    ('C_NEUT_pct', 'Neutrophils Percentage', 'نسبة العدلات', 'NEUT%', '%', 1, 30),
    ('C_NEUT_abs', 'Neutrophils Absolute', 'العدد المطلق للعدلات', 'NEUT#', '10^9/L', 2, 31),
    ('C_LYMPH_pct', 'Lymphocytes Percentage', 'نسبة الخلايا اللمفاوية', 'LYMPH%', '%', 1, 32),
    ('C_LYMPH_abs', 'Lymphocytes Absolute', 'العدد المطلق للخلايا اللمفاوية', 'LYMPH#', '10^9/L', 2, 33),
    ('C_MONO_pct', 'Monocytes Percentage', 'نسبة الخلايا الوحيدة', 'MONO%', '%', 1, 34),
    ('C_MONO_abs', 'Monocytes Absolute', 'العدد المطلق للخلايا الوحيدة', 'MONO#', '10^9/L', 2, 35),
    ('C_EO_pct', 'Eosinophils Percentage', 'نسبة الحمضات', 'EO%', '%', 1, 36),
    ('C_EO_abs', 'Eosinophils Absolute', 'العدد المطلق للحمضات', 'EO#', '10^9/L', 2, 37),
    ('C_BASO_pct', 'Basophils Percentage', 'نسبة الخلايا القاعدية', 'BASO%', '%', 1, 38),
    ('C_BASO_abs', 'Basophils Absolute', 'العدد المطلق للخلايا القاعدية', 'BASO#', '10^9/L', 2, 39),
    ('C_IG_pct', 'Immature Granulocytes %', 'نسبة المحببات غير الناضجة', 'IG%', '%', 1, 40),
    ('C_IG_abs', 'Immature Granulocytes #', 'العدد المطلق للمحببات غير الناضجة', 'IG#', '10^9/L', 2, 41)
) AS v(code, name_en, name_ar, display_code, unit, decimals, sort_order)
WHERE t.test_code = 'T_CBC_360'
ON CONFLICT (component_code) DO UPDATE
SET name_en = EXCLUDED.name_en,
    name_ar = EXCLUDED.name_ar,
    display_code = EXCLUDED.display_code,
    unit = EXCLUDED.unit,
    decimals = EXCLUDED.decimals,
    sort_order = EXCLUDED.sort_order,
    updated_at = clock_timestamp();

-- 3. Reference Ranges for 5-Part Diff & IG (Adults 18 to 100 Years)
INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high)
SELECT v.range_code, c.id, v.sex, v.age_min, v.age_max, v.age_unit, v.low, v.high
FROM (VALUES
    ('R_XN_NEUT_P', 'C_NEUT_pct', 'B', 18, 100, 'Years', 40.0, 75.0),
    ('R_XN_NEUT_A', 'C_NEUT_abs', 'B', 18, 100, 'Years', 1.8, 7.5),
    ('R_XN_LYMPH_P', 'C_LYMPH_pct', 'B', 18, 100, 'Years', 20.0, 45.0),
    ('R_XN_LYMPH_A', 'C_LYMPH_abs', 'B', 18, 100, 'Years', 1.0, 4.0),
    ('R_XN_MONO_P', 'C_MONO_pct', 'B', 18, 100, 'Years', 2.0, 10.0),
    ('R_XN_MONO_A', 'C_MONO_abs', 'B', 18, 100, 'Years', 0.1, 0.8),
    ('R_XN_EO_P', 'C_EO_pct', 'B', 18, 100, 'Years', 1.0, 6.0),
    ('R_XN_EO_A', 'C_EO_abs', 'B', 18, 100, 'Years', 0.02, 0.5),
    ('R_XN_BASO_P', 'C_BASO_pct', 'B', 18, 100, 'Years', 0.0, 2.0),
    ('R_XN_BASO_A', 'C_BASO_abs', 'B', 18, 100, 'Years', 0.01, 0.1),
    ('R_XN_IG_P', 'C_IG_pct', 'B', 18, 100, 'Years', 0.0, 0.6),
    ('R_XN_IG_A', 'C_IG_abs', 'B', 18, 100, 'Years', 0.0, 0.06)
) AS v(range_code, component_code, sex, age_min, age_max, age_unit, low, high)
JOIN components c ON c.component_code = v.component_code
ON CONFLICT (range_code) DO UPDATE
SET component_id = EXCLUDED.component_id,
    sex = EXCLUDED.sex,
    age_min = EXCLUDED.age_min,
    age_max = EXCLUDED.age_max,
    age_unit = EXCLUDED.age_unit,
    low = EXCLUDED.low,
    high = EXCLUDED.high,
    updated_at = clock_timestamp();

-- 4. Instrument Mapping: Sysmex XN-350 ASTM Test Codes (26 Parameters)
INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code)
SELECT i.id, t.id, c.id, v.analyzer_code
FROM instruments i
JOIN tests t ON t.test_code = 'T_CBC_360'
CROSS JOIN (VALUES
    ('C_WBC', 'WBC'),
    ('C_RBC', 'RBC'),
    ('C_HGB', 'HGB'),
    ('C_HCT', 'HCT'),
    ('C_MCV', 'MCV'),
    ('C_MCH', 'MCH'),
    ('C_MCHC', 'MCHC'),
    ('C_PLT', 'PLT'),
    ('C_RDW_SD', 'RDW-SD'),
    ('C_RDW_CV', 'RDW-CV'),
    ('C_PDW_CV', 'PDW'),
    ('C_MPV', 'MPV'),
    ('C_PLCR', 'P-LCR'),
    ('C_PCT', 'PCT'),
    -- 5-Part Differential (Percentages & Absolutes)
    ('C_NEUT_pct', 'NEUT%'),
    ('C_NEUT_abs', 'NEUT#'),
    ('C_LYMPH_pct', 'LYMPH%'),
    ('C_LYMPH_abs', 'LYMPH#'),
    ('C_MONO_pct', 'MONO%'),
    ('C_MONO_abs', 'MONO#'),
    ('C_EO_pct', 'EO%'),
    ('C_EO_abs', 'EO#'),
    ('C_BASO_pct', 'BASO%'),
    ('C_BASO_abs', 'BASO#'),
    -- Sysmex Advanced Markers
    ('C_IG_pct', 'IG%'),
    ('C_IG_abs', 'IG#')
) AS v(component_code, analyzer_code)
JOIN components c ON c.component_code = v.component_code
WHERE i.code = 'SYSMEX_XN350'
ON CONFLICT (instrument_id, component_id) DO UPDATE
SET instrument_test_code = EXCLUDED.instrument_test_code,
    updated_at = clock_timestamp();

COMMIT;
