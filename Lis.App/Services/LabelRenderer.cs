using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lis.Core.Services;
using ZXing;
using ZXing.Common;

namespace Lis.App.Services
{
    /// <summary>
    /// Renders and prints standard clinical specimen barcode tube labels
    /// with exact physical dimensions: 5.0 cm (Width) x 2.5 cm (Height) (50mm x 25mm).
    /// Supports real-time horizontal calibration offset to shift print left or right.
    /// </summary>
    public static class LabelRenderer
    {
        // Physical dimensions at 96 DPI (WPF Device Independent Pixels)
        // 5.0 cm = (50mm / 25.4mm/in) * 96 DIP = ~188.976 DIP
        // 2.5 cm = (25mm / 25.4mm/in) * 96 DIP = ~94.488 DIP
        public const double LabelWidthDip = 50.0 / 25.4 * 96.0;   // 188.976 DIP (exact 5.0 cm)
        public const double LabelHeightDip = 25.0 / 25.4 * 96.0;  // 94.488 DIP (exact 2.5 cm)

        // Native 203.2 DPI resolution for standard thermal label printers (8 dots/mm)
        // 50mm * 8 dots/mm = 400 dots
        // 25mm * 8 dots/mm = 200 dots
        public const int LabelWidthPx = 400;
        public const int LabelHeightPx = 200;
        public const double LabelDpi = 203.2;

        /// <summary>
        /// Default label printer configured in lab settings (e.g. HPRT SL32, Zebra GK420t)
        /// </summary>
        public static string? DefaultLabelPrinter { get; set; }

        /// <summary>
        /// Horizontal offset in millimeters. Positive shifts right, negative shifts left.
        /// Default is +10.0mm to shift printing into the physical sticker area and fix left clipping.
        /// </summary>
        public static double BarcodeOffsetMm { get; set; } = 15.0;

        public static FrameworkElement CreateLabelElement(SpecimenLabelData data)
        {
            double shiftDip = (BarcodeOffsetMm / 25.4) * 96.0;

            // Content block: Width ~158 DIP (~41.8mm), Height ~88 DIP (~23.3mm)
            var contentGrid = new Grid
            {
                Width = 158.0,
                Height = 88.0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(Math.Max(0.0, shiftDip), 3, 0, 3)
            };

            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(45, GridUnitType.Pixel) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(19, GridUnitType.Pixel) });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(21, GridUnitType.Pixel) });

            // 1. Barcode Image (Code 128)
            var barcodeBmp = GenerateBarcodeBitmap(data.AccessionNo, 316, 90);
            if (barcodeBmp != null)
            {
                var img = new Image
                {
                    Source = barcodeBmp,
                    Width = 154,
                    Height = 44,
                    Stretch = Stretch.Fill,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                Grid.SetRow(img, 0);
                contentGrid.Children.Add(img);
            }

            // 2. Line 1: [Accession - DOB] (Left)  |  [P.ID :MRN] (Right)
            var line1Grid = new Grid();
            line1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line1Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var txtLine1Left = new TextBlock
            {
                Text = data.Line1Left,
                FontFamily = new FontFamily("Simplified Arabic"),
                FontWeight = FontWeights.Bold,
                FontSize = 9.8,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            var txtLine1Right = new TextBlock
            {
                Text = data.Line1Right,
                FontFamily = new FontFamily("Simplified Arabic"),
                FontWeight = FontWeights.Bold,
                FontSize = 9.8,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(txtLine1Left, 0);
            Grid.SetColumn(txtLine1Right, 1);
            line1Grid.Children.Add(txtLine1Left);
            line1Grid.Children.Add(txtLine1Right);
            Grid.SetRow(line1Grid, 1);
            contentGrid.Children.Add(line1Grid);

            // 3. Line 2: [Patient Name (Arabic)] (Left)  |  [Specimen Type] (Right)
            var line2Grid = new Grid();
            line2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line2Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var txtLine2Left = new TextBlock
            {
                Text = data.Line2Left,
                FontFamily = new FontFamily("Simplified Arabic"),
                FontWeight = FontWeights.Bold,
                FontSize = 11.5,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            var txtLine2Right = new TextBlock
            {
                Text = data.Line2Right,
                FontFamily = new FontFamily("Simplified Arabic"),
                FontWeight = FontWeights.Normal,
                FontSize = 9.2,
                Foreground = Brushes.Black,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(txtLine2Left, 0);
            Grid.SetColumn(txtLine2Right, 1);
            line2Grid.Children.Add(txtLine2Left);
            line2Grid.Children.Add(txtLine2Right);
            Grid.SetRow(line2Grid, 2);
            contentGrid.Children.Add(line2Grid);

            double containerWidth = Math.Max(LabelWidthDip, LabelWidthDip + shiftDip);
            var container = new Border
            {
                Width = containerWidth,
                Height = LabelHeightDip,
                Background = Brushes.White,
                Child = contentGrid
            };

            container.Measure(new Size(containerWidth, LabelHeightDip));
            container.Arrange(new Rect(0, 0, containerWidth, LabelHeightDip));
            container.UpdateLayout();

            return container;
        }

        public static RenderTargetBitmap RenderLabel(SpecimenLabelData data)
        {
            var element = CreateLabelElement(data);
            int widthPx = (int)Math.Round(element.Width * (LabelDpi / 96.0));
            int heightPx = (int)Math.Round(element.Height * (LabelDpi / 96.0));

            var rtb = new RenderTargetBitmap(widthPx, heightPx, LabelDpi, LabelDpi, PixelFormats.Pbgra32);
            rtb.Render(element);
            rtb.Freeze();
            return rtb;
        }

        private static BitmapSource? GenerateBarcodeBitmap(string accessionNo, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(accessionNo)) return null;

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

            var pixelData = writer.Write(accessionNo);
            var bitmap = BitmapSource.Create(
                pixelData.Width,
                pixelData.Height,
                LabelDpi,
                LabelDpi,
                PixelFormats.Bgr32,
                null,
                pixelData.Pixels,
                pixelData.Width * 4);

            bitmap.Freeze();
            return bitmap;
        }

        public static PrintQueue? ResolveLabelPrintQueue(string? printerName)
        {
            try
            {
                using var printServer = new LocalPrintServer();
                var queues = printServer.GetPrintQueues().Cast<PrintQueue>().ToList();

                string? target = !string.IsNullOrWhiteSpace(printerName) ? printerName.Trim() : DefaultLabelPrinter?.Trim();

                if (!string.IsNullOrWhiteSpace(target))
                {
                    // 1. EXACT match first (Name or FullName)
                    var exact = queues.FirstOrDefault(q =>
                        string.Equals(q.Name, target, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(q.FullName, target, StringComparison.OrdinalIgnoreCase));
                    if (exact != null) return exact;

                    // 2. StartsWith match (e.g. target is "HPRT SL32BT" -> matches "HPRT SL32BT (Copy 1)")
                    var startsWith = queues.FirstOrDefault(q =>
                        q.Name.StartsWith(target, StringComparison.OrdinalIgnoreCase) ||
                        target.StartsWith(q.Name, StringComparison.OrdinalIgnoreCase));
                    if (startsWith != null) return startsWith;

                    // 3. Contains match
                    var contains = queues.FirstOrDefault(q =>
                        q.Name.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        target.IndexOf(q.Name, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (contains != null) return contains;

                    // 4. If target mentions "SL32" or "HPRT", prefer active (Copy 1) copy
                    if (target.IndexOf("SL32", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        target.IndexOf("HPRT", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        var sl32Copy = queues.FirstOrDefault(q => q.Name.IndexOf("SL32", StringComparison.OrdinalIgnoreCase) >= 0 && q.Name.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0)
                                    ?? queues.FirstOrDefault(q => q.Name.IndexOf("SL32", StringComparison.OrdinalIgnoreCase) >= 0)
                                    ?? queues.FirstOrDefault(q => q.Name.IndexOf("HPRT", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (sl32Copy != null) return sl32Copy;
                    }
                }

                // Fallback: auto-detect any HPRT or SL32 or Barcode printer in installed queues
                var autoDetected = queues.FirstOrDefault(q => q.Name.IndexOf("SL32", StringComparison.OrdinalIgnoreCase) >= 0 && q.Name.IndexOf("Copy", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? queues.FirstOrDefault(q => q.Name.IndexOf("SL32", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? queues.FirstOrDefault(q => q.Name.IndexOf("HPRT", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? queues.FirstOrDefault(q => q.Name.IndexOf("Barcode", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? queues.FirstOrDefault(q => q.Name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0);
                if (autoDetected != null) return autoDetected;

                return printServer.DefaultPrintQueue;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LabelRenderer] Error resolving label print queue: {ex.Message}");
                return null;
            }
        }

        public static string PrintLabel(SpecimenLabelData data, string? printerName = null)
        {
            var printDialog = new PrintDialog();
            var queue = ResolveLabelPrintQueue(printerName);

            if (queue != null)
            {
                printDialog.PrintQueue = queue;
            }
            else
            {
                throw new InvalidOperationException("لم يتم العثور على طابعة باركود مثبتة أو طابعة افتراضية في النظام. يرجى التأكد من تشغيل طابعة الملصقات واختيارها من إعدادات المختبر.");
            }

            var container = CreateLabelElement(data);
            printDialog.PrintVisual(container, $"TubeLabel_{data.AccessionNo}");

            return printDialog.PrintQueue.Name;
        }

        public static void SaveLabelToPng(SpecimenLabelData data, string filePath)
        {
            var rtb = RenderLabel(data);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(filePath);
            encoder.Save(fs);
        }
    }
}
