using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.ASTM;
using Lis.Core.Drivers;
using Lis.Core.Models;
using Lis.Core.Services;
using Lis.Core.Transport;


namespace Lis.Tests
{
    public class Phase3Tests
    {
        private readonly TestDatabaseFixture _fixture;
        private readonly IAuthService _authService;
        private readonly IPatientService _patientService;
        private readonly IOrderService _orderService;
        private readonly IReferenceRangeService _refRangeService;

        public Phase3Tests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
            _authService = new AuthService(_fixture.ConnectionFactory);
            _patientService = new PatientService(_fixture.ConnectionFactory, _authService);
            var accessionService = new AccessionService(_fixture.ConnectionFactory);
            _orderService = new OrderService(_fixture.ConnectionFactory, accessionService, _authService);
            _refRangeService = new ReferenceRangeService(_fixture.ConnectionFactory);
        }

        private async Task<long> GetAdminUserIdAsync()
        {
            await _authService.EnsureDefaultAdminUserAsync();
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            return await conn.ExecuteScalarAsync<long>("SELECT id FROM users WHERE username = 'admin';");
        }

        private async Task<InstrumentItem> EnsureAdvia360InstrumentAsync(int port)
        {
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            const string sql = @"
                INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, tcp_mode, host, port, active, updated_at)
                VALUES ('ADVIA360', 'Siemens ADVIA 360', 'ASTM', 'TCP', 'Bidirectional', 'Server', '127.0.0.1', @Port, TRUE, clock_timestamp())
                ON CONFLICT (code) DO UPDATE
                SET port = @Port,
                    operating_mode = 'Bidirectional',
                    tcp_mode = 'Server',
                    active = TRUE,
                    updated_at = clock_timestamp()
                RETURNING id, code, name, protocol, connection_type AS ConnectionType,
                          operating_mode AS OperatingMode, tcp_mode AS TcpMode, host, port,
                          active;
            ";
            return await conn.QuerySingleAsync<InstrumentItem>(sql, new { Port = port });
        }

        public Task Test13_AstmFraming_ChecksumValidation_AndCorruptedFrameHandling()
        {
            // 1. Build a valid ASTM frame
            string builtFrame = AstmFraming.BuildFrame(1, "H|\\^&|||ADVIA360||||||||1");

            Assert.True(builtFrame.StartsWith(((char)AstmConstants.STX).ToString()));
            Assert.True(builtFrame.EndsWith("\r\n"));

            // 2. Parse valid frame
            bool parsed = AstmFraming.TryParseFrame(builtFrame, out var frame);
            Assert.True(parsed);
            Assert.NotNull(frame);
            Assert.Equal(1, frame!.FrameNumber);
            Assert.True(frame.IsChecksumValid);
            Assert.Equal("H|\\^&|||ADVIA360||||||||1", frame.Content);

            // 3. Test corrupted checksum
            // Replace the 2-digit checksum with '00'
            int etxIndex = builtFrame.IndexOf((char)AstmConstants.ETX);
            string corruptedFrame = builtFrame.Substring(0, etxIndex + 1) + "00\r\n";
            bool parsedCorrupted = AstmFraming.TryParseFrame(corruptedFrame, out var badFrame);
            Assert.True(parsedCorrupted);
            Assert.NotNull(badFrame);
            Assert.False(badFrame!.IsChecksumValid);

            return Task.CompletedTask;
        }

        public Task Test14_AstmMessageParser_ParsesAllRecords_AndBuildsWorklist()
        {
            string rawMessage =
                "H|\\^&|||ADVIA360||||||||1\r\n" +
                "P|1||100001||Al-Sadr^Ali||19850520|M\r\n" +
                "O|1|000015||^^^CBC|R||||||A\r\n" +
                "Q|1|^000015||ALL||||||||O\r\n" +
                "R|1|^^^WBC|7.5|10^9/L||N||F||||20261022120000|ADVIA360\r\n" +
                "C|1|I|Specimen normal|G\r\n" +
                "L|1|N\r\n";

            var parsed = AstmMessageParser.ParseMessage(rawMessage);

            Assert.NotNull(parsed.Header);
            Assert.Equal("ADVIA360", parsed.Header!.SenderId);

            Assert.NotNull(parsed.Patient);
            Assert.Equal("100001", parsed.Patient!.LaboratoryAssignedId);
            Assert.Equal("Al-Sadr^Ali", parsed.Patient.PatientName);
            Assert.Equal("M", parsed.Patient.Sex);

            Assert.NotNull(parsed.Order);
            Assert.Equal("000015", parsed.Order!.SpecimenId);

            Assert.True(parsed.IsQuery);
            Assert.NotNull(parsed.Query);
            Assert.Equal("000015", parsed.Query!.ExtractAccessionNo());

            Assert.True(parsed.HasResults);
            Assert.Equal(1, parsed.Results.Count);
            Assert.Equal("WBC", parsed.Results[0].UniversalTestCode);
            Assert.Equal("7.5", parsed.Results[0].Value);
            Assert.Equal("10^9/L", parsed.Results[0].Units);
            Assert.Equal("N", parsed.Results[0].ResultAbnormalFlag);

            Assert.Equal(1, parsed.Comments.Count);
            Assert.Equal("Specimen normal", parsed.Comments[0].Text);

            Assert.NotNull(parsed.Terminator);
            Assert.Equal("N", parsed.Terminator!.TerminationCode);

            // Test BuildWorklistFrames
            var worklistFrames = AstmMessageParser.BuildWorklistFrames(
                "000015", 100001, "علي كاظم", new List<string> { "WBC", "HGB", "PLT" }, "EDTA Whole Blood");

            Assert.True(worklistFrames.Count >= 4); // H, P, O, L
            Assert.True(worklistFrames.Any(f => f.Contains("000015")));
            Assert.True(worklistFrames.Any(f => f.Contains("100001")));
            Assert.True(worklistFrames.Any(f => f.Contains("WBC")));

            return Task.CompletedTask;
        }

        public async Task Test15_EndToEnd_HostQuery_WorklistReply_AndStatusInProgress()
        {
            int testPort = 5161;
            var inst = await EnsureAdvia360InstrumentAsync(testPort);
            long adminId = await GetAdminUserIdAsync();

            // 1. Create Patient & Order for CBC
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "محمد عبد الحسين",
                FullNameEn = "Mohammed Abdul Hussein",
                Sex = "M",
                DateOfBirth = new DateTime(1990, 1, 1),
                Phone = "07701112233"
            }, adminId);

            long cbcTestId;
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");
            }

            var orderReq = new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            };
            var orderDetails = await _orderService.CreateOrderAsync(orderReq, adminId);
            Assert.Equal(1, orderDetails.Specimens.Count);
            string accessionNo = orderDetails.Specimens[0].Specimen.AccessionNo;
            Assert.Equal(6, accessionNo.Length);

            // 2. Start AstmInstrumentDriver in TCP Server mode
            var transport = new TcpTransport(TcpMode.Server, "127.0.0.1", testPort);
            using var driver = new AstmInstrumentDriver(inst, transport, _fixture.ConnectionFactory, _refRangeService);
            using var cts = new CancellationTokenSource();
            await driver.StartAsync(cts.Token);

            // Allow listener a moment to bind
            await Task.Delay(200);

            // 3. Connect Simulator and send ASTM Host Query
            using var sim = new SimulatorEngine("127.0.0.1", testPort);
            await sim.ConnectAsync();

            var worklist = await sim.QueryWorklistAsync(accessionNo);

            // 4. Assert Worklist content
            Assert.True(worklist.Count >= 3);
            Assert.True(worklist.Any(f => f.Contains(accessionNo)), "Worklist must contain queried accession");
            Assert.True(worklist.Any(f => f.Contains("WBC") || f.Contains("HGB")), "Worklist must contain mapped instrument tests");

            // 5. Assert database status transitioned to InProgress
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                var status = await conn.ExecuteScalarAsync<string>(
                    "SELECT status FROM specimens WHERE accession_no = @Acc;", new { Acc = accessionNo });
                Assert.Equal("InProgress", status);
            }

            await driver.StopAsync();
        }

        public async Task Test16_EndToEnd_SendResults_IngestedWithReferenceRangeFlags_StatusResultReceived()
        {
            int testPort = 5162;
            var inst = await EnsureAdvia360InstrumentAsync(testPort);
            long adminId = await GetAdminUserIdAsync();

            // 1. Create Patient & Order for CBC
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "زينب حيدر كريم",
                FullNameEn = "Zainab Haider Karim",
                Sex = "F",
                DateOfBirth = new DateTime(1995, 3, 10),
                Phone = "07705556677"
            }, adminId);

            long cbcTestId;
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");
            }

            var orderReq = new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            };
            var orderDetails = await _orderService.CreateOrderAsync(orderReq, adminId);
            string accessionNo = orderDetails.Specimens[0].Specimen.AccessionNo;
            long specId = orderDetails.Specimens[0].Specimen.Id;
            long orderId = orderDetails.Order.Id;

            // 2. Start Driver
            var transport = new TcpTransport(TcpMode.Server, "127.0.0.1", testPort);
            using var driver = new AstmInstrumentDriver(inst, transport, _fixture.ConnectionFactory, _refRangeService);
            using var cts = new CancellationTokenSource();
            await driver.StartAsync(cts.Token);
            await Task.Delay(200);

            // 3. Connect Simulator and send Results
            using var sim = new SimulatorEngine("127.0.0.1", testPort);
            await sim.ConnectAsync();

            var resultsDict = new Dictionary<string, string>
            {
                { "WBC", "7.5" },
                { "RBC", "4.5" },
                { "HGB", "13.2" },
                { "PLT", "240" }
            };

            await sim.SendResultsAsync(accessionNo, resultsDict, "ADVIA360");

            // Allow driver async task a moment to process DB transaction
            await Task.Delay(500);

            // 4. Verify in DB
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                // Check Specimen and Order status
                var specStatus = await conn.ExecuteScalarAsync<string>(
                    "SELECT status FROM specimens WHERE accession_no = @Acc;", new { Acc = accessionNo });
                Assert.Equal("ResultReceived", specStatus);

                var orderStatus = await conn.ExecuteScalarAsync<string>(
                    "SELECT status FROM orders WHERE id = @Id;", new { Id = orderId });
                Assert.Equal("ResultReceived", orderStatus);

                // Check Results records
                var resultsList = (await conn.QueryAsync<dynamic>(@"
                    SELECT r.value_text, r.value_numeric, r.flag, r.unit, c.display_code
                    FROM results r
                    JOIN components c ON c.id = r.component_id
                    JOIN order_tests ot ON ot.id = r.order_test_id
                    WHERE ot.specimen_id = @SpecId AND r.is_current = TRUE;
                ", new { SpecId = specId })).AsList();

                Assert.True(resultsList.Count >= 4);

                var hgbRes = resultsList.FirstOrDefault(r => (string)r.display_code == "HGB");
                Assert.NotNull(hgbRes);
                Assert.Equal(13.2m, (decimal)hgbRes.value_numeric);
                Assert.Equal("N", (string)hgbRes.flag); // Normal for adult female (12.0 - 15.0)

                // Verify middleware_log has entries
                var logCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM middleware_log WHERE raw_message LIKE '%' || @Acc || '%';",
                    new { Acc = accessionNo });
                Assert.True(logCount > 0, "middleware_log must record the transaction");
            }

            await driver.StopAsync();
        }

        public async Task Test17_QC_SampleHandling_StoredInQcResultsTable()
        {
            int testPort = 5163;
            var inst = await EnsureAdvia360InstrumentAsync(testPort);

            var transport = new TcpTransport(TcpMode.Server, "127.0.0.1", testPort);
            using var driver = new AstmInstrumentDriver(inst, transport, _fixture.ConnectionFactory, _refRangeService);
            using var cts = new CancellationTokenSource();
            await driver.StartAsync(cts.Token);
            await Task.Delay(200);

            using var sim = new SimulatorEngine("127.0.0.1", testPort);
            await sim.ConnectAsync();

            string qcBarcode = "QC_NORMAL_01";
            var qcResults = new Dictionary<string, string>
            {
                { "WBC", "6.8" },
                { "HGB", "14.0" }
            };

            await sim.SendResultsAsync(qcBarcode, qcResults, "ADVIA360");
            await Task.Delay(500);

            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                var qcRows = (await conn.QueryAsync<dynamic>(@"
                    SELECT qr.value, qr.accepted, c.display_code
                    FROM qc_results qr
                    JOIN components c ON c.id = qr.component_id
                    WHERE qr.instrument_id = @InstId
                    ORDER BY qr.id DESC LIMIT 2;
                ", new { InstId = inst.Id })).AsList();

                Assert.True(qcRows.Count >= 2);
                Assert.True(qcRows.Any(q => (decimal)q.value == 6.8m));
                Assert.True(qcRows.Any(q => (decimal)q.value == 14.0m));
            }

            await driver.StopAsync();
        }

        public async Task Test18_Rerun_StoresOlderInResultHistory_AndSetsNewestAsCurrent()
        {
            int testPort = 5164;
            var inst = await EnsureAdvia360InstrumentAsync(testPort);
            long adminId = await GetAdminUserIdAsync();

            // 1. Create Patient & Order for CBC
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "حسن رحيم طالب",
                FullNameEn = "Hassan Raheem Talib",
                Sex = "M",
                DateOfBirth = new DateTime(1988, 7, 14),
                Phone = "07709998877"
            }, adminId);

            long cbcTestId;
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");
            }

            var orderReq = new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            };
            var orderDetails = await _orderService.CreateOrderAsync(orderReq, adminId);
            string accessionNo = orderDetails.Specimens[0].Specimen.AccessionNo;
            long specId = orderDetails.Specimens[0].Specimen.Id;

            var transport = new TcpTransport(TcpMode.Server, "127.0.0.1", testPort);
            using var driver = new AstmInstrumentDriver(inst, transport, _fixture.ConnectionFactory, _refRangeService);
            using var cts = new CancellationTokenSource();
            await driver.StartAsync(cts.Token);
            await Task.Delay(200);

            // 2. Send initial result
            using var sim1 = new SimulatorEngine("127.0.0.1", testPort);
            await sim1.ConnectAsync();
            await sim1.SendResultsAsync(accessionNo, new Dictionary<string, string> { { "HGB", "14.2" } }, "ADVIA360");
            await Task.Delay(400);

            // 3. Send re-run result with updated value on the same connection
            await sim1.SendResultsAsync(accessionNo, new Dictionary<string, string> { { "HGB", "14.9" } }, "ADVIA360");
            await Task.Delay(500);

            // 4. Assert in DB: current result is 14.9, history contains 14.2
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                var currentResult = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT r.id, r.value_numeric, r.is_current
                    FROM results r
                    JOIN components c ON c.id = r.component_id
                    JOIN order_tests ot ON ot.id = r.order_test_id
                    WHERE ot.specimen_id = @SpecId AND c.display_code = 'HGB' AND r.is_current = TRUE;
                ", new { SpecId = specId });

                Assert.NotNull(currentResult);
                Assert.Equal(14.9m, (decimal)currentResult.value_numeric);

                var historyResult = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT rh.value_numeric, rh.reason
                    FROM result_history rh
                    ORDER BY rh.id DESC LIMIT 1;
                ");

                Assert.NotNull(historyResult);
                Assert.Equal(14.2m, (decimal)historyResult.value_numeric);
                Assert.Equal("Instrument re-run / update", (string)historyResult.reason);
            }

            await driver.StopAsync();
        }

        public async Task Test19_UnknownBarcodeQuery_ReturnsTerminatorSafely()
        {
            int testPort = 5165;
            var inst = await EnsureAdvia360InstrumentAsync(testPort);

            var transport = new TcpTransport(TcpMode.Server, "127.0.0.1", testPort);
            using var driver = new AstmInstrumentDriver(inst, transport, _fixture.ConnectionFactory, _refRangeService);
            using var cts = new CancellationTokenSource();
            await driver.StartAsync(cts.Token);
            await Task.Delay(200);

            using var sim = new SimulatorEngine("127.0.0.1", testPort);
            await sim.ConnectAsync();

            // Query an accession that does not exist
            var frames = await sim.QueryWorklistAsync("999991");

            // Driver replies with Terminator without crashing
            Assert.True(frames.Count >= 1);
            Assert.True(frames.Any(f => f.StartsWith("L|") || f.Contains("|N")));

            await driver.StopAsync();
        }

        public async Task Test20_ManualResultEntry_SaveAndValidate()
        {
            var resultService = new ResultService(_fixture.ConnectionFactory, _authService, _refRangeService);
            long adminId = await GetAdminUserIdAsync();

            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "محمد عبد الله",
                Sex = "M",
                DateOfBirth = new DateTime(1990, 1, 1),
                Phone = "07700000001"
            }, adminId);

            long cbcTestId;
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");
            }

            var order = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            var orderResults = await resultService.GetOrderResultsAsync(order.Order.Id);
            Assert.NotNull(orderResults);
            Assert.True(orderResults!.Results.Count > 0);

            var firstComp = orderResults.Results[0];
            var batch = new List<(long OrderTestId, long ComponentId, string? ValueText)>
            {
                (firstComp.OrderTestId, firstComp.ComponentId, "8.5")
            };

            // Test 1: Save with null userId
            await resultService.SaveOrderResultsBatchAsync(order.Order.Id, batch, null);

            // Test 2: Save with invalid/dummy userId = 999999
            await resultService.SaveOrderResultsBatchAsync(order.Order.Id, batch, 999999);

            // Test 3: Validate with invalid/dummy userId = 999999
            await resultService.ValidateOrderResultsAsync(order.Order.Id, 999999);
        }

        public async Task Test21_MultiInstrumentMapping_HbA1c_BothInstrumentsProduceResults()
        {
            var catalogService = new TestCatalogService(_fixture.ConnectionFactory, _authService);
            long adminId = await GetAdminUserIdAsync();

            // 1. Seed HbA1c test and component
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                string seedSql = await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine(_fixture.CatalogPath, "HBA1C_seed.sql"));
                await conn.ExecuteAsync(seedSql);

                // Ensure two instruments exist: ADVIA360 & COBAS_C311
                await conn.ExecuteAsync(@"
                    INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, active)
                    VALUES ('COBAS_C311', 'Roche Cobas c311', 'ASTM', 'TCP', 'Bidirectional', TRUE)
                    ON CONFLICT (code) DO UPDATE SET active = TRUE;
                ");
            }

            // 2. Query component ID for HbA1c
            long testId;
            long compId;
            long instAdviaId;
            long instCobasId;
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                testId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_HBA1C';");
                compId = await conn.ExecuteScalarAsync<long>("SELECT id FROM components WHERE component_code = 'C_HBA1C';");
                instAdviaId = await conn.ExecuteScalarAsync<long>("SELECT id FROM instruments WHERE code = 'ADVIA360';");
                instCobasId = await conn.ExecuteScalarAsync<long>("SELECT id FROM instruments WHERE code = 'COBAS_C311';");
            }

            // 3. Link Instrument 1 (ADVIA360) with code 'HBA1C'
            await catalogService.AddOrUpdateInstrumentMappingAsync(instAdviaId, testId, compId, "HBA1C", userId: adminId);

            // 4. Link Instrument 2 (COBAS_C311) with code 'A1C'
            await catalogService.AddOrUpdateInstrumentMappingAsync(instCobasId, testId, compId, "A1C", userId: adminId);

            // 5. Verify both mappings exist
            var mappings = (await catalogService.GetInstrumentMappingsForComponentAsync(compId)).ToList();
            Assert.True(mappings.Count >= 2);
            Assert.True(mappings.Any(m => m.InstrumentId == instAdviaId && m.InstrumentTestCode == "HBA1C"));
            Assert.True(mappings.Any(m => m.InstrumentId == instCobasId && m.InstrumentTestCode == "A1C"));

            // 6. Create patient & order for HbA1c
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "خالد محمود عبد الرضا",
                Sex = "M",
                DateOfBirth = new DateTime(1980, 5, 10),
                Phone = "07709998877"
            }, adminId);

            var order = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { testId }
            }, adminId);

            string accessionNo = order.Specimens[0].Specimen.AccessionNo;
            long orderId = order.Order.Id;

            // 7. Simulate Instrument 1 (ADVIA360) sending result: HBA1C = 7.2
            int port1 = 5171;
            var inst1Item = new InstrumentItem { Id = instAdviaId, Code = "ADVIA360", Name = "Siemens ADVIA 360", Port = port1, Host = "127.0.0.1", OperatingMode = "Bidirectional", TcpMode = "Server" };
            var transport1 = new Lis.Core.Transport.TcpTransport(Lis.Core.Transport.TcpMode.Server, "127.0.0.1", port1);
            using var driver1 = new AstmInstrumentDriver(inst1Item, transport1, _fixture.ConnectionFactory, _refRangeService);
            using var cts1 = new CancellationTokenSource();
            await driver1.StartAsync(cts1.Token);
            await Task.Delay(200);

            using var sim1 = new SimulatorEngine("127.0.0.1", port1);
            await sim1.ConnectAsync();
            await sim1.SendResultsAsync(accessionNo, new Dictionary<string, string> { { "HBA1C", "7.2" } }, "ADVIA360");
            await Task.Delay(400);

            // Verify result stored in results table with flag 'H' (7.2 > 5.7)
            var resultService = new ResultService(_fixture.ConnectionFactory, _authService, _refRangeService);
            var resultsDto = await resultService.GetOrderResultsAsync(orderId);
            Assert.NotNull(resultsDto);
            var hba1cRes = resultsDto!.Results.FirstOrDefault(r => r.ComponentId == compId);
            Assert.NotNull(hba1cRes);
            Assert.Equal("7.2", hba1cRes!.ValueText);
            Assert.Equal("H", hba1cRes.Flag);
            await driver1.StopAsync();

            // 8. Now simulate Instrument 2 (COBAS_C311) re-running or sending result with code 'A1C' = 7.0
            int port2 = 5172;
            var inst2Item = new InstrumentItem { Id = instCobasId, Code = "COBAS_C311", Name = "Roche Cobas c311", Port = port2, Host = "127.0.0.1", OperatingMode = "Bidirectional", TcpMode = "Server" };
            var transport2 = new Lis.Core.Transport.TcpTransport(Lis.Core.Transport.TcpMode.Server, "127.0.0.1", port2);
            using var driver2 = new AstmInstrumentDriver(inst2Item, transport2, _fixture.ConnectionFactory, _refRangeService);
            using var cts2 = new CancellationTokenSource();
            await driver2.StartAsync(cts2.Token);
            await Task.Delay(200);

            using var sim2 = new SimulatorEngine("127.0.0.1", port2);
            await sim2.ConnectAsync();
            await sim2.SendResultsAsync(accessionNo, new Dictionary<string, string> { { "A1C", "7.0" } }, "COBAS_C311");
            await Task.Delay(400);

            // Verify newest result updated in results table to 7.0
            var updatedResultsDto = await resultService.GetOrderResultsAsync(orderId);
            var updatedHba1cRes = updatedResultsDto!.Results.FirstOrDefault(r => r.ComponentId == compId);
            Assert.NotNull(updatedHba1cRes);
            Assert.Equal("7.0", updatedHba1cRes!.ValueText);
            Assert.Equal("H", updatedHba1cRes.Flag);

            // Verify previous result 7.2 preserved in result_history!
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                var historyCount = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM result_history rh JOIN results r ON r.id = rh.result_id WHERE r.component_id = @CompId;",
                    new { CompId = compId });
                Assert.True(historyCount >= 1);
            }
            await driver2.StopAsync();
        }

        private async Task<InstrumentItem> EnsureSysmexXN350InstrumentAsync(int port)
        {
            var seedPath = System.IO.Path.Combine(_fixture.CatalogPath, "SYSMEX_XN350_seed.sql");
            if (!System.IO.File.Exists(seedPath))
            {
                seedPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "catalog", "SYSMEX_XN350_seed.sql");
            }
            if (System.IO.File.Exists(seedPath))
            {
                var sql = await System.IO.File.ReadAllTextAsync(seedPath);
                using var seedConn = _fixture.ConnectionFactory.CreateConnection();
                await seedConn.ExecuteAsync(sql);
            }

            using var conn = _fixture.ConnectionFactory.CreateConnection();
            const string sqlUpdate = @"
                UPDATE instruments
                SET port = @Port,
                    host = '127.0.0.1',
                    operating_mode = 'Bidirectional',
                    tcp_mode = 'Server',
                    active = TRUE,
                    updated_at = clock_timestamp()
                WHERE code = 'SYSMEX_XN350'
                RETURNING id, code, name, protocol, connection_type AS ConnectionType,
                          operating_mode AS OperatingMode, tcp_mode AS TcpMode, host, port,
                          active;
            ";
            return await conn.QuerySingleAsync<InstrumentItem>(sqlUpdate, new { Port = port });
        }

        public async Task Test22_Sysmex_XN350_Integration_HostQuery_And_5PartDiffResults()
        {
            int testPort = 5185;
            var inst = await EnsureSysmexXN350InstrumentAsync(testPort);
            long adminId = await GetAdminUserIdAsync();

            // 1. Create Patient & Order for CBC
            var patient = await _patientService.CreatePatientAsync(new Patient
            {
                FullNameAr = "محمد باقر حميد",
                FullNameEn = "Mohammed Baqir Hameed",
                Sex = "M",
                DateOfBirth = new DateTime(1990, 6, 15),
                Phone = "07801122334"
            }, adminId);

            long cbcTestId;
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                cbcTestId = await conn.ExecuteScalarAsync<long>("SELECT id FROM tests WHERE test_code = 'T_CBC_360';");
            }

            var order = await _orderService.CreateOrderAsync(new CreateOrderRequest
            {
                PatientId = patient.Id,
                TestIds = new List<long> { cbcTestId }
            }, adminId);

            string accessionNo = order.Specimens[0].Specimen.AccessionNo;
            long orderId = order.Order.Id;

            // 2. Start Sysmex Driver
            var transport = new TcpTransport(TcpMode.Server, "127.0.0.1", testPort);
            using var driver = new AstmInstrumentDriver(inst, transport, _fixture.ConnectionFactory, _refRangeService);
            using var cts = new CancellationTokenSource();
            await driver.StartAsync(cts.Token);
            await Task.Delay(200);

            // 3. Connect Simulator and send Host Query
            using var sim = new SimulatorEngine("127.0.0.1", testPort);
            await sim.ConnectAsync();
            var worklist = await sim.QueryWorklistAsync(accessionNo);

            Assert.True(worklist.Count > 0, "Sysmex simulator must receive worklist reply");
            Assert.True(worklist.Any(f => f.Contains(accessionNo)), "Worklist must contain queried accession");

            // 4. Send Sysmex 5-Part Diff + IG results
            var resultsDict = new Dictionary<string, string>
            {
                { "WBC", "6.8" },
                { "RBC", "4.9" },
                { "HGB", "14.5" },
                { "HCT", "43.0" },
                { "PLT", "210" },
                { "NEUT%", "62.5" },
                { "NEUT#", "4.25" },
                { "LYMPH%", "28.0" },
                { "LYMPH#", "1.90" },
                { "MONO%", "6.5" },
                { "MONO#", "0.44" },
                { "EO%", "2.2" },
                { "EO#", "0.15" },
                { "BASO%", "0.8" },
                { "BASO#", "0.05" },
                { "IG%", "0.2" },
                { "IG#", "0.01" }
            };

            await sim.SendResultsAsync(accessionNo, resultsDict, "SYSMEX_XN350");
            await Task.Delay(500);

            // 5. Verify results in DB via ResultService
            var resultService = new ResultService(_fixture.ConnectionFactory, _authService, _refRangeService);
            var resultsDto = await resultService.GetOrderResultsAsync(orderId);
            Assert.NotNull(resultsDto);
            Assert.True(resultsDto!.Results.Count >= 17, $"Expected at least 17 results ingested, got {resultsDto.Results.Count}");

            // Verify normal flags calculated for adult reference ranges
            var neutRes = resultsDto.Results.FirstOrDefault(r => r.DisplayCode == "NEUT%");
            Assert.NotNull(neutRes);
            Assert.Equal("62.5", neutRes!.ValueText);
            Assert.Equal("N", neutRes.Flag); // 40.0 - 75.0 is normal

            var igRes = resultsDto.Results.FirstOrDefault(r => r.DisplayCode == "IG%");
            Assert.NotNull(igRes);
            Assert.Equal("0.2", igRes!.ValueText);
            Assert.Equal("N", igRes.Flag); // 0.0 - 0.6 is normal

            // Verify specimen status is ResultReceived
            using (var conn = _fixture.ConnectionFactory.CreateConnection())
            {
                var specStatus = await conn.ExecuteScalarAsync<string>(
                    "SELECT status FROM specimens WHERE accession_no = @Acc;", new { Acc = accessionNo });
                Assert.Equal("ResultReceived", specStatus);
            }

            await driver.StopAsync();
        }
    }
}
