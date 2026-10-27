using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Lis.Core.Services;
using ZXing;
using ZXing.Common;

namespace Lis.App.Services
{
    public static class ReportRenderer
    {
        private const double A4_WIDTH_DIP  = 794;  // 210mm at 96 DPI
        private const double A4_HEIGHT_DIP = 1123; // 297mm at 96 DPI
        private const double PAGE_PADDING  = 36;   // ~9.5mm margins

        public static FrameworkElement CreateReportVisual(OrderResultsDto order, LabSettingsDto settings)
        {
            var fontFamily = new FontFamily("Segoe UI, Tahoma, Arial");

            var page = new Border
            {
                Width = A4_WIDTH_DIP,
                MinHeight = A4_HEIGHT_DIP,
                Background = Brushes.White,
                Padding = new Thickness(PAGE_PADDING),
                FlowDirection = FlowDirection.LeftToRight
            };

            var rootStack = new StackPanel { Orientation = Orientation.Vertical };
            page.Child = rootStack;

            // ═══ 1. HEADER ════════════════════════════════════════════════════
            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); // Logo
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Names
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Barcode / Contacts

            // Logo
            if (settings.LogoData != null && settings.LogoData.Length > 0)
            {
                try
                {
                    var bmp = new BitmapImage();
                    using (var ms = new MemoryStream(settings.LogoData))
                    {
                        bmp.BeginInit();
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.StreamSource = ms;
                        bmp.EndInit();
                    }
                    var img = new Image
                    {
                        Source = bmp,
                        Width = 80,
                        Height = 80,
                        Stretch = Stretch.Uniform,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    Grid.SetColumn(img, 0);
                    headerGrid.Children.Add(img);
                }
                catch { }
            }
            else
            {
                var iconBorder = new Border
                {
                    Width = 72,
                    Height = 72,
                    Background = new SolidColorBrush(Color.FromRgb(240, 249, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(14, 165, 233)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(10),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Child = new TextBlock
                    {
                        Text = "🔬",
                        FontSize = 36,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Grid.SetColumn(iconBorder, 0);
                headerGrid.Children.Add(iconBorder);
            }

            // Names & Slogan
            var nameStack = new StackPanel
            {
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            nameStack.Children.Add(new TextBlock
            {
                Text = settings.LabNameAr ?? "مختبر التحليلات الطبية التخصصي",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                FontFamily = fontFamily
            });
            nameStack.Children.Add(new TextBlock
            {
                Text = settings.LabNameEn ?? "Specialized Medical Laboratory",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(14, 165, 233)),
                FontFamily = fontFamily,
                Margin = new Thickness(0, 2, 0, 0)
            });
            if (!string.IsNullOrWhiteSpace(settings.ReportHeaderText))
            {
                nameStack.Children.Add(new TextBlock
                {
                    Text = settings.ReportHeaderText,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    FontFamily = fontFamily,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }
            Grid.SetColumn(nameStack, 1);
            headerGrid.Children.Add(nameStack);

            // Right Barcode & Info
            var rightStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Order Barcode
            var barcodeImg = GenerateBarcodeImage($"ORD{order.OrderId:D6}", 150, 42);
            if (barcodeImg != null)
            {
                rightStack.Children.Add(barcodeImg);
            }

            rightStack.Children.Add(new TextBlock
            {
                Text = $"هاتف: {settings.Phones ?? "07700000000"}",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontFamily = fontFamily,
                Margin = new Thickness(0, 4, 0, 0)
            });
            rightStack.Children.Add(new TextBlock
            {
                Text = settings.Address ?? "العراق - بغداد",
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontFamily = fontFamily
            });

            Grid.SetColumn(rightStack, 2);
            headerGrid.Children.Add(rightStack);
            rootStack.Children.Add(headerGrid);

            // Separator Bar
            var titleBanner = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)), // Slate 900
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 12)
            };
            var titleGrid = new Grid();
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleTxt = new TextBlock
            {
                Text = "تقرير الفحوصات الطبية والمخبرية  |  MEDICAL LABORATORY REPORT",
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                FontFamily = fontFamily,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleTxt, 0);
            titleGrid.Children.Add(titleTxt);

            var statusBadge = new Border
            {
                Background = order.IsAllValidated ? new SolidColorBrush(Color.FromRgb(16, 185, 129)) : new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2, 8, 2),
                Child = new TextBlock
                {
                    Text = order.IsAllValidated ? "معتمد (Validated ✓)" : "تقرير أولي (Preliminary)",
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White
                }
            };
            Grid.SetColumn(statusBadge, 1);
            titleGrid.Children.Add(statusBadge);
            titleBanner.Child = titleGrid;
            rootStack.Children.Add(titleBanner);

            // ═══ 2. PATIENT INFORMATION CARD ═════════════════════════════════
            var patientBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 14)
            };

            var pGrid = new Grid();
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            pGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            pGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            pGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Row 0
            pGrid.Children.Add(CreateMetaItem("المريض (Patient):", order.FullNameAr, 0, 0, fontFamily, true));
            pGrid.Children.Add(CreateMetaItem("رقم الملف (P.ID):", order.Mrn.ToString(), 1, 0, fontFamily, true));
            pGrid.Children.Add(CreateMetaItem("رقم الطلب (Order #):", $"#{order.OrderId}", 2, 0, fontFamily, true));

            // Row 1
            pGrid.Children.Add(CreateMetaItem("العمر / الجنس:", $"{order.AgeDisplay} / {order.GenderDisplay}", 0, 1, fontFamily));
            pGrid.Children.Add(CreateMetaItem("تاريخ الطلب:", order.OrderDatetime.ToString("dd/MM/yyyy HH:mm"), 1, 1, fontFamily));
            pGrid.Children.Add(CreateMetaItem("الطبيب المحول:", string.IsNullOrWhiteSpace(order.ReferringDoctor) ? "غير محدد" : order.ReferringDoctor, 2, 1, fontFamily));

            patientBox.Child = pGrid;
            rootStack.Children.Add(patientBox);

            // ═══ 3. RESULTS TABLE ═════════════════════════════════════════════
            // Group results by Test Name
            var groups = order.Results.GroupBy(r => new { r.TestId, r.DisplayTestName }).ToList();

            foreach (var grp in groups)
            {
                // Test Header Banner
                var testHeader = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(1, 1, 1, 0),
                    CornerRadius = new CornerRadius(4, 4, 0, 0),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 6, 0, 0)
                };
                var thGrid = new Grid();
                thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                thGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var thText = new TextBlock
                {
                    Text = $"🔬 {grp.Key.DisplayTestName}",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    FontFamily = fontFamily
                };
                Grid.SetColumn(thText, 0);
                thGrid.Children.Add(thText);

                var sampleSpec = grp.First();
                var tubeText = new TextBlock
                {
                    Text = $"العينة: {sampleSpec.SpecimenTypeName} (باركود: {sampleSpec.AccessionNo})",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    FontFamily = fontFamily
                };
                Grid.SetColumn(tubeText, 1);
                thGrid.Children.Add(tubeText);

                testHeader.Child = thGrid;
                rootStack.Children.Add(testHeader);

                // Table Grid
                var tableBorder = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 10)
                };

                var tableGrid = new Grid();
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) }); // Investigation
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) }); // Result
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.9, GridUnitType.Star) }); // Flag
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.1, GridUnitType.Star) }); // Unit
                tableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.8, GridUnitType.Star) }); // Ref Range

                // Columns Header Row
                tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var colHeaders = new[] { "المكون (Component)", "النتيجة (Result)", "التنبيه (Flag)", "الوحدة (Unit)", "المعدل الطبيعي (Reference)" };
                for (int c = 0; c < colHeaders.Length; c++)
                {
                    var chBorder = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                        BorderThickness = new Thickness(0, 0, (c < 4 ? 1 : 0), 1),
                        Padding = new Thickness(8, 6, 8, 6)
                    };
                    var chText = new TextBlock
                    {
                        Text = colHeaders[c],
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                        FontFamily = fontFamily
                    };
                    chBorder.Child = chText;
                    Grid.SetRow(chBorder, 0);
                    Grid.SetColumn(chBorder, c);
                    tableGrid.Children.Add(chBorder);
                }

                // Data Rows
                int rowIndex = 1;
                foreach (var item in grp)
                {
                    tableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    bool isEven = rowIndex % 2 == 0;
                    var rowBg = isEven ? new SolidColorBrush(Color.FromRgb(250, 250, 250)) : Brushes.White;

                    // Highlight result color
                    Brush resultBrush = Brushes.Black;
                    FontWeight resultWeight = FontWeights.Normal;
                    if (item.Flag == "H" || item.Flag == "HH")
                    {
                        resultBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38)); // Red
                        resultWeight = FontWeights.Bold;
                    }
                    else if (item.Flag == "L" || item.Flag == "LL")
                    {
                        resultBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235)); // Blue
                        resultWeight = FontWeights.Bold;
                    }
                    else if (!string.IsNullOrWhiteSpace(item.ValueText))
                    {
                        resultWeight = FontWeights.Bold;
                    }

                    // Col 0: Component Name
                    tableGrid.Children.Add(CreateTableCell(item.DisplayComponentName, 0, rowIndex, rowBg, fontFamily, FontWeights.Normal, Brushes.Black));

                    // Col 1: Result Value
                    tableGrid.Children.Add(CreateTableCell(string.IsNullOrWhiteSpace(item.ValueText) ? "—" : item.ValueText, 1, rowIndex, rowBg, fontFamily, resultWeight, resultBrush));

                    // Col 2: Flag
                    string flagText = string.IsNullOrWhiteSpace(item.Flag) ? "" : item.Flag;
                    tableGrid.Children.Add(CreateTableCell(flagText, 2, rowIndex, rowBg, fontFamily, FontWeights.Bold, resultBrush));

                    // Col 3: Unit
                    tableGrid.Children.Add(CreateTableCell(item.Unit ?? "—", 3, rowIndex, rowBg, fontFamily, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(100, 116, 139))));

                    // Col 4: Reference Range
                    tableGrid.Children.Add(CreateTableCell(item.ReferenceRangeText, 4, rowIndex, rowBg, fontFamily, FontWeights.Normal, new SolidColorBrush(Color.FromRgb(71, 85, 105)), true));

                    rowIndex++;
                }

                tableBorder.Child = tableGrid;
                rootStack.Children.Add(tableBorder);
            }

            // ═══ 4. FOOTER & SIGNATURE ═════════════════════════════════════════
            var footerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 12, 0, 0),
                Margin = new Thickness(0, 14, 0, 0)
            };

            var footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) }); // Notice
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }); // Signature

            // Notes
            var noteStack = new StackPanel();
            noteStack.Children.Add(new TextBlock
            {
                Text = "ملاحظات طبية سريرية (Clinical Notes):",
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                FontFamily = fontFamily
            });
            noteStack.Children.Add(new TextBlock
            {
                Text = settings.ReportFooterText ?? "نتائج الفحوصات تعتمد سريرياً بالربط مع الفحص السريري والتاريخ المرضي للمريض.",
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontFamily = fontFamily,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            });
            noteStack.Children.Add(new TextBlock
            {
                Text = $"تاريخ ووقت الطباعة: {DateTime.Now:dd/MM/yyyy HH:mm} | المشغل: LIS System",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                FontFamily = fontFamily,
                Margin = new Thickness(0, 8, 0, 0)
            });
            Grid.SetColumn(noteStack, 0);
            footerGrid.Children.Add(noteStack);

            // Signature & Director Box
            var sigStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            sigStack.Children.Add(new TextBlock
            {
                Text = settings.ManagerTitle ?? "أخصائي التحليلات المرضية",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontFamily = fontFamily
            });
            sigStack.Children.Add(new TextBlock
            {
                Text = settings.ManagerName ?? "د. أحمد علي",
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                HorizontalAlignment = HorizontalAlignment.Right,
                FontFamily = fontFamily,
                Margin = new Thickness(0, 2, 0, 4)
            });

            // Signature image if available
            if (settings.SignatureData != null && settings.SignatureData.Length > 0)
            {
                try
                {
                    var sigBmp = new BitmapImage();
                    using (var ms = new MemoryStream(settings.SignatureData))
                    {
                        sigBmp.BeginInit();
                        sigBmp.CacheOption = BitmapCacheOption.OnLoad;
                        sigBmp.StreamSource = ms;
                        sigBmp.EndInit();
                    }
                    var sigImg = new Image
                    {
                        Source = sigBmp,
                        Width = 120,
                        Height = 50,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Right
                    };
                    sigStack.Children.Add(sigImg);
                }
                catch { }
            }
            else
            {
                sigStack.Children.Add(new Border
                {
                    Width = 110,
                    Height = 35,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Child = new TextBlock
                    {
                        Text = "الختم والتوقيع",
                        FontSize = 10,
                        Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                });
            }

            Grid.SetColumn(sigStack, 1);
            footerGrid.Children.Add(sigStack);
            footerBorder.Child = footerGrid;
            rootStack.Children.Add(footerBorder);

            return page;
        }

        private static Border CreateMetaItem(string label, string val, int col, int row, FontFamily ff, bool isBold = false)
        {
            var b = new Border { Margin = new Thickness(0, 2, 8, 2) };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                FontFamily = ff,
                Margin = new Thickness(0, 0, 6, 0)
            });
            sp.Children.Add(new TextBlock
            {
                Text = val,
                FontSize = 11.5,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                FontFamily = ff
            });
            b.Child = sp;
            Grid.SetColumn(b, col);
            Grid.SetRow(b, row);
            return b;
        }

        private static Border CreateTableCell(string text, int col, int row, Brush bg, FontFamily ff, FontWeight fw, Brush fg, bool isLastCol = false)
        {
            var b = new Border
            {
                Background = bg,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, (isLastCol ? 0 : 1), 1),
                Padding = new Thickness(8, 5, 8, 5)
            };
            var tb = new TextBlock
            {
                Text = text,
                FontSize = 11.5,
                FontFamily = ff,
                FontWeight = fw,
                Foreground = fg,
                VerticalAlignment = VerticalAlignment.Center
            };
            b.Child = tb;
            Grid.SetColumn(b, col);
            Grid.SetRow(b, row);
            return b;
        }

        private static Image? GenerateBarcodeImage(string text, int width, int height)
        {
            try
            {
                var writer = new BarcodeWriterPixelData
                {
                    Format = BarcodeFormat.CODE_128,
                    Options = new EncodingOptions
                    {
                        Height = height,
                        Width = width,
                        Margin = 0,
                        PureBarcode = false
                    }
                };

                var pixelData = writer.Write(text);
                var bitmap = new WriteableBitmap(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgra32, null);
                bitmap.WritePixels(new Int32Rect(0, 0, pixelData.Width, pixelData.Height), pixelData.Pixels, pixelData.Width * 4, 0);

                return new Image
                {
                    Source = bitmap,
                    Width = width,
                    Height = height,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
            }
            catch
            {
                return null;
            }
        }

        public static void PrintReport(OrderResultsDto order, LabSettingsDto settings, string? targetPrinter = null)
        {
            var printDialog = new PrintDialog();

            if (!string.IsNullOrWhiteSpace(targetPrinter))
            {
                try
                {
                    var printServer = new LocalPrintServer();
                    var pq = printServer.GetPrintQueue(targetPrinter);
                    if (pq != null)
                    {
                        printDialog.PrintQueue = pq;
                    }
                }
                catch { }
            }

            var visual = CreateReportVisual(order, settings);

            // Measure and Arrange A4
            visual.Measure(new Size(A4_WIDTH_DIP, double.PositiveInfinity));
            visual.Arrange(new Rect(new Point(0, 0), new Size(A4_WIDTH_DIP, visual.DesiredSize.Height)));
            visual.UpdateLayout();

            printDialog.PrintVisual(visual, $"تقرير المختبر - {order.FullNameAr} (#{order.OrderId})");
        }
    }
}
