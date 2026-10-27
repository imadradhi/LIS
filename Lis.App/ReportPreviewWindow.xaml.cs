using System;
using System.Windows;
using Lis.App.Services;
using Lis.Core.Services;

namespace Lis.App
{
    public partial class ReportPreviewWindow : Window
    {
        private readonly OrderResultsDto _orderResults;
        private readonly LabSettingsDto _settings;

        public ReportPreviewWindow(OrderResultsDto orderResults, LabSettingsDto settings)
        {
            InitializeComponent();
            _orderResults = orderResults;
            _settings = settings;

            TxtOrderBadge.Text = $"طلب #{_orderResults.OrderId} - {_orderResults.FullNameAr}";

            Loaded += (s, e) =>
            {
                var visual = ReportRenderer.CreateReportVisual(_orderResults, _settings);
                BorderDocumentHost.Child = visual;
            };
        }

        private void BtnPrintDirect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ReportRenderer.PrintReport(_orderResults, _settings, _settings.ReportPrinter);
                LisMessageBox.Show("تم إرسال التقرير الطبي إلى الطابعة بنجاح.", "طباعة التقرير", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"تعذر إرسال التقرير للطباعة: {ex.Message}", "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
