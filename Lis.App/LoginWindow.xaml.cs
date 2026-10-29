using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Lis.App.Services;
using Lis.Core.Data;
using Lis.Core.Services;
using Microsoft.Extensions.Configuration;

namespace Lis.App
{
    public partial class LoginWindow : Window
    {
        private IDbConnectionFactory _connectionFactory = null!;
        private IAuthService _authService = null!;
        private ILabSettingsService _labSettingsService = null!;
        private bool _isPasswordRevealed = false;

        public LoginWindow()
        {
            InitializeComponent();
            InitServices();
            UpdateServerBadge();
            Loaded += async (s, e) => await InitializeLoginAsync();
        }

        private void InitServices()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .Build();

            var connStr = config.GetConnectionString("DefaultConnection")
                ?? "Host=localhost;Port=5432;Database=lis_db;Username=postgres;Password=@Hig5udMz;";

            _connectionFactory = new DbConnectionFactory(connStr);
            _authService = new AuthService(_connectionFactory);
            _labSettingsService = new LabSettingsService(_connectionFactory, _authService);
        }

        private void UpdateServerBadge()
        {
            try
            {
                var settings = DatabaseConfigService.LoadSettings();
                if (TxtServerBadge != null)
                {
                    TxtServerBadge.Text = $"{settings.Host}:{settings.Port}";
                }
            }
            catch { }
        }

        private async void BtnServerSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ServerSettingsDialog { Owner = this };
            if (dlg.ShowDialog() == true && dlg.IsSettingsChanged)
            {
                BdrError.Visibility = Visibility.Collapsed;
                InitServices();
                UpdateServerBadge();
                await InitializeLoginAsync();
            }
        }

        private async Task InitializeLoginAsync()
        {
            try
            {
                // Ensure default admin user exists
                await _authService.EnsureDefaultAdminUserAsync();

                // Load saved lab settings and apply saved theme
                var settings = await _labSettingsService.GetSettingsAsync();
                if (!string.IsNullOrWhiteSpace(settings.Theme))
                {
                    ThemeManager.ApplyTheme(settings.Theme);
                }

                // If username is already filled, focus password box
                if (!string.IsNullOrWhiteSpace(TxtUsername.Text))
                {
                    TxtPassword.Focus();
                }
                else
                {
                    TxtUsername.Focus();
                }
            }
            catch (Exception ex)
            {
                TxtErrorMessage.Text = $"تعذر الاتصال بقاعدة البيانات: {ex.Message}";
                BdrError.Visibility = Visibility.Visible;
            }
        }

        private async void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            await AttemptLoginAsync();
        }

        private async Task AttemptLoginAsync()
        {
            // Sync password if currently revealed
            if (_isPasswordRevealed)
            {
                TxtPassword.Password = TxtPasswordVisible.Text;
            }

            string username = TxtUsername.Text.Trim();
            string password = TxtPassword.Password;

            if (string.IsNullOrWhiteSpace(username))
            {
                ShowError("يرجى إدخال اسم المستخدم (Username)");
                TxtUsername.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(password))
            {
                ShowError("يرجى إدخال كلمة المرور (Password)");
                if (_isPasswordRevealed) TxtPasswordVisible.Focus();
                else TxtPassword.Focus();
                return;
            }

            try
            {
                BtnLogin.IsEnabled = false;
                BtnLogin.Content = "⏳ جاري التحقق من الحساب...";

                var session = await _authService.LoginAsync(username, password);
                if (session == null)
                {
                    ShowError("اسم المستخدم أو كلمة المرور غير صحيحة! (Invalid credentials)");
                    TxtPassword.SelectAll();
                    TxtPassword.Focus();
                    return;
                }

                // Login successful - open MainWindow
                var mainWindow = new MainWindow(session);
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();

                this.Close();
            }
            catch (Exception ex)
            {
                ShowError($"خطأ أثناء تسجيل الدخول: {ex.Message}");
            }
            finally
            {
                BtnLogin.IsEnabled = true;
                BtnLogin.Content = "🚀 تسجيل الدخول إلى النظام (Sign In)";
            }
        }

        private void BtnTogglePassword_Click(object sender, RoutedEventArgs e)
        {
            _isPasswordRevealed = !_isPasswordRevealed;
            if (_isPasswordRevealed)
            {
                TxtPasswordVisible.Text = TxtPassword.Password;
                TxtPassword.Visibility = Visibility.Collapsed;
                TxtPasswordVisible.Visibility = Visibility.Visible;
                BtnTogglePassword.Content = "🙈";
                TxtPasswordVisible.Focus();
                TxtPasswordVisible.CaretIndex = TxtPasswordVisible.Text.Length;
            }
            else
            {
                TxtPassword.Password = TxtPasswordVisible.Text;
                TxtPasswordVisible.Visibility = Visibility.Collapsed;
                TxtPassword.Visibility = Visibility.Visible;
                BtnTogglePassword.Content = "👁️";
                TxtPassword.Focus();
            }
        }

        private void BtnAutoFill_Click(object sender, RoutedEventArgs e)
        {
            TxtUsername.Text = "admin";
            TxtPassword.Password = "Admin123";
            TxtPasswordVisible.Text = "Admin123";
            BdrError.Visibility = Visibility.Collapsed;
            if (_isPasswordRevealed) TxtPasswordVisible.Focus();
            else TxtPassword.Focus();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void ShowError(string message)
        {
            TxtErrorMessage.Text = message;
            BdrError.Visibility = Visibility.Visible;
        }

        private void TxtUsername_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (_isPasswordRevealed) TxtPasswordVisible.Focus();
                else TxtPassword.Focus();
            }
        }

        private async void TxtPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AttemptLoginAsync();
            }
        }

        private async void TxtPasswordVisible_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                await AttemptLoginAsync();
            }
        }
    }
}
