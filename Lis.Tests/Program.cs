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
                new Phase4Tests(fixture)
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
