using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lis.App.Services;
using Lis.Core.Services;

namespace Lis.Web.Services
{
    public class DirectPrintResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? TargetPrinter { get; set; }
    }

    /// <summary>
    /// Service for direct silent printing through Windows Print Spooler.
    /// Executes WPF and System.Printing workloads on dedicated STA threads.
    /// </summary>
    public class DirectPrintService
    {
        private readonly ILabSettingsService _settingsService;

        public DirectPrintService(ILabSettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <summary>
        /// Retrieves the list of all installed printers on the Windows host.
        /// </summary>
        public List<string> GetInstalledPrinters()
        {
            var printers = new List<string>();
            try
            {
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (!string.IsNullOrWhiteSpace(printer))
                    {
                        printers.Add(printer);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DirectPrintService] Error enumerating printers: {ex.Message}");
            }
            return printers;
        }

        /// <summary>
        /// Helper to execute an action on a dedicated Single-Threaded Apartment (STA) thread.
        /// Required for WPF elements and System.Printing PrintDialog in ASP.NET Core.
        /// </summary>
        private static Task<DirectPrintResult> RunOnStaThreadAsync(Func<DirectPrintResult> action)
        {
            var tcs = new TaskCompletionSource<DirectPrintResult>();
            var thread = new Thread(() =>
            {
                try
                {
                    var result = action();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetResult(new DirectPrintResult
                    {
                        Success = false,
                        Message = $"خطأ أثناء تنفيذ أمر الطباعة: {ex.Message}"
                    });
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            return tcs.Task;
        }

        /// <summary>
        /// Prints a specimen barcode tube label (50x25 mm) directly and silently to the configured label printer.
        /// </summary>
        public async Task<DirectPrintResult> PrintLabelDirectAsync(SpecimenLabelData data, string? printerOverride = null, double? offsetMm = null)
        {
            var settings = await _settingsService.GetSettingsAsync();
            string? printer = !string.IsNullOrWhiteSpace(printerOverride) ? printerOverride : settings.LabelPrinter;
            double offset = offsetMm ?? settings.BarcodeOffsetMm;

            return await RunOnStaThreadAsync(() =>
            {
                try
                {
                    LabelRenderer.BarcodeOffsetMm = offset;
                    string printedTo = LabelRenderer.PrintLabel(data, printer);
                    return new DirectPrintResult
                    {
                        Success = true,
                        TargetPrinter = printedTo,
                        Message = $"تم إرسال ملصق الباركود بنجاح وبشكل صامت إلى الطابعة [{printedTo}]."
                    };
                }
                catch (Exception ex)
                {
                    return new DirectPrintResult
                    {
                        Success = false,
                        TargetPrinter = printer,
                        Message = $"فشل إرسال ملصق الباركود إلى الطابعة '{printer}': {ex.Message}"
                    };
                }
            });
        }

        /// <summary>
        /// Prints an 80mm cashier thermal receipt directly and silently to the configured receipt printer.
        /// </summary>
        public async Task<DirectPrintResult> PrintReceiptDirectAsync(ReceiptPrintData data, string? printerOverride = null)
        {
            var settings = await _settingsService.GetSettingsAsync();
            string? printer = !string.IsNullOrWhiteSpace(printerOverride) ? printerOverride : settings.ReceiptPrinter;

            return await RunOnStaThreadAsync(() =>
            {
                try
                {
                    ReceiptRenderer.PrintReceipt(data, printer);
                    return new DirectPrintResult
                    {
                        Success = true,
                        TargetPrinter = printer ?? "Default",
                        Message = $"تم إرسال وصل الكاشير (80 مم) بنجاح وبشكل صامت إلى الطابعة [{printer ?? "الافتراضية"}]."
                    };
                }
                catch (Exception ex)
                {
                    return new DirectPrintResult
                    {
                        Success = false,
                        TargetPrinter = printer,
                        Message = $"فشل إرسال وصل الكاشير إلى الطابعة '{printer}': {ex.Message}"
                    };
                }
            });
        }

        /// <summary>
        /// Prints an A4 patient report directly and silently to the configured report printer.
        /// </summary>
        public async Task<DirectPrintResult> PrintReportDirectAsync(OrderResultsDto order, string? printerOverride = null)
        {
            var settings = await _settingsService.GetSettingsAsync();
            string? printer = !string.IsNullOrWhiteSpace(printerOverride) ? printerOverride : settings.ReportPrinter;

            return await RunOnStaThreadAsync(() =>
            {
                try
                {
                    ReportRenderer.PrintReport(order, settings, printer);
                    return new DirectPrintResult
                    {
                        Success = true,
                        TargetPrinter = printer ?? "Default",
                        Message = $"تم إرسال تقرير المريض A4 بنجاح وبشكل صامت إلى الطابعة [{printer ?? "الافتراضية"}]."
                    };
                }
                catch (Exception ex)
                {
                    return new DirectPrintResult
                    {
                        Success = false,
                        TargetPrinter = printer,
                        Message = $"فشل إرسال تقرير النتائج إلى الطابعة '{printer}': {ex.Message}"
                    };
                }
            });
        }
    }
}

