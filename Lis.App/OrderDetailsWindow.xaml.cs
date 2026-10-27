using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Lis.App.Services;
using Lis.Core.Services;

namespace Lis.App
{
    public class OrderTestRowView
    {
        public long OrderTestId { get; set; }
        public long TestId { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public string DisplayName => !string.IsNullOrWhiteSpace(NameAr) ? $"{NameAr} ({NameEn})" : NameEn;
        public decimal Price { get; set; }
        public string FormattedPrice => $"{Price:#,##0} IQD";
        public string Status { get; set; } = string.Empty;
        public string AccessionNo { get; set; } = string.Empty;
        public string SpecimenTypeName { get; set; } = string.Empty;
        public string TubeColor { get; set; } = "#718096";
        public long SpecimenId { get; set; }

        public System.Windows.Media.Color TubeColorBrush
        {
            get
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(TubeColor))
                    {
                        return (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(TubeColor);
                    }
                }
                catch { }
                return System.Windows.Media.Colors.Gray;
            }
        }
    }

    public class TestPickerItem
    {
        public long Id { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string NameAr { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string SpecimenTypeName { get; set; } = string.Empty;
        public string DisplayText => $"{TestCode} - {(!string.IsNullOrWhiteSpace(NameAr) ? NameAr : NameEn)} ({Price:#,##0} IQD)";
    }

    public partial class OrderDetailsWindow : Window
    {
        private readonly IOrderService _orderService;
        private readonly IBillingService _billingService;
        private readonly ILabSettingsService? _labSettingsService;
        private readonly ITestCatalogService? _testCatalogService;
        private readonly UserSession? _currentUser;
        private readonly long _orderId;
        private OrderDetailsDto? _details;
        private List<TestPickerItem> _allCatalogTests = new();

        public OrderDetailsWindow(
            long orderId,
            IOrderService orderService,
            IBillingService billingService,
            ILabSettingsService? labSettingsService = null,
            UserSession? currentUser = null,
            ITestCatalogService? testCatalogService = null)
        {
            InitializeComponent();
            _orderId = orderId;
            _orderService = orderService;
            _billingService = billingService;
            _labSettingsService = labSettingsService;
            _currentUser = currentUser;
            _testCatalogService = testCatalogService;

            Loaded += async (s, e) =>
            {
                await LoadCatalogTestsAsync();
                await LoadDetailsAsync();
            };
        }

        private async Task LoadCatalogTestsAsync()
        {
            try
            {
                if (_testCatalogService == null) return;
                var tests = await _testCatalogService.GetAllTestsAsync();
                _allCatalogTests = tests
                    .Where(t => t.Active)
                    .Select(t => new TestPickerItem
                    {
                        Id = t.Id,
                        TestCode = t.TestCode,
                        NameEn = t.NameEn,
                        NameAr = t.NameAr ?? string.Empty,
                        Price = t.Price,
                        SpecimenTypeName = t.SpecimenTypeName ?? string.Empty
                    })
                    .OrderBy(t => t.TestCode)
                    .ToList();

                CmbAvailableTests.ItemsSource = _allCatalogTests;
            }
            catch { /* non-critical */ }
        }

        private async Task LoadDetailsAsync()
        {
            try
            {
                _details = await _orderService.GetOrderByIdAsync(_orderId);

                if (_details == null)
                {
                    LisMessageBox.Show("لم يتم العثور على الطلب المطلوب.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                    Close();
                    return;
                }

                TxtOrderTitle.Text = $"طلب رقم #{_details.Order.Id} (زيارة رقم {_details.Order.VisitNo})";
                TxtPatientInfo.Text = $"المريض: {_details.Patient.FullNameAr} | P.ID: {_details.Patient.Mrn} | الجنس: {(_details.Patient.Sex == "M" ? "ذكر" : "أنثى")}";
                TxtOrderStatus.Text = _details.Order.Status;

                if (_details.Invoice != null)
                {
                    TxtTotal.Text = $"{_details.Invoice.Total:#,##0} IQD";
                    TxtDiscount.Text = $"{_details.Invoice.Discount:#,##0} IQD";
                    TxtNet.Text = $"{_details.Invoice.Net:#,##0} IQD";
                    TxtPaid.Text = $"{_details.Invoice.Paid:#,##0} IQD";
                    TxtBalance.Text = $"{_details.Invoice.Balance:#,##0} IQD";
                    BtnPay.IsEnabled = _details.Invoice.Balance > 0;
                }

                // Flatten ordered tests for grid display
                var testRows = new List<OrderTestRowView>();
                foreach (var s in _details.Specimens)
                {
                    foreach (var t in s.Tests)
                    {
                        testRows.Add(new OrderTestRowView
                        {
                            OrderTestId = t.OrderTestId,
                            TestId = t.TestId,
                            TestCode = t.TestCode,
                            NameEn = t.NameEn,
                            NameAr = t.NameAr ?? string.Empty,
                            Price = t.Price,
                            Status = t.Status,
                            AccessionNo = s.Specimen.AccessionNo,
                            SpecimenTypeName = s.SpecimenTypeName,
                            TubeColor = s.Color ?? "#718096",
                            SpecimenId = s.Specimen.Id
                        });
                    }
                }

                GridOrderedTests.ItemsSource = testRows;
                TxtTestsCount.Text = $"{testRows.Count} فحص";
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء تحميل تفاصيل الطلب: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnAddTestToOrder_Click(object sender, RoutedEventArgs e)
        {
            if (CmbAvailableTests.SelectedItem is not TestPickerItem selected)
            {
                LisMessageBox.Show("يرجى اختيار فحص من القائمة المنسدلة أولاً لإضافته إلى الطلب.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            // Check if test is already in order
            if (_details != null && _details.Specimens.Any(s => s.Tests.Any(t => t.TestId == selected.Id)))
            {
                var proceed = LisMessageBox.Show($"فحص ({selected.DisplayText}) موجود بالفعل في هذا الطلب.\nهل ترغب بإضافته مرة أخرى؟", "تأكيد الإضافة", MessageBoxButton.YesNo, MessageBoxImage.Question, this);
                if (proceed != MessageBoxResult.Yes) return;
            }

            try
            {
                await _orderService.AddTestToOrderAsync(_orderId, selected.Id, currentUserId: _currentUser?.Id);
                CmbAvailableTests.SelectedItem = null;
                await LoadDetailsAsync();
                LisMessageBox.Show($"تمت إضافة فحص ({selected.DisplayText}) إلى الطلب وتحديث الفاتورة بنجاح.", "تمت الإضافة", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء إضافة الفحص: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnBrowseCatalog_Click(object sender, RoutedEventArgs e)
        {
            if (_testCatalogService == null)
            {
                LisMessageBox.Show("خدمة كتالوج الفحوصات غير متوفرة.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            try
            {
                var allTests = await _testCatalogService.GetAllTestsAsync();
                var activeTests = allTests.Where(t => t.Active);
                var dialog = new SelectTestsDialog(activeTests) { Owner = this };
                if (dialog.ShowDialog() == true && dialog.SelectedTestIds.Any())
                {
                    int addedCount = 0;
                    foreach (var testId in dialog.SelectedTestIds)
                    {
                        await _orderService.AddTestToOrderAsync(_orderId, testId, currentUserId: _currentUser?.Id);
                        addedCount++;
                    }
                    await LoadDetailsAsync();
                    LisMessageBox.Show($"تمت إضافة ({addedCount}) فحص بنجاح وتحديث حسابات الفاتورة.", "تمت الإضافة", MessageBoxButton.OK, MessageBoxImage.Information, this);
                }
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء استعراض وإضافة الفحوصات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnRemoveTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is OrderTestRowView item)
            {
                var confirm = LisMessageBox.Show(
                    $"هل أنت متأكد من حذف فحص ({item.DisplayName}) من هذا الطلب؟\nسيتم حذف الفحص وتخفيض قيمة الفاتورة بمقدار ({item.FormattedPrice}) تلقائياً.",
                    "تأكيد حذف الفحص",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    this);

                if (confirm != MessageBoxResult.Yes) return;

                try
                {
                    await _orderService.RemoveTestFromOrderAsync(_orderId, item.OrderTestId, _currentUser?.Id);
                    await LoadDetailsAsync();
                    LisMessageBox.Show($"تم حذف فحص ({item.DisplayName}) وتعديل الفاتورة بنجاح.", "تم الحذف", MessageBoxButton.OK, MessageBoxImage.Information, this);
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"خطأ أثناء حذف الفحص: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private async void BtnPay_Click(object sender, RoutedEventArgs e)
        {
            if (_details?.Invoice == null || _details.Invoice.Balance <= 0)
            {
                LisMessageBox.Show("الفاتورة مسددة بالكامل بالفعل.", "ملاحظة", MessageBoxButton.OK, MessageBoxImage.Information, this);
                return;
            }

            try
            {
                var payDialog = new PaymentDialog(
                    _details.Invoice.InvoiceNo,
                    _details.Patient.FullNameAr,
                    _details.Invoice.Balance,
                    _currentUser?.FullName ?? "المحاسب (الكاشير)")
                {
                    Owner = this
                };

                if (payDialog.ShowDialog() == true)
                {
                    decimal payAmount = payDialog.PaidAmount;
                    string payMethod = payDialog.PaymentMethod;

                    await _billingService.RecordPaymentAsync(_details.Invoice.Id, payAmount, payMethod, _currentUser?.Id);
                    await LoadDetailsAsync();

                    // Print 80mm thermal cashier receipt immediately
                    await PrintCurrentOrderReceiptAsync(payAmount, payMethod);

                    string methodText = payMethod == "Card" ? "الكارت 💳" : "النقد 💵";
                    LisMessageBox.Show(
                        $"تم تسديد المبلغ ({payAmount:#,##0} IQD) بنجاح عبر {methodText}.\nتم إرسال وصل الكاشير الحراري (80 مم) للطباعة مباشرة.",
                        "تم الدفع والطباعة",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information,
                        this);
                }
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في تسجيل الدفعة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnPrintReceipt_Click(object sender, RoutedEventArgs e)
        {
            await PrintCurrentOrderReceiptAsync();
        }

        private async Task PrintCurrentOrderReceiptAsync(decimal? paidJustNow = null, string? paymentMethod = null)
        {
            if (_details == null || _details.Invoice == null) return;

            try
            {
                LabSettingsDto settings = _labSettingsService != null ? await _labSettingsService.GetSettingsAsync() : new LabSettingsDto();
                var printData = new ReceiptPrintData
                {
                    LabNameAr = settings.LabNameAr,
                    LabNameEn = settings.LabNameEn,
                    Address = settings.Address,
                    Phones = settings.Phones,
                    InvoiceNo = _details.Invoice.InvoiceNo,
                    OrderId = _details.Order.Id,
                    OrderDate = _details.Order.OrderDatetime.Kind == DateTimeKind.Utc ? _details.Order.OrderDatetime.ToLocalTime() : _details.Order.OrderDatetime,
                    PatientNameAr = _details.Patient.FullNameAr,
                    Mrn = _details.Patient.Mrn,
                    PatientPhone = _details.Patient.Phone,
                    Gender = _details.Patient.Sex == "F" ? "أنثى" : "ذكر",
                    CashierName = _currentUser?.FullName ?? "المحاسب (الكاشير)",
                    PaymentMethod = paymentMethod ?? "Cash",
                    TotalAmount = _details.Invoice.Total,
                    Discount = _details.Invoice.Discount,
                    NetAmount = _details.Invoice.Net,
                    PaidAmount = paidJustNow ?? _details.Invoice.Paid,
                    BalanceDue = _details.Invoice.Balance,
                    Items = _details.Specimens.SelectMany(s => s.Tests).Select(t => new ReceiptItemData
                    {
                        Code = t.TestCode,
                        Name = !string.IsNullOrWhiteSpace(t.NameAr) ? t.NameAr : t.NameEn,
                        Price = t.Price
                    }).ToList()
                };

                ReceiptRenderer.PrintReceipt(printData, settings.ReceiptPrinter);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"تعذر إرسال وصل الكاشير للطباعة: {ex.Message}", "تنبيه الطباعة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            }
        }

        private async void BtnCancelOrder_Click(object sender, RoutedEventArgs e)
        {
            var confirm = LisMessageBox.Show("هل أنت متأكد من إلغاء هذا الطلب بالكامل وجميع عينات وفواتير الفحص؟", "تأكيد الإلغاء", MessageBoxButton.YesNo, MessageBoxImage.Warning, this);

            if (confirm == MessageBoxResult.Yes)
            {
                try
                {
                    await _orderService.CancelOrderAsync(_orderId, "إلغاء يدوي من المستخدم");
                    await LoadDetailsAsync();
                    LisMessageBox.Show("تم إلغاء الطلب بنجاح.", "نجاح", MessageBoxButton.OK, MessageBoxImage.Information, this);
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"خطأ: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
