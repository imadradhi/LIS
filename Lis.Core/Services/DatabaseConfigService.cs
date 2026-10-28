using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Npgsql;

namespace Lis.Core.Services
{
    public class DatabaseConnectionSettings
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string Database { get; set; } = "lis_db";
        public string Username { get; set; } = "postgres";
        public string Password { get; set; } = "@Hig5udMz";

        public string BuildConnectionString(int timeoutSeconds = 15)
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = Host?.Trim(),
                Port = Port > 0 ? Port : 5432,
                Database = Database?.Trim(),
                Username = Username?.Trim(),
                Password = Password,
                Timeout = timeoutSeconds,
                CommandTimeout = timeoutSeconds
            };
            return builder.ConnectionString;
        }

        public static DatabaseConnectionSettings FromConnectionString(string connectionString)
        {
            var settings = new DatabaseConnectionSettings();
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(connectionString);
                if (!string.IsNullOrWhiteSpace(builder.Host)) settings.Host = builder.Host;
                if (builder.Port > 0) settings.Port = builder.Port;
                if (!string.IsNullOrWhiteSpace(builder.Database)) settings.Database = builder.Database;
                if (!string.IsNullOrWhiteSpace(builder.Username)) settings.Username = builder.Username;
                if (builder.Password != null) settings.Password = builder.Password;
            }
            catch
            {
                // Fallback to defaults if string is invalid
            }
            return settings;
        }
    }

    public static class DatabaseConfigService
    {
        public static string ResolveConfigFile(string? basePath = null)
        {
            basePath ??= AppDomain.CurrentDomain.BaseDirectory;
            string localPath = Path.Combine(basePath, "appsettings.json");
            if (File.Exists(localPath)) return Path.GetFullPath(localPath);

            // Check parent directories if running inside bin/Debug
            string candidate = basePath;
            for (int i = 0; i < 4; i++)
            {
                var parent = Directory.GetParent(candidate);
                if (parent == null) break;
                candidate = parent.FullName;
                string p = Path.Combine(candidate, "appsettings.json");
                if (File.Exists(p)) return Path.GetFullPath(p);
            }

            return Path.GetFullPath(localPath);
        }

        public static DatabaseConnectionSettings LoadSettings(string? basePath = null)
        {
            string filePath = ResolveConfigFile(basePath);
            if (!File.Exists(filePath))
            {
                return new DatabaseConnectionSettings();
            }

            try
            {
                string json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Priority 1: Check DatabaseSettings object first if present (user-friendly format)
                if (root.TryGetProperty("DatabaseSettings", out var dbSect))
                {
                    var s = new DatabaseConnectionSettings();
                    if (dbSect.TryGetProperty("Host", out var h) && !string.IsNullOrWhiteSpace(h.GetString()))
                        s.Host = h.GetString()!;
                    if (dbSect.TryGetProperty("Port", out var p) && p.TryGetInt32(out int port))
                        s.Port = port;
                    if (dbSect.TryGetProperty("Database", out var d) && !string.IsNullOrWhiteSpace(d.GetString()))
                        s.Database = d.GetString()!;
                    if (dbSect.TryGetProperty("Username", out var u) && !string.IsNullOrWhiteSpace(u.GetString()))
                        s.Username = u.GetString()!;
                    if (dbSect.TryGetProperty("Password", out var pw))
                        s.Password = pw.GetString() ?? "";

                    return s;
                }

                // Priority 2: Check ConnectionStrings:DefaultConnection
                if (root.TryGetProperty("ConnectionStrings", out var connSection) &&
                    connSection.TryGetProperty("DefaultConnection", out var defaultConnElem))
                {
                    string? connStr = defaultConnElem.GetString();
                    if (!string.IsNullOrWhiteSpace(connStr))
                    {
                        var s = DatabaseConnectionSettings.FromConnectionString(connStr);
                        return s;
                    }
                }
            }
            catch
            {
                // In case of parsing error, return default
            }

            return new DatabaseConnectionSettings();
        }

        public static void SaveSettings(DatabaseConnectionSettings settings, string? basePath = null)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string primaryFile = Path.Combine(appDir, "appsettings.json");

            string connStr = settings.BuildConnectionString();

            var model = new
            {
                ConnectionStrings = new
                {
                    DefaultConnection = connStr
                },
                DatabaseSettings = new
                {
                    Host = settings.Host.Trim(),
                    Port = settings.Port,
                    Database = settings.Database.Trim(),
                    Username = settings.Username.Trim(),
                    Password = settings.Password
                }
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(model, options);

            // Write to primary runtime directory
            File.WriteAllText(primaryFile, json);

            // Also check and update root project appsettings.json if in development
            string current = appDir;
            for (int i = 0; i < 4; i++)
            {
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
                string rootAppsettings = Path.Combine(current, "appsettings.json");
                if (File.Exists(rootAppsettings) && !string.Equals(Path.GetFullPath(rootAppsettings), Path.GetFullPath(primaryFile), StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        File.WriteAllText(rootAppsettings, json);
                    }
                    catch { }
                }
            }
        }

        public static async Task<(bool Success, string Message)> TestConnectionAsync(DatabaseConnectionSettings settings)
        {
            try
            {
                string connStr = settings.BuildConnectionString(timeoutSeconds: 5);
                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync();

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT current_database(), version();";
                await using var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    string dbName = reader.GetString(0);
                    return (true, $"تم الاتصال بنجاح بقاعدة البيانات [{dbName}] على السيرفر ({settings.Host}:{settings.Port})");
                }

                return (true, $"تم الاتصال بنجاح بالسيرفر ({settings.Host}:{settings.Port})");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}

