using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace Lis.Middleware
{
    public partial class App : Application
    {
        public App()
        {
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
        }
    }
}
