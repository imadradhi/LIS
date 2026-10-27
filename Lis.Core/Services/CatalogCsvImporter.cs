using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class ImportCatalogResult
    {
        public int TestsImported { get; set; }
        public int ComponentsImported { get; set; }
        public int ReferenceRangesImported { get; set; }
        public int InstrumentMappingsImported { get; set; }
        public List<string> Warnings { get; } = new List<string>();
    }

    public class CatalogCsvImporter
    {
        private readonly IDbConnectionFactory _connectionFactory;

        public CatalogCsvImporter(IDbConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory;
        }

        public async Task<ImportCatalogResult> ImportFromDirectoryAsync(string directoryPath)
        {
            var testsFile = Path.Combine(directoryPath, "tests.csv");
            var componentsFile = Path.Combine(directoryPath, "components.csv");
            var rangesFile = Path.Combine(directoryPath, "reference_ranges.csv");
            var instrumentTestsFile = Path.Combine(directoryPath, "instrument_tests.csv");

            if (!File.Exists(testsFile)) throw new FileNotFoundException($"Missing tests.csv at {testsFile}");
            if (!File.Exists(componentsFile)) throw new FileNotFoundException($"Missing components.csv at {componentsFile}");
            if (!File.Exists(rangesFile)) throw new FileNotFoundException($"Missing reference_ranges.csv at {rangesFile}");
            if (!File.Exists(instrumentTestsFile)) throw new FileNotFoundException($"Missing instrument_tests.csv at {instrumentTestsFile}");

            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            using var trans = conn.BeginTransaction();

            var result = new ImportCatalogResult();

            try
            {
                // 1. Import tests.csv
                result.TestsImported = await ImportTestsAsync(conn, trans, testsFile);

                // 2. Import components.csv
                result.ComponentsImported = await ImportComponentsAsync(conn, trans, componentsFile);

                // 3. Import reference_ranges.csv
                result.ReferenceRangesImported = await ImportReferenceRangesAsync(conn, trans, rangesFile);

                // 4. Import instrument_tests.csv
                result.InstrumentMappingsImported = await ImportInstrumentTestsAsync(conn, trans, instrumentTestsFile);

                trans.Commit();
                return result;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        private async Task<int> ImportTestsAsync(IDbConnection conn, IDbTransaction trans, string filePath)
        {
            var rows = ReadCsvRows(filePath);
            int count = 0;

            foreach (var row in rows)
            {
                if (!row.TryGetValue("Test_ID", out var testId) || string.IsNullOrWhiteSpace(testId))
                    continue;

                row.TryGetValue("Test_Name_Ar", out var nameAr);
                row.TryGetValue("Test_Name_En", out var nameEn);
                row.TryGetValue("Section", out var section);
                row.TryGetValue("Sample_Type", out var sampleType);
                row.TryGetValue("Price", out var priceStr);

                decimal initialPrice = 0;
                if (!string.IsNullOrWhiteSpace(priceStr) && decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedPrice))
                {
                    initialPrice = parsedPrice;
                }

                // Parse Department from "Arabic (English)"
                long? departmentId = null;
                if (!string.IsNullOrWhiteSpace(section))
                {
                    var (deptAr, deptEn) = ParseSection(section);
                    departmentId = await EnsureDepartmentAsync(conn, trans, deptEn, deptAr);
                }

                // Parse SpecimenType from Sample_Type
                long? specimenTypeId = null;
                if (!string.IsNullOrWhiteSpace(sampleType))
                {
                    specimenTypeId = await EnsureSpecimenTypeAsync(conn, trans, sampleType.Trim());
                }

                // Upsert test:
                // Rule: Re-importing must NOT overwrite a price that was changed in the app!
                // Price from CSV is only used on initial INSERT.
                const string upsertSql = @"
                    INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, active, updated_at)
                    VALUES (@TestCode, @NameEn, @NameAr, @DepartmentId, @SpecimenTypeId, @InitialPrice, TRUE, clock_timestamp())
                    ON CONFLICT (test_code) DO UPDATE
                    SET name_en = EXCLUDED.name_en,
                        name_ar = EXCLUDED.name_ar,
                        department_id = COALESCE(EXCLUDED.department_id, tests.department_id),
                        specimen_type_id = COALESCE(EXCLUDED.specimen_type_id, tests.specimen_type_id),
                        updated_at = clock_timestamp();
                ";

                await conn.ExecuteAsync(upsertSql, new
                {
                    TestCode = testId.Trim(),
                    NameEn = nameEn?.Trim() ?? testId.Trim(),
                    NameAr = nameAr?.Trim(),
                    DepartmentId = departmentId,
                    SpecimenTypeId = specimenTypeId,
                    InitialPrice = initialPrice
                }, trans);

                count++;
            }

            return count;
        }

        private async Task<int> ImportComponentsAsync(IDbConnection conn, IDbTransaction trans, string filePath)
        {
            var rows = ReadCsvRows(filePath);
            int count = 0;
            int sortOrder = 1;

            foreach (var row in rows)
            {
                if (!row.TryGetValue("Component_ID", out var compId) || string.IsNullOrWhiteSpace(compId))
                    continue;

                row.TryGetValue("Test_ID", out var testId);
                row.TryGetValue("Component_Name", out var nameEn);
                row.TryGetValue("Code", out var displayCode);
                row.TryGetValue("Unit", out var unit);
                row.TryGetValue("Decimal_Places", out var decimalsStr);

                short? decimals = null;
                if (!string.IsNullOrWhiteSpace(decimalsStr) && short.TryParse(decimalsStr, out var d))
                {
                    decimals = d;
                }

                // Lookup test_id
                var testDbId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM tests WHERE test_code = @TestCode", 
                    new { TestCode = testId?.Trim() }, 
                    trans);

                if (!testDbId.HasValue)
                {
                    throw new InvalidOperationException($"Cannot import component {compId}: Parent test '{testId}' was not found.");
                }

                const string upsertSql = @"
                    INSERT INTO components (test_id, component_code, name_en, display_code, unit, decimals, sort_order, updated_at)
                    VALUES (@TestId, @ComponentCode, @NameEn, @DisplayCode, @Unit, @Decimals, @SortOrder, clock_timestamp())
                    ON CONFLICT (component_code) DO UPDATE
                    SET test_id = EXCLUDED.test_id,
                        name_en = EXCLUDED.name_en,
                        display_code = EXCLUDED.display_code,
                        unit = EXCLUDED.unit,
                        decimals = EXCLUDED.decimals,
                        sort_order = EXCLUDED.sort_order,
                        updated_at = clock_timestamp();
                ";

                await conn.ExecuteAsync(upsertSql, new
                {
                    TestId = testDbId.Value,
                    ComponentCode = compId.Trim(),
                    NameEn = nameEn?.Trim() ?? compId.Trim(),
                    DisplayCode = displayCode?.Trim(),
                    Unit = unit?.Trim(),
                    Decimals = decimals,
                    SortOrder = sortOrder++
                }, trans);

                count++;
            }

            return count;
        }

        private async Task<int> ImportReferenceRangesAsync(IDbConnection conn, IDbTransaction trans, string filePath)
        {
            var rows = ReadCsvRows(filePath);
            int count = 0;

            foreach (var row in rows)
            {
                if (!row.TryGetValue("Range_ID", out var rangeId) || string.IsNullOrWhiteSpace(rangeId))
                    continue;

                row.TryGetValue("Component_ID", out var compId);
                row.TryGetValue("Gender", out var genderStr);
                row.TryGetValue("Age_Min", out var ageMinStr);
                row.TryGetValue("Age_Max", out var ageMaxStr);
                row.TryGetValue("Age_Unit", out var ageUnitStr);
                row.TryGetValue("Min_Value", out var minValStr);
                row.TryGetValue("Max_Value", out var maxValStr);

                // Map Gender: 'All' -> 'B', 'Male' -> 'M', 'Female' -> 'F'
                string sex = "B";
                var g = genderStr?.Trim();
                if (string.Equals(g, "Male", StringComparison.OrdinalIgnoreCase) || string.Equals(g, "M", StringComparison.OrdinalIgnoreCase))
                    sex = "M";
                else if (string.Equals(g, "Female", StringComparison.OrdinalIgnoreCase) || string.Equals(g, "F", StringComparison.OrdinalIgnoreCase))
                    sex = "F";

                decimal ageMin = 0;
                if (!string.IsNullOrWhiteSpace(ageMinStr) && decimal.TryParse(ageMinStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMin))
                    ageMin = parsedMin;

                decimal? ageMax = null;
                if (!string.IsNullOrWhiteSpace(ageMaxStr) && decimal.TryParse(ageMaxStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedMax))
                    ageMax = parsedMax;

                string ageUnit = "Years";
                if (!string.IsNullOrWhiteSpace(ageUnitStr))
                {
                    var u = ageUnitStr.Trim();
                    if (u.Equals("Days", StringComparison.OrdinalIgnoreCase)) ageUnit = "Days";
                    else if (u.Equals("Months", StringComparison.OrdinalIgnoreCase)) ageUnit = "Months";
                    else ageUnit = "Years";
                }

                decimal? low = null;
                if (!string.IsNullOrWhiteSpace(minValStr) && decimal.TryParse(minValStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var pLow))
                    low = pLow;

                decimal? high = null;
                if (!string.IsNullOrWhiteSpace(maxValStr) && decimal.TryParse(maxValStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var pHigh))
                    high = pHigh;

                var compDbId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM components WHERE component_code = @ComponentCode",
                    new { ComponentCode = compId?.Trim() },
                    trans);

                if (!compDbId.HasValue)
                {
                    throw new InvalidOperationException($"Cannot import reference range {rangeId}: Component '{compId}' was not found.");
                }

                const string upsertSql = @"
                    INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, updated_at)
                    VALUES (@RangeCode, @ComponentId, @Sex, @AgeMin, @AgeMax, @AgeUnit, @Low, @High, clock_timestamp())
                    ON CONFLICT (range_code) DO UPDATE
                    SET component_id = EXCLUDED.component_id,
                        sex = EXCLUDED.sex,
                        age_min = EXCLUDED.age_min,
                        age_max = EXCLUDED.age_max,
                        age_unit = EXCLUDED.age_unit,
                        low = EXCLUDED.low,
                        high = EXCLUDED.high,
                        updated_at = clock_timestamp();
                ";

                await conn.ExecuteAsync(upsertSql, new
                {
                    RangeCode = rangeId.Trim(),
                    ComponentId = compDbId.Value,
                    Sex = sex,
                    AgeMin = ageMin,
                    AgeMax = ageMax,
                    AgeUnit = ageUnit,
                    Low = low,
                    High = high
                }, trans);

                count++;
            }

            return count;
        }

        private async Task<int> ImportInstrumentTestsAsync(IDbConnection conn, IDbTransaction trans, string filePath)
        {
            var rows = ReadCsvRows(filePath);
            int count = 0;

            foreach (var row in rows)
            {
                if (!row.TryGetValue("Instrument_Code", out var instCode) || string.IsNullOrWhiteSpace(instCode))
                    continue;

                row.TryGetValue("Test_ID", out var testCode);
                row.TryGetValue("Component_ID", out var compCode);
                row.TryGetValue("Instrument_Test_Code", out var instTestCode);
                row.TryGetValue("Reportable_Low", out var repLowStr);
                row.TryGetValue("Reportable_High", out var repHighStr);

                if (string.IsNullOrWhiteSpace(instTestCode))
                    continue;

                decimal? repLow = null;
                if (!string.IsNullOrWhiteSpace(repLowStr) && decimal.TryParse(repLowStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var rLow))
                    repLow = rLow;

                decimal? repHigh = null;
                if (!string.IsNullOrWhiteSpace(repHighStr) && decimal.TryParse(repHighStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var rHigh))
                    repHigh = rHigh;

                // Ensure Instrument exists
                long instDbId = await EnsureInstrumentAsync(conn, trans, instCode.Trim());

                // Lookup test_id and component_id
                var testDbId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM tests WHERE test_code = @TestCode",
                    new { TestCode = testCode?.Trim() },
                    trans);

                var compDbId = await conn.ExecuteScalarAsync<long?>(
                    "SELECT id FROM components WHERE component_code = @ComponentCode",
                    new { ComponentCode = compCode?.Trim() },
                    trans);

                if (!testDbId.HasValue || !compDbId.HasValue)
                {
                    throw new InvalidOperationException($"Cannot map instrument test for {instCode}: Test '{testCode}' or Component '{compCode}' not found.");
                }

                const string upsertSql = @"
                    INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code, reportable_low, reportable_high, updated_at)
                    VALUES (@InstrumentId, @TestId, @ComponentId, @InstrumentTestCode, @ReportableLow, @ReportableHigh, clock_timestamp())
                    ON CONFLICT (instrument_id, component_id) DO UPDATE
                    SET test_id = EXCLUDED.test_id,
                        instrument_test_code = EXCLUDED.instrument_test_code,
                        reportable_low = EXCLUDED.reportable_low,
                        reportable_high = EXCLUDED.reportable_high,
                        updated_at = clock_timestamp();
                ";

                await conn.ExecuteAsync(upsertSql, new
                {
                    InstrumentId = instDbId,
                    TestId = testDbId.Value,
                    ComponentId = compDbId.Value,
                    InstrumentTestCode = instTestCode.Trim(),
                    ReportableLow = repLow,
                    ReportableHigh = repHigh
                }, trans);

                count++;
            }

            return count;
        }

        private async Task<long> EnsureDepartmentAsync(IDbConnection conn, IDbTransaction trans, string nameEn, string? nameAr)
        {
            var id = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM departments WHERE name_en = @NameEn",
                new { NameEn = nameEn },
                trans);

            if (id.HasValue) return id.Value;

            return await conn.ExecuteScalarAsync<long>(
                "INSERT INTO departments (name_en, name_ar) VALUES (@NameEn, @NameAr) RETURNING id",
                new { NameEn = nameEn, NameAr = nameAr },
                trans);
        }

        private async Task<long> EnsureSpecimenTypeAsync(IDbConnection conn, IDbTransaction trans, string nameEn)
        {
            var id = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM specimen_types WHERE name_en = @NameEn",
                new { NameEn = nameEn },
                trans);

            if (id.HasValue) return id.Value;

            return await conn.ExecuteScalarAsync<long>(
                "INSERT INTO specimen_types (name_en) VALUES (@NameEn) RETURNING id",
                new { NameEn = nameEn },
                trans);
        }

        private async Task<long> EnsureInstrumentAsync(IDbConnection conn, IDbTransaction trans, string instCode)
        {
            var id = await conn.ExecuteScalarAsync<long?>(
                "SELECT id FROM instruments WHERE code = @Code",
                new { Code = instCode },
                trans);

            if (id.HasValue) return id.Value;

            return await conn.ExecuteScalarAsync<long>(
                "INSERT INTO instruments (code, name, active) VALUES (@Code, @Name, TRUE) RETURNING id",
                new { Code = instCode, Name = instCode },
                trans);
        }

        private static (string? Ar, string En) ParseSection(string section)
        {
            // Format: "امراض الدم (Hematology)"
            var match = Regex.Match(section, @"^(.*?)\s*\((.*?)\)$");
            if (match.Success)
            {
                return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
            }

            return (null, section.Trim());
        }

        private static List<Dictionary<string, string>> ReadCsvRows(string filePath)
        {
            // Handle UTF-8 with BOM automatically
            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            var result = new List<Dictionary<string, string>>();

            if (lines.Length == 0) return result;

            // First line is header, strip BOM if present
            var headerLine = lines[0].TrimStart('\uFEFF');
            var headers = ParseCsvLine(headerLine);

            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) continue;

                var values = ParseCsvLine(line);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                for (int h = 0; h < headers.Count; h++)
                {
                    row[headers[h]] = h < values.Count ? values[h] : string.Empty;
                }

                result.Add(row);
            }

            return result;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var sb = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    values.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            values.Add(sb.ToString().Trim());
            return values;
        }
    }
}
