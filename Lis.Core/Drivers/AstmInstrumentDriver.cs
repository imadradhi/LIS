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
using Lis.Core.Utils;

namespace Lis.Core.Drivers
{
    public class AstmInstrumentDriver : IInstrumentDriver
    {
        private readonly InstrumentItem _instrument;
        private readonly ITransport _transport;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly IReferenceRangeService _referenceRangeService;
        private string _lastHeaderPassword = "DPC";

        private readonly StringBuilder _inboundBuffer = new StringBuilder();
        private readonly List<string> _inboundFrames = new List<string>();
        private bool _inTransaction = false;

        private TaskCompletionSource<byte>? _ackAwaiter;
        private readonly object _ackLock = new object();

        public bool InTransaction => _inTransaction;
        public string InstrumentCode => _instrument.Code;
        public bool IsConnected => _transport.IsConnected;
        public DateTime? LastActivity { get; private set; }
        public bool CaptureMode { get; set; } = false;

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
            // Pre-warm database connection pool so first query doesn't experience cold-start latency
            _ = Task.Run(async () =>
            {
                try
                {
                    using var conn = _connectionFactory.CreateConnection();
                    await conn.ExecuteScalarAsync("SELECT 1;");
                }
                catch { }
            });

            await _transport.StartAsync(cancellationToken);
        }

        public async Task StopAsync()
        {
            await _transport.StopAsync();
        }

        private async void OnTransportDataReceived(object? sender, byte[] bytes)
        {
            LastActivity = DateTime.UtcNow;
            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];

                lock (_ackLock)
                {
                    if (_ackAwaiter != null && (b == AstmConstants.ACK || b == AstmConstants.NAK || b == AstmConstants.EOT || b == AstmConstants.ENQ))
                    {
                        _ackAwaiter.TrySetResult(b);
                        if (b != AstmConstants.ENQ) continue;
                    }
                }

                if (b == AstmConstants.ENQ)
                {
                    // Instrument wants to send data, acknowledge with ACK
                    _inTransaction = true;
                    _inboundFrames.Clear();
                    _inboundBuffer.Clear();
                    await LogMiddlewareMessageAsync("<ENQ>", "IN", CaptureMode ? "CAPTURED" : "OK", "05");
                    await _transport.SendByteAsync(AstmConstants.ACK);
                    await LogMiddlewareMessageAsync("<ACK>", "OUT", CaptureMode ? "CAPTURED" : "OK", "06");
                }
                else if (b == AstmConstants.EOT)
                {
                    // End of Transmission
                    _inTransaction = false;
                    await LogMiddlewareMessageAsync("<EOT>", "IN", CaptureMode ? "CAPTURED" : "OK", "04");
                    string fullRawMessage = string.Join("\r\n", _inboundFrames);

                    if (!string.IsNullOrWhiteSpace(fullRawMessage))
                    {
                        if (CaptureMode)
                        {
                            // In Capture Mode: Save the full message without processing
                            await LogMiddlewareMessageAsync(fullRawMessage, "IN", "CAPTURED");
                        }
                        else
                        {
                            await HandleCompleteMessageAsync(fullRawMessage);
                        }
                    }
                    _inboundFrames.Clear();
                }
                else
                {
                    if (b == AstmConstants.STX && _inboundBuffer.Length > 0 && !_inboundBuffer.ToString().Contains('\n'))
                    {
                        // Reset if stale incomplete buffer exists
                        _inboundBuffer.Clear();
                    }

                    // Accumulate characters inside frame
                    char c = (char)b;
                    _inboundBuffer.Append(c);

                    // Check for Dimension Protocol (<STX>...<FS>...<ETX>)
                    if (b == DimensionProtocol.ETX && _inboundBuffer.ToString().Contains((char)DimensionProtocol.FS))
                    {
                        string dimFrame = _inboundBuffer.ToString().TrimStart((char)DimensionProtocol.ACK);
                        _inboundBuffer.Clear();

                        if (CaptureMode)
                        {
                            // Capture Mode: Unconditionally ACK frame and log without processing
                            await _transport.SendByteAsync(DimensionProtocol.ACK);
                            await LogMiddlewareMessageAsync(DimensionProtocol.ToReadableString(dimFrame), "IN", "CAPTURED");
                            await LogMiddlewareMessageAsync("<ACK>", "OUT", "CAPTURED", "06");

                            string trimmed = dimFrame.Trim('\x02', '\x03');
                            string[] fields = trimmed.Split((char)DimensionProtocol.FS);
                            string recordType = fields.Length > 0 ? fields[0].Trim() : string.Empty;

                            if (recordType.StartsWith("P", StringComparison.OrdinalIgnoreCase) || recordType.StartsWith("I", StringComparison.OrdinalIgnoreCase))
                            {
                                await _transport.SendAsync(DimensionProtocol.BuildNoRequest());
                                await LogMiddlewareMessageAsync("<STX>N<FS>6A<ETX>", "OUT", "CAPTURED");
                            }
                            else if (recordType.StartsWith("R", StringComparison.OrdinalIgnoreCase) || recordType.StartsWith("C", StringComparison.OrdinalIgnoreCase))
                            {
                                await _transport.SendAsync(DimensionProtocol.BuildAcceptance());
                                await LogMiddlewareMessageAsync("<STX>M<FS>A<FS><FS>E2<ETX>", "OUT", "CAPTURED");
                            }
                            return;
                        }

                        // Link-Layer Rule: Unconditionally send ACK (0x06) immediately if checksum is valid
                        bool isValid = DimensionProtocol.ValidateChecksum(dimFrame);
                        if (isValid)
                        {
                            await _transport.SendByteAsync(DimensionProtocol.ACK);
                        }
                        else
                        {
                            await _transport.SendByteAsync(DimensionProtocol.NAK);
                            await LogMiddlewareMessageAsync(DimensionProtocol.ToReadableString(dimFrame), "IN", "CHECKSUM_ERROR");
                            return;
                        }

                        // Dispatch frame logic
                        await HandleDimensionFrameAsync(dimFrame);
                    }
                    else if (c == '\n' && _inboundBuffer.ToString().EndsWith("\r\n"))
                    {
                        // ASTM Frame complete
                        string frameStr = _inboundBuffer.ToString();
                        _inboundBuffer.Clear();

                        if (CaptureMode)
                        {
                            // Capture Mode: Unconditionally accept frame with ACK and save
                            if (AstmFraming.TryParseFrame(frameStr, out var frame) && frame != null)
                            {
                                _inboundFrames.Add(frame.Content);
                            }
                            else
                            {
                                _inboundFrames.Add(frameStr.Trim());
                            }
                            await _transport.SendByteAsync(AstmConstants.ACK);
                            await LogMiddlewareMessageAsync(frameStr.Trim(), "IN", "CAPTURED");
                            await LogMiddlewareMessageAsync("<ACK>", "OUT", "CAPTURED", "06");
                        }
                        else
                        {
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
        }

        private async Task HandleDimensionFrameAsync(string frameStr)
        {
            string readable = DimensionProtocol.ToReadableString(frameStr);

            // Strip STX / ETX
            string trimmed = frameStr.Trim('\x02', '\x03');
            string[] fields = trimmed.Split((char)DimensionProtocol.FS);
            string recordType = fields.Length > 0 ? fields[0].Trim() : string.Empty;

            if (recordType.StartsWith("P", StringComparison.OrdinalIgnoreCase))
            {
                // Siemens Dimension Poll Message -> Reply with No-Request (<STX>N<FS>6A<ETX>)
                await _transport.SendAsync(DimensionProtocol.BuildNoRequest());
                await LogMiddlewareMessageAsync(readable, "IN", "OK");
                await LogMiddlewareMessageAsync("<STX>N<FS>6A<ETX>", "OUT", "OK");
            }
            else if (recordType.StartsWith("R", StringComparison.OrdinalIgnoreCase))
            {
                // Siemens Dimension Result Message -> Send Acceptance (<STX>M<FS>A<FS><FS>E2<ETX>)
                await _transport.SendAsync(DimensionProtocol.BuildAcceptance());
                await LogMiddlewareMessageAsync(readable, "IN", "OK");
                await LogMiddlewareMessageAsync("<STX>M<FS>A<FS><FS>E2<ETX>", "OUT", "OK");

                // Parse test results and save to results table
                if (DimensionProtocol.TryParseDimensionResult(frameStr, out var dimRecord) && dimRecord != null)
                {
                    var astmMsg = new ParsedAstmMessage
                    {
                        Order = new AstmOrderRecord { SpecimenId = dimRecord.AccessionNo }
                    };

                    foreach (var r in dimRecord.Results)
                    {
                        astmMsg.Results.Add(new AstmResultRecord
                        {
                            UniversalTestCode = r.TestCode,
                            Value = r.Value,
                            Units = r.Units,
                            ResultAbnormalFlag = r.Flag
                        });
                    }

                    await HandleResultsAsync(astmMsg);
                }
            }
            else if (recordType.StartsWith("C", StringComparison.OrdinalIgnoreCase))
            {
                // Siemens Dimension Calibration Result Message -> Send Acceptance (<STX>M<FS>A<FS><FS>E2<ETX>)
                await _transport.SendAsync(DimensionProtocol.BuildAcceptance());
                await LogMiddlewareMessageAsync(readable, "IN", "OK");
                await LogMiddlewareMessageAsync("<STX>M<FS>A<FS><FS>E2<ETX>", "OUT", "OK");
            }
            else if (recordType.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                // Siemens Dimension Acceptance/Rejection of Host Download -> Log instrument response
                await LogMiddlewareMessageAsync(readable, "IN", "OK");
            }
            else if (recordType.StartsWith("I", StringComparison.OrdinalIgnoreCase))
            {
                // Background log to eliminate query turnaround latency
                _ = Task.Run(async () =>
                {
                    try { await LogMiddlewareMessageAsync(readable, "IN", "OK"); } catch { }
                });

                string accessionNo = fields.Length > 1 ? fields[1].Trim() : string.Empty;
                if (!string.IsNullOrWhiteSpace(accessionNo))
                {
                    try
                    {
                        using var conn = _connectionFactory.CreateConnection();

                        // Single unified high-performance query for specimen, MRN & mapped test codes
                        const string lookupQuerySql = @"
                            SELECT s.id AS specimen_id, s.order_id, p.full_name_ar, p.mrn,
                                   it.instrument_test_code
                            FROM specimens s
                            JOIN orders o ON o.id = s.order_id
                            JOIN patients p ON p.id = o.patient_id
                            LEFT JOIN order_tests ot ON ot.specimen_id = s.id
                            LEFT JOIN instrument_tests it ON it.test_id = ot.test_id AND it.instrument_id = @InstrumentId
                            WHERE (
                                s.accession_no = @AccessionNo
                                OR s.accession_no = LPAD(@AccessionNo, 6, '0')
                                OR LPAD(s.accession_no, 6, '0') = LPAD(@AccessionNo, 6, '0')
                                OR TRIM(LEADING '0' FROM s.accession_no) = TRIM(LEADING '0' FROM @AccessionNo)
                            ) AND s.archived = FALSE
                            ORDER BY s.id DESC;
                        ";

                        var rows = (await conn.QueryAsync<dynamic>(lookupQuerySql, new
                        {
                            AccessionNo = accessionNo,
                            InstrumentId = _instrument.Id
                        })).ToList();

                        if (rows.Count > 0)
                        {
                            var firstRow = rows[0];
                            var testCodes = rows
                                .Where(r => r.instrument_test_code != null)
                                .Select(r => (string)r.instrument_test_code)
                                .Distinct()
                                .ToList();

                            if (testCodes.Count > 0)
                            {
                                // Download Work Order via Sample Request Message (D)
                                string safePatientId = firstRow.mrn != null ? firstRow.mrn.ToString() : accessionNo;
                                byte[] downloadPacket = DimensionProtocol.BuildSampleRequest(
                                    accessionNo,
                                    safePatientId,
                                    testCodes);

                                await Task.Delay(20);
                                await _transport.SendAsync(downloadPacket);
                                await LogMiddlewareMessageAsync(DimensionProtocol.ToReadableString(Encoding.ASCII.GetString(downloadPacket)), "OUT", "OK");

                                await conn.ExecuteAsync(
                                    "UPDATE specimens SET status = 'InProgress', updated_at = clock_timestamp() WHERE id = @Id AND status IN ('Ordered','Collected');",
                                    new { Id = (long)firstRow.specimen_id });

                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Dimension Query Error] {ex.Message}");
                    }
                }

                // If no tests found or unknown barcode, send No-Request immediately
                await Task.Delay(20);
                await _transport.SendAsync(DimensionProtocol.BuildNoRequest());
                await LogMiddlewareMessageAsync("<STX>N<FS>6A<ETX>", "OUT", "OK");
            }
            else
            {
                // Unrecognized Dimension frame -> Reply with No-Request to prevent instrument lock
                await _transport.SendAsync(DimensionProtocol.BuildNoRequest());
                await LogMiddlewareMessageAsync(readable, "IN", "OK");
                await LogMiddlewareMessageAsync("<STX>N<FS>6A<ETX>", "OUT", "OK");
            }
        }

        public async Task ProcessInboundMessageAsync(string rawMessage, bool logMessage = false)
        {
            if (CaptureMode)
            {
                if (logMessage)
                {
                    await LogMiddlewareMessageAsync(rawMessage, "IN", "CAPTURED");
                }
                return;
            }

            if (logMessage)
            {
                await LogMiddlewareMessageAsync(rawMessage, "IN", "OK");
            }

            var parsed = AstmMessageParser.ParseMessage(rawMessage);
            if (parsed.Header != null && !string.IsNullOrWhiteSpace(parsed.Header.AccessPassword))
            {
                _lastHeaderPassword = parsed.Header.AccessPassword;
            }

            if (parsed.IsQuery && string.Equals(_instrument.OperatingMode, "Bidirectional", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHostQueryAsync(parsed.Query!);
            }
            else if (parsed.HasResults)
            {
                await HandleResultsAsync(parsed);
            }
        }

        private async Task HandleCompleteMessageAsync(string rawMessage)
        {
            // 1. Log inbound raw message in background so urgent ASTM query turnarounds are never delayed
            _ = Task.Run(async () =>
            {
                try { await LogMiddlewareMessageAsync(rawMessage, "IN", "OK"); } catch { }
            });

            // 2. Parse and handle ASTM message immediately
            await ProcessInboundMessageAsync(rawMessage, logMessage: false);
        }

        private async Task HandleHostQueryAsync(AstmQueryRecord query)
        {
            string accessionNo = query.ExtractAccessionNo();
            if (string.IsNullOrWhiteSpace(accessionNo)) return;

            using var conn = _connectionFactory.CreateConnection();

            // Single unified high-performance query for specimen, patient & mapped test codes
            const string lookupQuerySql = @"
                SELECT s.id AS specimen_id, s.order_id, s.status AS specimen_status,
                       st.name_en AS specimen_type_name,
                       p.mrn, p.full_name_ar, p.sex, p.date_of_birth,
                       it.instrument_test_code
                FROM specimens s
                JOIN orders o ON o.id = s.order_id
                JOIN patients p ON p.id = o.patient_id
                JOIN specimen_types st ON st.id = s.specimen_type_id
                LEFT JOIN order_tests ot ON ot.specimen_id = s.id
                LEFT JOIN instrument_tests it ON it.test_id = ot.test_id AND it.instrument_id = @InstrumentId
                WHERE (
                    s.accession_no = @AccessionNo
                    OR s.accession_no = LPAD(@AccessionNo, 6, '0')
                    OR LPAD(s.accession_no, 6, '0') = LPAD(@AccessionNo, 6, '0')
                    OR TRIM(LEADING '0' FROM s.accession_no) = TRIM(LEADING '0' FROM @AccessionNo)
                ) AND s.archived = FALSE
                ORDER BY s.id DESC;
            ";

            var rows = (await conn.QueryAsync<dynamic>(lookupQuerySql, new
            {
                AccessionNo = accessionNo,
                InstrumentId = _instrument.Id
            })).ToList();

            if (rows.Count == 0)
            {
                // Unknown barcode query - send empty terminator
                await SendTerminatorOnlyAsync();
                return;
            }

            var firstRow = rows[0];
            var testCodes = rows
                .Where(r => r.instrument_test_code != null)
                .Select(r => (string)r.instrument_test_code)
                .Distinct()
                .ToList();

            if (testCodes.Count == 0)
            {
                // No tests for this instrument on this specimen
                await SendTerminatorOnlyAsync();
                return;
            }

            // Build worklist reply frames
            string safePatientName = (firstRow.mrn != null) ? $"Patient_{firstRow.mrn}" : "Patient";
            long safeMrn = firstRow.mrn != null ? (long)firstRow.mrn : 1;
            var frames = AstmMessageParser.BuildWorklistFrames(
                accessionNo,
                safeMrn,
                safePatientName,
                testCodes,
                (string)(firstRow.specimen_type_name ?? "Serum"),
                string.IsNullOrWhiteSpace(_lastHeaderPassword) ? "DPC" : _lastHeaderPassword,
                "LIS",
                "LIS",
                "N");

            // 1. Send reply via ASTM Handshake (ENQ -> wait ACK -> frames -> wait ACKs -> EOT)
            bool sent = await SendFramesWithHandshakeAsync(frames);

            // 2. Update specimen status to InProgress after sending worklist
            if (sent)
            {
                await conn.ExecuteAsync(
                    "UPDATE specimens SET status = 'InProgress', updated_at = clock_timestamp() WHERE id = @Id AND status IN ('Ordered','Collected');",
                    new { Id = (long)firstRow.specimen_id });
            }
        }

        private async Task HandleResultsAsync(ParsedAstmMessage msg)
        {
            string accessionNo = msg.Order?.ExtractAccessionNo() ?? string.Empty;
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
                    WHERE (
                        s.accession_no = @AccessionNo
                        OR s.accession_no = LPAD(@AccessionNo, 6, '0')
                        OR LPAD(s.accession_no, 6, '0') = LPAD(@AccessionNo, 6, '0')
                        OR TRIM(LEADING '0' FROM s.accession_no) = TRIM(LEADING '0' FROM @AccessionNo)
                    ) AND s.archived = FALSE
                    ORDER BY s.id DESC LIMIT 1;
                ", new { AccessionNo = accessionNo }, trans);

                // Fallback: If not found by exact accession (e.g. user entered rack/pos or secondary barcode on instrument),
                // match the currently active InProgress specimen
                if (specimen == null && !isQc)
                {
                    specimen = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                        SELECT s.id AS specimen_id, s.order_id, o.order_datetime,
                               p.sex, p.date_of_birth
                        FROM specimens s
                        JOIN orders o ON o.id = s.order_id
                        JOIN patients p ON p.id = o.patient_id
                        WHERE s.status = 'InProgress' AND s.archived = FALSE
                        ORDER BY s.id DESC LIMIT 1;
                    ", null, trans);
                }

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
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'bun' AND LOWER(TRIM(@Code)) = 'urea')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'urea' AND LOWER(TRIM(@Code)) = 'bun')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'alp' AND LOWER(TRIM(@Code)) = 'alpi')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'alt' AND LOWER(TRIM(@Code)) = 'alti')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'ast' AND LOWER(TRIM(@Code)) = 'asti')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'glu' AND LOWER(TRIM(@Code)) = 'gluc')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'alkp' AND LOWER(TRIM(@Code)) = 'alp')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'alp' AND LOWER(TRIM(@Code)) = 'alkp')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'tg' AND LOWER(TRIM(@Code)) = 'trig')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'trig' AND LOWER(TRIM(@Code)) = 'tg')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'a1c' AND LOWER(TRIM(@Code)) = 'hba1c')
                              OR (LOWER(TRIM(it.instrument_test_code)) = 'hba1c' AND LOWER(TRIM(@Code)) = 'a1c')
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

        private async Task<byte> WaitForAckAsync(int timeoutMs = 3000)
        {
            TaskCompletionSource<byte> tcs;
            lock (_ackLock)
            {
                _ackAwaiter = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
                tcs = _ackAwaiter;
            }

            using var cts = new CancellationTokenSource(timeoutMs);
            using (cts.Token.Register(() => tcs.TrySetResult(0), useSynchronizationContext: false))
            {
                try
                {
                    return await tcs.Task;
                }
                finally
                {
                    lock (_ackLock)
                    {
                        if (_ackAwaiter == tcs) _ackAwaiter = null;
                    }
                }
            }
        }

        private async Task<bool> SendFramesWithHandshakeAsync(List<string> frames)
        {
            try
            {
                // Brief pause (20ms) after instrument session before sending ENQ
                await Task.Delay(20);

                // 1. Send ENQ to analyzer
                await _transport.SendByteAsync(AstmConstants.ENQ);
                await LogMiddlewareMessageAsync("<ENQ>", "OUT", "OK", "05");

                // Wait for ACK from analyzer (up to 3.5 seconds)
                byte ack = await WaitForAckAsync(3500);
                if (ack != AstmConstants.ACK)
                {
                    if (ack == AstmConstants.ENQ)
                    {
                        await LogMiddlewareMessageAsync("Contention detected (analyzer sent ENQ). Host yielded.", "IN", "OK");
                    }
                    else
                    {
                        await LogMiddlewareMessageAsync(
                            ack == 0 ? "Timeout waiting for ACK after ENQ" : $"Received 0x{ack:X2} instead of ACK after ENQ",
                            "IN",
                            "ERROR");
                    }
                    await _transport.SendByteAsync(AstmConstants.EOT);
                    return false;
                }

                await LogMiddlewareMessageAsync("<ACK>", "IN", "OK", "06");

                // 2. Transmit each frame with lock-step ACK validation
                var fullOutbound = new StringBuilder();
                for (int idx = 0; idx < frames.Count; idx++)
                {
                    string frame = frames[idx];
                    byte[] data = Encoding.ASCII.GetBytes(frame);
                    bool frameAcked = false;

                    for (int attempt = 0; attempt < 3 && !frameAcked; attempt++)
                    {
                        await _transport.SendAsync(data);
                        await LogMiddlewareMessageAsync(frame.Trim(), "OUT", "OK");

                        byte frameAck = await WaitForAckAsync(3500);
                        if (frameAck == AstmConstants.ACK)
                        {
                            await LogMiddlewareMessageAsync("<ACK>", "IN", "OK", "06");
                            frameAcked = true;
                            fullOutbound.Append(frame);
                        }
                        else if (frameAck == AstmConstants.NAK)
                        {
                            await LogMiddlewareMessageAsync("<NAK>", "IN", "RETRY", "15");
                            await Task.Delay(150);
                        }
                        else
                        {
                            await LogMiddlewareMessageAsync(
                                frameAck == 0 ? $"Timeout waiting for ACK on Frame {idx + 1}" : $"Unexpected byte 0x{frameAck:X2} on Frame {idx + 1}",
                                "IN",
                                "ERROR");
                            break;
                        }
                    }

                    if (!frameAcked)
                    {
                        await _transport.SendByteAsync(AstmConstants.EOT);
                        await LogMiddlewareMessageAsync("<EOT>", "OUT", "ABORTED", "04");
                        return false;
                    }
                }

                // 3. Send EOT to terminate session normally
                await _transport.SendByteAsync(AstmConstants.EOT);
                await LogMiddlewareMessageAsync("<EOT>", "OUT", "OK", "04");
                return true;
            }
            catch (Exception ex)
            {
                await LogMiddlewareMessageAsync($"SendFramesWithHandshake exception: {ex.Message}", "SYS", "ERROR");
                try { await _transport.SendByteAsync(AstmConstants.EOT); } catch { }
                return false;
            }
        }

        private async Task SendTerminatorOnlyAsync()
        {
            var header = new AstmHeaderRecord
            {
                SenderId = "LIS",
                ReceiverId = "LIS",
                AccessPassword = string.IsNullOrWhiteSpace(_lastHeaderPassword) ? "DPC" : _lastHeaderPassword,
                Timestamp = DateTime.Now
            };
            var frames = new List<string>
            {
                AstmFraming.BuildFrame(1, header.ToAstmString()),
                AstmFraming.BuildFrame(2, new AstmTerminatorRecord { SequenceNumber = 1, TerminationCode = "N" }.ToAstmString())
            };
            await SendFramesWithHandshakeAsync(frames);
        }

        private async Task LogMiddlewareMessageAsync(string rawMessage, string direction, string parseStatus, string? rawHex = null)
        {
            try
            {
                rawHex ??= HexUtils.ToHexRepresentation(rawMessage);

                using var conn = _connectionFactory.CreateConnection();
                await conn.ExecuteAsync(@"
                    INSERT INTO middleware_log (raw_message, raw_hex, instrument_id, direction, timestamp, parse_status)
                    VALUES (@Raw, @RawHex, @InstId, @Dir, clock_timestamp(), @Status);
                ", new
                {
                    Raw = rawMessage,
                    RawHex = rawHex,
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
