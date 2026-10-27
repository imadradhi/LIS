using System;
using System.Collections.Generic;
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
using Microsoft.Extensions.Configuration;

namespace Lis.Middleware
{
    public partial class MainWindow : Window
    {
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly MiddlewareEngine _middlewareEngine;
        private readonly DispatcherTimer _pollTimer;

        public MainWindow()
        {
            InitializeComponent();

            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .Build();

            var connStr = config.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Port=5432;Database=lis_db;Username=postgres;Password=@Hig5udMz;";

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
                await LoadLogsAsync();
                await LoadStatsAsync();
            };

            Loaded += async (s, e) =>
            {
                await EnsureAdvia360ConfiguredAsync();

                // Auto-start middleware engine FIRST
                await _middlewareEngine.StartAsync();

                UpdateEngineUi(_middlewareEngine.IsRunning);
                await LoadInstrumentsAsync();
                await LoadLogsAsync();
                await LoadStatsAsync();

                _pollTimer.Start();
                StartInstrumentNotificationListener();
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
            if (isRunning)
            {
                BadgeEngineStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
                BadgeEngineStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                int count = _middlewareEngine.ActiveDriversCount;
                TxtEngineStatus.Text = count > 0 ? $"🟢 المحرك يعمل ({count} أجهزة)" : "🟢 المحرك يعمل ويستمع";
                TxtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                BtnToggleEngine.Content = "⏹️ إيقاف الخدمة";
            }
            else
            {
                BadgeEngineStatus.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#451A1A"));
                BadgeEngineStatus.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B91C1C"));
                TxtEngineStatus.Text = "⚪ المحرك متوقف";
                TxtEngineStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
                BtnToggleEngine.Content = "▶️ تشغيل الخدمة";
            }
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

                var displayList = new List<dynamic>();
                foreach (var inst in items)
                {
                    string connInfo = inst.ConnectionType == "TCP"
                        ? $"TCP Server : {inst.Port ?? 5100} • {inst.Protocol}"
                        : $"Serial {inst.ComPort ?? "COM1"} : {inst.BaudRate ?? 9600} • {inst.Protocol}";

                    string status = _middlewareEngine.IsRunning ? "🟢 يستمع" : "⚪ متوقف";
                    var bg = _middlewareEngine.IsRunning ? "#064E3B" : "#1E293B";
                    var fg = _middlewareEngine.IsRunning ? "#34D399" : "#94A3B8";

                    displayList.Add(new
                    {
                        code = inst.Code,
                        name = inst.Name,
                        connection_details = connInfo,
                        status_text = status,
                        StatusBgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                        StatusFgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg))
                    });
                }

                GridInstruments.ItemsSource = displayList;
                TxtStatInstCount.Text = items.Count.ToString();
            }
            catch { }
        }

        private async Task LoadLogsAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                var logs = (await conn.QueryAsync<dynamic>(@"
                    SELECT ml.id, ml.timestamp, COALESCE(i.code, 'Unknown') AS instrument_code,
                           ml.direction, ml.parse_status, ml.raw_message
                    FROM middleware_log ml
                    LEFT JOIN instruments i ON i.id = ml.instrument_id
                    ORDER BY ml.id DESC
                    LIMIT 60;
                ")).AsList();

                GridMiddlewareLogs.ItemsSource = logs;

                if (ChkAutoScroll.IsChecked == true && logs.Count > 0 && GridMiddlewareLogs.Items.Count > 0)
                {
                    GridMiddlewareLogs.ScrollIntoView(GridMiddlewareLogs.Items[0]);
                }
            }
            catch { }
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
                MessageBox.Show($"خطأ أثناء تبديل حالة الميدلوير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnRestartEngine_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _middlewareEngine.RestartAsync();
                await LoadInstrumentsAsync();
                MessageBox.Show("تمت إعادة تشغيل خدمة الميدلوير بنجاح.", "تمت إعادة التشغيل", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء إعادة تشغيل الميدلوير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefreshLogs_Click(object sender, RoutedEventArgs e)
        {
            _ = LoadLogsAsync();
            _ = LoadStatsAsync();
        }

        private void GridMiddlewareLogs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridMiddlewareLogs.SelectedItem != null)
            {
                dynamic row = GridMiddlewareLogs.SelectedItem;
                string raw = (string?)row.raw_message ?? "";
                string dir = (string?)row.direction ?? "";
                string code = (string?)row.instrument_code ?? "";
                string time = row.timestamp != null ? ((DateTime)row.timestamp).ToString("dd/MM/yyyy HH:mm:ss") : "";

                string dirText = dir == "OUT" ? "إرسال (OUT) 📤" : "استقبال (IN) 📥";
                TxtLogDetailHeader.Text = $"[{time}] - الجهاز: [{code}] - {dirText}";
                TxtLogDetailMessage.Text = raw;
                BorderLogDetails.Visibility = Visibility.Visible;
            }
            else
            {
                BorderLogDetails.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (GridMiddlewareLogs.SelectedItem != null)
                {
                    dynamic row = GridMiddlewareLogs.SelectedItem;
                    string raw = (string?)row.raw_message ?? "";
                    Clipboard.SetText(raw);
                    MessageBox.Show("تم نسخ الرسالة المحددة إلى الحافظة بنجاح.", "تم النسخ", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (GridMiddlewareLogs.ItemsSource is IEnumerable<dynamic> items)
                {
                    var sb = new StringBuilder();
                    foreach (var item in items)
                    {
                        string time = item.timestamp != null ? ((DateTime)item.timestamp).ToString("dd/MM/yyyy HH:mm:ss") : "";
                        sb.AppendLine($"[{time}] [{item.instrument_code}] [{item.direction}] [{item.parse_status}]");
                        sb.AppendLine(item.raw_message?.ToString() ?? "");
                        sb.AppendLine(new string('-', 50));
                    }

                    if (sb.Length > 0)
                    {
                        Clipboard.SetText(sb.ToString());
                        MessageBox.Show("تم نسخ سجل الحركات بالكامل إلى الحافظة بنجاح.", "تم النسخ", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("لا توجد سجلات لنسخها حالياً.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء النسخ: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "هل أنت متأكد من مسح جميع سجلات الاتصال من قاعدة البيانات؟",
                "تأكيد مسح السجل",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                using var conn = _connectionFactory.CreateConnection();
                await conn.ExecuteAsync("TRUNCATE TABLE middleware_log;");
                GridMiddlewareLogs.ItemsSource = null;
                BorderLogDetails.Visibility = Visibility.Collapsed;
                await LoadStatsAsync();
                MessageBox.Show("تم مسح سجلات الميدلوير بنجاح.", "تم المسح", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء مسح السجلات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnCopySelectedRaw_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(TxtLogDetailMessage.Text))
            {
                Clipboard.SetText(TxtLogDetailMessage.Text);
                MessageBox.Show("تم نسخ نص الرسالة إلى الحافظة بنجاح.", "تم النسخ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
