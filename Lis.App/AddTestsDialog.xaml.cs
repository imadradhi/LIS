using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Dapper;
using Lis.App.Services;
using Lis.Core.Data;
using Lis.Core.Models;
using Lis.Core.Services;

namespace Lis.App
{
    /// <summary>
    /// نافذة منبثقة لاختيار التحاليل وإتمام عملية الدفع وحفظ الطلب.
    /// </summary>
    public partial class AddTestsDialog : Window
    {
        // ── Dependencies ────────────────────────────────────────────────────────
        private readonly Patient               _patient;
        private readonly string?               _referringDoctor;
        private readonly string?               _clinicalNotes;
        private readonly IOrderService         _orderService;
        private readonly IBillingService       _billingService;
        private readonly ILabSettingsService   _settingsService;
        private readonly IDbConnectionFactory  _connectionFactory;
        private readonly UserSession?          _currentUser;

        // ── State ────────────────────────────────────────────────────────────────
        private readonly ObservableCollection<TestSelectableItem> _allTests = new();
        private bool _isInitialized = false;

        // ── Constructor ──────────────────────────────────────────────────────────
        public AddTestsDialog(
            Patient              patient,
            string?              referringDoctor,
            string?              clinicalNotes,
            IOrderService        orderService,
            IBillingService      billingService,
            ILabSettingsService  settingsService,
            IDbConnectionFactory connectionFactory,
            UserSession?         currentUser)
        {
            InitializeComponent();
            _isInitialized = true;

            _patient           = patient;
            _referringDoctor   = referringDoctor;
            _clinicalNotes     = clinicalNotes;
            _orderService      = orderService;
            _billingService    = billingService;
            _settingsService   = settingsService;
            _currentUser       = currentUser;
            _connectionFactory = connectionFactory;

            Loaded += OnLoaded;
        }

        // ── Loaded ───────────────────────────────────────────────────────────────
        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            TxtDialogTitle.Text     = $"إضافة التحاليل — {_patient.FullNameAr}";
            TxtPatientSubtitle.Text = $"P.ID: {_patient.Mrn} | {(_patient.Sex == "F" ? "أنثى" : "ذكر")} | 📱 {_patient.Phone ?? "—"}";

            await LoadTestsAsync();
            RefreshSummary();
        }

        // ── Load Tests from DB ───────────────────────────────────────────────────
        private async Task LoadTestsAsync()
        {
            try
            {
                using var conn = _connectionFactory.CreateConnection();
                const string sql = @"
                    SELECT t.id, t.test_code AS TestCode,
                           COALESCE(t.name_ar, t.name_en) AS NameAr, t.name_en AS NameEn,
                           t.price,
                           t.specimen_type_id AS SpecimenTypeId,
                           COALESCE(st.name_en, 'N/A') AS SpecimenTypeName
                    FROM tests t
                    LEFT JOIN specimen_types st ON st.id = t.specimen_type_id
                    WHERE t.active = TRUE
                    ORDER BY t.sort_order ASC, t.id ASC;";

                var rows = await conn.QueryAsync<TestCatalogDto>(sql);
                _allTests.Clear();
                foreach (var r in rows)
                {
                    string nameAr   = r.NameAr ?? string.Empty;
                    string nameEn   = r.NameEn ?? string.Empty;
                    string testCode = r.TestCode ?? string.Empty;
                    string specName = !string.IsNullOrWhiteSpace(r.SpecimenTypeName) ? r.SpecimenTypeName : "N/A";
                    decimal price   = r.Price;
                    long? specId    = r.SpecimenTypeId;

                    var item = new TestSelectableItem
                    {
                        Id               = r.Id,
                        TestCode         = testCode,
                        NameAr           = !string.IsNullOrWhiteSpace(nameAr) ? nameAr : (!string.IsNullOrWhiteSpace(nameEn) ? nameEn : testCode),
                        NameEn           = nameEn,
                        Price            = price,
                        SpecimenTypeId   = specId,
                        SpecimenTypeName = specName,
                        IsSelected       = false
                    };
                    item.OnSelectionChanged = RefreshSummary;
                    _allTests.Add(item);
                }
                GridTests.ItemsSource = _allTests;
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في تحميل التحاليل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Search ───────────────────────────────────────────────────────────────
        private void TxtTestSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || TxtTestSearch == null || GridTests == null) return;

            var q = TxtTestSearch.Text?.Trim().ToLower() ?? string.Empty;
            if (BtnClearTestSearch != null)
                BtnClearTestSearch.Visibility = string.IsNullOrEmpty(q) ? Visibility.Collapsed : Visibility.Visible;

            GridTests.ItemsSource = string.IsNullOrEmpty(q)
                ? _allTests
                : _allTests.Where(t =>
                    (!string.IsNullOrEmpty(t.NameAr) && t.NameAr.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(t.NameEn) && t.NameEn.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(t.TestCode) && t.TestCode.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }

        private void BtnClearTestSearch_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || TxtTestSearch == null) return;
            TxtTestSearch.Clear();
            if (BtnClearTestSearch != null) BtnClearTestSearch.Visibility = Visibility.Collapsed;
            if (GridTests != null) GridTests.ItemsSource = _allTests;
        }

        private void GridTests_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            RefreshSummary();
        }

        // ── Summary Refresh ──────────────────────────────────────────────────────
        private void RefreshSummary()
        {
            if (!_isInitialized) return;
            if (TxtSelectedCount == null || ListSelectedTests == null || BorderNoTests == null ||
                TxtTotal == null || TxtDiscount == null || TxtNet == null)
                return;

            var selected = _allTests.Where(t => t.IsSelected).ToList();

            // Update selected count badge
            TxtSelectedCount.Text = $"{selected.Count} تحليل محدد";

            // Update summary list
            ListSelectedTests.ItemsSource = selected;
            BorderNoTests.Visibility = selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Totals
            decimal total = selected.Sum(t => t.Price);
            decimal.TryParse(TxtDiscount.Text, out var discount);
            decimal net     = Math.Max(0, total - discount);

            TxtTotal.Text   = $"{total:#,##0} IQD";
            TxtNet.Text     = $"{net:#,##0} IQD";
        }

        private void BillingInputs_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            RefreshSummary();
        }

        private void PaymentMethod_Changed(object sender, RoutedEventArgs e)
        {
            /* reserved */
        }

        // ── Actions ──────────────────────────────────────────────────────────────

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
        private void BtnCancel_Click(object sender, RoutedEventArgs e) => Close();

        private async void BtnSaveOrder_Click(object sender, RoutedEventArgs e)
        {
            await SaveOrderAsync();
        }

        private async Task SaveOrderAsync()
        {
            var selectedTests = _allTests.Where(t => t.IsSelected).Select(t => t.Id).ToList();
            if (selectedTests.Count == 0)
            {
                LisMessageBox.Show("يرجى اختيار تحليل واحد على الأقل.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            decimal discount = 0;
            if (TxtDiscount != null) decimal.TryParse(TxtDiscount.Text, out discount);

            try
            {
                if (BtnSaveOrder != null) BtnSaveOrder.IsEnabled = false;
                if (TxtStatusMsg != null) TxtStatusMsg.Text = "⏳ جاري حفظ الطلب وتوليد الباركود...";

                var req = new CreateOrderRequest
                {
                    PatientId       = _patient.Id,
                    ReferringDoctor = string.IsNullOrWhiteSpace(_referringDoctor) ? null : _referringDoctor.Trim(),
                    ClinicalNotes   = string.IsNullOrWhiteSpace(_clinicalNotes) ? null : _clinicalNotes.Trim(),
                    TestIds         = selectedTests,
                    Discount        = discount,
                    InitialPayment  = 0,
                    PaymentMethod   = "Cash"
                };

                var orderDetails = await _orderService.CreateOrderAsync(req, _currentUser?.Id);

                int specCount = orderDetails.Specimens?.Count ?? 0;
                LisMessageBox.Show(
                    $"✅ تم حفظ الطلب بنجاح وإضافة التحاليل!\n\n" +
                    $"رقم الطلب: #{orderDetails.Order.Id}\n" +
                    $"رقم المريض (P.ID): {_patient.Mrn}\n" +
                    $"عدد أنابيب العينات: {specCount}\n\n" +
                    $"💡 ملاحظة: تم إرسال العينات إلى (قائمة العمل) لسحب الدم وطباعة الباركود عند الطلب.",
                    "تم الحفظ بنجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);

                DialogResult = true;
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في حفظ الطلب: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
            finally
            {
                if (BtnSaveOrder != null) BtnSaveOrder.IsEnabled = true;
                if (TxtStatusMsg != null) TxtStatusMsg.Text = "";
            }
        }
    }
}
