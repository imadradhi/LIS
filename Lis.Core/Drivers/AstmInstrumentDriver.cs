using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.ASTM;
using Lis.Core.Data;
using Lis.Core.Models;
using Lis.Core.Services;
using Lis.Core.Transport;

namespace Lis.Core.Drivers
{
    public class AstmInstrumentDriver : IInstrumentDriver
    {
        private readonly InstrumentItem _instrument;
        private readonly ITransport _transport;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IReferenceRangeService _referenceRangeService;

        private readonly StringBuilder _inboundBuffer = new StringBuilder();
        private readonly List<string> _inboundFrames = new List<string>();
        private bool _inTransaction = false;

        public bool InTransaction => _inTransaction;
        public string InstrumentCode => _instrument.Code;
        public bool IsConnected => _transport.IsConnected;

        public event EventHandler<string>? StatusChanged;

        public AstmInstrumentDriver(
            InstrumentItem instrument,
            ITransport transport,
            IDbConnectionFactory connectionFactory,
            IReferenceRangeService referenceRangeService)
        {
            _instrument = instrument;
            _transport = transport;
            _connectionFactory = connectionFactory;
            _referenceRangeService = referenceRangeService;

            _transport.DataReceived += OnTransportDataReceived;
            _transport.ConnectionStateChanged += (s, connected) =>
            {
                StatusChanged?.Invoke(this, connected ? "Connected" : "Disconnected");
            };
        }

        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await _transport.StartAsync(cancellationToken);
        }

        public async Task StopAsync()
        {
            await _transport.StopAsync();
        }

        private async void OnTransportDataReceived(object? sender, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];

                if (b == AstmConstants.ENQ)
                {
                    // Instrument wants to send data, acknowledge with ACK
                    _inTransaction = true;
                    _inboundFrames.Clear();
                    _inboundBuffer.Clear();
                    await _transport.SendByteAsync(AstmConstants.ACK);
                }
                else if (b == AstmConstants.EOT)
                {
                    // End of Transmission
                    _inTransaction = false;
                    string fullRawMessage = string.Join("\r\n", _inboundFrames);

                    if (!string.IsNullOrWhiteSpace(fullRawMessage))
                    {
                        await HandleCompleteMessageAsync(fullRawMessage);
                    }
                }
                else
                {
                    // Accumulate characters inside frame
                    char c = (char)b;
                    _inboundBuffer.Append(c);

                    if (c == '\n' && _inboundBuffer.ToString().EndsWith("\r\n"))
                    {
                        // Frame complete
                        string frameStr = _inboundBuffer.ToString();
                        _inboundBuffer.Clear();

                        if (AstmFraming.TryParseFrame(frameStr, out var frame) && frame != null)
                        {
                            if (frame.IsChecksumValid)
                            {
                                _inboundFrames.Add(frame.Content);
                                await _transport.SendByteAsync(AstmConstants.ACK);
                            }
                            else
                            {
                                // Checksum failed
                                await _transport.SendByteAsync(AstmConstants.NAK);
                            }
                        }
                    }
                }
            }
        }

        private async Task HandleCompleteMessageAsync(string rawMessage)
        {
            // 1. Log inbound raw message
            await LogMiddlewareMessageAsync(rawMessage, "IN", "OK");

            // 2. Parse ASTM message
            var parsed = AstmMessageParser.ParseMessage(rawMessage);

            // 3. Check if Host Query
            if (parsed.IsQuery && string.Equals(_instrument.OperatingMode, "Bidirectional", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHostQueryAsync(parsed.Query!);
            }
            // 4. Check if Results
            else if (parsed.HasResults)
            {
                await HandleResultsAsync(parsed);
            }
        }

        private async Task HandleHostQueryAsync(AstmQueryRecord query)
        {
            string accessionNo = query.ExtractAccessionNo();
            if (string.IsNullOrWhiteSpace(accessionNo)) return;

            using var conn = _connectionFactory.CreateConnection();

            // Find active non-archived specimen matching accession
            const string lookupSpecimenSql = @"
                SELECT s.id AS specimen_id, s.order_id, s.status AS specimen_status,
                       st.name_en AS specimen_type_name,
                       p.mrn, p.full_name_ar, p.sex, p.date_of_birth
                FROM specimens s
                JOIN orders o ON o.id = s.order_id
                JOIN patients p ON p.id = o.patient_id
                JOIN specimen_types st ON st.id = s.specimen_type_id
                WHERE s.accession_no = @AccessionNo AND s.archived = FALSE
                ORDER BY s.id DESC
                LIMIT 1;
            ";

            var specimen = await conn.QueryFirstOrDefaultAsync<dynamic>(lookupSpecimenSql, new { AccessionNo = accessionNo });

            if (specimen == null)
            {
                // Unknown barcode query - send empty terminator
                await SendTerminatorOnlyAsync();
                return;
            }

            // Find tests for this specimen that are mapped to THIS instrument in instrument_tests
            const string lookupTestsSql = @"
                SELECT DISTINCT it.instrument_test_code
                FROM order_tests ot
                JOIN instrument_tests it ON it.test_id = ot.test_id
                WHERE ot.specimen_id = @SpecimenId 
                  AND it.instrument_id = @InstrumentId;
            ";

            var testCodes = (await conn.QueryAsync<string>(lookupTestsSql, new
            {
                SpecimenId = (long)specimen.specimen_id,
                InstrumentId = _instrument.Id
            })).ToList();

            if (testCodes.Count == 0)
            {
                // No tests for this instrument on this specimen
                await SendTerminatorOnlyAsync();
                return;
            }

            // Build worklist reply frames
            var frames = AstmMessageParser.BuildWorklistFrames(
                accessionNo,
                (long)specimen.mrn,
                (string)specimen.full_name_ar,
                testCodes,
                (string)specimen.specimen_type_name);

            // Update specimen status to InProgress before sending worklist
            await conn.ExecuteAsync(
                "UPDATE specimens SET status = 'InProgress', updated_at = clock_timestamp() WHERE id = @Id AND status IN ('Ordered','Collected');",
                new { Id = (long)specimen.specimen_id });

            // Send reply via ASTM Handshake (ENQ -> wait ACK -> frames -> wait ACKs -> EOT)
            await SendFramesWithHandshakeAsync(frames);
        }

        private async Task HandleResultsAsync(ParsedAstmMessage msg)
        {
            string accessionNo = msg.Order?.SpecimenId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(accessionNo)) return;

            using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
            using var trans = conn.BeginTransaction();

            try
            {
                // Check if this is a QC sample (e.g. starts with QC or not found in patient specimens)
                bool isQc = accessionNo.StartsWith("QC", StringComparison.OrdinalIgnoreCase);

                var specimen = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT s.id AS specimen_id, s.order_id, o.order_datetime,
                           p.sex, p.date_of_birth
                    FROM specimens s
                    JOIN orders o ON o.id = s.order_id
                    JOIN patients p ON p.id = o.patient_id
                    WHERE s.accession_no = @AccessionNo AND s.archived = FALSE
                    ORDER BY s.id DESC LIMIT 1;
                ", new { AccessionNo = accessionNo }, trans);

                if (isQc || specimen == null)
                {
                    // Decision 15: Save QC results separately in qc_results
                    foreach (var r in msg.Results)
                    {
                        var compId = await conn.ExecuteScalarAsync<long?>(@"
                            SELECT component_id FROM instrument_tests
                            WHERE instrument_id = @InstId AND instrument_test_code = @Code LIMIT 1;
                        ", new { InstId = _instrument.Id, Code = r.UniversalTestCode }, trans);

                        if (compId.HasValue && decimal.TryParse(r.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numVal))
                        {
                            await conn.ExecuteAsync(@"
                                INSERT INTO qc_results (instrument_id, component_id, value, rule_violation, accepted, received_at)
                                VALUES (@InstId, @CompId, @Value, @Violation, TRUE, clock_timestamp());
                            ", new
                            {
                                InstId = _instrument.Id,
                                CompId = compId.Value,
                                Value = numVal,
                                Violation = r.ResultAbnormalFlag == "N" ? null : r.ResultAbnormalFlag
                            }, trans);
                        }
                    }

                    trans.Commit();
                    return;
                }

                // Patient Result: Save into results & result_history
                if (specimen == null) return;
                long specimenId = (long)specimen.specimen_id;
                long orderId = (long)specimen.order_id;
                string sex = (string)specimen.sex;
                DateTime dob = (DateTime)specimen.date_of_birth;
                DateTime orderDate = (DateTime)specimen.order_datetime;

                foreach (var r in msg.Results)
                {
                    // Find matching component_id and order_test_id
                    var mapping = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                        SELECT it.component_id, ot.id AS order_test_id
                        FROM instrument_tests it
                        JOIN order_tests ot ON ot.test_id = it.test_id
                        WHERE ot.specimen_id = @SpecimenId
                          AND it.instrument_id = @InstId
                          AND (
                              LOWER(TRIM(it.instrument_test_code)) = LOWER(TRIM(@Code))
                              OR REPLACE(LOWER(TRIM(it.instrument_test_code)), '-', '') = REPLACE(LOWER(TRIM(@Code)), '-', '')
                          )
                        LIMIT 1;
                    ", new
                    {
                        SpecimenId = specimenId,
                        InstId = _instrument.Id,
                        Code = r.UniversalTestCode
                    }, trans);

                    if (mapping == null) continue;

                    long compId = (long)mapping.component_id;
                    long orderTestId = (long)mapping.order_test_id;

                    decimal? numVal = null;
                    if (decimal.TryParse(r.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedVal))
                        numVal = parsedVal;

                    // Evaluate Reference Range and Flag
                    var eval = await _referenceRangeService.EvaluateRangeAsync(
                        compId, sex, dob, orderDate, numVal, conn, trans);

                    string flag = eval.Flag ?? r.ResultAbnormalFlag;

                    // Decision 14: Never overwrite results. Store in result_history if prior result exists
                    var existingResult = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                        SELECT id, value_text, value_numeric, flag, instrument_id
                        FROM results
                        WHERE order_test_id = @OrderTestId AND component_id = @CompId AND is_current = TRUE;
                    ", new { OrderTestId = orderTestId, CompId = compId }, trans);

                    if (existingResult != null)
                    {
                        // Copy older value to result_history
                        await conn.ExecuteAsync(@"
                            INSERT INTO result_history (result_id, value_text, value_numeric, flag, instrument_id, changed_at, reason)
                            VALUES (@ResultId, @ValueText, @ValueNum, @Flag, @InstId, clock_timestamp(), 'Instrument re-run / update');

                            UPDATE results
                            SET is_current = FALSE
                            WHERE id = @ResultId;
                        ", new
                        {
                            ResultId = (long)existingResult.id,
                            ValueText = (string?)existingResult.value_text,
                            ValueNum = (decimal?)existingResult.value_numeric,
                            Flag = (string?)existingResult.flag,
                            InstId = (long?)existingResult.instrument_id
                        }, trans);
                    }

                    // Insert newest result with is_current = TRUE
                    await conn.ExecuteAsync(@"
                        INSERT INTO results (order_test_id, component_id, value_text, value_numeric, unit, flag, instrument_id, resulted_at, is_current, updated_at)
                        VALUES (@OrderTestId, @CompId, @ValueText, @ValueNumeric, @Unit, @Flag, @InstrumentId, clock_timestamp(), TRUE, clock_timestamp());
                    ", new
                    {
                        OrderTestId = orderTestId,
                        CompId = compId,
                        ValueText = r.Value,
                        ValueNumeric = numVal,
                        Unit = r.Units,
                        Flag = flag,
                        InstrumentId = _instrument.Id
                    }, trans);
                }

                // Update Specimen and Order status to ResultReceived
                await conn.ExecuteAsync(@"
                    UPDATE specimens SET status = 'ResultReceived', updated_at = clock_timestamp() WHERE id = @Id;
                    UPDATE order_tests SET status = 'ResultReceived', updated_at = clock_timestamp() WHERE specimen_id = @Id;
                    UPDATE orders SET status = 'ResultReceived', updated_at = clock_timestamp() WHERE id = @OrderId;
                ", new { Id = specimenId, OrderId = orderId }, trans);

                // Decision 16: Emit PostgreSQL notification for desktop real-time refresh
                await conn.ExecuteAsync("SELECT pg_notify('lis_results_channel', @Payload);", new { Payload = $"ORDER_{orderId}_SPEC_{specimenId}" }, trans);

                trans.Commit();

                // Evaluate any derived / calculated tests for this order
                try
                {
                    var calcService = new CalculatedComponentService(_connectionFactory, _referenceRangeService);
                    await calcService.EvaluateCalculatedComponentsForOrderAsync(orderId, null);
                }
                catch
                {
                    // Non-fatal if formula calculation fails
                }
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        private async Task SendFramesWithHandshakeAsync(List<string> frames)
        {
            // 1. Send ENQ
            await _transport.SendByteAsync(AstmConstants.ENQ);

            // Wait a brief moment or ACK
            await Task.Delay(50);

            // 2. Send each frame
            var fullOutbound = new StringBuilder();
            foreach (var frame in frames)
            {
                byte[] data = Encoding.ASCII.GetBytes(frame);
                await _transport.SendAsync(data);
                fullOutbound.Append(frame);
                await Task.Delay(50);
            }

            // 3. Send EOT
            await _transport.SendByteAsync(AstmConstants.EOT);

            // Log outbound message
            await LogMiddlewareMessageAsync(fullOutbound.ToString(), "OUT", "OK");
        }

        private async Task SendTerminatorOnlyAsync()
        {
            var frames = new List<string>
            {
                AstmFraming.BuildFrame(1, new AstmHeaderRecord().ToAstmString()),
                AstmFraming.BuildFrame(2, new AstmTerminatorRecord { TerminationCode = "N" }.ToAstmString())
            };
            await SendFramesWithHandshakeAsync(frames);
        }

        private async Task LogMiddlewareMessageAsync(string rawMessage, string direction, string parseStatus)
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                await conn.ExecuteAsync(@"
                    INSERT INTO middleware_log (raw_message, instrument_id, direction, timestamp, parse_status)
                    VALUES (@Raw, @InstId, @Dir, clock_timestamp(), @Status);
                ", new
                {
                    Raw = rawMessage,
                    InstId = _instrument.Id,
                    Dir = direction,
                    Status = parseStatus
                });
            }
            catch { }
        }

        public void Dispose()
        {
            _transport.Dispose();
        }
    }
}
