-- =============================================================================
-- LIS Database Schema - Phase 1 Complete Schema
-- Target: PostgreSQL 18 (lis_db)
-- All tables, sequences, constraints, indexes, and triggers
-- =============================================================================

BEGIN;

-- -----------------------------------------------------------------------------
-- 1. Sequences
-- -----------------------------------------------------------------------------
CREATE SEQUENCE IF NOT EXISTS accession_seq START 1 MAXVALUE 999999 CYCLE;
CREATE SEQUENCE IF NOT EXISTS mrn_seq START 100001;
CREATE SEQUENCE IF NOT EXISTS invoice_seq START 1001;

-- -----------------------------------------------------------------------------
-- 2. Helper Functions & Triggers
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION set_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = clock_timestamp();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- -----------------------------------------------------------------------------
-- 3. Core Master Tables (Departments, Specimen Types, Rejection Reasons)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS departments (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name_en     TEXT NOT NULL UNIQUE,
    name_ar     TEXT,
    letter      CHAR(1),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS specimen_types (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name_en     TEXT NOT NULL UNIQUE,
    name_ar     TEXT,
    color       TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS rejection_reasons (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name_en     TEXT NOT NULL UNIQUE,
    name_ar     TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 4. Test Catalog (Tests, Components, Profiles, Reference Ranges)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS tests (
    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    test_code        TEXT NOT NULL UNIQUE,
    name_en          TEXT NOT NULL,
    name_ar          TEXT,
    department_id    BIGINT REFERENCES departments(id) ON DELETE SET NULL,
    specimen_type_id BIGINT REFERENCES specimen_types(id) ON DELETE SET NULL,
    is_profile       BOOLEAN NOT NULL DEFAULT FALSE,
    price            NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (price >= 0),
    sort_order       INT NOT NULL DEFAULT 0,
    active           BOOLEAN NOT NULL DEFAULT TRUE,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS components (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    test_id         BIGINT NOT NULL REFERENCES tests(id) ON DELETE CASCADE,
    component_code  TEXT NOT NULL UNIQUE,
    name_en         TEXT NOT NULL,
    name_ar         TEXT,
    display_code    TEXT,
    unit            TEXT,
    decimals        SMALLINT DEFAULT 1,
    result_type     TEXT NOT NULL DEFAULT 'numeric' CHECK (result_type IN ('numeric','text','option')),
    notes           TEXT,
    sort_order      INT NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS profile_items (
    profile_test_id BIGINT NOT NULL REFERENCES tests(id) ON DELETE CASCADE,
    member_test_id  BIGINT NOT NULL REFERENCES tests(id) ON DELETE CASCADE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    PRIMARY KEY (profile_test_id, member_test_id)
);

CREATE TABLE IF NOT EXISTS reference_ranges (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    range_code     TEXT UNIQUE,
    component_id   BIGINT NOT NULL REFERENCES components(id) ON DELETE CASCADE,
    sex            CHAR(1) NOT NULL CHECK (sex IN ('M','F','B')),
    age_min        NUMERIC(6,2) NOT NULL DEFAULT 0,
    age_max        NUMERIC(6,2),
    age_unit       TEXT NOT NULL DEFAULT 'Years' CHECK (age_unit IN ('Days','Months','Years')),
    age_from_days  INT GENERATED ALWAYS AS (
                       floor(age_min * CASE age_unit WHEN 'Days' THEN 1 WHEN 'Months' THEN 30.4375 ELSE 365.25 END)::int
                   ) STORED,
    age_to_days    INT GENERATED ALWAYS AS (
                       floor(age_max * CASE age_unit WHEN 'Days' THEN 1 WHEN 'Months' THEN 30.4375 ELSE 365.25 END)::int
                   ) STORED,
    low            NUMERIC,
    high           NUMERIC,
    critical_low   NUMERIC,
    critical_high  NUMERIC,
    note           TEXT,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE INDEX IF NOT EXISTS idx_reference_ranges_lookup 
ON reference_ranges (component_id, sex, age_from_days, age_to_days);

-- -----------------------------------------------------------------------------
-- 5. Instruments and Mapping
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS instruments (
    id              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    code            TEXT NOT NULL UNIQUE,
    name            TEXT NOT NULL,
    protocol        TEXT CHECK (protocol IN ('ASTM','HL7')),
    connection_type TEXT CHECK (connection_type IN ('TCP','RS232')),
    operating_mode  TEXT CHECK (operating_mode IN ('Bidirectional','ResultsOnly')),
    tcp_mode        TEXT CHECK (tcp_mode IN ('Server','Client')),
    host            TEXT,
    port            INT,
    com_port        TEXT,
    baud_rate       INT,
    data_bits       INT,
    parity          TEXT,
    stop_bits       TEXT,
    flow_control    TEXT,
    active          BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS instrument_tests (
    id                   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    instrument_id        BIGINT NOT NULL REFERENCES instruments(id) ON DELETE CASCADE,
    test_id              BIGINT NOT NULL REFERENCES tests(id) ON DELETE CASCADE,
    component_id         BIGINT NOT NULL REFERENCES components(id) ON DELETE CASCADE,
    instrument_test_code TEXT NOT NULL,
    reportable_low       NUMERIC,
    reportable_high      NUMERIC,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (instrument_id, component_id)
);

-- -----------------------------------------------------------------------------
-- 6. Users & Authentication (Flat model, no roles, audit purpose)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    username      TEXT NOT NULL UNIQUE,
    full_name     TEXT NOT NULL,
    password_hash TEXT NOT NULL,
    active        BOOLEAN NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 7. Referring Doctors
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS doctors (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name       TEXT NOT NULL,
    specialty  TEXT,
    phone      TEXT,
    active     BOOLEAN NOT NULL DEFAULT TRUE,
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 8. Patients, Orders, Specimens, Order Tests
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS patients (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    mrn           BIGINT NOT NULL UNIQUE DEFAULT nextval('mrn_seq'),
    full_name_ar  TEXT NOT NULL,
    full_name_en  TEXT,
    sex           CHAR(1) NOT NULL CHECK (sex IN ('M','F')),
    date_of_birth DATE NOT NULL,
    phone         TEXT,
    national_id   TEXT,
    notes         TEXT,
    created_by    BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE INDEX IF NOT EXISTS idx_patients_search 
ON patients (mrn, phone, full_name_ar);

CREATE TABLE IF NOT EXISTS orders (
    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    patient_id       BIGINT NOT NULL REFERENCES patients(id) ON DELETE RESTRICT,
    visit_no         INT NOT NULL DEFAULT 1,
    order_datetime   TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    referring_doctor TEXT,
    clinical_notes   TEXT,
    status           TEXT NOT NULL DEFAULT 'Ordered' 
                     CHECK (status IN ('Ordered','Collected','InProgress','ResultReceived','Validated','Printed','Rejected','Cancelled')),
    created_by       BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS specimens (
    id               BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id         BIGINT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    specimen_type_id BIGINT NOT NULL REFERENCES specimen_types(id) ON DELETE RESTRICT,
    accession_no     CHAR(6) NOT NULL CONSTRAINT chk_accession CHECK (accession_no ~ '^[0-9]{6}$'),
    status           TEXT NOT NULL DEFAULT 'Ordered'
                     CHECK (status IN ('Ordered','Collected','InProgress','ResultReceived','Validated','Rejected','Cancelled')),
    collected_at     TIMESTAMPTZ,
    issued_at        TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    archived         BOOLEAN NOT NULL DEFAULT FALSE,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- Unique index for active accessions only
CREATE UNIQUE INDEX IF NOT EXISTS ux_specimens_active_accession
ON specimens (accession_no) WHERE archived = FALSE;

CREATE INDEX IF NOT EXISTS idx_specimens_accession_lookup
ON specimens (accession_no, archived);

CREATE TABLE IF NOT EXISTS order_tests (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id    BIGINT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
    specimen_id BIGINT NOT NULL REFERENCES specimens(id) ON DELETE CASCADE,
    test_id     BIGINT NOT NULL REFERENCES tests(id) ON DELETE RESTRICT,
    status      TEXT NOT NULL DEFAULT 'Ordered'
                CHECK (status IN ('Ordered','Collected','InProgress','ResultReceived','Validated','Printed','Rejected','Cancelled')),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 9. Results and Result History
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS results (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_test_id BIGINT NOT NULL REFERENCES order_tests(id) ON DELETE CASCADE,
    component_id  BIGINT NOT NULL REFERENCES components(id) ON DELETE RESTRICT,
    value_text    TEXT,
    value_numeric NUMERIC,
    unit          TEXT,
    flag          TEXT CHECK (flag IN ('N','H','L','HH','LL')),
    instrument_id BIGINT REFERENCES instruments(id) ON DELETE SET NULL,
    resulted_at   TIMESTAMPTZ,
    validated_by  BIGINT REFERENCES users(id) ON DELETE SET NULL,
    validated_at  TIMESTAMPTZ,
    is_current    BOOLEAN NOT NULL DEFAULT TRUE,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS result_history (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    result_id     BIGINT NOT NULL REFERENCES results(id) ON DELETE CASCADE,
    value_text    TEXT,
    value_numeric NUMERIC,
    flag          TEXT,
    instrument_id BIGINT REFERENCES instruments(id) ON DELETE SET NULL,
    changed_at    TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    changed_by    BIGINT REFERENCES users(id) ON DELETE SET NULL,
    reason        TEXT
);

CREATE TABLE IF NOT EXISTS result_comments (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    result_id    BIGINT NOT NULL REFERENCES results(id) ON DELETE CASCADE,
    comment_text TEXT NOT NULL,
    user_id      BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 10. Specimen Rejections
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS specimen_rejections (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    specimen_id BIGINT NOT NULL REFERENCES specimens(id) ON DELETE CASCADE,
    reason_id   BIGINT NOT NULL REFERENCES rejection_reasons(id) ON DELETE RESTRICT,
    notes       TEXT,
    user_id     BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 11. Derived / Calculated Components
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS calculated_components (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    component_id        BIGINT NOT NULL UNIQUE REFERENCES components(id) ON DELETE CASCADE,
    formula             TEXT NOT NULL,
    input_component_ids JSONB NOT NULL DEFAULT '[]'::jsonb,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 12. Quality Control (QC)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS qc_materials (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name        TEXT NOT NULL,
    lot_no      TEXT NOT NULL,
    level       SMALLINT NOT NULL CHECK (level IN (1,2,3)),
    expiry_date DATE NOT NULL,
    active      BOOLEAN NOT NULL DEFAULT TRUE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS qc_targets (
    id             BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    qc_material_id BIGINT NOT NULL REFERENCES qc_materials(id) ON DELETE CASCADE,
    instrument_id  BIGINT NOT NULL REFERENCES instruments(id) ON DELETE CASCADE,
    component_id   BIGINT NOT NULL REFERENCES components(id) ON DELETE CASCADE,
    mean           NUMERIC NOT NULL,
    sd             NUMERIC NOT NULL,
    unit           TEXT,
    created_at     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    UNIQUE (qc_material_id, instrument_id, component_id)
);

CREATE TABLE IF NOT EXISTS qc_results (
    id                BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    instrument_id     BIGINT NOT NULL REFERENCES instruments(id) ON DELETE CASCADE,
    qc_material_id    BIGINT REFERENCES qc_materials(id) ON DELETE SET NULL,
    component_id      BIGINT NOT NULL REFERENCES components(id) ON DELETE RESTRICT,
    value             NUMERIC NOT NULL,
    received_at       TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    rule_violation    TEXT,
    accepted          BOOLEAN NOT NULL DEFAULT TRUE,
    corrective_action TEXT,
    user_id           BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at        TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 13. Invoicing and Payments (Billing)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS invoices (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    order_id   BIGINT NOT NULL UNIQUE REFERENCES orders(id) ON DELETE RESTRICT,
    invoice_no BIGINT NOT NULL UNIQUE DEFAULT nextval('invoice_seq'),
    total      NUMERIC(12,2) NOT NULL DEFAULT 0,
    discount   NUMERIC(12,2) NOT NULL DEFAULT 0,
    net        NUMERIC(12,2) NOT NULL DEFAULT 0,
    paid       NUMERIC(12,2) NOT NULL DEFAULT 0,
    balance    NUMERIC(12,2) NOT NULL DEFAULT 0,
    status     TEXT NOT NULL DEFAULT 'Unpaid' CHECK (status IN ('Unpaid','Partial','Paid','Cancelled')),
    created_by BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS invoice_items (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id    BIGINT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    test_id       BIGINT NOT NULL REFERENCES tests(id) ON DELETE RESTRICT,
    price_at_time NUMERIC(12,2) NOT NULL
);

CREATE TABLE IF NOT EXISTS payments (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    invoice_id BIGINT NOT NULL REFERENCES invoices(id) ON DELETE CASCADE,
    amount     NUMERIC(12,2) NOT NULL CHECK (amount > 0),
    method     TEXT NOT NULL DEFAULT 'Cash' CHECK (method IN ('Cash','Card','Other')),
    user_id    BIGINT REFERENCES users(id) ON DELETE SET NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 14. Lab Settings (Header, Footer, Logo, Manager Signature)
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS lab_settings (
    id                 BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
    lab_name_ar        TEXT NOT NULL DEFAULT 'مختبر التحليلات الطبية',
    lab_name_en        TEXT NOT NULL DEFAULT 'Medical Laboratory',
    logo_data          BYTEA,
    address            TEXT,
    phones             TEXT,
    email              TEXT,
    report_header_text TEXT,
    report_footer_text TEXT,
    manager_name       TEXT,
    manager_title      TEXT,
    signature_data     BYTEA,
    paper_size         TEXT NOT NULL DEFAULT 'A4',
    currency           TEXT NOT NULL DEFAULT 'IQD',
    label_printer      TEXT,
    report_printer     TEXT,
    receipt_printer    TEXT,
    barcode_offset_mm  DOUBLE PRECISION NOT NULL DEFAULT 15.0,
    theme              TEXT NOT NULL DEFAULT 'Night',
    created_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

-- -----------------------------------------------------------------------------
-- 15. Audit Log & Middleware Raw Traffic Log
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS audit_log (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    user_id    BIGINT REFERENCES users(id) ON DELETE SET NULL,
    action     TEXT NOT NULL,
    table_name TEXT NOT NULL,
    record_id  TEXT NOT NULL,
    old_value  JSONB,
    new_value  JSONB,
    timestamp  TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp()
);

CREATE TABLE IF NOT EXISTS middleware_log (
    id            BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    raw_message   TEXT NOT NULL,
    instrument_id BIGINT REFERENCES instruments(id) ON DELETE SET NULL,
    direction     TEXT NOT NULL CHECK (direction IN ('IN','OUT')),
    timestamp     TIMESTAMPTZ NOT NULL DEFAULT clock_timestamp(),
    parse_status  TEXT
);

-- -----------------------------------------------------------------------------
-- 16. Accession Generator Function (PostgreSQL)
-- Generates unique 6-digit accession, skips already active ones (wraparound safe)
-- -----------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION generate_next_accession()
RETURNS CHAR(6) AS $$
DECLARE
    v_acc CHAR(6);
    v_exists BOOLEAN;
    v_attempts INT := 0;
BEGIN
    LOOP
        v_attempts := v_attempts + 1;
        IF v_attempts > 1000000 THEN
            RAISE EXCEPTION 'Exhausted available active accession numbers in cycle.';
        END IF;

        v_acc := LPAD(nextval('accession_seq')::TEXT, 6, '0');

        SELECT EXISTS (
            SELECT 1 FROM specimens WHERE accession_no = v_acc AND archived = FALSE
        ) INTO v_exists;

        IF NOT v_exists THEN
            RETURN v_acc;
        END IF;
    END LOOP;
END;
$$ LANGUAGE plpgsql;

-- -----------------------------------------------------------------------------
-- 17. Seed Baseline Reference Data
-- -----------------------------------------------------------------------------
INSERT INTO rejection_reasons (name_en, name_ar)
VALUES 
    ('Hemolyzed sample', 'عينة متحللة'),
    ('Clotted blood', 'دم متجلط'),
    ('Insufficient volume', 'كمية غير كافية'),
    ('Wrong tube type', 'نوع أنبوب غير مطابق'),
    ('Lipemic sample', 'عينة دهنية')
ON CONFLICT (name_en) DO NOTHING;

-- Initial Lab Setting row if not exists
INSERT INTO lab_settings (id, lab_name_ar, lab_name_en, currency)
OVERRIDING SYSTEM VALUE
VALUES (1, 'مختبر التحليلات الطبية التخصصي', 'Specialized Medical Laboratory', 'IQD')
ON CONFLICT (id) DO NOTHING;

COMMIT;
