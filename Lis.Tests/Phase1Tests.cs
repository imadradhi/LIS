using System;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.Services;
namespace Lis.Tests
{
    public class Phase1Tests
    {
        private readonly TestDatabaseFixture _fixture;

        public Phase1Tests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public async Task Test1_ImportCatalog_ImportsExactCounts()
        {
            var importer = new CatalogCsvImporter(_fixture.ConnectionFactory);
            var result = await importer.ImportFromDirectoryAsync(_fixture.CatalogPath);

            Assert.Equal(1, result.TestsImported);
            Assert.Equal(22, result.ComponentsImported);
            Assert.Equal(22, result.ReferenceRangesImported);
            Assert.Equal(22, result.InstrumentMappingsImported);

            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var testCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tests WHERE test_code = 'T_CBC_360';");
            var compCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM components WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_CBC_360');");
            var rangeCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM reference_ranges rr JOIN components c ON c.id = rr.component_id JOIN tests t ON t.id = c.test_id WHERE t.test_code = 'T_CBC_360';");
            var instTestCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM instrument_tests it JOIN tests t ON t.id = it.test_id WHERE t.test_code = 'T_CBC_360';");

            Assert.Equal(1, testCount);
            Assert.True(compCount >= 22);
            Assert.True(rangeCount >= 22);
            Assert.True(instTestCount >= 22);
        }

        public async Task Test2_ImportCatalog_IsIdempotent()
        {
            var importer = new CatalogCsvImporter(_fixture.ConnectionFactory);

            // Run import again
            var result = await importer.ImportFromDirectoryAsync(_fixture.CatalogPath);

            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var testCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM tests WHERE test_code = 'T_CBC_360';");
            var compCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM components WHERE test_id IN (SELECT id FROM tests WHERE test_code = 'T_CBC_360');");
            var rangeCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM reference_ranges rr JOIN components c ON c.id = rr.component_id JOIN tests t ON t.id = c.test_id WHERE t.test_code = 'T_CBC_360';");
            var instTestCount = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM instrument_tests it JOIN tests t ON t.id = it.test_id WHERE t.test_code = 'T_CBC_360';");

            // Row counts must NOT increase on repeat import
            Assert.Equal(1, testCount);
            Assert.True(compCount >= 22);
            Assert.True(rangeCount >= 22);
            Assert.True(instTestCount >= 22);
        }

        public async Task Test3_ImportCatalog_DoesNotOverwriteEditedPrice()
        {
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            // Simulate user changing CBC price from 1000 to 2500 IQD in app
            await conn.ExecuteAsync("UPDATE tests SET price = 2500.00 WHERE test_code = 'T_CBC_360';");

            var importer = new CatalogCsvImporter(_fixture.ConnectionFactory);
            await importer.ImportFromDirectoryAsync(_fixture.CatalogPath);

            var updatedPrice = await conn.ExecuteScalarAsync<decimal>("SELECT price FROM tests WHERE test_code = 'T_CBC_360';");

            // Must preserve the user-edited price 2500!
            Assert.Equal(2500.00m, updatedPrice);

            // Reset back to 1000 for standard state
            await conn.ExecuteAsync("UPDATE tests SET price = 1000.00 WHERE test_code = 'T_CBC_360';");
        }

        public async Task Test4_AccessionService_GeneratesValid6DigitAccession()
        {
            var service = new AccessionService(_fixture.ConnectionFactory);
            var acc = await service.GenerateNextAccessionAsync();

            Assert.NotNull(acc);
            Assert.Equal(6, acc.Length);
            Assert.True(service.IsValidAccession(acc));
            Assert.True(int.TryParse(acc, out var num) && num >= 1 && num <= 999999);
        }

        public async Task Test5_AccessionService_SkipsActiveAccession()
        {
            using var conn = _fixture.ConnectionFactory.CreateConnection();

            // Find current nextval
            var nextval = await conn.ExecuteScalarAsync<long>("SELECT last_value FROM accession_seq;");
            var targetAcc = ((nextval % 999999) + 1).ToString("D6");

            // Ensure a patient, order, and specimen type exist to anchor specimen
            var specTypeId = await conn.ExecuteScalarAsync<long>("SELECT id FROM specimen_types LIMIT 1;");
            if (specTypeId == 0)
            {
                specTypeId = await conn.ExecuteScalarAsync<long>("INSERT INTO specimen_types (name_en) VALUES ('EDTA Whole Blood') RETURNING id;");
            }

            var patientId = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO patients (full_name_ar, sex, date_of_birth) VALUES ('فحص أمان', 'M', '1990-01-01') RETURNING id;");
            var orderId = await conn.ExecuteScalarAsync<long>(
                "INSERT INTO orders (patient_id) VALUES (@PatientId) RETURNING id;", new { PatientId = patientId });

            // Insert active specimen reserving targetAcc
            await conn.ExecuteAsync(@"
                INSERT INTO specimens (order_id, specimen_type_id, accession_no, archived)
                VALUES (@OrderId, @SpecTypeId, @AccessionNo, FALSE);",
                new { OrderId = orderId, SpecTypeId = specTypeId, AccessionNo = targetAcc });

            var service = new AccessionService(_fixture.ConnectionFactory);
            var generated = await service.GenerateNextAccessionAsync();

            // It must NOT generate targetAcc because targetAcc is active!
            Assert.NotEqual(targetAcc, generated);
            Assert.True(service.IsValidAccession(generated));

            // Clean up test data
            await conn.ExecuteAsync("DELETE FROM orders WHERE id = @OrderId;", new { OrderId = orderId });
            await conn.ExecuteAsync("DELETE FROM patients WHERE id = @PatientId;", new { PatientId = patientId });
        }

        public async Task Test6_ReferenceRangeService_MatchesGenderSpecificAndCalculatesFlags()
        {
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var hgbCompId = await conn.ExecuteScalarAsync<long>("SELECT id FROM components WHERE component_code = 'C_HGB';");

            var service = new ReferenceRangeService(_fixture.ConnectionFactory);

            var dob = new DateTime(1990, 1, 1);
            var orderDate = new DateTime(2026, 1, 1); // Age 36 years

            // Male HGB range is 13.0 - 17.0
            var maleEvalNormal = await service.EvaluateRangeAsync(hgbCompId, "M", dob, orderDate, 14.5m);
            Assert.True(maleEvalNormal.HasRange);
            Assert.Equal(13.0m, maleEvalNormal.Low);
            Assert.Equal(17.0m, maleEvalNormal.High);
            Assert.Equal("N", maleEvalNormal.Flag);

            var maleEvalLow = await service.EvaluateRangeAsync(hgbCompId, "M", dob, orderDate, 11.5m);
            Assert.Equal("L", maleEvalLow.Flag);

            var maleEvalHigh = await service.EvaluateRangeAsync(hgbCompId, "M", dob, orderDate, 18.5m);
            Assert.Equal("H", maleEvalHigh.Flag);

            // Female HGB range is 12.0 - 15.0
            var femaleEvalNormal = await service.EvaluateRangeAsync(hgbCompId, "F", dob, orderDate, 12.5m);
            Assert.True(femaleEvalNormal.HasRange);
            Assert.Equal(12.0m, femaleEvalNormal.Low);
            Assert.Equal(15.0m, femaleEvalNormal.High);
            Assert.Equal("N", femaleEvalNormal.Flag);

            // Value 16.0 is High for Female (max 15.0) but Normal for Male (max 17.0)
            var femaleEvalHigh = await service.EvaluateRangeAsync(hgbCompId, "F", dob, orderDate, 16.0m);
            Assert.Equal("H", femaleEvalHigh.Flag);

            var maleEvalWithSameVal = await service.EvaluateRangeAsync(hgbCompId, "M", dob, orderDate, 16.0m);
            Assert.Equal("N", maleEvalWithSameVal.Flag);
        }

        public async Task Test7_ReferenceRangeService_HandlesPediatricPatientWithoutError()
        {
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            var hgbCompId = await conn.ExecuteScalarAsync<long>("SELECT id FROM components WHERE component_code = 'C_HGB';");

            var service = new ReferenceRangeService(_fixture.ConnectionFactory);

            // Patient age 5 years (catalog currently covers only 18 to 100 years)
            var dob = new DateTime(2021, 1, 1);
            var orderDate = new DateTime(2026, 1, 1);

            var result = await service.EvaluateRangeAsync(hgbCompId, "M", dob, orderDate, 12.0m);

            // Must NOT fail or throw; HasRange should be false and Flag should be null
            Assert.False(result.HasRange);
            Assert.Null(result.Flag);
            Assert.Null(result.Low);
            Assert.Null(result.High);
        }
    }
}
