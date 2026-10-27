using System;
using System.Collections.Generic;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ZXing;
using ZXing.Common;
using Lis.Core.Services;

namespace Lis.App.Services
{
    public class ReceiptItemData
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string FormattedPrice => $"{Price:#,##0} IQD";
    }

    public class ReceiptPrintData
    {
        public string LabNameAr { get; set; } = "مختبر التحليلات الطبية التخصصي";
        public string LabNameEn { get; set; } = "Specialized Medical Laboratory";
        public string? Address { get; set; } = "العراق - بغداد";
        public string? Phones { get; set; } = "07700000000";

        public long InvoiceNo { get; set; }
        public long OrderId { get; set; }
        public DateTime OrderDate { get; set; } = DateTime.Now;

        public string PatientNameAr { get; set; } = string.Empty;
        public long Mrn { get; set; }
        public string? PatientPhone { get; set; }
        public string? Gender { get; set; }

        public string CashierName { get; set; } = "المحاسب (الكاشير)";
        public string PaymentMethod { get; set; } = "Cash"; // "Cash" or "Card"
        public string PaymentMethodDisplay => PaymentMethod == "Card" ? "كارت / بطاقة إلكترونية 💳" : "نقداً (Cash) 💵";

        public List<ReceiptItemData> Items { get; set; } = new List<ReceiptItemData>();

        public decimal TotalAmount { get; set; }
        public decimal Discount { get; set; }
        public decimal NetAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal BalanceDue { get; set; }
        public string Currency { get; set; } = "IQD";

        public string BarcodeValue => $"INV{InvoiceNo:D6}";
    }

    /// <summary>
    /// Renders and prints standard 80mm (8 cm) continuous thermal receipts for cashiers.
    /// Exact width: 80 mm = ~302 DIP (Printable area ~284 DIP).
    /// </summary>
    public static class ReceiptRenderer
    {
        // 80mm width in 96 DPI DIP
        public const double ReceiptWidthDip = 80.0 / 25.4 * 96.0; // ~302.36 DIP
        public const double PrintableWidthDip = 284.0;
        public const double ReceiptDpi = 203.2;

        public static string? DefaultReceiptPrinter { get; set; }

        public static FrameworkElement CreateReceiptElement(ReceiptPrintData data)
        {
            var root = new Border
            {
                Width = ReceiptWidthDip,
                Background = Brushes.White,
                Padding = new Thickness(9, 14, 9, 14)
            };

            var stack = new StackPanel
            {
                Width = PrintableWidthDip,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            root.Child = stack;

            var fontFamily = new FontFamily("Simplified Arabic");

            // 1. Header: Lab Name
            var txtLabNameAr = new TextBlock
            {
                Text = data.LabNameAr,
                FontFamily = fontFamily,
                FontWeight = FontWeights.Bold,
                FontSize = 13.5,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            stack.Children.Add(txtLabNameAr);

            if (!string.IsNullOrWhiteSpace(data.LabNameEn))
            {
                var txtLabNameEn = new TextBlock
                {
                    Text = data.LabNameEn,
                    FontFamily = fontFamily,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 10,
                    Foreground = Brushes.Black,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0)
                };
                stack.Children.Add(txtLabNameEn);
            }

            if (!string.IsNullOrWhiteSpace(data.Phones) || !string.IsNullOrWhiteSpace(data.Address))
            {
                string contact = string.Join(" • ", new[] { data.Address, data.Phones });
                var txtContact = new TextBlock
                {
                    Text = contact,
                    FontFamily = fontFamily,
                    FontSize = 9.5,
                    Foreground = Brushes.Black,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                stack.Children.Add(txtContact);
            }

            // Separator
            stack.Children.Add(CreateDashedLine());

            // 2. Receipt Badge Title
            var titleBorder = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 3, 6, 3),
                Margin = new Thickness(0, 2, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            titleBorder.Child = new TextBlock
            {
                Text = "وصل قبض مريض (CASHIER RECEIPT)",
                FontFamily = fontFamily,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center
            };
            stack.Children.Add(titleBorder);

            // 3. Metadata Rows
            stack.Children.Add(CreateKeyValueRow("رقم الفاتورة (Inv #):", $"INV-{data.InvoiceNo}", fontFamily, isBold: true));
            stack.Children.Add(CreateKeyValueRow("رقم الطلب (Order #):", $"#{data.OrderId}", fontFamily));
            DateTime receiptDt = data.OrderDate.Kind == DateTimeKind.Utc ? data.OrderDate.ToLocalTime() : data.OrderDate;
            stack.Children.Add(CreateKeyValueRow("التاريخ والوقت:", receiptDt.ToString("dd/MM/yyyy HH:mm"), fontFamily));
            stack.Children.Add(CreateKeyValueRow("الكاشير / القاصة:", data.CashierName, fontFamily));
            stack.Children.Add(CreateKeyValueRow("اسم المريض:", data.PatientNameAr, fontFamily, isBold: true, fontSize: 11));
            stack.Children.Add(CreateKeyValueRow("رقم الملف (MRN):", $"P.ID: {data.Mrn}", fontFamily));
            if (!string.IsNullOrWhiteSpace(data.PatientPhone))
            {
                stack.Children.Add(CreateKeyValueRow("الهاتف:", data.PatientPhone, fontFamily));
            }

            // Separator
            stack.Children.Add(CreateDashedLine());

            // 4. Items Table Header
            var tableHeader = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            tableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tableHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85, GridUnitType.Pixel) });

            var thName = new TextBlock { Text = "الفحص (Test)", FontFamily = fontFamily, FontWeight = FontWeights.Bold, FontSize = 10, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Right };
            var thPrice = new TextBlock { Text = "السعر (Price)", FontFamily = fontFamily, FontWeight = FontWeights.Bold, FontSize = 10, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(thName, 0);
            Grid.SetColumn(thPrice, 1);
            tableHeader.Children.Add(thName);
            tableHeader.Children.Add(thPrice);
            stack.Children.Add(tableHeader);

            // 5. Items Rows
            foreach (var item in data.Items)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(85, GridUnitType.Pixel) });

                string displayName = string.IsNullOrWhiteSpace(item.Code) ? item.Name : $"[{item.Code}] {item.Name}";
                var tdName = new TextBlock { Text = displayName, FontFamily = fontFamily, FontSize = 9.5, Foreground = Brushes.Black, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Right };
                var tdPrice = new TextBlock { Text = item.FormattedPrice, FontFamily = fontFamily, FontWeight = FontWeights.SemiBold, FontSize = 9.5, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left };

                Grid.SetColumn(tdName, 0);
                Grid.SetColumn(tdPrice, 1);
                row.Children.Add(tdName);
                row.Children.Add(tdPrice);
                stack.Children.Add(row);
            }

            // Separator
            stack.Children.Add(CreateDashedLine());

            // 6. Financial Summary Block
            stack.Children.Add(CreateKeyValueRow("إجمالي الفحوصات:", $"{data.TotalAmount:#,##0} {data.Currency}", fontFamily));
            if (data.Discount > 0)
            {
                stack.Children.Add(CreateKeyValueRow("الخصم الممنوح:", $"-{data.Discount:#,##0} {data.Currency}", fontFamily));
            }
            stack.Children.Add(CreateKeyValueRow("الصافي المطلوب:", $"{data.NetAmount:#,##0} {data.Currency}", fontFamily, isBold: true, fontSize: 11));

            // Highlighted Payment Box
            var payBox = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1.5),
                Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
                Padding = new Thickness(8, 6, 8, 6),
                Margin = new Thickness(0, 6, 0, 6)
            };
            var payStack = new StackPanel();
            payBox.Child = payStack;

            var payRow1 = new Grid();
            payRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            payRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var lblPaid = new TextBlock { Text = "المقبوض في القاصة (PAID):", FontFamily = fontFamily, FontWeight = FontWeights.Bold, FontSize = 12, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Right };
            var valPaid = new TextBlock { Text = $"{data.PaidAmount:#,##0} {data.Currency}", FontFamily = fontFamily, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(lblPaid, 0);
            Grid.SetColumn(valPaid, 1);
            payRow1.Children.Add(lblPaid);
            payRow1.Children.Add(valPaid);
            payStack.Children.Add(payRow1);

            var payRow2 = new Grid { Margin = new Thickness(0, 3, 0, 0) };
            payRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            payRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var lblMethod = new TextBlock { Text = "طريقة الدفع (Payment Method):", FontFamily = fontFamily, FontSize = 10, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Right };
            var valMethod = new TextBlock { Text = data.PaymentMethodDisplay, FontFamily = fontFamily, FontWeight = FontWeights.Bold, FontSize = 10.5, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(lblMethod, 0);
            Grid.SetColumn(valMethod, 1);
            payRow2.Children.Add(lblMethod);
            payRow2.Children.Add(valMethod);
            payStack.Children.Add(payRow2);

            stack.Children.Add(payBox);

            // Remaining Balance
            if (data.BalanceDue > 0)
            {
                stack.Children.Add(CreateKeyValueRow("المتبقي بذمة المريض:", $"{data.BalanceDue:#,##0} {data.Currency}", fontFamily, isBold: true, fontSize: 11));
            }
            else
            {
                var txtPaidFull = new TextBlock
                {
                    Text = "✓ الفاتورة مسددة بالكامل (Paid in Full)",
                    FontFamily = fontFamily,
                    FontWeight = FontWeights.Bold,
                    FontSize = 10,
                    Foreground = Brushes.Black,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 2, 0, 2)
                };
                stack.Children.Add(txtPaidFull);
            }

            // Separator
            stack.Children.Add(CreateDashedLine());

            // 7. Barcode (Code 128)
            var barcodeBmp = GenerateBarcodeBitmap(data.BarcodeValue, 320, 65);
            if (barcodeBmp != null)
            {
                var barcodeImg = new Image
                {
                    Source = barcodeBmp,
                    Width = 190,
                    Height = 38,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 2)
                };
                stack.Children.Add(barcodeImg);

                var txtBarcodeText = new TextBlock
                {
                    Text = $"*{data.BarcodeValue}*",
                    FontFamily = fontFamily,
                    FontSize = 9.5,
                    Foreground = Brushes.Black,
                    TextAlignment = TextAlignment.Center
                };
                stack.Children.Add(txtBarcodeText);
            }

            // 8. Footer Notes
            var txtThankYou = new TextBlock
            {
                Text = "نتمنى لكم دوام الصحة والعافية",
                FontFamily = fontFamily,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 6, 0, 2)
            };
            stack.Children.Add(txtThankYou);

            var txtInstruction = new TextBlock
            {
                Text = "يرجى إبراز هذا الوصل عند مراجعة المختبر لاستلام النتائج",
                FontFamily = fontFamily,
                FontSize = 8.5,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            stack.Children.Add(txtInstruction);

            // Measure and arrange dynamically
            root.Measure(new Size(ReceiptWidthDip, double.PositiveInfinity));
            double measuredHeight = root.DesiredSize.Height;
            root.Height = measuredHeight;
            root.Arrange(new Rect(0, 0, ReceiptWidthDip, measuredHeight));
            root.UpdateLayout();

            return root;
        }

        private static FrameworkElement CreateDashedLine()
        {
            return new TextBlock
            {
                Text = "--------------------------------------------------------",
                Foreground = Brushes.Black,
                FontSize = 8.5,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 3, 0, 3)
            };
        }

        private static FrameworkElement CreateKeyValueRow(string key, string value, FontFamily fontFamily, bool isBold = false, double fontSize = 10)
        {
            var grid = new Grid { Margin = new Thickness(0, 1.5, 0, 1.5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var txtKey = new TextBlock
            {
                Text = key,
                FontFamily = fontFamily,
                FontSize = fontSize,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var txtVal = new TextBlock
            {
                Text = value,
                FontFamily = fontFamily,
                FontSize = fontSize,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Left
            };

            Grid.SetColumn(txtKey, 0);
            Grid.SetColumn(txtVal, 1);
            grid.Children.Add(txtKey);
            grid.Children.Add(txtVal);

            return grid;
        }

        private static BitmapSource? GenerateBarcodeBitmap(string text, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            try
            {
                var writer = new BarcodeWriterPixelData
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions
                    {
                        Width = width,
                        Height = height,
                        Margin = 1,
                        PureBarcode = true
                    }
                };

                var pixelData = writer.Write(text);
                var bitmap = BitmapSource.Create(
                    pixelData.Width,
                    pixelData.Height,
                    ReceiptDpi,
                    ReceiptDpi,
                    PixelFormats.Bgr32,
                    null,
                    pixelData.Pixels,
                    pixelData.Width * 4);

                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public static bool IsLabelPrinterName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.Contains("SL32", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("HPRT", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Barcode", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Label", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("Zebra", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("GK420", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("ZD", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("TSC", StringComparison.OrdinalIgnoreCase);
        }

        public static PrintQueue? ResolveReceiptPrintQueue(string? printerName = null)
        {
            string? targetPrinter = !string.IsNullOrWhiteSpace(printerName) ? printerName : DefaultReceiptPrinter;

            try
            {
                using var printServer = new LocalPrintServer();
                var queues = printServer.GetPrintQueues();

                // 1. If explicit printer name is provided and matches, use it (as long as it's not a barcode printer)
                if (!string.IsNullOrWhiteSpace(targetPrinter))
                {
                    foreach (var q in queues)
                    {
                        if (string.Equals(q.Name, targetPrinter, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(q.FullName, targetPrinter, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!IsLabelPrinterName(q.FullName))
                                return q;
                        }
                    }
                }

                // 2. Search for thermal receipt printers (POS80, POS, XP-80, XP80, Xprinter, Receipt, etc.)
                foreach (var q in queues)
                {
                    string name = q.FullName ?? q.Name;
                    if (IsLabelPrinterName(name)) continue;

                    if (name.Contains("POS80", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("POS-80", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("XP-80", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("XP80", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("POSPrinter", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Xprinter", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Receipt", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Thermal", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("POS", StringComparison.OrdinalIgnoreCase))
                    {
                        return q;
                    }
                }

                // 3. If targetPrinter was specified and not a label printer, try partial match
                if (!string.IsNullOrWhiteSpace(targetPrinter) && !IsLabelPrinterName(targetPrinter))
                {
                    foreach (var q in queues)
                    {
                        string name = q.FullName ?? q.Name;
                        if (IsLabelPrinterName(name)) continue;

                        if (name.Contains(targetPrinter, StringComparison.OrdinalIgnoreCase) ||
                            targetPrinter.Contains(name, StringComparison.OrdinalIgnoreCase))
                        {
                            return q;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ReceiptRenderer] Error resolving receipt printer: {ex.Message}");
            }

            return null;
        }

        public static void PrintReceipt(ReceiptPrintData data, string? printerName = null)
        {
            try
            {
                var printDialog = new PrintDialog();

                var queue = ResolveReceiptPrintQueue(printerName);
                if (queue != null)
                {
                    printDialog.PrintQueue = queue;
                }
                else
                {
                    // Guard against printing receipt on a default label printer
                    try
                    {
                        using var printServer = new LocalPrintServer();
                        var defaultQueue = printServer.DefaultPrintQueue;
                        if (defaultQueue != null && IsLabelPrinterName(defaultQueue.FullName))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ReceiptRenderer] Aborted print: Default printer '{defaultQueue.FullName}' is a label printer and no valid receipt printer was resolved.");
                            return;
                        }
                    }
                    catch { }
                }

                var container = CreateReceiptElement(data);

                try
                {
                    printDialog.PrintTicket.PageMediaSize = new PageMediaSize(container.Width, container.Height);
                }
                catch { }

                printDialog.PrintVisual(container, $"Receipt_INV_{data.InvoiceNo}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to print receipt: {ex.Message}");
            }
        }

        public static FrameworkElement CreateTillSummaryElement(UserCashSummaryDto tillData, LabSettingsDto settings, DateTime reportDate)
        {
            var root = new Border
            {
                Width = ReceiptWidthDip,
                Background = Brushes.White,
                Padding = new Thickness(9, 14, 9, 14)
            };

            var stack = new StackPanel
            {
                Width = PrintableWidthDip,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            root.Child = stack;

            var fontFamily = new FontFamily("Simplified Arabic");

            // 1. Lab Name
            stack.Children.Add(new TextBlock
            {
                Text = settings.LabNameAr,
                FontFamily = fontFamily,
                FontWeight = FontWeights.Bold,
                FontSize = 13.5,
                Foreground = Brushes.Black,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });

            if (!string.IsNullOrWhiteSpace(settings.LabNameEn))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = settings.LabNameEn,
                    FontFamily = fontFamily,
                    FontSize = 9.5,
                    Foreground = Brushes.DimGray,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0)
                });
            }

            stack.Children.Add(CreateDashedLine());

            // 2. Title: كشف إغلاق القاصة
            var borderTitle = new Border
            {
                Background = Brushes.Black,
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 4, 0, 8),
                CornerRadius = new CornerRadius(3)
            };
            borderTitle.Child = new TextBlock
            {
                Text = "كشف حركة القاصة اليومية (Till Summary)",
                FontFamily = fontFamily,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center
            };
            stack.Children.Add(borderTitle);

            // Till Info
            stack.Children.Add(CreateKeyValueRow("صاحب القاصة / الحساب:", tillData.FullName, fontFamily, true));
            stack.Children.Add(CreateKeyValueRow("تاريخ التقرير:", reportDate.ToString("dd/MM/yyyy"), fontFamily));
            stack.Children.Add(CreateKeyValueRow("وقت الطباعة:", DateTime.Now.ToString("dd/MM/yyyy hh:mm tt"), fontFamily));
            stack.Children.Add(CreateKeyValueRow("إجمالي عدد العمليات:", $"{tillData.TransactionsCount} عملية دفع", fontFamily, true));

            stack.Children.Add(CreateDashedLine());

            // Collections
            stack.Children.Add(CreateKeyValueRow("💵 المقبوض نقداً (Cash):", tillData.FormattedCash, fontFamily, true, 11));
            stack.Children.Add(CreateKeyValueRow("💳 المقبوض إلكتروني (Card):", tillData.FormattedCard, fontFamily, true, 11));

            var totalBorder = new Border
            {
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 6, 6, 6),
                Margin = new Thickness(0, 6, 0, 6),
                Background = new SolidColorBrush(Color.FromRgb(240, 240, 240))
            };
            totalBorder.Child = CreateKeyValueRow("💰 إجمالي دخل القاصة:", tillData.FormattedTotal, fontFamily, true, 12);
            stack.Children.Add(totalBorder);

            stack.Children.Add(CreateDashedLine());

            // Signatures
            var sigGrid = new Grid { Margin = new Thickness(0, 10, 0, 6) };
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sigGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var cashSig = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            cashSig.Children.Add(new TextBlock { Text = "توقيع المحاسب", FontFamily = fontFamily, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center });
            cashSig.Children.Add(new TextBlock { Text = "..............", FontFamily = fontFamily, FontSize = 10, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });

            var supSig = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            supSig.Children.Add(new TextBlock { Text = "توقيع المشرف / الإدارة", FontFamily = fontFamily, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center });
            supSig.Children.Add(new TextBlock { Text = "..............", FontFamily = fontFamily, FontSize = 10, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });

            Grid.SetColumn(cashSig, 0);
            Grid.SetColumn(supSig, 1);
            sigGrid.Children.Add(cashSig);
            sigGrid.Children.Add(supSig);
            stack.Children.Add(sigGrid);

            root.Measure(new Size(ReceiptWidthDip, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, ReceiptWidthDip, root.DesiredSize.Height));
            root.UpdateLayout();

            return root;
        }

        public static void PrintTillReport(UserCashSummaryDto tillData, LabSettingsDto settings, DateTime reportDate, string? printerName = null)
        {
            try
            {
                var printDialog = new PrintDialog();
                var queue = ResolveReceiptPrintQueue(printerName ?? settings.ReceiptPrinter);
                if (queue != null)
                {
                    printDialog.PrintQueue = queue;
                }
                else
                {
                    try
                    {
                        using var printServer = new LocalPrintServer();
                        var defaultQueue = printServer.DefaultPrintQueue;
                        if (defaultQueue != null && IsLabelPrinterName(defaultQueue.FullName))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ReceiptRenderer] Aborted print: Default printer '{defaultQueue.FullName}' is a label printer and no valid receipt printer was resolved.");
                            return;
                        }
                    }
                    catch { }
                }

                var container = CreateTillSummaryElement(tillData, settings, reportDate);
                try
                {
                    printDialog.PrintTicket.PageMediaSize = new PageMediaSize(container.Width, container.Height);
                }
                catch { }

                printDialog.PrintVisual(container, $"TillReport_{tillData.UserId}_{reportDate:yyyyMMdd}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to print till report: {ex.Message}");
            }
        }
    }
}
