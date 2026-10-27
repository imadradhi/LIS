# LIS Project: Work Plan for the AI Coding Agent

> **Role of the agent:** senior C# / PostgreSQL developer building a Laboratory Information System (LIS) for a medical laboratory.
> **Working rules:** work phase by phase, finish and test each phase before starting the next, ask before changing any decision in section 2, never invent medical data (reference ranges, test codes, instrument codes). Leave them as configurable data to be filled by the lab.

---

## 1. Project Summary

A Windows desktop LIS for one lab, developed by a single developer.

**Workflow:**

1. The employee registers the patient (or finds an existing one) and records the requested tests.
2. After saving, the app generates one **specimen barcode (Accession Number) per tube** and prints a label.
3. The employee sticks the label on the tube and loads it into the analyzer.
4. The analyzer reads the barcode and sends a **query** to the **LIS Middleware** (separate app).
5. The Middleware checks PostgreSQL and returns **only the tests that this analyzer performs** for that barcode.
6. The analyzer runs the tests and sends **results** to the Middleware, which saves them with status "Pending review".
7. The employee reviews, **validates (approves)**, and **prints the report** for the patient.

**Instruments (first batch, more will be added later):**

| Instrument | Department | Typical specimen |
|---|---|---|
| Siemens Dimension EXL 200 | Chemistry | Serum |
| Siemens Immulite 2000 XPi | Hormones / Immunoassay | Serum |
| Sysmex XN-350 | Hematology (CBC + 5-Part Diff + IG) | Whole blood EDTA (ASTM / TCP) |
| Siemens Advia 360 | Hematology | Whole blood EDTA (ASTM / TCP) |
| Siemens Advia 560 | Hematology (to confirm) | Whole blood EDTA |

Protocol (ASTM E1394 / LIS2-A2 or HL7, TCP/IP or RS-232) must be confirmed per instrument from its Interface Manual. Design the Middleware so each instrument has its own **driver/adapter** behind a common interface.

---

## 2. Fixed Decisions (do not change without asking)

1. **Technology:** C# (.NET 8), Windows Desktop (WPF preferred, WinForms acceptable), PostgreSQL, Entity Framework Core or Dapper (choose one and stay consistent).
2. **Architecture:** 3 deployable parts: `Lis.App` (desktop), `Lis.Middleware` (Windows Service / console in dev), PostgreSQL. Shared code in `Lis.Core` (entities, enums, DB access).
3. **Patient identity (MRN):** one permanent number per patient, never changes. Simple sequence (e.g. 100001, 100002...). Shown on the label as `P.ID`. **Never sent to analyzers.**
4. **Accession Number:** **exactly 6 digits** (`000001` to `999999`), generated from a PostgreSQL sequence with `CYCLE`, and must be **unique among active (non-archived) specimens**. One accession per **tube/specimen**, new for each visit. This is the only value encoded in the barcode.
5. **Barcode:** Code 128 (digits only), no extra checksum, quiet zone at least 3 mm.
6. **Label:** 5 cm x 2.5 cm (see section 6).
7. **Every patient visit creates a new Order**; one Order can have several Specimens (tubes), each specimen holds several tests.
8. **Result states:** `Ordered -> Collected -> InProgress -> ResultReceived -> Validated -> Printed` (plus `Rejected` and `Cancelled`).
9. **Audit log is mandatory** for any create/edit/validate/cancel action on orders and results.
10. **Language:** UI in Arabic (RTL) with English test names; reports bilingual if needed.
11. Start with **one instrument fully working** (Dimension EXL 200 or the one the lab chooses), then add the others.
12. **Instrument communication:** the Middleware has a **settings screen where, for each instrument, the user chooses ONE connection type: `TCP/IP` or `RS-232`**. The choice is stored in `instruments.connection_type` and the matching fields appear:
    - **If TCP/IP:** Mode (`Server` = instrument connects to us, or `Client` = we connect to the instrument), IP address, Port.
    - **If RS-232:** COM port, Baud rate, Data bits, Parity, Stop bits, Flow control.
    - Common fields: instrument name/code, protocol (ASTM / HL7), operating mode (decision 13), enabled (yes/no), **Test Connection** button, live status (Connected / Disconnected).
    - Implementation: a transport abstraction (`ITransport` with `TcpTransport` and `SerialTransport`), created by a factory from the saved setting, so the ASTM/HL7 protocol logic is independent of the physical link. Nothing is hard-coded; all values come from the `instruments` table.
    - Auto-reconnect on connection loss. TCP preferred where available; RS-232 for older instruments (USB-to-Serial adapter or serial-to-Ethernet converter).
    - The Middleware runs continuously as a **Windows Service** on an always-on PC with a static IP (see decision 18).
13. **Instrument operating mode (per instrument, configurable):**
    - `Bidirectional`: the instrument sends a **host query** with the barcode, the Middleware replies with the **worklist** (only tests mapped to that instrument).
    - `ResultsOnly`: the instrument sends results directly, identified by the sample ID (accession). Common on hematology analyzers. The Middleware matches the accession to the specimen and saves the results, with no worklist reply.
    - Confirm the mode of each instrument from its Interface Manual and settings.
14. **Reruns and multiple results:** never overwrite. Every result received is stored; the newest is `is_current = true`, older ones stay in `result_history`. The validator can choose which result is final. Also store instrument result flags/comments (e.g. dilution, instrument alarms) and show them to the validator.
15. **QC (control) samples:** analyzers also send quality-control runs that have control barcodes. The Middleware must **recognize unknown barcodes / QC samples**, store them separately (`qc_results`) and never raise an error or mix them with patient results.
16. **Real-time updates:** when the Middleware saves a result, the desktop app must refresh automatically (PostgreSQL `LISTEN/NOTIFY`, with a polling fallback every few seconds).
17. **Security:** passwords hashed with Argon2 or bcrypt (never plain text); separate PostgreSQL users for App and Middleware with minimum privileges (no superuser); auto-lock or logout after inactivity; users can be disabled but never deleted.
18. **Deployment topology (default, confirm with owner):** one always-on PC acts as server (PostgreSQL + Middleware as a service), connected to the analyzers; reception and result workstations run `Lis.App` over the LAN; UPS recommended for the server and analyzers.
19. **No roles/permissions:** every logged-in user has full access to all features. Keep a simple **login (username + password)** only to record **who did each action** (audit log, validator name on the report). No role tables, no permission screens. Admin-only protection is limited to the settings screens (optional password).
20. **Volume:** expected maximum is about **100 patients per day** (roughly 200 to 300 tubes). A single PC server, a 6-digit accession and PostgreSQL are more than enough; do not over-engineer for scale.
21. **No data migration:** there is no old system or Excel data. Start with an empty database.
22. **Billing is in scope:** every test has a price; each order produces an invoice/receipt with discount and payment tracking (see Phase 2).
23. **QC is in scope** (see section 7, Phase 5 and the QC explanation below).
24. **Report header is editable from the app** (no code change): lab name (Arabic/English), logo image, address, phones, email, report footer text, doctor/manager name and signature or stamp image, paper size. Stored in `lab_settings`, with a live preview in the settings screen. The same applies to the invoice/receipt header.

**What is QC (for the developer):** Quality Control means running **control samples with known target values** on each analyzer (usually 2 levels, normal and abnormal, once or twice per day or per shift) to prove the instrument gives accurate results. The LIS must: receive the QC results from the instrument (they arrive with the control sample's own barcode/ID), store them separately from patient results, compare each value to the control's **mean and SD** (target value and acceptable range from the control lot insert), show them on a **Levey-Jennings chart**, flag violations using **Westgard rules** (e.g. 1-3s, 2-2s, R-4s), and record corrective action. A patient result should show a warning if the QC for that test and instrument failed today.

25. **Currency and price:** the currency is the **Iraqi dinar (IQD)**, amounts are shown as whole numbers with thousands separators (e.g. 1,000), the currency name is a setting in `lab_settings`. Every test has a price (**CBC starts at 1,000 IQD**) that the user can change at any time from the catalog screen. Invoices copy the price at the time of the order, so a later price change never alters old invoices. A bulk-edit option (for example "increase all prices by N%") is welcome.
26. **Middleware Log Retention Policy (Database Bloat Prevention):** raw ASTM/HL7 communication frames in `middleware_log` will not be kept indefinitely. A rolling retention window (e.g. 7 to 14 days or rolling daily log files on disk) is enforced to prevent database bloat, automatically cleaning older raw communication frames while patient results in `results` and `result_history` remain permanently archived and untouched.

---

## 3. Test Catalog (real files provided by the lab)

The catalog is **data, not code**. The agent builds the structure, a **CSV importer** and a **catalog screen** for editing; it must never hard-code tests. **Four real CSV files are delivered with this plan** (folder `catalog/`) for the first test (CBC on ADVIA 360). They define the exact import format, and the same format is used for every future test and instrument.

### 3.1 Concepts

| Concept | Meaning | Example |
|---|---|---|
| **Test (orderable)** | What the employee requests; has a price | CBC |
| **Component (result parameter)** | One value reported inside a test | WBC, HGB, PLT |
| **Reference range** | Normal range of a component by **sex and age band** | HGB, Male, 18 to 100 Years: 13.0 to 17.0 |
| **Instrument mapping** | The code the analyzer uses for each component | `LYM%` on the ADVIA 360 |
| **Profile / Panel** | A group of tests ordered together (later) | Liver Function Test |

### 3.2 Delivered files (UTF-8 with BOM; the importer must handle the BOM)

**`tests.csv`**: one row per orderable test.
```
Test_ID,Test_Name_Ar,Test_Name_En,Section,Sample_Type,Price
T_CBC_360,صورة الدم الكاملة (CBC),Complete Blood Count,امراض الدم (Hematology),EDTA Whole Blood,1000
```
- `Section` is written as `Arabic (English)`: split it into the department's Arabic and English names.
- `Sample_Type` becomes the specimen type (this text is printed on the tube label).
- `Price` is in **Iraqi dinar (IQD)**. It is only the **initial value**: the price must be **editable from the app** (decision 25).

**`components.csv`**: what each test contains.
```
Component_ID,Test_ID,Component_Name,Code,Unit,Decimal_Places
C_WBC,T_CBC_360,White Blood Cells,WBC,10^9/L,1
```
- `Component_ID` is the unique key; `Code` is the short label shown on screen and on the report; `Decimal_Places` controls how results are displayed and printed.
- File order = display order on screen and report.

**`reference_ranges.csv`**: normal ranges by sex and age.
```
Range_ID,Component_ID,Gender,Age_Min,Age_Max,Age_Unit,Min_Value,Max_Value
R10,C_HGB,Male,18,100,Years,13.0,17.0
```
- `Gender`: `All` (stored as `B`), `Male` (`M`), `Female` (`F`).
- `Age_Unit`: `Days`, `Months` or `Years`. The importer stores the values as entered and the database converts them to days automatically (year = 365.25 days, month = 30.4375 days, rounded down). `Age_Min` is inclusive, `Age_Max` is **exclusive**.
- A component with no row (currently PDW-SD, P-LCR, P-LCC) simply gets **no High/Low flag**.
- **Current data covers adults only (18 to 100 years).** For a child, or any patient outside every band, there is no range: show the result without a flag and display a small notice "no reference range for this age", never an error. The lab will add pediatric bands later from the catalog screen.
- Lookup rule: pick the row matching the patient's age (at the order date) and sex; if none matches the sex, use the `All` row; if still none, no range. The lookup query is in section 4.

**`instrument_tests.csv`**: mapping of each component to the instrument's own code.
```
Instrument_Code,Test_ID,Component_ID,Instrument_Test_Code,Reportable_Low,Reportable_High
ADVIA360,T_CBC_360,C_LYM_pct,LYM%,,
```
- `Instrument_Test_Code` is the code the analyzer sends/receives. It may differ from the display `Code` (e.g. the instrument sends `LYM`, the screen shows `LYM#`). **Always match instrument messages through this table**, never through `Code`.
- `Reportable_Low/High` is the instrument's measuring range (optional).

### 3.3 Import and edit requirements

- The importer is **repeatable (upsert)**: it matches by `Test_ID`, `Component_ID`, `Range_ID` and (`Instrument_Code`, `Component_ID`), so running it twice never duplicates rows.
- **Re-importing must NOT overwrite a price that was changed in the app.** Price from the CSV is used only when the test is first created.
- The catalog screen lets the user add/edit tests, components, units, decimals, price, reference ranges (age band rows, with a warning if bands of the same component and sex overlap or leave a gap), and instrument mapping, with no need for CSV.
- Units note: the lab's files use `10^9/L` and `10^12/L`; these are numerically identical to the analyzer's `10^3/uL` and `10^6/uL`, so **no value conversion** is needed.

### 3.4 What the lab must still provide for the next tests

- [ ] For each new test: the same four CSV formats (Chemistry on Dimension EXL 200, Hormones on Immulite 2000 XPi, and Sysmex hematology if used).
- [ ] Price for each test.
- [ ] Pediatric (and other) reference ranges when needed.
- [ ] Interface Manual for every instrument + connection type.
- [ ] Report layout sample (the header itself is editable in the app).

---

## 4. Database Design (PostgreSQL)

Use snake_case, `created_at` / `updated_at` / `created_by` on all tables, UUID or bigint identity PKs. Core tables:

| Table | Purpose / key columns |
|---|---|
| `patients` | id, `mrn` (unique, from sequence), full_name_ar, full_name_en, sex, date_of_birth, phone, national_id (optional), notes |
| `orders` | id, patient_id, visit_no, order_datetime, referring_doctor, clinical_notes, status |
| `specimen_types` | id, name_en, name_ar, color |
| `specimens` | id, order_id, specimen_type_id, `accession_no CHAR(6)`, status, collected_at, issued_at, archived |
| `departments` | id, name_en, name_ar, letter |
| `tests` | id, test_code (= Test_ID), name_en, name_ar, department_id, specimen_type_id, is_profile, `price NUMERIC(12,2)` in IQD (editable in the app), sort_order, active |
| `components` | id, test_id, component_code (= Component_ID), name_en, name_ar, display_code (= Code), unit, decimals, result_type, notes, sort_order |
| `profile_items` | profile_test_id, member_test_id |
| `reference_ranges` | id, range_code, component_id, sex (M/F/B), age_min, age_max, age_unit (Days/Months/Years), `age_from_days` and `age_to_days` (generated columns, computed from the three age fields), low, high, critical_low, critical_high |
| `instruments` | id, code, name, protocol (ASTM/HL7), `connection_type` (TCP/RS232), `operating_mode` (Bidirectional/ResultsOnly), tcp_mode (Server/Client), host, port, com_port, baud_rate, data_bits, parity, stop_bits, flow_control, active |
| `instrument_tests` | instrument_id, test_id, component_id, instrument_test_code, reportable_low, reportable_high |
| `order_tests` | id, order_id, specimen_id, test_id, status |
| `results` | id, order_test_id, component_id, value_text, value_numeric, unit, flag (N/H/L/HH/LL), instrument_id, resulted_at, validated_by, validated_at, is_current |
| `result_history` | previous values when a result is re-run/edited |
| `users` | id, username, full_name, password hash, active (no roles: all users have full access; used only to record who did what) |
| `audit_log` | user_id, action, table_name, record_id, old_value, new_value, timestamp |
| `middleware_log` | raw messages in/out, instrument_id, direction, timestamp, parse_status |
| `qc_materials` | id, name, lot_no, level (1/2/3), expiry_date, active |
| `qc_targets` | qc_material_id, instrument_id, component_id, mean, sd, unit |
| `qc_results` | instrument_id, qc_material_id, component_id, value, received_at, rule_violation (e.g. 1-3s), accepted (bool), corrective_action, user_id |
| `specimen_rejections` | specimen_id, reason_id, notes, user_id, datetime (e.g. hemolyzed, clotted, insufficient volume) |
| `rejection_reasons` | id, name_en, name_ar |
| `result_comments` | result/order_test id, comment text, user, datetime (lab comments printed on the report) |
| `doctors` | id, name, specialty, phone (referring doctors list) |
| `calculated_components` | component_id, formula (derived values such as Indirect Bilirubin, LDL calc, A/G ratio), input components |
| `lab_settings` | key/value or single row: lab name (ar/en), address, phones, email, logo (image), report header/footer text, manager/pathologist name, signature/stamp image, paper size, currency, label printer, report printer, receipt printer |
| `invoices` | id, order_id, invoice_no (sequence), total, discount, net, paid, balance, status (Unpaid/Partial/Paid/Cancelled), created_by, datetime |
| `invoice_items` | invoice_id, test_id, price_at_time (price copied at order time, so later price changes do not alter old invoices) |
| `payments` | invoice_id, amount, method (cash/card/other), datetime, user_id |
| `price_lists` (optional) | id, name (e.g. normal, insurance), test_id, price (only if the lab uses several price lists) |

### Reference range lookup (required implementation)

```sql
SELECT low, high, critical_low, critical_high
FROM reference_ranges
WHERE component_id = :component
  AND sex IN (:sex, 'B')
  AND age_from_days <= :age_days
  AND (age_to_days IS NULL OR :age_days < age_to_days)
ORDER BY (sex = :sex) DESC      -- the sex-specific row first
LIMIT 1;                         -- no row = no flag, no error
```
`:age_days` = order date minus date of birth, in days. A reference seed script, `catalog/CBC_ADVIA360_seed.sql`, shows the exact table definitions, including the generated age columns.

### Accession generation (required implementation)

```sql
CREATE SEQUENCE accession_seq START 1 MAXVALUE 999999 CYCLE;
CREATE SEQUENCE mrn_seq START 100001;

ALTER TABLE specimens
  ADD CONSTRAINT chk_accession CHECK (accession_no ~ '^[0-9]{6}$');

-- Unique only among active specimens (wraparound safety)
CREATE UNIQUE INDEX ux_specimens_active_accession
  ON specimens (accession_no) WHERE archived = false;
```

Generation function must: take `nextval`, left-pad to 6 digits, **skip any value already used by an active specimen** (loop), and run inside the same transaction that inserts the specimen. Specimens older than N days are marked `archived = true` by a scheduled job. The Middleware must always match the **latest non-archived** specimen for a barcode.

---

## 5. Solution Structure

```
Lis.sln
 ├─ Lis.Core            (entities, enums, DbContext/Repositories, accession service)
 ├─ Lis.App             (WPF/WinForms UI, printing, reports)
 ├─ Lis.Middleware      (service host, instrument listeners)
 │    └─ Drivers/       (IInstrumentDriver + one class per instrument)
 ├─ Lis.InstrumentSim   (fake analyzer for testing, sends ASTM messages)
 └─ Lis.Tests           (unit + integration tests)
```

Common driver interface (example):

```csharp
public interface IInstrumentDriver
{
    string InstrumentCode { get; }
    Task StartAsync(CancellationToken ct);
    // Parse incoming message -> either a Query or Results
    // Build reply -> worklist for the queried barcode (only tests this instrument supports)
}
```

---

## 6. Label Specification (5 cm x 2.5 cm)

| Zone | Content |
|---|---|
| Top (about 45%) | Code 128 barcode with the 6-digit accession only. Height at least 10 mm, module width 0.3 to 0.4 mm |
| Line 1 left | `Accession - Date of birth` (e.g. `200586 - 27-01-1980`) |
| Line 1 right | `P.ID: <MRN>` |
| Line 2 left | Patient name (Arabic, RTL) |
| Line 2 right | Specimen type name (e.g. `whole blood, EDTA`) |

Implementation notes:
- Render the label as an image/GDI drawing in C# (Arabic shaping is unreliable in ZPL). Use **ZXing.Net** or **BarcodeLib** for the barcode. Send to the label printer through the Windows driver or as a bitmap.
- One label template defined in a single place (dimensions in mm, positions configurable).
- Print one label per specimen automatically after saving the order. Include a **Reprint label** button.
- Optional: small department letter or color mark in a corner.

---

## 7. Phases, Tasks, and Acceptance Criteria

### Phase 1: Database and Catalog (about 1 week)
- Create all tables, sequences, constraints, indexes, and seed data for specimen types, departments and rejection reasons.
- Build the CSV importers for the four delivered files (section 3.2) with BOM handling, upsert behavior, and the rule that re-import never overwrites an edited price. Import the delivered CBC files as the first real data.
- **Done when:** the schema runs on a clean DB, the delivered CBC files import with 1 test, 22 components, 22 reference ranges and 22 instrument mappings, importing twice gives the same counts, and accession generation passes tests (uniqueness, padding, wraparound skip).

### Phase 2: Desktop App, Registration, Labels and Billing (5 to 6 weeks)
- Simple login (username/password, no roles) to record who did each action.
- Patient search (by name, MRN, phone) and create/edit patient.
- New order: select tests/profiles, auto-group tests by specimen type, create one specimen (accession) per group.
- Print labels per section 6, with reprint.
- Order list with status filters, and a **department worklist** (specimens waiting for results).
- Search by scanning a barcode (barcode scanner works as keyboard input): scan an accession to open its order.
- **Specimen receiving** step (status Collected), **rejection** with reason and recollection (new accession), **add-on tests** to an existing order (new or existing tube), and cancel order/test with reason.
- Duplicate patient warning (same name + date of birth), age calculation in days/months/years.
- **Lab settings screen** with editable report/invoice header and footer (lab name, logo, address, phones, signature/stamp image), currency, printers, with a print preview (decision 24).
- **Billing:** price per test (editable in the catalog screen), invoice created with the order, discount, partial/full payment, receipt printing (A5 or thermal), unpaid balance list, cancel/refund with reason, and a **daily cash report** (total invoiced, paid, discounts, by user).
- **Done when:** registering a patient with tests from 2 specimen types produces 2 correct labels, and a returning patient keeps the same MRN with new accessions.

### Phase 3: Middleware (3 to 4 weeks)
- Build `Lis.InstrumentSim` first and test everything against it.
- Common listener (TCP and serial), ASTM framing (ENQ/ACK/STX/ETX/checksum/EOT), message parser, and logging of all raw traffic.
- Host query handling: receive barcode, find specimen, return only tests mapped in `instrument_tests` for that instrument.
- Receive results, map instrument codes to components, save with flags, status `ResultReceived`.
- Support both operating modes (`Bidirectional` and `ResultsOnly`), QC sample handling, reruns and dilutions, and instrument flags/comments (decisions 13 to 15).
- Capture **real sample messages** from each instrument (raw log) and use them as test fixtures for the parser; keep one test project per driver.
- Notify the app in real time when results arrive (decision 16).
- Middleware settings screen with TCP/RS-232 selection per instrument and Test Connection button.
- Handle errors: unknown barcode, no matching tests, duplicate results, connection loss and auto-reconnect.
- Then implement the first real instrument driver, then the others one at a time.
- **Done when:** the simulator and the first real instrument complete the full cycle (query, worklist, result, saved in DB) with no manual DB edits.

### Phase 4: Results, Validation, and Reports (3 to 4 weeks)
- Results screen per order (grouped by department), showing flags and previous results for the same patient.
- Manual result entry for tests without an instrument.
- Validate (single and bulk), re-run request, edit with reason (saved in history and audit log).
- Calculated (derived) tests from formulas, lab comments per result, **critical value alert** (prominent warning before validation) and optional **delta check** (big change vs previous result).
- Patient history: previous results and a cumulative (trend) report for the same patient.
- Report printing using the editable header from `lab_settings` (decision 24), with preview and **PDF export**, Arabic RTL support, separate layout per department if needed (QuestPDF or FastReport): patient header, results with units, ranges, flags, validator name, date.
- **Done when:** an order goes from registration to a printed report with correct flags and audit trail.

### Phase 5: Quality Control (QC) (2 weeks)
- QC materials (lot, level, expiry) and targets (mean/SD) per instrument and test, entered in the app.
- Middleware recognizes QC samples (decision 15) and saves them in `qc_results`.
- QC screen: daily list of QC results, **Levey-Jennings chart** per test/instrument/level, **Westgard rule** evaluation, accept/reject with corrective action note.
- Warning on the results screen when today's QC for that test and instrument failed or was not run.
- Printable QC report for a date range.
- **Done when:** QC results from the simulator appear on a Levey-Jennings chart, a deliberate out-of-range value triggers the correct rule, and patient results show the QC warning.

### Phase 6: Hardening and Field Test (2 to 3 weeks)
- Security review (decision 17), restore test of a backup on another machine (a backup that was never restored is not trusted), off-PC copy of backups (external disk or network folder).
- Audit log viewer, log retention for `middleware_log`, automatic daily PostgreSQL backup (pg_dump scheduled), config file for DB and printers, installer/deployment notes.
- Instrument validation checklist per instrument: results from the LIS match the instrument printout for a set of real samples.
- Short user guide (reception, technician, validator) and admin guide (backup, restore, adding an instrument, adding a test); staff training.
- Pilot in the lab with real samples in parallel with the old method.
- **Done when:** a full working day runs without data loss or manual fixes.

### Phase 7 (optional, after go-live)
- Statistics dashboard: daily counts, tests per department, turnaround time.
- Add more instruments, and optional SMS/WhatsApp or online result delivery.

---

## 8. Quality Rules for the Agent

1. Write unit tests for: accession generation, test grouping by specimen type, flag calculation, ASTM parsing.
2. All DB changes through **migrations** (EF Core migrations or numbered SQL scripts).
3. No hard-coded connection strings, ports, printer names, or test lists: use configuration.
4. Log everything the Middleware sends and receives (raw text) for troubleshooting.
5. Never delete medical data; use status flags and the audit log.
6. Use structured logging (Serilog) with log files rotated daily for both App and Middleware; show friendly error messages to the user and details in the log.
7. All dates stored in UTC or with timezone, displayed in local time; date format dd-MM-yyyy; support Arabic digits only if the owner asks.
8. After each phase: give a short summary of what was built, how to run it, and what is pending. Ask for the missing catalog or instrument information instead of guessing.

---

## 9. Open Items (the owner will provide)

- [x] CBC catalog files for ADVIA 360 (delivered in `catalog/`); [ ] catalog files for the other tests and instruments (same four formats)
- [x] Sysmex model confirmed: **Sysmex XN-350** (ASTM E1394/E1381 over TCP/IP, Bidirectional, CBC + 5-Part Diff + IG).
- [ ] Confirm the exact type of Advia 360 / 560
- [ ] Interface Manuals for all instruments, and connection type (LAN or serial) for each
- [ ] Label printer model
- [ ] Report layout sample
- [ ] Number of workstations (single PC or network) and whether the Middleware runs on the same PC as the DB
- [ ] Prices for the remaining tests (CBC is set to 1,000 IQD; currency is IQD).
- [ ] Report and invoice header details (can be entered later from the settings screen).
- [ ] QC control materials in use (names, lot numbers, levels, mean and SD from the insert), per instrument.
- [ ] Barcode scanner model (or use keyboard-wedge scanner), and report/receipt printer type (A4 / A5 / thermal).

**Answered by the owner:** currency IQD and CBC price 1,000 (editable in the app); no old data to migrate; billing needed; QC needed; no user roles (open access); maximum about 100 patients per day; report header must be editable from the app.

---

## 10. Saved Decisions & Workflows (Added During Development)

- **Decision 25:** Middleware Live Traffic Log — Added Clear & Copy buttons, and distinct color-coding for frames (Green `#10B981` for IN reception, Cyan `#38BDF8` for OUT transmission).
- **Decision 26:** Middleware Log Retention Policy — 30-day auto-purge for `middleware_log` with manual truncation capability via UI.
- **Decision 27:** Sysmex XN-350 Automated CBC Integration:
  - **Device Configuration:** Code `SYSMEX_XN350`, Protocol `ASTM`, Connection `TCP Server` (Port 5100), Mode `Bidirectional`.
  - **Test Mapping:** Complete Blood Count (`T_CBC_360`) mapped with 26 parameters in `instrument_tests`:
    - Standard CBC: WBC, RBC, HGB, HCT, MCV, MCH, MCHC, PLT, RDW-SD, RDW-CV, MPV, PCT, PDW, P-LCR.
    - 5-Part Diff: NEUT%, LYMPH%, MONO%, EO%, BASO%, NEUT#, LYMPH#, MONO#, EO#, BASO#.
    - Sysmex Advanced Markers: IG% and IG# (Immature Granulocytes).
  - **Status:** [COMPLETED & VERIFIED] Seed script `catalog/SYSMEX_XN350_seed.sql` executed, end-to-end integration test `Test22_Sysmex_XN350_Integration_HostQuery_And_5PartDiffResults` passing with full Host Query, worklist retrieval, and 5-part differential + IG ingestion.

