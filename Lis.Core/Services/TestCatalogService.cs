using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Data;

namespace Lis.Core.Services
{
    public class DepartmentDto
    {
        public long Id { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string DisplayName => !string.IsNullOrWhiteSpace(NameAr) ? $"{NameAr} ({NameEn})" : NameEn;
        public string CleanTitle => !string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn;
    }

    public class SpecimenTypeDto
    {
        public long Id { get; set; }
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string? Color { get; set; }
        public string DisplayName => !string.IsNullOrWhiteSpace(NameAr) ? $"{NameAr} ({NameEn})" : NameEn;
    }

    public class TestCatalogDto
    {
        public long Id { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public long? DepartmentId { get; set; }
        public string? DepartmentName { get; set; }
        public long? SpecimenTypeId { get; set; }
        public string? SpecimenTypeName { get; set; }
        public string? SpecimenColor { get; set; }
        public decimal Price { get; set; }
        public bool Active { get; set; } = true;
        public int SortOrder { get; set; }

        public string FormattedPrice => $"{Price:#,##0} IQD";
        public string StatusDisplay => Active ? "مفعل (Active) 🟢" : "معطل (Inactive) 🔴";
    }

    public class ComponentDto
    {
        public long Id { get; set; }
        public long TestId { get; set; }
        public string ComponentCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string DisplayCode { get; set; } = string.Empty;
        public string? Unit { get; set; }
        public short Decimals { get; set; } = 1;
        public string ResultType { get; set; } = "numeric"; // 'numeric','text','option'
        public string? Notes { get; set; }
        public int SortOrder { get; set; }
        public int RangesCount { get; set; }
        public long? InstrumentId { get; set; }
        public string? InstrumentTestCode { get; set; }
        public string? InstrumentName { get; set; }

        public string DisplaySummary => $"{DisplayCode} - {NameEn}" + (!string.IsNullOrWhiteSpace(Unit) ? $" ({Unit})" : "");
    }

    public class ReferenceRangeDto
    {
        public long Id { get; set; }
        public string? RangeCode { get; set; }
        public long ComponentId { get; set; }
        public string Sex { get; set; } = "B"; // 'M', 'F', 'B'
        public decimal AgeMin { get; set; } = 0;
        public decimal? AgeMax { get; set; } = 100;
        public string AgeUnit { get; set; } = "Years"; // 'Days', 'Months', 'Years'
        public decimal? Low { get; set; }
        public decimal? High { get; set; }
        public decimal? CriticalLow { get; set; }
        public decimal? CriticalHigh { get; set; }
        public string? Note { get; set; }

        public string SexDisplay => Sex switch
        {
            "M" => "ذكر (Male) 👨",
            "F" => "أنثى (Female) 👩",
            _ => "كلاهما (Both/All) 🚻"
        };

        public string AgeDisplay => AgeMax.HasValue
            ? $"{AgeMin:0.#} - {AgeMax.Value:0.#} {AgeUnit}"
            : $">= {AgeMin:0.#} {AgeUnit}";

        public string RangeDisplay => (Low.HasValue || High.HasValue)
            ? $"{Low?.ToString("0.##") ?? "—"}  إلى  {High?.ToString("0.##") ?? "—"}"
            : "بدون مدى رقمي";

        public string CriticalDisplay => (CriticalLow.HasValue || CriticalHigh.HasValue)
            ? $"[{CriticalLow?.ToString("0.##") ?? "—"} / {CriticalHigh?.ToString("0.##") ?? "—"}]"
            : "—";
    }

    public class InstrumentSimpleDto
    {
        public long Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string DisplayName => $"{Name} ({Code})";
    }

    public class InstrumentMappingItemDto
    {
        public long Id { get; set; }
        public long InstrumentId { get; set; }
        public string InstrumentCode { get; set; } = string.Empty;
        public string InstrumentName { get; set; } = string.Empty;
        public long TestId { get; set; }
        public long ComponentId { get; set; }
        public string InstrumentTestCode { get; set; } = string.Empty;
        public decimal? ReportableLow { get; set; }
        public decimal? ReportableHigh { get; set; }
        public string DisplayTitle => $"{InstrumentName} ({InstrumentCode}) ➜ [{InstrumentTestCode}]";
    }

    public interface ITestCatalogService
    {
        Task<IEnumerable<TestCatalogDto>> GetAllTestsAsync(string? search = null);
        Task<TestCatalogDto?> GetTestByIdAsync(long testId);
        Task<IEnumerable<DepartmentDto>> GetDepartmentsAsync();
        Task<IEnumerable<SpecimenTypeDto>> GetSpecimenTypesAsync();
        Task<long> SaveTestAsync(TestCatalogDto test, long? userId = null);
        Task ToggleTestActiveAsync(long testId, bool active, long? userId = null);
        Task<bool> DeleteTestAsync(long testId, long? userId = null);

        // Components / Parameters
        Task<IEnumerable<ComponentDto>> GetComponentsForTestAsync(long testId);
        Task<ComponentDto?> GetComponentByIdAsync(long componentId);
        Task<long> SaveComponentAsync(ComponentDto comp, long? userId = null);
        Task<bool> DeleteComponentAsync(long componentId, long? userId = null);

        // Reference Ranges
        Task<IEnumerable<ReferenceRangeDto>> GetReferenceRangesAsync(long componentId);
        Task<long> SaveReferenceRangeAsync(ReferenceRangeDto range, long? userId = null);
        Task<bool> DeleteReferenceRangeAsync(long rangeId, long? userId = null);

        // Instruments & Channel Mappings
        Task<IEnumerable<InstrumentSimpleDto>> GetInstrumentsAsync();
        Task<IEnumerable<InstrumentMappingItemDto>> GetInstrumentMappingsForComponentAsync(long componentId);
        Task AddOrUpdateInstrumentMappingAsync(long instrumentId, long testId, long componentId, string instrumentTestCode, decimal? reportableLow = null, decimal? reportableHigh = null, long? userId = null);
        Task DeleteInstrumentMappingAsync(long instrumentId, long componentId, long? userId = null);
    }

    public class TestCatalogService : ITestCatalogService
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IAuthService _authService;

        public TestCatalogService(IDbConnectionFactory connectionFactory, IAuthService authService)
        {
            _connectionFactory = connectionFactory;
            _authService = authService;
        }

        public async Task<IEnumerable<TestCatalogDto>> GetAllTestsAsync(string? search = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT t.id, t.test_code AS TestCode, t.name_en AS NameEn, t.name_ar AS NameAr,
                       t.department_id AS DepartmentId, d.name_en AS DepartmentName,
                       t.specimen_type_id AS SpecimenTypeId, st.name_en AS SpecimenTypeName, st.color AS SpecimenColor,
                       t.price, t.active, t.sort_order AS SortOrder
                FROM tests t
                LEFT JOIN departments d ON d.id = t.department_id
                LEFT JOIN specimen_types st ON st.id = t.specimen_type_id
                WHERE (@Search IS NULL OR @Search = '' OR 
                       t.test_code ILIKE '%' || @Search || '%' OR 
                       t.name_en ILIKE '%' || @Search || '%' OR 
                       COALESCE(t.name_ar, '') ILIKE '%' || @Search || '%')
                ORDER BY t.sort_order ASC, t.id ASC;
            ";

            return await conn.QueryAsync<TestCatalogDto>(sql, new { Search = search?.Trim() });
        }

        public async Task<IEnumerable<DepartmentDto>> GetDepartmentsAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryAsync<DepartmentDto>(@"
                SELECT id, name_en AS NameEn, name_ar AS NameAr
                FROM departments
                ORDER BY id ASC;
            ");
        }

        public async Task<IEnumerable<SpecimenTypeDto>> GetSpecimenTypesAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryAsync<SpecimenTypeDto>(@"
                SELECT id, name_en AS NameEn, name_ar AS NameAr, color AS Color
                FROM specimen_types
                ORDER BY id ASC;
            ");
        }

        public async Task<long> SaveTestAsync(TestCatalogDto test, long? userId = null)
        {
            if (string.IsNullOrWhiteSpace(test.TestCode)) throw new ArgumentException("كود التحليل مطلوب.", nameof(test.TestCode));
            if (string.IsNullOrWhiteSpace(test.NameEn)) throw new ArgumentException("اسم التحليل بالإنجليزية مطلوب.", nameof(test.NameEn));
            if (test.Price < 0) throw new ArgumentException("سعر التحليل لا يمكن أن يكون سالباً.", nameof(test.Price));

            using var conn = _connectionFactory.CreateConnection();

            test.TestCode = test.TestCode.Trim().ToUpperInvariant();
            test.NameEn = test.NameEn.Trim();
            test.NameAr = test.NameAr?.Trim();

            if (test.Id > 0)
            {
                // Update
                const string updateSql = @"
                    UPDATE tests
                    SET test_code = @TestCode,
                        name_en = @NameEn,
                        name_ar = @NameAr,
                        department_id = @DepartmentId,
                        specimen_type_id = @SpecimenTypeId,
                        price = @Price,
                        active = @Active,
                        sort_order = @SortOrder,
                        updated_at = clock_timestamp()
                    WHERE id = @Id;
                ";

                await conn.ExecuteAsync(updateSql, test);
                await _authService.LogAuditAsync(userId, "UPDATE_TEST", "tests", test.Id.ToString(), null, test);
                return test.Id;
            }
            else
            {
                // Check code uniqueness
                var existing = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM tests WHERE test_code = @Code;",
                    new { Code = test.TestCode });

                if (existing > 0)
                {
                    throw new InvalidOperationException($"كود التحليل ({test.TestCode}) مسجل مسبقاً! يرجى اختيار كود آخر.");
                }

                const string insertSql = @"
                    INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, active, sort_order, updated_at)
                    VALUES (@TestCode, @NameEn, @NameAr, @DepartmentId, @SpecimenTypeId, @Price, @Active, @SortOrder, clock_timestamp())
                    RETURNING id;
                ";

                long newId = await conn.ExecuteScalarAsync<long>(insertSql, test);
                await _authService.LogAuditAsync(userId, "CREATE_TEST", "tests", newId.ToString(), null, test);
                return newId;
            }
        }

        public async Task ToggleTestActiveAsync(long testId, bool active, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(@"
                UPDATE tests
                SET active = @Active, updated_at = clock_timestamp()
                WHERE id = @Id;
            ", new { Id = testId, Active = active });

            await _authService.LogAuditAsync(userId, active ? "ENABLE_TEST" : "DISABLE_TEST", "tests", testId.ToString(), null, new { active });
        }

        public async Task<bool> DeleteTestAsync(long testId, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();

            // Check if used in any orders
            var orderCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM order_tests WHERE test_id = @Id;",
                new { Id = testId });

            if (orderCount > 0)
            {
                // Soft delete by disabling
                await conn.ExecuteAsync(@"
                    UPDATE tests SET active = FALSE, updated_at = clock_timestamp() WHERE id = @Id;
                ", new { Id = testId });
                await _authService.LogAuditAsync(userId, "SOFT_DELETE_TEST", "tests", testId.ToString(), null, new { reason = "Test has existing orders" });
                return false; // Indicating soft delete / disabled
            }

            // Also check instrument_tests or components
            await conn.ExecuteAsync("DELETE FROM instrument_tests WHERE test_id = @Id;", new { Id = testId });
            await conn.ExecuteAsync("DELETE FROM components WHERE test_id = @Id;", new { Id = testId });
            await conn.ExecuteAsync("DELETE FROM tests WHERE id = @Id;", new { Id = testId });
            await _authService.LogAuditAsync(userId, "DELETE_TEST", "tests", testId.ToString(), null, null);
            return true;
        }

        public async Task<TestCatalogDto?> GetTestByIdAsync(long testId)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT t.id, t.test_code AS TestCode, t.name_en AS NameEn, t.name_ar AS NameAr,
                       t.department_id AS DepartmentId, d.name_en AS DepartmentName,
                       t.specimen_type_id AS SpecimenTypeId, st.name_en AS SpecimenTypeName, st.color AS SpecimenColor,
                       t.price, t.active, t.sort_order AS SortOrder
                FROM tests t
                LEFT JOIN departments d ON d.id = t.department_id
                LEFT JOIN specimen_types st ON st.id = t.specimen_type_id
                WHERE t.id = @Id;
            ";
            return await conn.QueryFirstOrDefaultAsync<TestCatalogDto>(sql, new { Id = testId });
        }

        public async Task<IEnumerable<ComponentDto>> GetComponentsForTestAsync(long testId)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT c.id, c.test_id AS TestId, c.component_code AS ComponentCode, 
                       c.name_en AS NameEn, c.name_ar AS NameAr, c.display_code AS DisplayCode,
                       c.unit, c.decimals, c.result_type AS ResultType, c.notes, c.sort_order AS SortOrder,
                       (SELECT COUNT(*) FROM reference_ranges rr WHERE rr.component_id = c.id) AS RangesCount,
                       it.instrument_id AS InstrumentId, it.instrument_test_code AS InstrumentTestCode,
                       ins.name AS InstrumentName
                FROM components c
                LEFT JOIN instrument_tests it ON it.component_id = c.id
                LEFT JOIN instruments ins ON ins.id = it.instrument_id
                WHERE c.test_id = @TestId
                ORDER BY c.sort_order ASC, c.id ASC;
            ";
            return await conn.QueryAsync<ComponentDto>(sql, new { TestId = testId });
        }

        public async Task<ComponentDto?> GetComponentByIdAsync(long componentId)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT c.id, c.test_id AS TestId, c.component_code AS ComponentCode, 
                       c.name_en AS NameEn, c.name_ar AS NameAr, c.display_code AS DisplayCode,
                       c.unit, c.decimals, c.result_type AS ResultType, c.notes, c.sort_order AS SortOrder,
                       (SELECT COUNT(*) FROM reference_ranges rr WHERE rr.component_id = c.id) AS RangesCount,
                       it.instrument_id AS InstrumentId, it.instrument_test_code AS InstrumentTestCode,
                       ins.name AS InstrumentName
                FROM components c
                LEFT JOIN instrument_tests it ON it.component_id = c.id
                LEFT JOIN instruments ins ON ins.id = it.instrument_id
                WHERE c.id = @Id;
            ";
            return await conn.QueryFirstOrDefaultAsync<ComponentDto>(sql, new { Id = componentId });
        }

        public async Task<long> SaveComponentAsync(ComponentDto comp, long? userId = null)
        {
            if (string.IsNullOrWhiteSpace(comp.NameEn)) throw new ArgumentException("اسم البارامتر بالإنجليزية مطلوب.", nameof(comp.NameEn));
            if (string.IsNullOrWhiteSpace(comp.DisplayCode)) comp.DisplayCode = comp.NameEn.Split(' ')[0];

            using var conn = _connectionFactory.CreateConnection();

            comp.DisplayCode = comp.DisplayCode.Trim();
            comp.NameEn = comp.NameEn.Trim();
            comp.NameAr = comp.NameAr?.Trim();
            comp.Unit = comp.Unit?.Trim();
            if (string.IsNullOrWhiteSpace(comp.ResultType)) comp.ResultType = "numeric";

            if (comp.Id > 0)
            {
                // Update
                const string updateSql = @"
                    UPDATE components
                    SET name_en = @NameEn,
                        name_ar = @NameAr,
                        display_code = @DisplayCode,
                        unit = @Unit,
                        decimals = @Decimals,
                        result_type = @ResultType,
                        notes = @Notes,
                        sort_order = @SortOrder,
                        updated_at = clock_timestamp()
                    WHERE id = @Id;
                ";
                await conn.ExecuteAsync(updateSql, comp);

                // Update instrument test mapping if specified
                if (comp.InstrumentId.HasValue && comp.InstrumentId.Value > 0 && !string.IsNullOrWhiteSpace(comp.InstrumentTestCode))
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code, updated_at)
                        VALUES (@InstrumentId, @TestId, @Id, @InstrumentTestCode, clock_timestamp())
                        ON CONFLICT (instrument_id, component_id) 
                        DO UPDATE SET instrument_test_code = EXCLUDED.instrument_test_code, updated_at = clock_timestamp();
                    ", new { comp.InstrumentId, comp.TestId, comp.Id, InstrumentTestCode = comp.InstrumentTestCode.Trim() });
                }
                else if (!comp.InstrumentId.HasValue || comp.InstrumentId.Value <= 0 || string.IsNullOrWhiteSpace(comp.InstrumentTestCode))
                {
                    await conn.ExecuteAsync("DELETE FROM instrument_tests WHERE component_id = @Id;",
                        new { comp.Id });
                }

                await _authService.LogAuditAsync(userId, "UPDATE_COMPONENT", "components", comp.Id.ToString(), null, comp);
                return comp.Id;
            }
            else
            {
                // Create unique component_code if not set
                if (string.IsNullOrWhiteSpace(comp.ComponentCode))
                {
                    string safeDisplay = System.Text.RegularExpressions.Regex.Replace(comp.DisplayCode, @"[^a-zA-Z0-9_]", "_");
                    comp.ComponentCode = $"C_{comp.TestId}_{safeDisplay}_{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}";
                }
                else
                {
                    comp.ComponentCode = comp.ComponentCode.Trim();
                }

                const string insertSql = @"
                    INSERT INTO components (test_id, component_code, name_en, name_ar, display_code, unit, decimals, result_type, notes, sort_order, updated_at)
                    VALUES (@TestId, @ComponentCode, @NameEn, @NameAr, @DisplayCode, @Unit, @Decimals, @ResultType, @Notes, @SortOrder, clock_timestamp())
                    RETURNING id;
                ";
                long newId = await conn.ExecuteScalarAsync<long>(insertSql, comp);
                comp.Id = newId;

                // Insert instrument mapping if specified
                if (comp.InstrumentId.HasValue && comp.InstrumentId.Value > 0 && !string.IsNullOrWhiteSpace(comp.InstrumentTestCode))
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code, updated_at)
                        VALUES (@InstrumentId, @TestId, @Id, @InstrumentTestCode, clock_timestamp())
                        ON CONFLICT (instrument_id, component_id) 
                        DO UPDATE SET instrument_test_code = EXCLUDED.instrument_test_code, updated_at = clock_timestamp();
                    ", new { comp.InstrumentId, comp.TestId, comp.Id, InstrumentTestCode = comp.InstrumentTestCode.Trim() });
                }

                await _authService.LogAuditAsync(userId, "CREATE_COMPONENT", "components", newId.ToString(), null, comp);
                return newId;
            }
        }

        public async Task<bool> DeleteComponentAsync(long componentId, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();

            // Check if results exist
            var resCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM results WHERE component_id = @Id;",
                new { Id = componentId });

            if (resCount > 0)
            {
                throw new InvalidOperationException("لا يمكن حذف هذا البارامتر لأنه مرتبط بنتائج تحاليل مسجلة لمرضى مسبقاً في النظام.");
            }

            await conn.ExecuteAsync("DELETE FROM reference_ranges WHERE component_id = @Id;", new { Id = componentId });
            await conn.ExecuteAsync("DELETE FROM instrument_tests WHERE component_id = @Id;", new { Id = componentId });
            await conn.ExecuteAsync("DELETE FROM components WHERE id = @Id;", new { Id = componentId });
            await _authService.LogAuditAsync(userId, "DELETE_COMPONENT", "components", componentId.ToString(), null, null);
            return true;
        }

        public async Task<IEnumerable<ReferenceRangeDto>> GetReferenceRangesAsync(long componentId)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT id, range_code AS RangeCode, component_id AS ComponentId, sex,
                       age_min AS AgeMin, age_max AS AgeMax, age_unit AS AgeUnit,
                       low, high, critical_low AS CriticalLow, critical_high AS CriticalHigh, note
                FROM reference_ranges
                WHERE component_id = @ComponentId
                ORDER BY sex ASC, age_from_days ASC, id ASC;
            ";
            return await conn.QueryAsync<ReferenceRangeDto>(sql, new { ComponentId = componentId });
        }

        public async Task<long> SaveReferenceRangeAsync(ReferenceRangeDto range, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();

            if (string.IsNullOrWhiteSpace(range.Sex)) range.Sex = "B";
            if (string.IsNullOrWhiteSpace(range.AgeUnit)) range.AgeUnit = "Years";

            if (range.Id > 0)
            {
                const string updateSql = @"
                    UPDATE reference_ranges
                    SET sex = @Sex,
                        age_min = @AgeMin,
                        age_max = @AgeMax,
                        age_unit = @AgeUnit,
                        low = @Low,
                        high = @High,
                        critical_low = @CriticalLow,
                        critical_high = @CriticalHigh,
                        note = @Note,
                        updated_at = clock_timestamp()
                    WHERE id = @Id;
                ";
                await conn.ExecuteAsync(updateSql, range);
                await _authService.LogAuditAsync(userId, "UPDATE_RANGE", "reference_ranges", range.Id.ToString(), null, range);
                return range.Id;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(range.RangeCode))
                {
                    range.RangeCode = $"R_{range.ComponentId}_{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
                }

                const string insertSql = @"
                    INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high, critical_low, critical_high, note, updated_at)
                    VALUES (@RangeCode, @ComponentId, @Sex, @AgeMin, @AgeMax, @AgeUnit, @Low, @High, @CriticalLow, @CriticalHigh, @Note, clock_timestamp())
                    RETURNING id;
                ";
                long newId = await conn.ExecuteScalarAsync<long>(insertSql, range);
                await _authService.LogAuditAsync(userId, "CREATE_RANGE", "reference_ranges", newId.ToString(), null, range);
                return newId;
            }
        }

        public async Task<bool> DeleteReferenceRangeAsync(long rangeId, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync("DELETE FROM reference_ranges WHERE id = @Id;", new { Id = rangeId });
            await _authService.LogAuditAsync(userId, "DELETE_RANGE", "reference_ranges", rangeId.ToString(), null, null);
            return true;
        }

        public async Task<IEnumerable<InstrumentSimpleDto>> GetInstrumentsAsync()
        {
            using var conn = _connectionFactory.CreateConnection();
            return await conn.QueryAsync<InstrumentSimpleDto>(@"
                SELECT id, code, name
                FROM instruments
                ORDER BY id ASC;
            ");
        }

        public async Task<IEnumerable<InstrumentMappingItemDto>> GetInstrumentMappingsForComponentAsync(long componentId)
        {
            using var conn = _connectionFactory.CreateConnection();
            string sql = @"
                SELECT it.id, it.instrument_id AS InstrumentId,
                       i.code AS InstrumentCode, i.name AS InstrumentName,
                       it.test_id AS TestId, it.component_id AS ComponentId,
                       it.instrument_test_code AS InstrumentTestCode,
                       it.reportable_low AS ReportableLow,
                       it.reportable_high AS ReportableHigh
                FROM instrument_tests it
                JOIN instruments i ON i.id = it.instrument_id
                WHERE it.component_id = @ComponentId
                ORDER BY i.name ASC;
            ";
            return await conn.QueryAsync<InstrumentMappingItemDto>(sql, new { ComponentId = componentId });
        }

        public async Task AddOrUpdateInstrumentMappingAsync(
            long instrumentId,
            long testId,
            long componentId,
            string instrumentTestCode,
            decimal? reportableLow = null,
            decimal? reportableHigh = null,
            long? userId = null)
        {
            if (string.IsNullOrWhiteSpace(instrumentTestCode))
                throw new ArgumentException("كود الفحص في الجهاز مطلوب.", nameof(instrumentTestCode));

            using var conn = _connectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO instrument_tests (instrument_id, test_id, component_id, instrument_test_code, reportable_low, reportable_high, updated_at)
                VALUES (@InstrumentId, @TestId, @ComponentId, @InstrumentTestCode, @ReportableLow, @ReportableHigh, clock_timestamp())
                ON CONFLICT (instrument_id, component_id)
                DO UPDATE SET instrument_test_code = EXCLUDED.instrument_test_code,
                              reportable_low = EXCLUDED.reportable_low,
                              reportable_high = EXCLUDED.reportable_high,
                              updated_at = clock_timestamp();
            ";
            await conn.ExecuteAsync(sql, new
            {
                InstrumentId = instrumentId,
                TestId = testId,
                ComponentId = componentId,
                InstrumentTestCode = instrumentTestCode.Trim(),
                ReportableLow = reportableLow,
                ReportableHigh = reportableHigh
            });

            await _authService.LogAuditAsync(userId, "UPDATE_INSTRUMENT_MAPPING", "instrument_tests", $"{instrumentId}_{componentId}", null, new { instrumentId, testId, componentId, instrumentTestCode });
        }

        public async Task DeleteInstrumentMappingAsync(long instrumentId, long componentId, long? userId = null)
        {
            using var conn = _connectionFactory.CreateConnection();
            await conn.ExecuteAsync(
                "DELETE FROM instrument_tests WHERE instrument_id = @InstrumentId AND component_id = @ComponentId;",
                new { InstrumentId = instrumentId, ComponentId = componentId });

            await _authService.LogAuditAsync(userId, "DELETE_INSTRUMENT_MAPPING", "instrument_tests", $"{instrumentId}_{componentId}", null, null);
        }
    }
}
