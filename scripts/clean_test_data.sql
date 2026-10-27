-- =============================================================================
-- Clean Test / Transactional Data Script
-- Cleans all patient data, orders, specimens, results, history, billing, QC
-- Resets operational sequences (accession_seq, mrn_seq, invoice_seq)
-- Removes test mappings (e.g. ADVIA360 with HBA1C)
-- Preserves Master Data: tests, components, reference ranges, specimen types, 
--                        departments, instruments, users, lab settings.
-- =============================================================================

BEGIN;

-- 1. Remove non-HbA1c instrument mappings for HbA1c (preserving only Dimension EXL 200 & Roche Cobas c311)
DELETE FROM instrument_tests 
WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_HBA1C')
  AND instrument_id NOT IN (SELECT id FROM instruments WHERE code IN ('EXL200', 'COBAS_C311'));

-- 1b. Remove duplicate / rogue components under HbA1c (preserving only official C_HBA1C)
DELETE FROM reference_ranges WHERE component_id IN (
    SELECT id FROM components WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_HBA1C') AND component_code != 'C_HBA1C'
);
DELETE FROM instrument_tests WHERE component_id IN (
    SELECT id FROM components WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_HBA1C') AND component_code != 'C_HBA1C'
);
DELETE FROM components 
WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_HBA1C') 
  AND component_code != 'C_HBA1C';

UPDATE tests SET name_en = 'Glycated Hemoglobin', name_ar = 'السكر التراكمي' WHERE test_code = 'T_HBA1C';
UPDATE components SET name_en = 'HbA1c', name_ar = 'السكر التراكمي', display_code = 'HbA1c' WHERE component_code = 'C_HBA1C';


-- 2. Truncate all transactional tables in cascading order
TRUNCATE TABLE 
    result_history,
    result_comments,
    results,
    order_tests,
    specimen_rejections,
    specimens,
    payments,
    invoice_items,
    invoices,
    orders,
    patients,
    qc_results,
    middleware_log
RESTART IDENTITY CASCADE;

-- 3. Reset operational sequences
ALTER SEQUENCE IF EXISTS accession_seq RESTART WITH 1;
ALTER SEQUENCE IF EXISTS mrn_seq RESTART WITH 100001;
ALTER SEQUENCE IF EXISTS invoice_seq RESTART WITH 1001;

COMMIT;
