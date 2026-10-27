using System;
using System.Windows;
using System.Windows.Media;
using Lis.Core.Services;

namespace Lis.Middleware
{
    public partial class ServerSettingsDialog : Window
    {
        public bool IsSettingsChanged { get; private set; }

        public ServerSettingsDialog()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        private void LoadCurrentSettings()
        {
            try
            {
                var settings = DatabaseConfigService.LoadSettings();
                TxtServerHost.Text = settings.Host;
                TxtServerPort.Text = settings.Port.ToString();
                TxtDatabaseName.Text = settings.Database;
                TxtUsername.Text = settings.Username;
                TxtPassword.Password = settings.Password;
            }
            catch (Exception ex)
            {
                ShowStatus($"Error loading settings: {ex.Message}", false);
            }
        }

        private DatabaseConnectionSettings CollectSettings()
        {
            int.TryParse(TxtServerPort.Text.Trim(), out int port);
            if (port <= 0) port = 5432;

            return new DatabaseConnectionSettings
            {
                Host = TxtServerHost.Text.Trim(),
                Port = port,
                Database = TxtDatabaseName.Text.Trim(),
                Username = TxtUsername.Text.Trim(),
                Password = TxtPassword.Password
            };
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            var settings = CollectSettings();
            if (string.IsNullOrWhiteSpace(settings.Host))
            {
                ShowStatus("Please enter server host or IP.", false);
                return;
            }

            BtnTest.IsEnabled = false;
            BtnSave.IsEnabled = false;
            ShowStatus("⏳ Testing connection...", null);

            var (success, message) = await DatabaseConfigService.TestConnectionAsync(settings);

            BtnTest.IsEnabled = true;
            BtnSave.IsEnabled = true;

            if (success)
            {
                ShowStatus($"✅ {message}", true);
            }
            else
            {
                ShowStatus($"❌ Connection failed:\n{message}", false);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var settings = CollectSettings();
            if (string.IsNullOrWhiteSpace(settings.Host))
            {
                ShowStatus("Please enter server host or IP.", false);
                return;
            }

            try
            {
                DatabaseConfigService.SaveSettings(settings);
                IsSettingsChanged = true;
                MessageBox.Show("Server settings saved successfully to appsettings.json.\nPlease restart Middleware to apply the new connection.", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowStatus($"Error saving settings: {ex.Message}", false);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowStatus(string message, bool? isSuccess)
        {
            BdrTestStatus.Visibility = Visibility.Visible;
            TxtTestStatus.Text = message;

            if (isSuccess == null) // Pending
            {
                BdrTestStatus.Background = new SolidColorBrush(Color.FromArgb(50, 56, 189, 248));
                BdrTestStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
                TxtTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(56, 189, 248));
            }
            else if (isSuccess.Value) // Success
            {
                BdrTestStatus.Background = new SolidColorBrush(Color.FromArgb(50, 16, 185, 129));
                BdrTestStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                TxtTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            }
            else // Error
            {
                BdrTestStatus.Background = new SolidColorBrush(Color.FromArgb(60, 239, 68, 68));
                BdrTestStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                TxtTestStatus.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            }
        }
    }
}
