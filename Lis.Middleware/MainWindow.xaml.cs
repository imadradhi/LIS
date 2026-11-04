using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Dapper;
using Lis.Core.Data;
using Lis.Core.Models;
using Lis.Core.Services;
using Lis.Core.Utils;
using Microsoft.Win32;

namespace Lis.Middleware
{
    public partial class MainWindow : Window
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly MiddlewareEngine _middlewareEngine;
        private readonly DispatcherTimer _pollTimer;

        private readonly List<LogDisplayItem> _allLoadedLogs = new();
        private bool _isGridHexMode = false;
        private int _inspectorHexMode = 0; // 0 = ASCII, 1 = Formatted Dump, 2 = Raw Hex Bytes
        private bool _isPopulatingInstrumentsCombo = false;

        public MainWindow()
        {
            InitializeComponent();

            var dbSettings = DatabaseConfigService.LoadSettings(AppDomain.CurrentDomain.BaseDirectory);
            var connStr = dbSettings.BuildConnectionString();

            _connectionFactory = new DbConnectionFactory(connStr);
            _middlewareEngine = new MiddlewareEngine(_connectionFactory);

            _middlewareEngine.StateChanged += (s, isRunning) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    UpdateEngineUi(isRunning);
                    await LoadInstrumentsAsync();
                });
            };

            _middlewareEngine.CaptureModeChanged += (s, active) =>
            {
                UpdateCaptureModeUi(active);
            };

            _middlewareEngine.InstrumentStatusChanged += (s, ev) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    await LoadInstrumentsAsync();
                    await LoadLogsAsync();
                });
            };

            // Periodic refresh timer for live traffic & stats
            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _pollTimer.Tick += async (s, e) =>
            {
                await LoadInstrumentsAsync();
                await LoadLogsAsync();
                await LoadStatsAsync();
            };

            Loaded += async (s, e) =>
            {
                try
                {
                    await AutoPurgeOldLogsAsync(7);
                    await EnsureSchemaUpdatedAsync();
                    await EnsureAdvia360ConfiguredAsync();

                    // Auto-start middleware engine FIRST
                    await _middlewareEngine.StartAsync();

                    UpdateEngineUi(_middlewareEngine.IsRunning);
                    UpdateCaptureModeUi(_middlewareEngine.CaptureMode);
                    await LoadInstrumentsAsync();
                    await LoadLogsAsync();
                    await LoadStatsAsync();

                    _pollTimer.Start();
                    StartInstrumentNotificationListener();
                }
                catch
                {
                    UpdateEngineUi(false);
                    TxtEngineStatus.Text = "⚠️ Database Connection Error (Check Server Settings)";
                    TxtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
                }
            };

            Closing += async (s, e) =>
            {
                _pollTimer.Stop();
                await _middlewareEngine.StopAsync();
            };
        }

        private void StartInstrumentNotificationListener()
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();
                        if (conn.State != System.Data.ConnectionState.Open)
                            await conn.OpenAsync();
                        using var cmd = new Npgsql.NpgsqlCommand("LISTEN lis_instruments_channel;", conn);
                        await cmd.ExecuteNonQueryAsync();

                        conn.Notification += (o, e) =>
                        {
                            Dispatcher.Invoke(async () =>
                            {
                                if (_middlewareEngine.IsRunning)
                                {
                                    await _middlewareEngine.RestartAsync();
                                }
                                await LoadInstrumentsAsync();
                            });
                        };

                        while (conn.State == System.Data.ConnectionState.Open)
                        {
                            await conn.WaitAsync();
                        }
                    }
                    catch
                    {
                        await Task.Delay(5000);
                    }
                }
            });
        }

        private void UpdateEngineUi(bool isRunning)
        {
            BadgeEngineStatus.Background = Brushes.Transparent;
            BadgeEngineStatus.BorderBrush = Brushes.Transparent;
            BadgeEngineStatus.BorderThickness = new Thickness(0);

            if (isRunning)
            {
                int count = _middlewareEngine.ActiveDriversCount;
                TxtEngineStatus.Text = count > 0 ? $"🟢 Engine Running ({count} Active)" : "🟢 Engine Running & Listening";
                TxtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                BtnToggleEngine.Content = "⏹️ Stop Engine";
            }
            else
            {
                TxtEngineStatus.Text = "⚪ Engine Stopped";
                TxtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                BtnToggleEngine.Content = "▶️ Start Engine";
            }
        }

        private async Task EnsureSchemaUpdatedAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                await conn.ExecuteAsync("ALTER TABLE middleware_log ADD COLUMN IF NOT EXISTS raw_hex TEXT;");
            }
            catch { }
        }

        private async Task EnsureAdvia360ConfiguredAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                const string sql = @"
                    INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, tcp_mode, host, port, active, updated_at)
                    VALUES ('ADVIA360', 'Siemens ADVIA 360', 'ASTM', 'TCP', 'Bidirectional', 'Server', '0.0.0.0', 5100, TRUE, clock_timestamp())
                    ON CONFLICT (code) DO NOTHING;
                ";
                await conn.ExecuteAsync(sql);
            }
            catch { }
        }

        private void BtnToggleCapture_Click(object sender, RoutedEventArgs e)
        {
            _middlewareEngine.CaptureMode = !_middlewareEngine.CaptureMode;
            UpdateCaptureModeUi(_middlewareEngine.CaptureMode);
        }

        private void BtnDisableCapture_Click(object sender, RoutedEventArgs e)
        {
            _middlewareEngine.CaptureMode = false;
            UpdateCaptureModeUi(false);
        }

        private void UpdateCaptureModeUi(bool active)
        {
            Dispatcher.Invoke(() =>
            {
                if (active)
                {
                    BtnToggleCapture.Content = "🟡 Capture Mode: ACTIVE";
                    BtnToggleCapture.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    BannerCaptureMode.Visibility = Visibility.Visible;
                }
                else
                {
                    BtnToggleCapture.Content = "🛡️ Capture Mode: OFF";
                    BtnToggleCapture.Foreground = (Brush)FindResource("TextPrimaryBrush");
                    BannerCaptureMode.Visibility = Visibility.Collapsed;
                }
            });
        }

        private async Task AutoPurgeOldLogsAsync(int retentionDays = 7)
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                await conn.ExecuteAsync($"DELETE FROM middleware_log WHERE timestamp < (CURRENT_DATE - INTERVAL '{retentionDays} days');");
            }
            catch { }
        }

        private async Task LoadInstrumentsAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var items = (await conn.QueryAsync<InstrumentItem>(@"
                    SELECT id, code, name, protocol, connection_type AS ConnectionType,
                           operating_mode AS OperatingMode, tcp_mode AS TcpMode, host, port,
                           com_port AS ComPort, baud_rate AS BaudRate, active
                    FROM instruments
                    WHERE active = TRUE
                    ORDER BY id ASC;
                ")).AsList();

                // Query the latest incoming communication time for each instrument from database
                var lastSeenDb = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var rows = (await conn.QueryAsync<dynamic>(@"
                        SELECT COALESCE(i.code, 'Unknown') AS code, MAX(ml.timestamp) AS last_time
                        FROM middleware_log ml
                        JOIN instruments i ON i.id = ml.instrument_id
                        WHERE ml.direction = 'IN'
                        GROUP BY i.code;
                    ")).AsList();

                    foreach (var row in rows)
                    {
                        if (row.code != null && row.last_time != null)
                        {
                            var dt = (DateTime)row.last_time;
                            lastSeenDb[(string)row.code] = dt.Kind == DateTimeKind.Utc ? dt.ToLocalTime() : dt;
                        }
                    }
                }
                catch { }

                int liveCount = 0;
                var displayList = new List<dynamic>();

                foreach (var inst in items)
                {
                    string connInfo = inst.ConnectionType == "TCP"
                        ? $"TCP Server : {inst.Port ?? 5100} • {inst.Protocol}"
                        : $"Serial {inst.ComPort ?? "COM1"} : {inst.BaudRate ?? 9600} • {inst.Protocol}";

                    // Prioritize real-time in-memory activity, fallback to database timestamp
                    DateTime? memActivityUtc = _middlewareEngine.GetLastActivity(inst.Code);
                    DateTime? effectiveTime = null;

                    if (memActivityUtc.HasValue)
                    {
                        effectiveTime = memActivityUtc.Value.Kind == DateTimeKind.Utc ? memActivityUtc.Value.ToLocalTime() : memActivityUtc.Value;
                    }
                    else if (lastSeenDb.TryGetValue(inst.Code, out var dbTime))
                    {
                        effectiveTime = dbTime.Kind == DateTimeKind.Utc ? dbTime.ToLocalTime() : dbTime;
                    }

                    string statusText;
                    string lastSeenText;
                    string bg;
                    string fg;

                    if (!_middlewareEngine.IsRunning)
                    {
                        statusText = "⚪ STOPPED";
                        lastSeenText = "Service paused";
                        bg = "Transparent";
                        fg = "#94A3B8";
                    }
                    else
                    {
                        if (effectiveTime.HasValue)
                        {
                            var elapsed = DateTime.Now - effectiveTime.Value;
                            if (elapsed.TotalSeconds < 0) elapsed = TimeSpan.Zero;

                            if (elapsed.TotalSeconds < 60)
                            {
                                statusText = "🟢 LIVE";
                                int secs = Math.Max(1, (int)elapsed.TotalSeconds);
                                lastSeenText = $"Active ({secs}s ago)";
                                bg = "Transparent";
                                fg = "#34D399";
                                liveCount++;
                            }
                            else if (elapsed.TotalMinutes < 10)
                            {
                                statusText = "🟡 IDLE";
                                int mins = Math.Max(1, (int)elapsed.TotalMinutes);
                                lastSeenText = $"Standby ({mins}m ago)";
                                bg = "Transparent";
                                fg = "#F59E0B";
                            }
                            else
                            {
                                statusText = "🔴 OFFLINE";
                                lastSeenText = $"Last: {effectiveTime.Value:dd/MM HH:mm}";
                                bg = "Transparent";
                                fg = "#EF4444";
                            }
                        }
                        else
                        {
                            statusText = "🔴 OFFLINE";
                            lastSeenText = "No signal received";
                            bg = "Transparent";
                            fg = "#EF4444";
                        }
                    }

                    displayList.Add(new
                    {
                        code = inst.Code,
                        name = inst.Name,
                        connection_details = connInfo,
                        status_text = statusText,
                        last_seen_text = lastSeenText,
                        StatusBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                        StatusFgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg))
                    });
                }

                GridInstruments.ItemsSource = displayList;
                TxtStatInstCount.Text = $"{liveCount} / {items.Count}";
                TxtStatInstCount.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(liveCount > 0 ? "#34D399" : "#94A3B8"));

                // Refresh Instrument filter combobox options if changed
                UpdateInstrumentFilterCombo(items.Select(i => i.Code).Distinct().ToList());
            }
            catch { }
        }

        private void UpdateInstrumentFilterCombo(List<string> codes)
        {
            if (_isPopulatingInstrumentsCombo) return;

            string currentSelected = (CmbFilterInstrument.SelectedItem as string) ?? "All Instruments";

            var options = new List<string> { "All Instruments" };
            options.AddRange(codes);

            // Only update if items differ
            if (CmbFilterInstrument.Items.Count != options.Count)
            {
                _isPopulatingInstrumentsCombo = true;
                CmbFilterInstrument.ItemsSource = options;
                CmbFilterInstrument.SelectedItem = options.Contains(currentSelected) ? currentSelected : "All Instruments";
                _isPopulatingInstrumentsCombo = false;
            }
        }

        private async Task LoadLogsAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var rows = (await conn.QueryAsync<dynamic>(@"
                    SELECT ml.id, ml.timestamp, COALESCE(i.code, 'Unknown') AS instrument_code,
                           ml.direction, ml.parse_status, ml.raw_message, ml.raw_hex
                    FROM middleware_log ml
                    LEFT JOIN instruments i ON i.id = ml.instrument_id
                    ORDER BY ml.id DESC
                    LIMIT 120;
                ")).AsList();

                var list = new List<LogDisplayItem>();
                foreach (var row in rows)
                {
                    list.Add(LogDisplayItem.Create(
                        (long)row.id,
                        (DateTime?)row.timestamp,
                        (string?)row.instrument_code ?? "Unknown",
                        (string?)row.direction ?? "IN",
                        (string?)row.parse_status ?? "OK",
                        (string?)row.raw_message ?? "",
                        (string?)row.raw_hex,
                        _isGridHexMode
                    ));
                }

                _allLoadedLogs.Clear();
                _allLoadedLogs.AddRange(list);

                ApplyLogFilters();
            }
            catch { }
        }

        private void BtnToggleGridMode_Click(object sender, RoutedEventArgs e)
        {
            _isGridHexMode = !_isGridHexMode;
            BtnToggleGridMode.Content = _isGridHexMode ? "📝 Show ASCII in Table" : "🔢 Show Hex in Table";
            foreach (var item in _allLoadedLogs)
            {
                item.IsHexMode = _isGridHexMode;
            }
            ApplyLogFilters();
        }

        private void ApplyLogFilters()
        {
            IEnumerable<LogDisplayItem> filtered = _allLoadedLogs;

            // 1. Filter by Instrument
            string selectedInst = (CmbFilterInstrument.SelectedItem as string) ?? "All Instruments";
            if (!string.IsNullOrEmpty(selectedInst) && selectedInst != "All Instruments")
            {
                filtered = filtered.Where(l => string.Equals(l.instrument_code, selectedInst, StringComparison.OrdinalIgnoreCase));
            }

            // 2. Hide Idle Polls (POLL / NO-ORDER)
            if (ChkHideIdlePolls.IsChecked == true)
            {
                filtered = filtered.Where(l => l.type_badge != "POLL" && l.type_badge != "NO-ORDER");
            }

            // 3. Search Query (Barcode / Text / Hex)
            string query = TxtSearchQuery.Text.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(l =>
                    l.raw_message.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    l.raw_hex.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    l.description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    l.instrument_code.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var resultList = filtered.ToList();
            GridMiddlewareLogs.ItemsSource = resultList;

            if (ChkAutoScroll.IsChecked == true && resultList.Count > 0 && GridMiddlewareLogs.Items.Count > 0)
            {
                GridMiddlewareLogs.ScrollIntoView(GridMiddlewareLogs.Items[0]);
            }
        }

        private async Task LoadStatsAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var stats = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT 
                        COUNT(CASE WHEN direction = 'IN' THEN 1 END) AS in_count,
                        COUNT(CASE WHEN direction = 'OUT' THEN 1 END) AS out_count
                    FROM middleware_log
                    WHERE timestamp >= CURRENT_DATE;
                ");

                if (stats != null)
                {
                    TxtStatInCount.Text = (stats.in_count ?? 0).ToString();
                    TxtStatOutCount.Text = (stats.out_count ?? 0).ToString();
                }
            }
            catch { }
        }

        private async void BtnToggleEngine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_middlewareEngine.IsRunning)
                {
                    await _middlewareEngine.StopAsync();
                }
                else
                {
                    await _middlewareEngine.StartAsync();
                }
                await LoadInstrumentsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error switching middleware state: {ex.Message}", "Engine Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRestartEngine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _middlewareEngine.RestartAsync();
                await LoadInstrumentsAsync();
                MessageBox.Show("Middleware service restarted successfully.", "Restart Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error restarting middleware: {ex.Message}", "Restart Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnServerSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ServerSettingsDialog { Owner = this };
            dlg.ShowDialog();
        }

        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadInstrumentsAsync();
            _ = LoadLogsAsync();
            _ = LoadStatsAsync();
        }

        private void CmbFilterInstrument_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isPopulatingInstrumentsCombo)
            {
                ApplyLogFilters();
            }
        }

        private void ChkHideIdlePolls_Changed(object sender, RoutedEventArgs e)
        {
            ApplyLogFilters();
        }

        private void TxtSearchQuery_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyLogFilters();
        }

        private void GridMiddlewareLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridMiddlewareLogs.SelectedItem is LogDisplayItem item)
            {
                TxtLogDetailHeader.Text = $"[{item.timestamp_formatted}] • Instrument: [{item.instrument_code}] • {item.DirectionBadgeText} • Type: [{item.type_badge}]";
                TxtLogDetailDesc.Text = $"💡 Interpretation: {item.description}";
                UpdateSelectedDetailMessage(item);
                BorderLogDetails.Visibility = Visibility.Visible;
            }
            else
            {
                BorderLogDetails.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateSelectedDetailMessage(LogDisplayItem? item = null)
        {
            item ??= GridMiddlewareLogs.SelectedItem as LogDisplayItem;
            if (item == null) return;

            switch (_inspectorHexMode)
            {
                case 1: // Formatted Dump
                    TxtLogDetailMessage.Text = HexUtils.ToFormattedHexDump(item.raw_message);
                    TxtLogDetailMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"));
                    break;
                case 2: // Raw Hex Bytes
                    TxtLogDetailMessage.Text = item.raw_hex;
                    TxtLogDetailMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                    break;
                default: // ASCII Text
                    TxtLogDetailMessage.Text = item.raw_message;
                    TxtLogDetailMessage.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                    break;
            }
        }

        private void BtnToggleHex_Click(object sender, RoutedEventArgs e)
        {
            _inspectorHexMode = (_inspectorHexMode + 1) % 3;
            switch (_inspectorHexMode)
            {
                case 0:
                    BtnToggleHex.Content = "📜 Formatted Dump";
                    break;
                case 1:
                    BtnToggleHex.Content = "🔢 Raw Hex Bytes";
                    break;
                case 2:
                    BtnToggleHex.Content = "📝 ASCII Text";
                    break;
            }
            UpdateSelectedDetailMessage();
        }

        private void BtnCopySelectedHex_Click(object sender, RoutedEventArgs e)
        {
            if (GridMiddlewareLogs.SelectedItem is LogDisplayItem selected && !string.IsNullOrEmpty(selected.raw_hex))
            {
                Clipboard.SetText(selected.raw_hex);
                MessageBox.Show("Raw hex bytes copied to clipboard.", "Hex Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GridMiddlewareLogs.SelectedItem is LogDisplayItem selected)
                {
                    Clipboard.SetText(selected.raw_message);
                    MessageBox.Show("Selected frame payload copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (GridMiddlewareLogs.ItemsSource is IEnumerable<LogDisplayItem> items)
                {
                    var sb = new StringBuilder();
                    foreach (var item in items)
                    {
                        sb.AppendLine($"[{item.timestamp_formatted}] [{item.instrument_code}] [{item.direction}] [{item.type_badge}] [{item.parse_status}]");
                        sb.AppendLine($"Meaning: {item.description}");
                        sb.AppendLine($"ASCII: {item.raw_message}");
                        sb.AppendLine($"HEX:   {item.raw_hex}");
                        sb.AppendLine(new string('-', 60));
                    }

                    if (sb.Length > 0)
                    {
                        Clipboard.SetText(sb.ToString());
                        MessageBox.Show("All communication logs copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("No logs available to copy.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error copying logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GridMiddlewareLogs.ItemsSource is not IEnumerable<LogDisplayItem> items || !items.Any())
                {
                    MessageBox.Show("No logs available to export.", "Export Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var saveDlg = new SaveFileDialog
                {
                    Filter = "CSV Files (*.csv)|*.csv|Text Files (*.txt)|*.txt",
                    FileName = $"LIS_Middleware_Logs_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    DefaultExt = ".csv"
                };

                if (saveDlg.ShowDialog() == true)
                {
                    var isCsv = saveDlg.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);
                    var sb = new StringBuilder();

                    if (isCsv)
                    {
                        sb.AppendLine("ID,Timestamp,Instrument,Direction,Type,Status,Interpretation,Payload_ASCII,Payload_HEX");
                        foreach (var item in items)
                        {
                            string safePayload = item.raw_message?.Replace("\"", "\"\"") ?? "";
                            string safeHex = item.raw_hex?.Replace("\"", "\"\"") ?? "";
                            string safeDesc = item.description?.Replace("\"", "\"\"") ?? "";
                            sb.AppendLine($"{item.id},\"{item.timestamp_formatted}\",\"{item.instrument_code}\",\"{item.direction}\",\"{item.type_badge}\",\"{item.parse_status}\",\"{safeDesc}\",\"{safePayload}\",\"{safeHex}\"");
                        }
                    }
                    else
                    {
                        foreach (var item in items)
                        {
                            sb.AppendLine($"[{item.timestamp_formatted}] [{item.instrument_code}] [{item.direction}] [{item.type_badge}] [{item.parse_status}]");
                            sb.AppendLine($"Interpretation: {item.description}");
                            sb.AppendLine($"ASCII: {item.raw_message}");
                            sb.AppendLine($"HEX:   {item.raw_hex}");
                            sb.AppendLine(new string('-', 70));
                        }
                    }

                    File.WriteAllText(saveDlg.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show($"Successfully exported {items.Count()} log records to:\n{saveDlg.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error exporting logs: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Are you sure you want to clear all communication logs from the database?",
                "Confirm Clear Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                using var conn = _connectionFactory.CreateConnection();
                await conn.ExecuteAsync("TRUNCATE TABLE middleware_log;");
                _allLoadedLogs.Clear();
                GridMiddlewareLogs.ItemsSource = null;
                BorderLogDetails.Visibility = Visibility.Collapsed;
                await LoadStatsAsync();
                MessageBox.Show("Communication logs cleared successfully.", "Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error clearing logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnPurgeOldLogs_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Purge all communication logs older than 7 days?\n(This helps keep the database performant while preserving recent logs).",
                "Purge Old Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                using var conn = _connectionFactory.CreateConnection();
                int purged = await conn.ExecuteAsync("DELETE FROM middleware_log WHERE timestamp < (CURRENT_DATE - INTERVAL '7 days');");
                await LoadLogsAsync();
                await LoadStatsAsync();
                MessageBox.Show($"Successfully purged {purged} log records older than 7 days.", "Purge Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error purging old logs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCopySelectedRaw_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtLogDetailMessage.Text))
            {
                Clipboard.SetText(TxtLogDetailMessage.Text);
                MessageBox.Show("Frame content copied to clipboard.", "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string ToHexDump(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";

            string decoded = raw
                .Replace("<STX>", "\x02")
                .Replace("<ETX>", "\x03")
                .Replace("<EOT>", "\x04")
                .Replace("<ENQ>", "\x05")
                .Replace("<ACK>", "\x06")
                .Replace("<NAK>", "\x15")
                .Replace("<FS>", "\x1C")
                .Replace("<CR>", "\x0D")
                .Replace("<LF>", "\x0A");

            byte[] bytes = Encoding.ASCII.GetBytes(decoded);
            var sbHex = new StringBuilder();
            var sbAscii = new StringBuilder();
            var sbResult = new StringBuilder();

            for (int i = 0; i < bytes.Length; i++)
            {
                sbHex.Append(bytes[i].ToString("X2")).Append(' ');
                char c = (char)bytes[i];
                sbAscii.Append(char.IsControl(c) ? '.' : c);

                if ((i + 1) % 16 == 0 || i == bytes.Length - 1)
                {
                    int pad = 16 - ((i % 16) + 1);
                    if (pad > 0 && i == bytes.Length - 1)
                    {
                        sbHex.Append(new string(' ', pad * 3));
                    }

                    int lineOffset = (i / 16) * 16;
                    sbResult.AppendLine($"{lineOffset:X4}  {sbHex} |{sbAscii}|");
                    sbHex.Clear();
                    sbAscii.Clear();
                }
            }

            return sbResult.ToString();
        }
    }

    /// <summary>
    /// Formatted and decoded log item for display in Middleware UI.
    /// </summary>
    public class LogDisplayItem
    {
        public long id { get; set; }
        public DateTime? timestamp { get; set; }
        public string timestamp_formatted => timestamp?.ToString("dd/MM/yyyy HH:mm:ss") ?? "";
        public string instrument_code { get; set; } = "";
        public string direction { get; set; } = "IN";
        public string parse_status { get; set; } = "OK";
        public string raw_message { get; set; } = "";

        public string type_badge { get; set; } = "FRAME";
        public string description { get; set; } = "";

        public string DirectionBadgeText => direction == "OUT" ? "📤 OUT" : "📥 IN";

        public Brush DirectionBgBrush => Brushes.Transparent;

        public Brush DirectionFgBrush => direction == "OUT"
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));

        public Brush StatusBgBrush => Brushes.Transparent;

        public Brush StatusFgBrush => parse_status switch
        {
            "OK" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")),
            "CAPTURED" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"))
        };

        public Brush TypeBadgeBgBrush { get; set; } = Brushes.Transparent;
        public Brush TypeBadgeFgBrush { get; set; } = Brushes.White;

        public string raw_hex { get; set; } = "";
        public bool IsHexMode { get; set; } = false;

        public string display_payload => IsHexMode ? raw_hex : raw_message;

        public Brush PayloadFgBrush => IsHexMode
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));

        public static LogDisplayItem Create(long id, DateTime? time, string code, string dir, string status, string raw, string? hex = null, bool isHexMode = false)
        {
            var localTime = time.HasValue ? (time.Value.Kind == DateTimeKind.Utc ? time.Value.ToLocalTime() : time.Value) : (DateTime?)null;
            var hexVal = !string.IsNullOrWhiteSpace(hex) ? hex : HexUtils.ToHexRepresentation(raw);
            var item = new LogDisplayItem
            {
                id = id,
                timestamp = localTime,
                instrument_code = code,
                direction = dir,
                parse_status = status,
                raw_message = raw,
                raw_hex = hexVal,
                IsHexMode = isHexMode
            };

            var (type, desc, bg, fg) = DecodeFrame(raw, dir);
            item.type_badge = type;
            item.description = desc;
            item.TypeBadgeBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            item.TypeBadgeFgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));

            return item;
        }

        private static (string Type, string Desc, string Bg, string Fg) DecodeFrame(string raw, string dir)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return ("EMPTY", "Empty Transmission Frame", "Transparent", "#64748B");

            string trimmed = raw.Trim();

            // Link-level control bytes
            if (trimmed == "<ACK>" || trimmed == "\x06")
                return ("ACK", dir == "OUT" ? "Host Acknowledged Frame" : "Device Acknowledged Frame", "Transparent", "#34D399");

            if (trimmed == "<ENQ>" || trimmed == "\x05")
                return ("ENQ", "Link Session Request (Enquiry)", "Transparent", "#F59E0B");

            if (trimmed == "<EOT>" || trimmed == "\x04")
                return ("EOT", "Link Session Terminated (End of Transmission)", "Transparent", "#94A3B8");

            if (trimmed == "<NAK>" || trimmed == "\x15")
                return ("NAK", "Link Negative Acknowledge (Frame Error)", "Transparent", "#EF4444");

            // Dimension EXL Protocol (<STX>X<FS>...)
            string clean = trimmed.Replace("<STX>", "").Replace("<ETX>", "").Trim();
            if (clean.Contains("<FS>") || clean.Contains('\x1c'))
            {
                string[] parts = clean.Split(new[] { "<FS>", "\x1c" }, StringSplitOptions.None);
                if (parts.Length > 0)
                {
                    string recType = parts[0].Trim();
                    switch (recType)
                    {
                        case "P":
                            return ("POLL", "Device Idle Poll (Keepalive check)", "Transparent", "#38BDF8");

                        case "N":
                            return ("NO-ORDER", "Host Response: No Pending Orders for Analyzer", "Transparent", "#94A3B8");

                        case "I":
                            string barcode = parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1].Trim() : "Unknown";
                            return ("QUERY", $"Sample Query for Barcode [{barcode}]", "Transparent", "#F59E0B");

                        case "D":
                            string bcode = parts.Length > 5 ? parts[5].Trim() : "";
                            var tests = new List<string>();
                            for (int i = 12; i < parts.Length - 1; i++)
                            {
                                if (!string.IsNullOrWhiteSpace(parts[i]))
                                    tests.Add(parts[i].Trim());
                            }
                            string testList = tests.Count > 0 ? string.Join(", ", tests) : "0 tests";
                            return ("ORDER", $"Worklist Download: Barcode [{bcode}] -> [{testList}]", "Transparent", "#A78BFA");

                        case "R":
                            string rBar = parts.Length > 1 ? parts[1].Trim() : "";
                            string rTest = parts.Length > 3 ? parts[3].Trim() : "";
                            string rVal = parts.Length > 4 ? parts[4].Trim() : "";
                            string rUnit = parts.Length > 7 ? parts[7].Trim() : "";
                            return ("RESULT", $"Test Result: Barcode [{rBar}], {rTest} = {rVal} {rUnit}", "Transparent", "#34D399");

                        case "M":
                            string st = parts.Length > 1 ? parts[1].Trim() : "";
                            if (st == "A")
                            {
                                string msg = dir == "OUT"
                                    ? "Host Acceptance: Result Stored Successfully"
                                    : "Device Acceptance: Worklist Download Accepted";
                                return ("ACCEPT", msg, "Transparent", "#10B981");
                            }
                            else
                            {
                                return ("REJECT", $"Frame Rejected: Status ({st})", "Transparent", "#EF4444");
                            }

                        case "C":
                            return ("CALIB", "Instrument Calibration / Status Packet", "Transparent", "#C084FC");
                    }
                }
            }

            // Standard ASTM 1394 Records
            if (trimmed.Length >= 2 && trimmed[1] == '|')
            {
                char astmType = trimmed[0];
                switch (astmType)
                {
                    case 'H':
                        return ("ASTM-HDR", "ASTM Transmission Header (Start of Session)", "Transparent", "#38BDF8");

                    case 'P':
                        return ("PATIENT", "ASTM Patient Demographic Record", "Transparent", "#22D3EE");

                    case 'O':
                        return ("ORDER", "ASTM Order / Worklist Record", "Transparent", "#A78BFA");

                    case 'R':
                        return ("RESULT", "ASTM Test Result Measurement Record", "Transparent", "#34D399");

                    case 'Q':
                        return ("QUERY", "ASTM Query for Sample Worklist", "Transparent", "#F59E0B");

                    case 'L':
                        return ("ASTM-TRM", "ASTM Transmission Terminator (End of Session)", "Transparent", "#94A3B8");
                }
            }

            return (dir == "OUT" ? "OUT-DATA" : "IN-DATA", "Raw Transmission Data", "Transparent", dir == "OUT" ? "#38BDF8" : "#34D399");
        }
    }
}
