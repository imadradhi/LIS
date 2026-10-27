using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace Lis.App
{
    public partial class App : Application
    {
        public App()
        {
            ShutdownMode = ShutdownMode.OnLastWindowClose;

            // Adopt dd/MM/yyyy globally across the application
            var culture = new CultureInfo("en-GB");
            culture.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
            culture.DateTimeFormat.LongDatePattern = "dd/MM/yyyy HH:mm:ss";
            culture.DateTimeFormat.DateSeparator = "/";

            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

            DispatcherUnhandledException += (s, args) =>
            {
                File.AppendAllText("app_crash.log", $"[DispatcherUnhandledException] {DateTime.Now:dd/MM/yyyy HH:mm:ss}: {args.Exception}\n");
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                File.AppendAllText("app_crash.log", $"[AppDomainUnhandledException] {DateTime.Now:dd/MM/yyyy HH:mm:ss}: {args.ExceptionObject}\n");
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                var loginWin = new LoginWindow();
                loginWin.Show();
            }
            catch (Exception ex)
            {
                File.AppendAllText("app_crash.log", $"[OnStartup Window Creation Failed] {DateTime.Now:dd/MM/yyyy HH:mm:ss}: {ex}\n");
                LisMessageBox.Show($"حدث خطأ أثناء بدء التطبيق:\n{ex.Message}", "خطأ تشغيل", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
    }
}
