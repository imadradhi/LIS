using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Models;
using Lis.Core.Services;

namespace Lis.Tests
{
    public class Phase4Tests
    {
        private readonly TestDatabaseFixture _fixture;
        private readonly IAuthService _authService;
        private readonly IPatientService _patientService;
        private readonly IOrderService _orderService;
        private readonly IReferenceRangeService _refRangeService;
        private readonly IResultService _resultService;
        private readonly ICalculatedComponentService _calcService;

        public Phase4Tests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
            _authService = new AuthService(_fixture.ConnectionFactory);
            _patientService = new PatientService(_fixture.ConnectionFactory, _authService);
            var accessionService = new AccessionService(_fixture.ConnectionFactory);
            _orderService = new OrderService(_fixture.ConnectionFactory, accessionService, _authService);
            _refRangeService = new ReferenceRangeService(_fixture.ConnectionFactory);
            _resultService = new ResultService(_fixture.ConnectionFactory, _authService, _refRangeService);
            _calcService = new CalculatedComponentService(_fixture.ConnectionFactory, _refRangeService);
        }

        private async Task<long> GetAdminUserIdAsync()
        {
            await _authService.EnsureDefaultAdminUserAsync();
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<long>("SELECT id FROM users WHERE username = 'admin';");
        }

        public async Task Test23_CalculatedComponents_EvaluatesFormulaAndSavesResult()
        {
            long adminId = await GetAdminUserIdAsync();
            using var conn = _fixture.ConnectionFactory.CreateConnection();

            // 1. Ensure a Liver Function / Protein test exists with Total Protein (TP), Albumin (ALB), and Globulin (GLOB)
            var deptId = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO departments (name_en, name_ar) VALUES ('Biochemistry', 'الكيمياء السريرية') ON CONFLICT (name_en) DO UPDATE SET name_ar = EXCLUDED.name_ar RETURNING id;");
            var specTypeId = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO specimen_types (name_en, name_ar, color) VALUES ('Serum', 'مصل الدم', 'Yellow') ON CONFLICT (name_en) DO UPDATE SET name_ar = EXCLUDED.name_ar RETURNING id;");

            var testId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO tests (test_code, name_en, name_ar, department_id, specimen_type_id, price, active)
                VALUES ('T_LFT_PROT', 'Total Protein & Albumin', 'فحص البروتين والالبومين', @DeptId, @SpecTypeId, 5000, TRUE)
                ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en RETURNING id;
            ", new { DeptId = deptId, SpecTypeId = specTypeId });

            var tpCompId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO components (test_id, component_code, name_en, display_code, unit, decimals, sort_order)
                VALUES (@TestId, 'C_TP_TEST', 'Total Protein', 'TP', 'g/dL', 1, 1)
                ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en RETURNING id;
            ", new { TestId = testId });

            var albCompId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO components (test_id, component_code, name_en, display_code, unit, decimals, sort_order)
                VALUES (@TestId, 'C_ALB_TEST', 'Albumin', 'ALB', 'g/dL', 1, 2)
                ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en RETURNING id;
            ", new { TestId = testId });

            var globCompId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO components (test_id, component_code, name_en, display_code, unit, decimals, sort_order)
                VALUES (@TestId, 'C_GLOB_TEST', 'Globulin (Calc)', 'GLOB', 'g/dL', 1, 3)
                ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en RETURNING id;
            ", new { TestId = testId });

            // 2. Set Reference range for Globulin (2.0 - 3.5 g/dL)
            await conn.ExecuteAsync(@"
                INSERT INTO reference_ranges (range_code, component_id, sex, age_min, age_max, age_unit, low, high)
                VALUES ('R_GLOB_TEST', @CompId, 'B', 18, 100, 'Years', 2.0, 3.5)
                ON CONFLICT (range_code) DO UPDATE SET low = EXCLUDED.low, high = EXCLUDED.high;
            ", new { CompId = globCompId });

            // 3. Register Formula: GLOB = [C_TP_TEST] - [C_ALB_TEST]
            await _calcService.RegisterOrUpdateCalculatedComponentAsync(
                "C_GLOB_TEST",
                "[C_TP_TEST] - [C_ALB_TEST]",
                new List<string> { "C_TP_TEST", "C_ALB_TEST" });

            // 4. Create patient and order
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "يوسف كمال جاسم",
                FullNameEn = "Yousif Kamal Jassim",
                Sex = "M",
                DateOfBirth = new DateTime(1985, 4, 12),
                Phone = "07703344556"
            }, adminId);

            var order = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { testId }
            }, adminId);

            long orderId = order.Order.Id;
            long otId = order.Specimens[0].Tests[0].OrderTestId;

            // 5. Save input results: TP = 7.5, ALB = 4.5 -> Expected GLOB = 3.0 (Normal)
            var batch = new List<(long OrderTestId, long ComponentId, string? ValueText)>
            {
                (otId, tpCompId, "7.5"),
                (otId, albCompId, "4.5")
            };

            await _resultService.SaveOrderResultsBatchAsync(orderId, batch, adminId);

            // 6. Verify that C_GLOB_TEST was automatically evaluated and saved!
            var orderResults = await _resultService.GetOrderResultsAsync(orderId);
            Assert.NotNull(orderResults);

            var globRes = orderResults!.Results.FirstOrDefault(r => r.ComponentCode == "C_GLOB_TEST");
            Assert.NotNull(globRes);
            Assert.Equal("3.0", globRes!.ValueText);
            Assert.Equal(3.0m, globRes.ValueNumeric);
            Assert.Equal("N", globRes.Flag); // 2.0 - 3.5 is normal
        }

        public async Task Test24_DeltaCheck_DetectsSignificantChangeVsPreviousVisit()
        {
            long adminId = await GetAdminUserIdAsync();
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");
            var hgbCompId = await conn.ExecuteScalarAsync<long>("SELECT id FROM components WHERE component_code = 'C_HGB';");

            // 1. Create Patient
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "فاطمة عباس مهدي",
                FullNameEn = "Fatima Abbas Mahdi",
                Sex = "F",
                DateOfBirth = new DateTime(1992, 8, 20),
                Phone = "07802233445"
            }, adminId);

            // 2. Visit 1 (Prior visit): HGB was 14.0 g/dL -> Validated
            var order1 = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            long order1Id = order1.Order.Id;
            long ot1Id = order1.Specimens[0].Tests[0].OrderTestId;
            await _resultService.SaveComponentResultAsync(ot1Id, hgbCompId, "14.0", adminId);
            await _resultService.ValidateOrderResultsAsync(order1Id, adminId);

            // 3. Visit 2 (Current visit): HGB dropped severely to 6.5 g/dL (drop > 50%)
            var order2 = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            long order2Id = order2.Order.Id;
            long ot2Id = order2.Specimens[0].Tests[0].OrderTestId;
            await _resultService.SaveComponentResultAsync(ot2Id, hgbCompId, "6.5", adminId);

            // 4. Retrieve order 2 results and verify Delta Check
            var order2Results = await _resultService.GetOrderResultsAsync(order2Id);
            Assert.NotNull(order2Results);

            var hgbRes = order2Results!.Results.FirstOrDefault(r => r.ComponentCode == "C_HGB");
            Assert.NotNull(hgbRes);
            Assert.Equal("6.5", hgbRes!.ValueText);
            Assert.Equal(14.0m, hgbRes.PreviousValueNumeric);

            // Delta % = ((6.5 - 14.0) / 14.0) * 100 = -53.6%
            Assert.NotNull(hgbRes.DeltaChangePct);
            Assert.True(hgbRes.DeltaChangePct!.Value <= -50.0m, $"Delta % should be <= -50%, got {hgbRes.DeltaChangePct}");
            Assert.True(hgbRes.IsDeltaAlert, "IsDeltaAlert must be true for drop > 50%");
            Assert.True(order2Results.HasDeltaAlerts, "Order must flag HasDeltaAlerts");
        }

        public async Task Test25_CriticalValueAlert_FlagsCriticalResults()
        {
            long adminId = await GetAdminUserIdAsync();
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");
            var pltCompId = await conn.ExecuteScalarAsync<long>("SELECT id FROM components WHERE component_code = 'C_PLT';");

            // Set Critical Low range for Platelets (< 20 is critical)
            await conn.ExecuteAsync(@"
                UPDATE reference_ranges
                SET critical_low = 20.0, critical_high = 1000.0
                WHERE component_id = @CompId;
            ", new { CompId = pltCompId });

            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "علي عبد الصمد حسن",
                FullNameEn = "Ali Abdul Samad Hassan",
                Sex = "M",
                DateOfBirth = new DateTime(1980, 2, 14),
                Phone = "07706677889"
            }, adminId);

            var order = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            long orderId = order.Order.Id;
            long otId = order.Specimens[0].Tests[0].OrderTestId;

            // Save critical low platelet count = 12 10^9/L (< 20)
            await _resultService.SaveComponentResultAsync(otId, pltCompId, "12", adminId);

            var orderResults = await _resultService.GetOrderResultsAsync(orderId);
            Assert.NotNull(orderResults);

            var pltRes = orderResults!.Results.FirstOrDefault(r => r.ComponentCode == "C_PLT");
            Assert.NotNull(pltRes);
            Assert.Equal("12", pltRes!.ValueText);
            Assert.Equal("LL", pltRes.Flag); // Critical Low
            Assert.True(pltRes.IsCritical, "IsCritical must be true for LL");
            Assert.True(orderResults.HasCriticalResults, "Order must indicate HasCriticalResults = true");
            Assert.True(pltRes.CriticalDisplayText != null && pltRes.CriticalDisplayText.Contains("قيمة حرجة"));
        }
    }
}
