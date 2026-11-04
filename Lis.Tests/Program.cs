using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Lis.Tests
{
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=============================================================");
            Console.WriteLine("       LIS - Laboratory Information System Test Suite        ");
            Console.WriteLine("=============================================================");
            Console.ResetColor();

            if (args.Length > 0 && args[0] == "--test-dimension")
            {
                string port = "COM7";
                Console.WriteLine($"\n[DIMENSION TEST] Opening {port} at 9600 8-N-1 (DTR=true, RTS=true)...");
                using var sp = new System.IO.Ports.SerialPort(port, 9600, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
                {
                    ReadTimeout = 500,
                    WriteTimeout = 500,
                    DtrEnable = true,
                    RtsEnable = true
                };
                sp.Open();
                Console.WriteLine($"[DIMENSION TEST] Port {port} OPEN! Running continuous Dimension poll & ENQ handler for 60 seconds...");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var msgBuf = new List<byte>();
                int pollCount = 0;

                while (sw.ElapsedMilliseconds < 60000)
                {
                    try
                    {
                        int b = sp.ReadByte();
                        if (b != -1)
                        {
                            if (b == 0x05) // <ENQ>
                            {
                                Console.WriteLine($"[RX +{sw.ElapsedMilliseconds}ms] <ENQ> received! Sending <ACK> (0x06)...");
                                sp.Write(new byte[] { 0x06 }, 0, 1);
                                continue;
                            }

                            msgBuf.Add((byte)b);
                            if (b == 0x03) // <ETX>
                            {
                                byte[] frame = msgBuf.ToArray();
                                msgBuf.Clear();
                                string hex = BitConverter.ToString(frame);
                                string ascii = System.Text.Encoding.ASCII.GetString(frame)
                                    .Replace("\x02", "<STX>").Replace("\x03", "<ETX>").Replace("\x1c", "<FS>");
                                Console.WriteLine($"[RX +{sw.ElapsedMilliseconds}ms] Frame: {ascii} (Hex: {hex})");

                                if (frame.Length >= 2 && frame[0] == 0x02 && frame[1] == (byte)'P')
                                {
                                    pollCount++;
                                    Console.WriteLine($"--> Answering Dimension POLL #{pollCount} with <STX>N<FS>6A<ETX>...");
                                    byte[] noRequest = new byte[] { 0x02, (byte)'N', 0x1C, (byte)'6', (byte)'A', 0x03 };
                                    sp.Write(noRequest, 0, noRequest.Length);
                                    Console.WriteLine($"--> Sent {noRequest.Length} bytes.");
                                }
                            }
                        }
                    }
                    catch (TimeoutException) { }
                }
                Console.WriteLine($"[DIMENSION TEST] Finished. Total polls answered: {pollCount}");
                sp.Close();
                return 0;
            }

            if (args.Length > 0 && args[0] == "--sniff")
            {
                string port = args.Length > 1 ? args[1] : "COM7";
                int baud = args.Length > 2 && int.TryParse(args[2], out var b) ? b : 9600;
                Console.WriteLine($"\n[SNIFFER] Opening {port} at {baud} 8-N-1 (DTR=true, RTS=true)...");
                try
                {
                    using var sp = new System.IO.Ports.SerialPort(port, baud, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One)
                    {
                        ReadTimeout = 1000,
                        WriteTimeout = 1000,
                        DtrEnable = true,
                        RtsEnable = true
                    };
                    sp.Open();
                    Console.WriteLine($"[SNIFFER] {port} is OPEN! CDHolding={sp.CDHolding}, CtsHolding={sp.CtsHolding}, DsrHolding={sp.DsrHolding}");
                    Console.WriteLine("[SNIFFER] Listening for incoming bytes for 12 seconds... (Press Ctrl+C to abort)");

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    int totalBytes = 0;
                    byte[] buf = new byte[256];
                    while (sw.ElapsedMilliseconds < 12000)
                    {
                        try
                        {
                            int n = sp.Read(buf, 0, buf.Length);
                            if (n > 0)
                            {
                                totalBytes += n;
                                string hex = BitConverter.ToString(buf, 0, n);
                                string ascii = System.Text.Encoding.ASCII.GetString(buf, 0, n).Replace("\r", "\\r").Replace("\n", "\\n");
                                Console.WriteLine($"[RX +{sw.ElapsedMilliseconds}ms] {n} bytes: Hex=[{hex}] Ascii=[{ascii}]");
                            }
                        }
                        catch (TimeoutException) { }
                    }
                    Console.WriteLine($"[SNIFFER] Done listening. Total bytes received: {totalBytes}");
                    sp.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SNIFFER ERROR] {ex.Message}");
                }
                return 0;
            }

            if (args.Length > 0 && args[0] == "--diag")
            {
                var diagFixture = new TestDatabaseFixture();
                using var conn = diagFixture.ConnectionFactory.CreateConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await ((System.Data.Common.DbConnection)conn).OpenAsync();

                Console.WriteLine("\n=== INSTRUMENTS TABLE ===");
                using var cmd = (System.Data.Common.DbCommand)conn.CreateCommand();
                cmd.CommandText = "SELECT id, code, name, protocol, connection_type, operating_mode, tcp_mode, host, port, com_port, baud_rate, data_bits, parity, stop_bits, flow_control, active FROM instruments ORDER BY id;";
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    Console.WriteLine($"ID: {reader["id"]} | Code: {reader["code"]} | Name: {reader["name"]} | Proto: {reader["protocol"]} | Conn: {reader["connection_type"]} | COM: {reader["com_port"]} (Baud: {reader["baud_rate"]}, Data: {reader["data_bits"]}, Parity: {reader["parity"]}, Stop: {reader["stop_bits"]}, Flow: {reader["flow_control"]}) | TCP: {reader["host"]}:{reader["port"]} | Active: {reader["active"]}");
                }
                reader.Close();

                Console.WriteLine("\n=== RECENT MIDDLEWARE LOGS (LAST 25) ===");
                using var logCmd = (System.Data.Common.DbCommand)conn.CreateCommand();
                logCmd.CommandText = @"
                    SELECT ml.id, ml.timestamp, COALESCE(i.code, 'Unknown') AS inst_code, ml.direction, ml.parse_status, ml.raw_message
                    FROM middleware_log ml
                    LEFT JOIN instruments i ON i.id = ml.instrument_id
                    ORDER BY ml.id DESC
                    LIMIT 25;
                ";
                using var logReader = await logCmd.ExecuteReaderAsync();
                while (await logReader.ReadAsync())
                {
                    string raw = logReader["raw_message"]?.ToString() ?? "";
                    if (raw.Length > 80) raw = raw.Substring(0, 80) + "...";
                    Console.WriteLine($"[{logReader["timestamp"]:HH:mm:ss}] #{logReader["id"]} {logReader["inst_code"]} | {logReader["direction"]} | {logReader["parse_status"]} | {raw}");
                }
                logReader.Close();

                Console.WriteLine("\n=== SERIAL PORTS CHECK ===");
                var ports = System.IO.Ports.SerialPort.GetPortNames();
                Console.WriteLine($"Available Ports in OS: {string.Join(", ", ports)}");
                foreach (var p in ports)
                {
                    try
                    {
                        using var sp = new System.IO.Ports.SerialPort(p, 9600);
                        sp.Open();
                        Console.WriteLine($"  - Port {p}: OPEN SUCCEEDED (Not locked by any process)");
                        sp.Close();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        Console.WriteLine($"  - Port {p}: LOCKED / IN USE (Likely opened by Lis.Middleware or another app)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  - Port {p}: Error opening: {ex.Message}");
                    }
                }

                return 0;
            }

            if (args.Length > 0 && args[0] == "--clean")
            {
                var fixtureForClean = new TestDatabaseFixture();
                using var conn = fixtureForClean.ConnectionFactory.CreateConnection();
                if (conn.State != System.Data.ConnectionState.Open)
                    await ((System.Data.Common.DbConnection)conn).OpenAsync();

                var sqlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "scripts", "clean_test_data.sql");
                if (!System.IO.File.Exists(sqlPath))
                {
                    sqlPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), "scripts", "clean_test_data.sql");
                }
                var sql = await System.IO.File.ReadAllTextAsync(sqlPath);
                using var cmd = (System.Data.Common.DbCommand)conn.CreateCommand();
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();

                using var checkCmd = (System.Data.Common.DbCommand)conn.CreateCommand();
                checkCmd.CommandText = @"
                    SELECT 
                        (SELECT count(*) FROM patients) as patient_count,
                        (SELECT count(*) FROM orders) as order_count,
                        (SELECT count(*) FROM results) as result_count,
                        (SELECT count(*) FROM tests) as test_count,
                        (SELECT count(*) FROM components) as component_count,
                        (SELECT count(*) FROM users) as user_count;
                ";
                using var reader = await checkCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    Console.WriteLine($"   - Patients Remaining: {reader["patient_count"]}");
                    Console.WriteLine($"   - Orders Remaining:   {reader["order_count"]}");
                    Console.WriteLine($"   - Results Remaining:  {reader["result_count"]}");
                    Console.WriteLine($"   - Tests Preserved:    {reader["test_count"]}");
                    Console.WriteLine($"   - Comps Preserved:    {reader["component_count"]}");
                    Console.WriteLine($"   - Users Preserved:    {reader["user_count"]}");
                }
                reader.Close();

                using var instCmd = (System.Data.Common.DbCommand)conn.CreateCommand();
                instCmd.CommandText = "SELECT id, code, name, protocol, active FROM instruments ORDER BY id;";
                using var instReader = await instCmd.ExecuteReaderAsync();
                while (await instReader.ReadAsync())
                {
                    Console.WriteLine($"[INST] #{instReader["id"]} code={instReader["code"]} name={instReader["name"]} proto={instReader["protocol"]} active={instReader["active"]}");
                }
                instReader.Close();

                using var mapCmd = (System.Data.Common.DbCommand)conn.CreateCommand();
                mapCmd.CommandText = @"
                    SELECT it.id, i.code as inst_code, t.test_code, c.component_code, it.instrument_test_code
                    FROM instrument_tests it
                    JOIN instruments i ON i.id = it.instrument_id
                    JOIN tests t ON t.id = it.test_id
                    JOIN components c ON c.id = it.component_id
                    ORDER BY i.id, it.id;
                ";
                using var mapReader = await mapCmd.ExecuteReaderAsync();
                while (await mapReader.ReadAsync())
                {
                    Console.WriteLine($"[MAPPING] Inst={mapReader["inst_code"]} Test={mapReader["test_code"]} Comp={mapReader["component_code"]} Code={mapReader["instrument_test_code"]}");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(">> SUCCESS: Database transactional test data and patient records have been wiped cleanly.");
                Console.ResetColor();
                return 0;
            }

            var fixture = new TestDatabaseFixture();
            int passed = 0;
            int failed = 0;

            object[] testInstances = new object[]
            {
                new Phase1Tests(fixture),
                new Phase2Tests(fixture),
                new Phase3Tests(fixture),
                new Phase4Tests(fixture),
                new Phase5Tests(fixture),
                new CaptureAndHexTests(fixture)
            };

            foreach (var testClass in testInstances)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"\n--- Running {testClass.GetType().Name} ---");
                Console.ResetColor();

                var methods = testClass.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

                foreach (var m in methods)
                {
                    if (m.Name.StartsWith("Test"))
                    {
                        Console.Write($"Running {m.Name,-65} ... ");
                        try
                        {
                            var task = (Task)m.Invoke(testClass, null)!;
                            await task;
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine("[PASS]");
                            Console.ResetColor();
                            passed++;
                        }
                        catch (TargetInvocationException ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[FAIL]");
                            Console.ResetColor();
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"   Error: {ex.InnerException?.Message ?? ex.Message}");
                            if (ex.InnerException?.StackTrace != null)
                            {
                                Console.WriteLine($"   Stack: {ex.InnerException.StackTrace}");
                            }
                            Console.ResetColor();
                            failed++;
                        }
                        catch (Exception ex)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine("[FAIL]");
                            Console.ResetColor();
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"   Error: {ex.Message}");
                            Console.ResetColor();
                            failed++;
                        }
                    }
                }
            }

            Console.WriteLine("\n-------------------------------------------------------------");
            if (failed == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"ALL TESTS PASSED ({passed}/{passed})");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"TESTS FAILED: {failed} failed, {passed} passed.");
                Console.ResetColor();
                return 1;
            }
        }
    }
}
