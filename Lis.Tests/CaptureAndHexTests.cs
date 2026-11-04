using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Lis.Core.ASTM;
using Lis.Core.Drivers;
using Lis.Core.Models;
using Lis.Core.Services;
using Lis.Core.Transport;
using Lis.Core.Utils;

namespace Lis.Tests
{
    public class CaptureAndHexTests
    {
        private readonly TestDatabaseFixture _fixture;

        public CaptureAndHexTests(TestDatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        public Task Test_HexUtils_ToHexString_And_Representation()
        {
            // 1. Raw byte array to Hex
            byte[] bytes = new byte[] { 0x02, 0x31, 0x48, 0x0D, 0x0A };
            string hex = HexUtils.ToHexString(bytes);
            Assert.Equal("02 31 48 0D 0A", hex);

            // 2. String with control tokens to Hex
            string tokenStr = "<ENQ>";
            Assert.Equal("05", HexUtils.ToHexRepresentation(tokenStr));

            string ackStr = "<ACK>";
            Assert.Equal("06", HexUtils.ToHexRepresentation(ackStr));

            string frameStr = "<STX>1H|\\^&|||LIS<CR><ETX>42<CR><LF>";
            string frameHex = HexUtils.ToHexRepresentation(frameStr);
            Assert.True(frameHex.StartsWith("02 31 48 7C"));
            Assert.True(frameHex.EndsWith("0D 0A"));

            // 3. Formatted Hex Dump contains offsets, hex, and printable text
            string dump = HexUtils.ToFormattedHexDump(frameStr);
            Assert.True(dump.Contains("0000 "));
            Assert.True(dump.Contains("|.1H|"));

            return Task.CompletedTask;
        }

        public async Task Test_AstmDriver_CaptureMode_Acknowledges_And_Stores_Without_Processing()
        {
            using var conn = _fixture.ConnectionFactory.CreateConnection();
            await conn.ExecuteAsync("ALTER TABLE middleware_log ADD COLUMN IF NOT EXISTS raw_hex TEXT;");

            var instRow = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT id, code, name FROM instruments WHERE active = TRUE LIMIT 1;");
            long instId = instRow != null ? (long)instRow.id : 1;
            string instCode = instRow != null ? (string)instRow.code : "ADVIA360";
            string instName = instRow != null ? (string)instRow.name : "Siemens ADVIA 360";

            // Mock transport for simulating instrument communication
            var mockTransport = new MockTransport();
            var instrument = new InstrumentItem
            {
                Id = instId,
                Code = instCode,
                Name = instName,
                Protocol = "ASTM",
                OperatingMode = "Bidirectional",
                Active = true
            };

            var refRangeService = new ReferenceRangeService(_fixture.ConnectionFactory);
            using var driver = new AstmInstrumentDriver(instrument, mockTransport, _fixture.ConnectionFactory, refRangeService)
            {
                CaptureMode = true
            };

            Assert.True(driver.CaptureMode);

            // 1. Simulate Instrument sending ENQ (0x05)
            mockTransport.SimulateReceive(new byte[] { AstmConstants.ENQ });
            await Task.Delay(50);

            // Verify driver replied with ACK (0x06)
            Assert.Equal(1, mockTransport.SentBytes.Count);
            Assert.Equal(AstmConstants.ACK, mockTransport.SentBytes[0]);
            mockTransport.SentBytes.Clear();

            // 2. Simulate Instrument sending ASTM Order Record frame: <STX>1O|1|TEST-BARCODE-999|||R<CR><ETX>XX<CR><LF>
            string astmFrame = AstmFraming.BuildFrame(1, "O|1|TEST-BARCODE-999|||R");
            mockTransport.SimulateReceive(Encoding.ASCII.GetBytes(astmFrame));
            await Task.Delay(50);

            // Verify driver replied with ACK
            Assert.Equal(1, mockTransport.SentBytes.Count);
            Assert.Equal(AstmConstants.ACK, mockTransport.SentBytes[0]);
            mockTransport.SentBytes.Clear();

            // 3. Simulate Instrument sending EOT (0x04)
            mockTransport.SimulateReceive(new byte[] { AstmConstants.EOT });
            await Task.Delay(100);

            // 4. Verify Database middleware_log has records with CAPTURED status and non-null raw_hex
            var capturedLogs = (await conn.QueryAsync<dynamic>(@"
                SELECT raw_message, raw_hex, direction, parse_status
                FROM middleware_log
                WHERE instrument_id = @InstId
                ORDER BY id DESC
                LIMIT 5;
            ", new { InstId = instrument.Id })).AsList();

            Assert.True(capturedLogs.Count > 0, "Middleware log must record captured traffic");
            Assert.True(capturedLogs.Exists(l => (string)l.parse_status == "CAPTURED"), "Logs must have parse_status = 'CAPTURED'");
            Assert.True(capturedLogs.Exists(l => !string.IsNullOrEmpty((string)l.raw_hex)), "Logs must populate raw_hex column");

            // Clean up test logs
            await conn.ExecuteAsync("DELETE FROM middleware_log WHERE instrument_id = @InstId;", new { InstId = instrument.Id });
        }

        private class MockTransport : ITransport
        {
            public List<byte> SentBytes { get; } = new();
            public List<byte[]> SentBuffers { get; } = new();

            public bool IsConnected => true;
            public string Description => "MockTransport";

            public event EventHandler<byte[]>? DataReceived;
            public event EventHandler<bool>? ConnectionStateChanged;
            public event EventHandler<Exception>? ErrorOccurred;

            public Task StartAsync(System.Threading.CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task StopAsync() => Task.CompletedTask;

            public Task SendAsync(byte[] data, System.Threading.CancellationToken cancellationToken = default)
            {
                SentBuffers.Add(data);
                return Task.CompletedTask;
            }

            public Task SendByteAsync(byte b, System.Threading.CancellationToken cancellationToken = default)
            {
                SentBytes.Add(b);
                return Task.CompletedTask;
            }

            public void SimulateReceive(byte[] data)
            {
                DataReceived?.Invoke(this, data);
            }

            public void Dispose() { }
        }
    }
}
