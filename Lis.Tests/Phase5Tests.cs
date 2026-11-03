using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Services;

namespace Lis.Tests
{
    public class Phase5Tests
    {
        private readonly TestDatabaseFixture _fixture;
        private readonly IQcService _qcService;

        public Phase5Tests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
            _qcService = new QcService(_fixture.ConnectionFactory);
        }

        public Task Test28_WestgardRules_Evaluates1_3sRuleCorrectly()
        {
            decimal mean = 100m;
            decimal sd = 5m;

            // Value = 116 (+3.2 SD) -> 1-3s violation
            var eval = _qcService.EvaluateWestgard(116m, mean, sd, new List<decimal> { 100m, 101m });
            Assert.True(eval.IsRejection, "Rule 1-3s must be a rejection");
            Assert.Equal("1-3s", eval.RuleViolated);

            // Value = 84 (-3.2 SD) -> 1-3s violation
            var evalLow = _qcService.EvaluateWestgard(84m, mean, sd, new List<decimal> { 100m });
            Assert.True(evalLow.IsRejection, "Rule 1-3s low must be a rejection");
            Assert.Equal("1-3s", evalLow.RuleViolated);
            return Task.CompletedTask;
        }

        public Task Test29_WestgardRules_Evaluates1_2sWarningCorrectly()
        {
            decimal mean = 100m;
            decimal sd = 5m;

            // Value = 112 (+2.4 SD) with normal prior -> 1-2s warning
            var eval = _qcService.EvaluateWestgard(112m, mean, sd, new List<decimal> { 100m, 99m });
            Assert.True(eval.IsWarning, "Rule 1-2s must be a warning");
            Assert.False(eval.IsRejection, "Rule 1-2s must not be a rejection");
            Assert.Equal("1-2s", eval.RuleViolated);
            return Task.CompletedTask;
        }

        public Task Test30_WestgardRules_Evaluates2_2sRuleCorrectly()
        {
            decimal mean = 100m;
            decimal sd = 5m;

            // 2 consecutive points > +2 SD (111 and 112)
            var eval = _qcService.EvaluateWestgard(112m, mean, sd, new List<decimal> { 111m, 100m });
            Assert.True(eval.IsRejection, "Rule 2-2s must be a rejection");
            Assert.Equal("2-2s", eval.RuleViolated);

            // 2 consecutive points < -2 SD (88 and 89)
            var evalLow = _qcService.EvaluateWestgard(88m, mean, sd, new List<decimal> { 89m });
            Assert.True(evalLow.IsRejection, "Rule 2-2s low must be a rejection");
            Assert.Equal("2-2s", evalLow.RuleViolated);
            return Task.CompletedTask;
        }

        public Task Test31_WestgardRules_EvaluatesR_4sRuleCorrectly()
        {
            decimal mean = 100m;
            decimal sd = 5m;

            // Prior was 89 (-2.2 SD), Current is 111 (+2.2 SD) -> Range = 4.4 SD
            var eval = _qcService.EvaluateWestgard(111m, mean, sd, new List<decimal> { 89m });
            Assert.True(eval.IsRejection, "Rule R-4s must be a rejection");
            Assert.Equal("R-4s", eval.RuleViolated);
            return Task.CompletedTask;
        }

        public Task Test32_WestgardRules_Evaluates4_1sAnd10_xRulesCorrectly()
        {
            decimal mean = 100m;
            decimal sd = 5m;

            // 4 consecutive points > +1 SD (106, 106, 107, 106)
            var eval4_1s = _qcService.EvaluateWestgard(106m, mean, sd, new List<decimal> { 106m, 107m, 106m });
            Assert.True(eval4_1s.IsRejection, "Rule 4-1s must be a rejection");
            Assert.Equal("4-1s", eval4_1s.RuleViolated);

            // 10 consecutive points on same side of mean (all > 100)
            var history9 = Enumerable.Repeat(102m, 9).ToList();
            var eval10x = _qcService.EvaluateWestgard(103m, mean, sd, history9);
            Assert.True(eval10x.IsRejection, "Rule 10-x must be a rejection");
            Assert.Equal("10-x", eval10x.RuleViolated);
            return Task.CompletedTask;
        }

        public Task Test33_WestgardRules_NormalInControlReturnsNoViolation()
        {
            decimal mean = 100m;
            decimal sd = 5m;

            // Value = 102 (+0.4 SD)
            var eval = _qcService.EvaluateWestgard(102m, mean, sd, new List<decimal> { 99m, 101m });
            Assert.Null(eval.RuleViolated);
            Assert.False(eval.IsRejection);
            Assert.False(eval.IsWarning);
            return Task.CompletedTask;
        }

        public async Task Test34_QcService_MaterialsAndTargetsCRUD()
        {
            // 1. Create a QC Material
            var mat = new QcMaterialDto
            {
                Name = "Bio-Rad Liquichek Test Lot",
                LotNo = "LOT" + Guid.NewGuid().ToString("N")[..6].ToUpper(),
                Level = 1,
                ExpiryDate = DateTime.UtcNow.AddYears(1),
                Active = true
            };
            long matId = await _qcService.SaveMaterialAsync(mat);
            Assert.True(matId > 0, "Material ID must be positive");

            var fetchedMat = await _qcService.GetMaterialByIdAsync(matId);
            Assert.NotNull(fetchedMat);
            Assert.Equal(mat.Name, fetchedMat.Name);

            // 2. Ensure an instrument and component exist
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var instId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, active)
                VALUES ('INST_QC_TEST', 'QC Test Analyzer', 'ASTM', 'TCP', 'Bidirectional', TRUE)
                ON CONFLICT (code) DO UPDATE SET name = EXCLUDED.name RETURNING id;
            ");

            var testId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO tests (test_code, name_en, name_ar, price, active)
                VALUES ('T_QC_TEST', 'QC Test Test', 'فحص ضبط جودة', 1000, TRUE)
                ON CONFLICT (test_code) DO UPDATE SET name_en = EXCLUDED.name_en RETURNING id;
            ");

            var compId = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO components (test_id, component_code, name_en, display_code, unit, decimals, sort_order)
                VALUES (@TestId, 'C_QC_COMP', 'QC Component', 'QCC', 'mg/dL', 1, 1)
                ON CONFLICT (component_code) DO UPDATE SET name_en = EXCLUDED.name_en RETURNING id;
            ", new { TestId = testId });

            // 3. Save QC Target
            var target = new QcTargetDto
            {
                QcMaterialId = matId,
                InstrumentId = instId,
                ComponentId = compId,
                Mean = 120m,
                Sd = 6m,
                Unit = "mg/dL"
            };
            long targetId = await _qcService.SaveTargetAsync(target);
            Assert.True(targetId > 0, "Target ID must be positive");

            var fetchedTarget = await _qcService.GetTargetAsync(matId, instId, compId);
            Assert.NotNull(fetchedTarget);
            Assert.Equal(120m, fetchedTarget.Mean);
            Assert.Equal(6m, fetchedTarget.Sd);
            Assert.Equal(5m, fetchedTarget.CvPercent); // 6 / 120 * 100 = 5%

            // 4. Record QC Results and test Today Status
            var runNormal = new QcResultDto
            {
                InstrumentId = instId,
                ComponentId = compId,
                QcMaterialId = matId,
                Value = 121m,
                ReceivedAt = DateTime.UtcNow
            };
            long runId = await _qcService.RecordResultAsync(runNormal);
            Assert.True(runId > 0, "Run ID must be positive");

            var status = await _qcService.GetTodayQcStatusAsync(instId, compId);
            Assert.Equal("Passed", status.Status);
            Assert.True(status.IsSafeToValidate);

            // Record out-of-control run (145 > +4 SD)
            var runViolated = new QcResultDto
            {
                InstrumentId = instId,
                ComponentId = compId,
                QcMaterialId = matId,
                Value = 145m,
                ReceivedAt = DateTime.UtcNow
            };
            await _qcService.RecordResultAsync(runViolated);

            var statusAfterViolation = await _qcService.GetTodayQcStatusAsync(instId, compId);
            Assert.Equal("Failed", statusAfterViolation.Status);
            Assert.False(statusAfterViolation.IsSafeToValidate);
        }
    }
}
