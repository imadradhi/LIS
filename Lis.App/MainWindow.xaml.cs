using System;
using System.Text;
using System.Collections.Generic;

using System.Collections.ObjectModel;

using System.ComponentModel;

using System.Globalization;

using System.IO;

using System.Linq;

using System.Runtime.CompilerServices;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using Dapper;

using Lis.App.Services;

using Lis.Core.Data;

using Lis.Core.Models;

using Lis.Core.Services;

using Microsoft.Extensions.Configuration;

namespace Lis.App

{

    public class TestSelectableItem : INotifyPropertyChanged

    {

        public long Id { get; set; }

        public string TestCode { get; set; } = string.Empty;

        public string NameAr { get; set; } = string.Empty;

        public string NameEn { get; set; } = string.Empty;

        public string SpecimenTypeName { get; set; } = string.Empty;

        public long? SpecimenTypeId { get; set; }

        public decimal Price { get; set; }

        public string FormattedPrice => $"{Price:#,##0} IQD";

        private bool _isSelected;

        public bool IsSelected

        {

            get => _isSelected;

            set

            {

                if (_isSelected != value)

                {

                    _isSelected = value;

                    OnPropertyChanged();

                    OnSelectionChanged?.Invoke();

                }

            }

        }

        public Action? OnSelectionChanged { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    }

    public class TubePreviewItem

    {

        public string DisplayTitle { get; set; } = string.Empty;

        public string TestsSummary { get; set; } = string.Empty;

    }

    public class PatientDropdownItem

    {

        public Patient Patient { get; set; } = null!;

        public long Id => Patient.Id;

        public long Mrn => Patient.Mrn;

        public string FullNameAr => Patient.FullNameAr;

        public string FullNameEn => Patient.FullNameEn ?? string.Empty;

        public string Phone => string.IsNullOrWhiteSpace(Patient.Phone) ? "بدون هاتف" : Patient.Phone;

        public string Sex => Patient.Sex;

        public string GenderDisplay => Patient.Sex == "F" ? "أنثى" : "ذكر";

        public string GenderEmoji => Patient.Sex == "F" ? "👩" : "👨";

        public string GenderBadgeBg => Patient.Sex == "F" ? "#3D1A2E" : "#1A2B4C";

        public string GenderBadgeBorder => Patient.Sex == "F" ? "#EC4899" : "#0284C7";

        public string AgeDisplay

        {

            get

            {

                var today = DateTime.Today;

                var age = today.Year - Patient.DateOfBirth.Year;

                if (Patient.DateOfBirth.Date > today.AddYears(-age)) age--;

                return age > 0 ? $"{age} سنة" : "أقل من سنة";

            }

        }

        public string Subtitle => $"{GenderDisplay} • {AgeDisplay} • {Phone}";

    }

    public partial class MainWindow : Window

    {

        private readonly IDbConnectionFactory _connectionFactory;

        private readonly IAuthService _authService;

        private readonly IPatientService _patientService;

        private readonly IOrderService _orderService;

        private readonly IBillingService _billingService;

        private readonly ILabSettingsService _labSettingsService;
        private readonly ITestCatalogService _testCatalogService;
        private readonly IResultService _resultService;
        private OrderResultsDto? _currentOrderResults;
        private List<OrderWorklistHeaderDto> _allResultsOrders = new List<OrderWorklistHeaderDto>();
        private UserSession? _currentUser;
        private ObservableCollection<TestSelectableItem> _allTests = new ObservableCollection<TestSelectableItem>();
        private Patient? _selectedExistingPatient;

        public MainWindow(UserSession? session = null)
        {
            InitializeComponent();
            _currentUser = session;

            // Load connection string from appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .Build();

            var connStr = config.GetConnectionString("DefaultConnection") 
                ?? "Host=localhost;Port=5432;Database=lis_db;Username=postgres;Password=@Hig5udMz;";

            _connectionFactory = new DbConnectionFactory(connStr);
            _authService = new AuthService(_connectionFactory);
            _patientService = new PatientService(_connectionFactory, _authService);
            var accessionService = new AccessionService(_connectionFactory);
            _orderService = new OrderService(_connectionFactory, accessionService, _authService);
            _billingService = new BillingService(_connectionFactory, _authService);
            _labSettingsService = new LabSettingsService(_connectionFactory, _authService);
            _testCatalogService = new TestCatalogService(_connectionFactory, _authService);
            var refRangeService = new ReferenceRangeService(_connectionFactory);
            _resultService = new ResultService(_connectionFactory, _authService, refRangeService);

            Loaded += async (s, e) => await InitializeAppAsync();
        }

        private async Task InitializeAppAsync()

        {

            try

            {

                // Ensure default user and login
                await _authService.EnsureDefaultAdminUserAsync();
                if (_currentUser == null)
                {
                    _currentUser = await _authService.LoginAsync("admin", "Admin123");
                    if (_currentUser == null)
                    {
                        using var uConn = _connectionFactory.CreateConnection();
                        var u = await uConn.QueryFirstOrDefaultAsync<dynamic>("SELECT id, username, full_name FROM users WHERE active = TRUE ORDER BY id ASC LIMIT 1;");
                        if (u != null)
                        {
                            _currentUser = new UserSession { Id = (long)u.id, Username = (string)u.username, FullName = (string)u.full_name };
                        }
                        else
                        {
                            _currentUser = new UserSession { Id = 1, Username = "admin", FullName = "مدير النظام (Admin)" };
                        }
                    }
                }
                TxtCurrentUserName.Text = _currentUser.FullName;

                // Load Lab Settings & Branding

                await LoadLabSettingsAsync();

                // Load Available Tests for Registration

                await LoadAvailableTestsAsync();

                // Set initial date picker

                DpDateOfBirth.SelectedDate = DateTime.Today.AddYears(-30);

                DpOrdersDateFilter.SelectedDate = DateTime.Today;

                DpCashReportDate.SelectedDate = DateTime.Today;

                // Start PostgreSQL notification listener for real-time instrument results
                StartNotificationListener();
            }

            catch (Exception ex)

            {

                LisMessageBox.Show($"خطأ أثناء بدء النظام والاتصال بقاعدة البيانات: {ex.Message}", "خطأ في الاتصال", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

        private async Task LoadLabSettingsAsync()

        {

            try

            {

                var settings = await _labSettingsService.GetSettingsAsync();

                TxtHeaderLabName.Text = settings.LabNameAr;

                TxtCurrencyBadge.Text = $"{settings.Currency} (الدينار العراقي)";

                // Fill settings inputs

                TxtSettingsLabNameAr.Text = settings.LabNameAr;

                TxtSettingsLabNameEn.Text = settings.LabNameEn;

                TxtSettingsAddress.Text = settings.Address;

                TxtSettingsPhones.Text = settings.Phones;

                TxtSettingsEmail.Text = settings.Email;

                TxtSettingsManagerName.Text = settings.ManagerName;

                TxtSettingsManagerTitle.Text = settings.ManagerTitle;

                TxtSettingsHeaderText.Text = settings.ReportHeaderText;

                TxtSettingsFooterText.Text = settings.ReportFooterText;

                LoadInstalledPrinters();
                string labelPrinter = !string.IsNullOrWhiteSpace(settings.LabelPrinter) && settings.LabelPrinter != "HPRT SL32"
                    ? settings.LabelPrinter
                    : (CmbSettingsLabelPrinter.Items.Cast<string>().FirstOrDefault(p => p.Contains("SL32BT", StringComparison.OrdinalIgnoreCase)) ?? "HPRT SL32BT");

                string receiptPrinter = !string.IsNullOrWhiteSpace(settings.ReceiptPrinter) && settings.ReceiptPrinter != "XP-80C"
                    ? settings.ReceiptPrinter
                    : (CmbSettingsReceiptPrinter.Items.Cast<string>().FirstOrDefault(p => p.Contains("POS80", StringComparison.OrdinalIgnoreCase) || (p.Contains("POS", StringComparison.OrdinalIgnoreCase) && !ReceiptRenderer.IsLabelPrinterName(p)) || p.Contains("XP-80", StringComparison.OrdinalIgnoreCase)) ?? "POSPrinter POS80");

                CmbSettingsLabelPrinter.Text = labelPrinter;
                CmbSettingsReportPrinter.Text = settings.ReportPrinter ?? "Default A4 Printer";
                CmbSettingsReceiptPrinter.Text = receiptPrinter;
                LabelRenderer.DefaultLabelPrinter = labelPrinter;
                ReceiptRenderer.DefaultReceiptPrinter = receiptPrinter;

                // Load and restore saved barcode offset

                LabelRenderer.BarcodeOffsetMm = settings.BarcodeOffsetMm;

                SetBarcodeOffsetInCombo(settings.BarcodeOffsetMm);

                // Load and apply saved theme

                ThemeManager.ApplyTheme(settings.Theme);

                SetThemeInCombos(settings.Theme);

                UpdateLivePreview();

            }

            catch { }

        }

        private void UpdateLivePreview()

        {

            if (TxtPreviewLabNameAr == null) return;

            TxtPreviewLabNameAr.Text = TxtSettingsLabNameAr?.Text ?? "مختبر التحليلات الطبية";

            TxtPreviewLabNameEn.Text = TxtSettingsLabNameEn?.Text ?? "Medical Laboratory";

            TxtPreviewAddress.Text = TxtSettingsAddress?.Text ?? "العراق";

            TxtPreviewPhones.Text = $"هاتف: {TxtSettingsPhones?.Text}";

            TxtPreviewHeaderTitle.Text = TxtSettingsHeaderText?.Text ?? "Clinical Laboratory";

            TxtPreviewFooterText.Text = TxtSettingsFooterText?.Text ?? "نتائج الفحوصات تعتمد سريرياً بالربط مع حالة المريض";

            TxtPreviewManagerName.Text = TxtSettingsManagerName?.Text ?? "د. أحمد علي";

            TxtPreviewManagerTitle.Text = TxtSettingsManagerTitle?.Text ?? "أخصائي التحليلات المرضية";

        }

        private async Task LoadAvailableTestsAsync()

        {

            using var conn = _connectionFactory.CreateConnection();

            const string sql = @"

                SELECT t.id, t.test_code AS TestCode, t.name_en AS NameEn, t.name_ar AS NameAr,

                       t.price, t.specimen_type_id AS SpecimenTypeId,

                       COALESCE(st.name_en, 'Whole Blood') AS SpecimenTypeName

                FROM tests t

                LEFT JOIN specimen_types st ON st.id = t.specimen_type_id

                WHERE t.active = TRUE

                ORDER BY t.sort_order ASC, t.id ASC;

            ";

            var tests = await conn.QueryAsync<dynamic>(sql);

            _allTests.Clear();

            foreach (var t in tests)

            {

                var item = new TestSelectableItem

                {

                    Id = (long)t.id,

                    TestCode = (string)t.testcode,

                    NameEn = (string)t.nameen,

                    NameAr = (string?)t.namear ?? (string)t.nameen,

                    Price = (decimal)t.price,

                    SpecimenTypeId = (long?)t.specimentypeid,

                    SpecimenTypeName = (string)t.specimentypename,

                    IsSelected = false

                };

                item.OnSelectionChanged = () => { }; // selection handled in AddTestsDialog

                _allTests.Add(item);

            }

            // GridTests lives in AddTestsDialog, not MainWindow

        }


        private async void BtnManageTests_Click(object sender, RoutedEventArgs e)
        {
            var win = new ManageTestsWindow(_testCatalogService, _currentUser) { Owner = this };
            win.ShowDialog();
            if (win.CatalogChanged)
            {
                await LoadAvailableTestsAsync();
            }
        }

        private void RecalculateBilling() { /* no-op: billing moved to AddTestsDialog */ }

        private void BillingInputs_TextChanged(object sender, TextChangedEventArgs e) { }


        private void DpDateOfBirth_SelectedDateChanged(object sender, SelectionChangedEventArgs e)

        {

            if (DpDateOfBirth.SelectedDate.HasValue)

            {

                var age = _patientService.CalculateAge(DpDateOfBirth.SelectedDate.Value);

                TxtCalculatedAge.Text = age.FormattedText;

                _ = CheckDuplicatesAsync();

            }

        }

        private async Task CheckDuplicatesAsync()

        {

            if (BorderDuplicateWarning == null || _patientService == null) return;

            var name = TxtPatientNameAr?.Text?.Trim();

            if (!string.IsNullOrWhiteSpace(name) && DpDateOfBirth?.SelectedDate.HasValue == true)

            {

                var dups = await _patientService.CheckDuplicatesAsync(name, DpDateOfBirth.SelectedDate.Value, _selectedExistingPatient?.Id);

                BorderDuplicateWarning.Visibility = (dups != null && dups.Any()) ? Visibility.Visible : Visibility.Collapsed;

            }

            else

            {

                BorderDuplicateWarning.Visibility = Visibility.Collapsed;

            }

        }

        private async Task LoadPatientDropdownAsync(string? query = null)

        {

            try

            {

                var results = (await _patientService.SearchPatientsAsync(query ?? string.Empty, 25)).ToList();

                var items = results.Select(p => new PatientDropdownItem { Patient = p }).ToList();

                TxtPatientCountBadge.Text = $"{items.Count} مريض";

                if (items.Count > 0)

                {

                    BorderEmptyPatients.Visibility = Visibility.Collapsed;

                    ListPatientDropdown.Visibility = Visibility.Visible;

                    ListPatientDropdown.ItemsSource = items;

                }

                else

                {

                    BorderEmptyPatients.Visibility = Visibility.Visible;

                    ListPatientDropdown.Visibility = Visibility.Collapsed;

                    ListPatientDropdown.ItemsSource = null;

                }

                PopupPatientSearch.IsOpen = true;

            }

            catch (Exception ex)

            {

                System.Diagnostics.Debug.WriteLine($"Error loading patients dropdown: {ex.Message}");

            }

        }

        private async void TxtPatientSearch_TextChanged(object sender, TextChangedEventArgs e)

        {

            string q = TxtPatientSearch.Text.Trim();

            // Manage watermark and clear button

            TxtSearchWatermark.Visibility = string.IsNullOrEmpty(TxtPatientSearch.Text) ? Visibility.Visible : Visibility.Collapsed;

            BtnClearSearch.Visibility = string.IsNullOrEmpty(TxtPatientSearch.Text) ? Visibility.Collapsed : Visibility.Visible;

            if (TxtPatientSearch.IsFocused)

            {

                await LoadPatientDropdownAsync(q);

            }

        }

        private void TxtPatientSearch_GotFocus(object sender, RoutedEventArgs e)

        {

            TxtSearchWatermark.Visibility = string.IsNullOrEmpty(TxtPatientSearch.Text) ? Visibility.Visible : Visibility.Collapsed;

        }

        private void TxtPatientSearch_LostFocus(object sender, RoutedEventArgs e)

        {

            TxtSearchWatermark.Visibility = string.IsNullOrEmpty(TxtPatientSearch.Text) ? Visibility.Visible : Visibility.Collapsed;

        }

        private async void BtnTogglePatientDropdown_Click(object sender, RoutedEventArgs e)

        {

            if (PopupPatientSearch.IsOpen)

            {

                PopupPatientSearch.IsOpen = false;

            }

            else

            {

                await LoadPatientDropdownAsync(TxtPatientSearch.Text.Trim());

            }

        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)

        {

            TxtPatientSearch.Clear();

            PopupPatientSearch.IsOpen = false;

            TxtPatientSearch.Focus();

        }

        private void TxtPatientSearch_PreviewKeyDown(object sender, KeyEventArgs e)

        {

            if (e.Key == Key.Down)

            {

                if (PopupPatientSearch.IsOpen && ListPatientDropdown.Items.Count > 0)

                {

                    ListPatientDropdown.Focus();

                    ListPatientDropdown.SelectedIndex = 0;

                    e.Handled = true;

                }

            }

            else if (e.Key == Key.Escape)

            {

                PopupPatientSearch.IsOpen = false;

                e.Handled = true;

            }

        }

        private void ListPatientDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (ListPatientDropdown.SelectedItem is PatientDropdownItem selectedItem)

            {

                SelectPatient(selectedItem.Patient, selectedItem);

                ListPatientDropdown.SelectedItem = null;

            }

        }

        private void SelectPatient(Patient p, PatientDropdownItem? item = null)

        {

            _selectedExistingPatient = p;

            TxtAssignedMrn.Text = $"🟢 مسجل (MRN: {p.Mrn})";

            if (TryFindResource("SuccessBrush") is SolidColorBrush successBrush)

            {

                TxtAssignedMrn.Foreground = successBrush;

            }

            TxtPatientNameAr.Text = p.FullNameAr;

            TxtPatientPhone.Text = p.Phone ?? string.Empty;

            CmbPatientSex.SelectedIndex = p.Sex == "F" ? 1 : 0;

            DpDateOfBirth.SelectedDate = p.DateOfBirth;

            string ageDisplay = item?.AgeDisplay ?? "محسوب";

            string genderDisplay = p.Sex == "F" ? "أنثى" : "ذكر";

            string phoneDisplay = string.IsNullOrWhiteSpace(p.Phone) ? "بدون هاتف" : p.Phone;

            TxtSelectedPatientName.Text = p.FullNameAr;

            TxtSelectedPatientMrn.Text = $"P.ID: {p.Mrn} | {genderDisplay} ({ageDisplay}) | 📱 {phoneDisplay}";

            BorderSelectedPatientBanner.Visibility = Visibility.Visible;

            PopupPatientSearch.IsOpen = false;

        }

        private void BtnUnselectPatient_Click(object sender, RoutedEventArgs e)

        {

            BtnNewPatient_Click(sender, e);

        }

        private void BtnNewPatient_Click(object sender, RoutedEventArgs e)

        {

            _selectedExistingPatient = null;

            TxtPatientSearch.Clear();

            BorderSelectedPatientBanner.Visibility = Visibility.Collapsed;

            TxtAssignedMrn.Text = "تلقائي جديد (New Auto)";

            if (TryFindResource("AccentBrush") is SolidColorBrush accentBrush)

            {

                TxtAssignedMrn.Foreground = accentBrush;

            }

            TxtPatientNameAr.Clear();

            TxtPatientPhone.Clear();

            TxtReferringDoctor.Clear();

            CmbPatientSex.SelectedIndex = 0;

            DpDateOfBirth.SelectedDate = DateTime.Today.AddYears(-30);

            TxtPatientNameAr.Focus();

            PopupPatientSearch.IsOpen = false;

        }

        private void TxtTestFilter_TextChanged(object sender, TextChangedEventArgs e) { /* moved to AddTestsDialog */ }

        private void RbOrderPaymentMethod_Checked(object sender, RoutedEventArgs e) { /* moved to AddTestsDialog */ }


        // BtnSaveOrderAndPrint_Click is kept for backward compat (e.g. OrderDetailsWindow usage)
        // but functionality is now in AddTestsDialog.BtnSaveAndPay_Click
        private async void BtnSaveOrderAndPrint_Click(object sender, RoutedEventArgs e)
        {
            await Task.CompletedTask; // Handled by AddTestsDialog
        }


        private async void TxtQuickScan_KeyDown(object sender, KeyEventArgs e)

        {

            if (e.Key == Key.Enter)

            {

                await ExecuteQuickScanAsync();

            }

        }

        private async void BtnQuickScan_Click(object sender, RoutedEventArgs e)

        {

            await ExecuteQuickScanAsync();

        }

        private async Task ExecuteQuickScanAsync()

        {

            string accession = TxtQuickScan.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(accession)) return;

            try

            {

                var order = await _orderService.GetOrderByAccessionAsync(accession);

                if (order != null)

                {

                    TxtQuickScan.Clear();

                    var win = new OrderDetailsWindow(order.Order.Id, _orderService, _billingService, _labSettingsService, _currentUser, _testCatalogService) { Owner = this };

                    win.ShowDialog();

                }

                else

                {

                    LisMessageBox.Show($"لم يتم العثور على أي عينة نشطة بالباركود ({accession}).", "غير موجود", MessageBoxButton.OK, MessageBoxImage.Warning);

                    TxtQuickScan.SelectAll();

                }

            }

            catch (Exception ex)

            {

                LisMessageBox.Show($"خطأ أثناء البحث عن الباركود: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (ViewRegistration == null) return;

            ViewRegistration.Visibility  = NavRegistration.IsChecked  == true ? Visibility.Visible : Visibility.Collapsed;
            ViewWorklist.Visibility      = NavWorklist.IsChecked      == true ? Visibility.Visible : Visibility.Collapsed;
            if (ViewResults != null && NavResults != null)
                ViewResults.Visibility   = NavResults.IsChecked       == true ? Visibility.Visible : Visibility.Collapsed;
            if (ViewTests != null && NavTests != null)
                ViewTests.Visibility     = NavTests.IsChecked         == true ? Visibility.Visible : Visibility.Collapsed;
            ViewBilling.Visibility       = NavBilling.IsChecked       == true ? Visibility.Visible : Visibility.Collapsed;
            ViewInstruments.Visibility   = NavInstruments.IsChecked   == true ? Visibility.Visible : Visibility.Collapsed;
            ViewSettings.Visibility      = NavSettings.IsChecked      == true ? Visibility.Visible : Visibility.Collapsed;

            // Orders list is embedded in the Registration view — always reload when tab is active
            if (NavRegistration.IsChecked == true) _ = LoadOrdersGridAsync();
            if (NavWorklist.IsChecked     == true) _ = LoadWorklistGridAsync();
            if (NavResults?.IsChecked     == true) _ = LoadResultsOrdersListAsync();
            if (NavTests?.IsChecked       == true) _ = LoadCatalogTestsViewAsync();
            if (NavBilling.IsChecked      == true) _ = LoadBillingReportAsync();
            if (NavInstruments.IsChecked  == true)
            {
                _ = LoadInstrumentsGridAsync();
            }
        }

        // ── Tests Catalog View State & Handlers ────────────────────────────────
        private List<TestCatalogDto> _allCatalogTests = new List<TestCatalogDto>();
        private TestCatalogDto? _editingCatalogTest = null;
        private bool _catalogLookupsLoaded = false;

        private async Task LoadCatalogTestsViewAsync()
        {
            if (_testCatalogService == null || GridCatalogTests == null) return;

            try
            {
                // 1. Load departments and specimen types dropdowns if not loaded yet
                if (!_catalogLookupsLoaded)
                {
                    var depts = (await _testCatalogService.GetDepartmentsAsync()).ToList();
                    CmbCatFormDepartment.ItemsSource = depts;
                    if (depts.Count > 0) CmbCatFormDepartment.SelectedIndex = 0;

                    // Add "All Departments" option for filter
                    var filterDepts = new List<DepartmentDto>
                    {
                        new DepartmentDto { Id = -1, NameEn = "All Departments", NameAr = "جميع الأقسام" }
                    };
                    filterDepts.AddRange(depts);
                    CmbCatDeptFilter.ItemsSource = filterDepts;
                    CmbCatDeptFilter.DisplayMemberPath = "CleanTitle";
                    CmbCatDeptFilter.SelectedValuePath = "Id";
                    CmbCatDeptFilter.SelectedIndex = 0;

                    var specTypes = (await _testCatalogService.GetSpecimenTypesAsync()).ToList();
                    CmbCatFormSpecimenType.ItemsSource = specTypes;
                    if (specTypes.Count > 0) CmbCatFormSpecimenType.SelectedIndex = 0;

                    _catalogLookupsLoaded = true;
                }

                // 2. Load all tests
                var tests = (await _testCatalogService.GetAllTestsAsync()).ToList();
                _allCatalogTests = tests;
                ApplyCatalogFilter();
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في تحميل دليل الفحوصات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private void TxtCatSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyCatalogFilter();
        private void CmbCatDeptFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyCatalogFilter();
        private void CmbCatStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyCatalogFilter();

        private void ApplyCatalogFilter()
        {
            if (GridCatalogTests == null || _allCatalogTests == null) return;

            string search = TxtCatSearch?.Text?.Trim() ?? string.Empty;
            long selectedDeptId = -1;
            if (CmbCatDeptFilter?.SelectedValue is long dId) selectedDeptId = dId;

            int statusIdx = CmbCatStatusFilter?.SelectedIndex ?? 0; // 0=All, 1=Active, 2=Inactive

            var filtered = _allCatalogTests.Where(t =>
            {
                // Search filter
                bool matchSearch = string.IsNullOrWhiteSpace(search) ||
                    t.TestCode.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    t.NameEn.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(t.NameAr) && t.NameAr.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);

                if (!matchSearch) return false;

                // Dept filter
                if (selectedDeptId > 0 && t.DepartmentId != selectedDeptId) return false;

                // Status filter
                if (statusIdx == 1 && !t.Active) return false;
                if (statusIdx == 2 && t.Active) return false;

                return true;
            }).ToList();

            GridCatalogTests.ItemsSource = filtered;
            if (TxtCatCountBadge != null) TxtCatCountBadge.Text = $"{filtered.Count} فحص";
        }

        private async void GridCatalogTests_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridCatalogTests?.SelectedItem is TestCatalogDto item)
            {
                PopulateCatalogTestForm(item);
                await LoadComponentsQuickViewAsync(item.Id, item.TestCode);
            }
        }

        private void PopulateCatalogTestForm(TestCatalogDto item)
        {
            _editingCatalogTest = item;
            if (TxtCatFormTitle != null) TxtCatFormTitle.Text = $"✏️ تعديل الفحص: {item.TestCode}";
            if (TxtCatFormTestCode != null)
            {
                TxtCatFormTestCode.Text = item.TestCode;
                TxtCatFormTestCode.IsEnabled = false;
            }
            if (TxtCatFormNameAr != null) TxtCatFormNameAr.Text = item.NameAr ?? string.Empty;
            if (TxtCatFormNameEn != null) TxtCatFormNameEn.Text = item.NameEn;
            if (TxtCatFormPrice != null) TxtCatFormPrice.Text = item.Price.ToString("0");
            if (ChkCatFormActive != null) ChkCatFormActive.IsChecked = item.Active;

            if (BtnCatOpenComponentsQuick != null)
            {
                BtnCatOpenComponentsQuick.Visibility = Visibility.Visible;
                BtnCatOpenComponentsQuick.Content = $"🔬 تفاصيل وبارامترات: {item.TestCode} (Parameters)";
            }

            if (item.DepartmentId.HasValue && CmbCatFormDepartment != null)
                CmbCatFormDepartment.SelectedValue = item.DepartmentId.Value;

            if (item.SpecimenTypeId.HasValue && CmbCatFormSpecimenType != null)
                CmbCatFormSpecimenType.SelectedValue = item.SpecimenTypeId.Value;
        }

        private async Task LoadComponentsQuickViewAsync(long testId, string testCode)
        {
            if (_testCatalogService == null || GridCatComponentsQuickView == null) return;
            try
            {
                var comps = (await _testCatalogService.GetComponentsForTestAsync(testId)).ToList();
                GridCatComponentsQuickView.ItemsSource = comps;
                if (RbCatTabComponents != null) RbCatTabComponents.Content = $"🔬 الباراميترات ({comps.Count})";
                if (TxtCatSelectedTestName != null) TxtCatSelectedTestName.Text = $"التحليل: {testCode} ({comps.Count} بارامتر مسجل)";
            }
            catch { /* non-critical */ }
        }

        private void BtnCatStartNewTest_Click(object sender, RoutedEventArgs e)
        {
            ClearCatalogTestForm();
            TxtCatFormTestCode?.Focus();
        }

        private void BtnCatClearForm_Click(object sender, RoutedEventArgs e)
        {
            ClearCatalogTestForm();
        }

        private void ClearCatalogTestForm()
        {
            _editingCatalogTest = null;
            if (TxtCatFormTitle != null) TxtCatFormTitle.Text = "➕ إضافة فحص جديد (New Test)";
            if (TxtCatFormTestCode != null)
            {
                TxtCatFormTestCode.Clear();
                TxtCatFormTestCode.IsEnabled = true;
            }
            if (TxtCatFormNameAr != null) TxtCatFormNameAr.Clear();
            if (TxtCatFormNameEn != null) TxtCatFormNameEn.Clear();
            if (TxtCatFormPrice != null) TxtCatFormPrice.Text = "0";
            if (ChkCatFormActive != null) ChkCatFormActive.IsChecked = true;
            if (BtnCatOpenComponentsQuick != null) BtnCatOpenComponentsQuick.Visibility = Visibility.Collapsed;
            if (GridCatalogTests != null) GridCatalogTests.SelectedItem = null;
            if (GridCatComponentsQuickView != null) GridCatComponentsQuickView.ItemsSource = null;
            if (RbCatTabComponents != null) RbCatTabComponents.Content = "🔬 الباراميترات (0)";
            if (TxtCatSelectedTestName != null) TxtCatSelectedTestName.Text = "التحليل: غير محدد";
            if (RbCatTabInfo != null) RbCatTabInfo.IsChecked = true;
        }

        private void BtnCatEditTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TestCatalogDto item)
            {
                PopulateCatalogTestForm(item);
                if (RbCatTabInfo != null) RbCatTabInfo.IsChecked = true;
            }
        }

        private void BtnCatManageComponents_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TestCatalogDto item)
            {
                OpenCatalogComponentsWindow(item.Id, item.TestCode);
            }
        }

        private void BtnCatOpenComponentsQuick_Click(object sender, RoutedEventArgs e)
        {
            if (_editingCatalogTest != null && _editingCatalogTest.Id > 0)
            {
                OpenCatalogComponentsWindow(_editingCatalogTest.Id, _editingCatalogTest.TestCode);
            }
            else if (GridCatalogTests?.SelectedItem is TestCatalogDto item)
            {
                OpenCatalogComponentsWindow(item.Id, item.TestCode);
            }
            else
            {
                LisMessageBox.Show("يرجى اختيار فحص أولاً لإدارة بارامتراته.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            }
        }

        private void OpenCatalogComponentsWindow(long testId, string testCode)
        {
            var win = new ManageComponentsWindow(testId, _testCatalogService, _currentUser) { Owner = this };
            win.ShowDialog();
            if (win.CatalogChanged)
            {
                _ = LoadCatalogTestsViewAsync();
                _ = LoadComponentsQuickViewAsync(testId, testCode);
                _ = LoadAvailableTestsAsync();
            }
        }

        private async void BtnCatDeleteTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TestCatalogDto item)
            {
                var confirm = LisMessageBox.Show(
                    $"هل أنت متأكد من رغبتك في حذف الفحص ({item.TestCode} - {item.NameEn})؟\n\nملاحظة: إذا كان للفحص طلبات سابقة سيتم تعطيله تلقائياً للحفاظ على سلامة السجلات.",
                    "تأكيد الحذف",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    this);

                if (confirm == MessageBoxResult.Yes)
                {
                    try
                    {
                        bool deleted = await _testCatalogService.DeleteTestAsync(item.Id, _currentUser?.Id);
                        LisMessageBox.Show(
                            deleted ? "تم حذف الفحص بنجاح." : "تم تعطيل الفحص لوجود طلبات سابقة مرتبطة به.",
                            "نجاح العملية",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information,
                            this);

                        await LoadCatalogTestsViewAsync();
                        await LoadAvailableTestsAsync();
                        ClearCatalogTestForm();
                    }
                    catch (Exception ex)
                    {
                        LisMessageBox.Show($"خطأ أثناء حذف الفحص: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                    }
                }
            }
        }

        private async void BtnCatSaveTest_Click(object sender, RoutedEventArgs e)
        {
            string code   = TxtCatFormTestCode?.Text?.Trim() ?? string.Empty;
            string nameAr = TxtCatFormNameAr?.Text?.Trim() ?? string.Empty;
            string nameEn = TxtCatFormNameEn?.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(code))
            {
                LisMessageBox.Show("يرجى إدخال كود التحليل الفريد (مثال: T_TSH, T_CBC).", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtCatFormTestCode?.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(nameEn))
            {
                LisMessageBox.Show("يرجى إدخال اسم التحليل بالإنجليزية.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtCatFormNameEn?.Focus();
                return;
            }

            if (!decimal.TryParse(TxtCatFormPrice?.Text, out decimal price) || price < 0)
            {
                LisMessageBox.Show("يرجى إدخال سعر صحيح للتحليل بالدينار.", "سعر غير صحيح", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtCatFormPrice?.Focus();
                return;
            }

            long? deptId = CmbCatFormDepartment?.SelectedValue as long?;
            long? specId = CmbCatFormSpecimenType?.SelectedValue as long?;
            bool active = ChkCatFormActive?.IsChecked ?? true;

            try
            {
                var test = new TestCatalogDto
                {
                    Id             = _editingCatalogTest?.Id ?? 0,
                    TestCode       = code,
                    NameAr         = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr,
                    NameEn         = nameEn,
                    Price          = price,
                    DepartmentId   = deptId,
                    SpecimenTypeId = specId,
                    Active         = active
                };

                long testId = await _testCatalogService.SaveTestAsync(test, _currentUser?.Id);

                LisMessageBox.Show(
                    _editingCatalogTest == null ? "✅ تم إضافة التحليل الجديد بنجاح!" : "✅ تم تحديث بيانات التحليل بنجاح!",
                    "تم الحفظ",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);

                await LoadCatalogTestsViewAsync();
                await LoadAvailableTestsAsync();

                // Select the saved test
                var saved = _allCatalogTests.FirstOrDefault(t => t.Id == testId);
                if (saved != null)
                {
                    GridCatalogTests.SelectedItem = saved;
                    PopulateCatalogTestForm(saved);
                    await LoadComponentsQuickViewAsync(saved.Id, saved.TestCode);
                }
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في حفظ التحليل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private void CatTab_Checked(object sender, RoutedEventArgs e)
        {
            if (PanelCatTestForm == null || PanelCatComponents == null) return;

            if (RbCatTabInfo?.IsChecked == true)
            {
                PanelCatTestForm.Visibility   = Visibility.Visible;
                PanelCatComponents.Visibility = Visibility.Collapsed;
            }
            else if (RbCatTabComponents?.IsChecked == true)
            {
                PanelCatTestForm.Visibility   = Visibility.Collapsed;
                PanelCatComponents.Visibility = Visibility.Visible;
            }
        }

        private async Task LoadOrdersGridAsync()

        {

            // Support new ComboBox name only (CmbOrderStatusFilter removed)
            var statusCmb = CmbOrdersStatusFilter;
            if (_orderService == null || DpOrdersDateFilter == null || TxtOrdersSearch == null || GridOrders == null)
                return;

            try

            {

                var statusCmb2 = CmbOrdersStatusFilter;
                string? status = (statusCmb2?.SelectedItem as ComboBoxItem)?.Content?.ToString();
                if (status != null && (status.StartsWith("جميع") || status.Contains("All"))) status = null;

                DateTime? date = DpOrdersDateFilter.SelectedDate;

                string? query = TxtOrdersSearch.Text?.Trim();

                var list = await _orderService.GetOrdersListAsync(status, date, query);

                GridOrders.ItemsSource = list;

            }

            catch (Exception ex)

            {

                LisMessageBox.Show($"خطأ في تحميل سجل الطلبات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

        private void TxtOrdersFilter_Changed(object sender, TextChangedEventArgs e)           => _ = LoadOrdersGridAsync();
        private void CmbOrderStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = LoadOrdersGridAsync();
        private void CmbOrdersStatusFilter_Changed(object sender, SelectionChangedEventArgs e) => _ = LoadOrdersGridAsync();
        private void DpOrdersDateFilter_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => _ = LoadOrdersGridAsync();
        private void BtnRefreshOrders_Click(object sender, RoutedEventArgs e)                 => _ = LoadOrdersGridAsync();

        /// <summary>
        /// New reception workflow: save patient + open AddTests dialog immediately.
        /// </summary>
        private async void BtnCreateOrder_Click(object sender, RoutedEventArgs e)
        {
            string nameAr = TxtPatientNameAr?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(nameAr))
            {
                LisMessageBox.Show("يرجى إدخال اسم المريض بالعربية.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPatientNameAr?.Focus();
                return;
            }
            if (!DpDateOfBirth.SelectedDate.HasValue)
            {
                LisMessageBox.Show("يرجى تحديد تاريخ ميلاد المريض.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 1. Create or reuse patient
                Patient? patient = _selectedExistingPatient;
                if (patient == null)
                {
                    patient = new Patient
                    {
                        FullNameAr  = nameAr,
                        Sex         = CmbPatientSex.SelectedIndex == 1 ? "F" : "M",
                        DateOfBirth = DpDateOfBirth.SelectedDate.Value,
                        Phone       = string.IsNullOrWhiteSpace(TxtPatientPhone?.Text) ? null : TxtPatientPhone.Text.Trim()
                    };
                    patient = await _patientService.CreatePatientAsync(patient, _currentUser?.Id);
                }

                if (patient == null)
                {
                    LisMessageBox.Show("تعذر إنشاء أو تحديد المريض.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string? refDoc = string.IsNullOrWhiteSpace(TxtReferringDoctor?.Text) ? null : TxtReferringDoctor.Text.Trim();
                string? notes  = null;

                // 2. Open AddTests dialog (modal popup)
                var addTestsDlg = new AddTestsDialog(
                    patient,
                    refDoc,
                    notes,
                    _orderService,
                    _billingService,
                    _labSettingsService,
                    _connectionFactory,
                    _currentUser)
                { Owner = this };

                bool? dlgResult = addTestsDlg.ShowDialog();

                // 3. If order was saved, reset form and refresh orders list
                if (dlgResult == true)
                {
                    BtnNewPatient_Click(this, new RoutedEventArgs());
                    _ = LoadOrdersGridAsync();
                    _ = LoadWorklistGridAsync();
                }
                else
                {
                    // If dialog was closed without saving, keep the created patient selected
                    _selectedExistingPatient = patient;
                    BorderSelectedPatientBanner.Visibility = Visibility.Visible;
                    TxtAssignedMrn.Text = $"🟢 مسجل (MRN: {patient.Mrn})";
                    if (TryFindResource("SuccessBrush") is SolidColorBrush successBrush)
                    {
                        TxtAssignedMrn.Foreground = successBrush;
                    }
                }
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnViewOrderDetails_Click(object sender, RoutedEventArgs e)

        {

            if (sender is FrameworkElement fe && fe.DataContext != null)

            {

                dynamic row = fe.DataContext;

                long orderId = (long)row.id;

                var win = new OrderDetailsWindow(orderId, _orderService, _billingService, _labSettingsService, _currentUser, _testCatalogService) { Owner = this };
                win.ShowDialog();
                _ = LoadOrdersGridAsync();
            }
        }

        private void GridOrders_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (GridOrders.SelectedItem != null)
            {
                dynamic row = GridOrders.SelectedItem;
                long orderId = (long)row.id;
                var win = new OrderDetailsWindow(orderId, _orderService, _billingService, _labSettingsService, _currentUser, _testCatalogService) { Owner = this };
                win.ShowDialog();
                _ = LoadOrdersGridAsync();
            }

        }

        private async Task LoadWorklistGridAsync()

        {

            if (_orderService == null || CmbWorklistStatus == null || GridWorklist == null)

                return;

            try

            {

                var selectedItem = CmbWorklistStatus.SelectedItem as ComboBoxItem;
                string? status = selectedItem?.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(status))
                {
                    string? txt = selectedItem?.Content?.ToString();
                    if (txt != null && (txt.StartsWith("الكل") || txt.StartsWith("جميع") || txt.Contains("All")))
                        status = null;
                    else
                        status = txt;
                }

                var list = await _orderService.GetDepartmentWorklistAsync(null, status);
                GridWorklist.ItemsSource = list;

            }

            catch (Exception ex)

            {

                LisMessageBox.Show($"خطأ في تحميل قائمة العمل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

        private void CmbWorklistFilter_Changed(object sender, SelectionChangedEventArgs e) => _ = LoadWorklistGridAsync();

        private void BtnRefreshWorklist_Click(object sender, RoutedEventArgs e) => _ = LoadWorklistGridAsync();

        private async void BtnWorklistCollect_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
            {
                try
                {
                    dynamic row = fe.DataContext;
                    long specId = (long)row.specimen_id;

                    await _orderService.MarkSpecimenCollectedAsync(specId, _currentUser?.Id);
                    await LoadWorklistGridAsync();

                    LisMessageBox.Show("تم استلام العينة بنجاح.", "تم الاستلام", MessageBoxButton.OK, MessageBoxImage.Information, this);
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"خطأ أثناء استلام العينة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private async void BtnWorklistPrintLabel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
            {
                dynamic row = fe.DataContext;
                try
                {
                    string accessionNo = "";
                    try { accessionNo = row.accession_no?.ToString() ?? ""; } catch { }

                    long mrn = 0;
                    try
                    {
                        if (row.mrn != null)
                            mrn = Convert.ToInt64(row.mrn);
                    }
                    catch { }

                    string patientNameAr = "";
                    try { patientNameAr = row.full_name_ar?.ToString() ?? ""; } catch { }

                    DateTime dob = DateTime.MinValue;
                    try
                    {
                        if (row.date_of_birth != null)
                        {
                            object dobVal = row.date_of_birth;
                            if (dobVal is DateTime dt)
                                dob = dt;
                            else if (dobVal is DateOnly dOnly)
                                dob = dOnly.ToDateTime(TimeOnly.MinValue);
                            else if (DateTime.TryParse(dobVal.ToString(), out var parsedDob))
                                dob = parsedDob;
                        }
                    }
                    catch { }

                    string specimenTypeName = "عينة";
                    try { specimenTypeName = row.specimen_type_name?.ToString() ?? "عينة"; } catch { }

                    string? colorHex = null;
                    try { colorHex = row.color?.ToString() ?? "#475569"; } catch { }

                    var data = new SpecimenLabelData
                    {
                        AccessionNo      = accessionNo,
                        Mrn              = mrn,
                        PatientNameAr    = patientNameAr,
                        DateOfBirth      = dob,
                        SpecimenTypeName = specimenTypeName,
                        ColorHex         = colorHex
                    };

                    string? targetPrinter = LabelRenderer.DefaultLabelPrinter;
                    if (string.IsNullOrWhiteSpace(targetPrinter) && _labSettingsService != null)
                    {
                        try
                        {
                            var settings = await _labSettingsService.GetSettingsAsync();
                            targetPrinter = settings?.LabelPrinter;
                            if (!string.IsNullOrWhiteSpace(targetPrinter))
                                LabelRenderer.DefaultLabelPrinter = targetPrinter;
                        }
                        catch { }
                    }

                    LabelRenderer.PrintLabel(data, targetPrinter);
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"تعذر طباعة ملصق الباركود: {ex.Message}", "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                }
            }
        }

        private async void BtnWorklistReject_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
            {
                try
                {
                    dynamic row = fe.DataContext;
                    long specId = (long)row.specimen_id;

                    var prompt = LisMessageBox.Show(
                        "هل ترغب برفض العينة مع إنشاء باركود جديد لإعادة السحب؟",
                        "رفض العينة",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question,
                        this);

                    if (prompt == MessageBoxResult.Cancel) return;

                    bool recollect = prompt == MessageBoxResult.Yes;

                    await _orderService.RejectSpecimenAsync(specId, 1, "تم الرفض من قائمة العمل", recollect, _currentUser?.Id);
                    await LoadWorklistGridAsync();

                    LisMessageBox.Show(
                        recollect ? "تم رفض العينة وإنشاء باركود جديد لإعادة السحب بنجاح." : "تم رفض العينة بنجاح.",
                        "تم الرفض",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information,
                        this);
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"خطأ أثناء رفض العينة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private async Task LoadBillingReportAsync()
        {
            if (_billingService == null || DpCashReportDate == null || TxtKpiInvoiced == null || GridUserCashSummary == null || GridUnpaidInvoices == null)
                return;

            try
            {
                DateTime date = DpCashReportDate.SelectedDate ?? DateTime.Today;
                var report = await _billingService.GetDailyCashReportAsync(date);

                TxtKpiInvoiced.Text = $"{report.TotalInvoiced:#,##0} IQD";
                TxtKpiDiscount.Text = $"{report.TotalDiscount:#,##0} IQD";
                TxtKpiNet.Text = $"{report.TotalNet:#,##0} IQD";
                if (TxtKpiCash != null) TxtKpiCash.Text = $"{report.TotalCashCollected:#,##0} IQD";
                if (TxtKpiCard != null) TxtKpiCard.Text = $"{report.TotalCardCollected:#,##0} IQD";
                TxtKpiCollected.Text = $"{report.TotalCollected:#,##0} IQD";
                TxtKpiBalance.Text = $"{report.TotalBalance:#,##0} IQD";

                // Update Current User Till (قاصة الحساب الحالي)
                if (TxtCurrentTillUser != null)
                {
                    TxtCurrentTillUser.Text = $"الموظف: {_currentUser?.FullName ?? "مدير النظام (Admin)"}";
                }

                var myTill = report.UserSummaries.FirstOrDefault(u => u.UserId == _currentUser?.Id || u.FullName == _currentUser?.FullName);
                if (myTill != null)
                {
                    if (TxtCurrentTillCash != null) TxtCurrentTillCash.Text = $"{myTill.CashCollected:#,##0} IQD";
                    if (TxtCurrentTillCard != null) TxtCurrentTillCard.Text = $"{myTill.CardCollected:#,##0} IQD";
                    if (TxtCurrentTillTotal != null) TxtCurrentTillTotal.Text = $"{myTill.TotalCollected:#,##0} IQD";
                    if (TxtCurrentTillCount != null) TxtCurrentTillCount.Text = $"{myTill.TransactionsCount} عملية";
                }
                else
                {
                    if (TxtCurrentTillCash != null) TxtCurrentTillCash.Text = "0 IQD";
                    if (TxtCurrentTillCard != null) TxtCurrentTillCard.Text = "0 IQD";
                    if (TxtCurrentTillTotal != null) TxtCurrentTillTotal.Text = "0 IQD";
                    if (TxtCurrentTillCount != null) TxtCurrentTillCount.Text = "0 عملية";
                }

                GridUserCashSummary.ItemsSource = report.UserSummaries;

                var unpaid = await _billingService.GetUnpaidInvoicesAsync();
                GridUnpaidInvoices.ItemsSource = unpaid;
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في تحميل التقرير المالي: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DpCashReportDate_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => _ = LoadBillingReportAsync();

        private void BtnRefreshCashReport_Click(object sender, RoutedEventArgs e) => _ = LoadBillingReportAsync();

        private async void BtnPrintTillReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DateTime date = DpCashReportDate?.SelectedDate ?? DateTime.Today;
                var report = await _billingService.GetDailyCashReportAsync(date);
                var myTill = report.UserSummaries.FirstOrDefault(u => u.UserId == _currentUser?.Id || u.FullName == _currentUser?.FullName)
                    ?? new UserCashSummaryDto
                    {
                        UserId = _currentUser?.Id,
                        FullName = _currentUser?.FullName ?? "الكاشير",
                        CashCollected = 0,
                        CardCollected = 0,
                        TotalCollected = 0,
                        TransactionsCount = 0
                    };

                var settings = await _labSettingsService.GetSettingsAsync();
                ReceiptRenderer.PrintTillReport(myTill, settings, date, settings.ReceiptPrinter);

                LisMessageBox.Show(
                    $"تم إرسال كشف حركة القاصة (80 مم) لحساب [{myTill.FullName}] إلى الطابعة بنجاح.",
                    "طباعة كشف القاصة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء طباعة كشف القاصة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnPayInvoice_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
            {
                dynamic row = fe.DataContext;
                long invId = (long)row.id;
                long invNo = (long)row.invoice_no;
                string patientName = (string)row.full_name_ar;
                decimal balance = (decimal)row.balance;

                try
                {
                    var dlg = new PaymentDialog(invNo, patientName, balance, _currentUser?.FullName ?? "المحاسب (الكاشير)")
                    {
                        Owner = this
                    };

                    if (dlg.ShowDialog() == true)
                    {
                        decimal payAmount = dlg.PaidAmount;
                        string payMethod = dlg.PaymentMethod;

                        await _billingService.RecordPaymentAsync(invId, payAmount, payMethod, _currentUser?.Id);
                        await LoadBillingReportAsync();

                        // Print 80mm thermal cashier receipt immediately
                        try
                        {
                            var settings = await _labSettingsService.GetSettingsAsync();
                            var receiptData = new ReceiptPrintData
                            {
                                LabNameAr = settings.LabNameAr,
                                LabNameEn = settings.LabNameEn,
                                Address = settings.Address,
                                Phones = settings.Phones,
                                InvoiceNo = invNo,
                                OrderId = (long)row.order_id,
                                OrderDate = DateTime.Now,
                                PatientNameAr = patientName,
                                Mrn = 0,
                                PatientPhone = (string?)row.phone,
                                CashierName = _currentUser?.FullName ?? "المحاسب (الكاشير)",
                                PaymentMethod = payMethod,
                                TotalAmount = (decimal)row.net,
                                Discount = 0,
                                NetAmount = (decimal)row.net,
                                PaidAmount = payAmount,
                                BalanceDue = Math.Max(0, balance - payAmount)
                            };

                            ReceiptRenderer.PrintReceipt(receiptData, settings.ReceiptPrinter);
                        }
                        catch (Exception pEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Failed to print cashier receipt: {pEx.Message}");
                        }

                        string methodText = payMethod == "Card" ? "الكارت 💳" : "النقد 💵";
                        LisMessageBox.Show(
                            $"تم تسديد المبلغ ({payAmount:#,##0} IQD) بنجاح عبر {methodText}.\nتم إرسال وصل الكاشير الحراري (80 مم) للطباعة فوراً.",
                            "تم التسديد والطباعة",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information,
                            this);
                    }
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"خطأ في تسديد الفاتورة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private void SettingsInputs_Changed(object sender, TextChangedEventArgs e)

        {

            UpdateLivePreview();

        }

        private async void BtnSaveSettings_Click(object sender, RoutedEventArgs e)

        {

            try

            {

                ApplyBarcodeOffset();

                string selectedTheme = ThemeManager.CurrentTheme;

                if (CmbSettingsTheme?.SelectedItem is ComboBoxItem themeItem && themeItem.Tag != null)

                {

                    selectedTheme = themeItem.Tag.ToString()!;

                }

                var settings = new LabSettingsDto

                {

                    LabNameAr = TxtSettingsLabNameAr.Text.Trim(),

                    LabNameEn = TxtSettingsLabNameEn.Text.Trim(),

                    Address = TxtSettingsAddress.Text.Trim(),

                    Phones = TxtSettingsPhones.Text.Trim(),

                    Email = TxtSettingsEmail.Text.Trim(),

                    ManagerName = TxtSettingsManagerName.Text.Trim(),

                    ManagerTitle = TxtSettingsManagerTitle.Text.Trim(),

                    ReportHeaderText = TxtSettingsHeaderText.Text.Trim(),
                    ReportFooterText = TxtSettingsFooterText.Text.Trim(),
                    LabelPrinter = CmbSettingsLabelPrinter.Text.Trim(),
                    ReportPrinter = CmbSettingsReportPrinter.Text.Trim(),
                    ReceiptPrinter = CmbSettingsReceiptPrinter.Text.Trim(),
                    BarcodeOffsetMm = LabelRenderer.BarcodeOffsetMm,
                    Theme = selectedTheme
                };

                await _labSettingsService.SaveSettingsAsync(settings, _currentUser?.Id);
                TxtHeaderLabName.Text = settings.LabNameAr;
                LabelRenderer.DefaultLabelPrinter = settings.LabelPrinter;
                ReceiptRenderer.DefaultReceiptPrinter = settings.ReceiptPrinter;
                ThemeManager.ApplyTheme(settings.Theme);
                SetThemeInCombos(settings.Theme);

                LisMessageBox.Show("تم حفظ إعدادات المختبر والطابعات والسمة (Theme) بنجاح في قاعدة البيانات.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في حفظ الإعدادات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadInstalledPrinters()
        {
            try
            {
                var printers = new List<string>();
                try
                {
                    var printServer = new System.Printing.LocalPrintServer();
                    foreach (var q in printServer.GetPrintQueues())
                    {
                        if (q.IsPendingDeletion) continue;
                        printers.Add(q.FullName);
                    }
                }
                catch { }

                string currentLabel = CmbSettingsLabelPrinter.Text;
                string currentReport = CmbSettingsReportPrinter.Text;
                string currentReceipt = CmbSettingsReceiptPrinter.Text;

                CmbSettingsLabelPrinter.ItemsSource = printers;
                CmbSettingsReportPrinter.ItemsSource = printers;
                CmbSettingsReceiptPrinter.ItemsSource = printers;

                if (string.IsNullOrEmpty(currentLabel) || currentLabel == "HPRT SL32")
                {
                    currentLabel = printers.FirstOrDefault(p => p.Contains("SL32BT", StringComparison.OrdinalIgnoreCase))
                                   ?? printers.FirstOrDefault(p => p.Contains("HPRT", StringComparison.OrdinalIgnoreCase))
                                   ?? "HPRT SL32BT";
                }

                if (string.IsNullOrEmpty(currentReceipt) || currentReceipt == "XP-80C")
                {
                    currentReceipt = printers.FirstOrDefault(p => p.Contains("POS80", StringComparison.OrdinalIgnoreCase))
                                     ?? printers.FirstOrDefault(p => p.Contains("POS", StringComparison.OrdinalIgnoreCase) && !ReceiptRenderer.IsLabelPrinterName(p))
                                     ?? printers.FirstOrDefault(p => p.Contains("XP-80", StringComparison.OrdinalIgnoreCase))
                                     ?? "POSPrinter POS80";
                }

                if (!string.IsNullOrEmpty(currentLabel)) CmbSettingsLabelPrinter.Text = currentLabel;
                if (!string.IsNullOrEmpty(currentReport)) CmbSettingsReportPrinter.Text = currentReport;
                if (!string.IsNullOrEmpty(currentReceipt)) CmbSettingsReceiptPrinter.Text = currentReceipt;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load printers: {ex.Message}");
            }
        }

        private void BtnRefreshPrinters_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledPrinters();
            int count = CmbSettingsLabelPrinter.Items.Count;
            LisMessageBox.Show($"تم الكشف عن {count} طابعة مثبتة على هذا الجهاز بنجاح.", "كشف الطابعات (Detect Printers)", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnBrowseLabelPrinter_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledPrinters();
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() == true && dialog.PrintQueue != null)
            {
                CmbSettingsLabelPrinter.Text = dialog.PrintQueue.FullName;
            }
            else
            {
                CmbSettingsLabelPrinter.IsDropDownOpen = true;
            }
        }

        private void BtnBrowseReportPrinter_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledPrinters();
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() == true && dialog.PrintQueue != null)
            {
                CmbSettingsReportPrinter.Text = dialog.PrintQueue.FullName;
            }
            else
            {
                CmbSettingsReportPrinter.IsDropDownOpen = true;
            }
        }

        private void BtnBrowseReceiptPrinter_Click(object sender, RoutedEventArgs e)
        {
            LoadInstalledPrinters();
            var dialog = new System.Windows.Controls.PrintDialog();
            if (dialog.ShowDialog() == true && dialog.PrintQueue != null)
            {
                CmbSettingsReceiptPrinter.Text = dialog.PrintQueue.FullName;
            }
            else
            {
                CmbSettingsReceiptPrinter.IsDropDownOpen = true;
            }
        }

        private async void BtnPrintTestReceipt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = await _labSettingsService.GetSettingsAsync();
                string? printer = CmbSettingsReceiptPrinter?.Text?.Trim();

                var testData = new ReceiptPrintData
                {
                    LabNameAr = settings.LabNameAr,
                    LabNameEn = settings.LabNameEn,
                    Address = settings.Address,
                    Phones = settings.Phones,
                    InvoiceNo = 10001,
                    OrderId = 5001,
                    OrderDate = DateTime.Now,
                    PatientNameAr = "محمد عبد الله أحمد (تجريبي)",
                    Mrn = 100025,
                    PatientPhone = "07701234567",
                    Gender = "ذكر",
                    CashierName = _currentUser?.FullName ?? "مدير النظام (Admin)",
                    PaymentMethod = "Cash",
                    TotalAmount = 50000,
                    Discount = 5000,
                    NetAmount = 45000,
                    PaidAmount = 45000,
                    BalanceDue = 0,
                    Items = new List<ReceiptItemData>
                    {
                        new ReceiptItemData { Code = "CBC", Name = "Complete Blood Count", Price = 15000 },
                        new ReceiptItemData { Code = "LIPID", Name = "Lipid Profile Panel", Price = 20000 },
                        new ReceiptItemData { Code = "TSH", Name = "Thyroid Stimulating Hormone", Price = 15000 }
                    }
                };

                ReceiptRenderer.PrintReceipt(testData, printer);
                LisMessageBox.Show($"تم إرسال وصل كاشير حراري تجريبي (80 مم) إلى الطابعة:\n{printer ?? "الافتراضية"}", "طباعة وصل تجريبي", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"تعذر إرسال وصل الكاشير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            }
        }

        private void CmbBarcodeOffset_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            ApplyBarcodeOffset();

        }

        private void ApplyBarcodeOffset()

        {

            if (CmbBarcodeOffset == null) return;

            if (CmbBarcodeOffset.SelectedItem is ComboBoxItem item && item.Tag != null)

            {

                if (double.TryParse(item.Tag.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double offsetMm))

                {

                    LabelRenderer.BarcodeOffsetMm = offsetMm;

                    return;

                }

            }

            string text = CmbBarcodeOffset.Text?.Trim() ?? string.Empty;

            var match = System.Text.RegularExpressions.Regex.Match(text, @"[-+]?[0-9]*\.?[0-9]+");

            if (match.Success && double.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out double val))

            {

                LabelRenderer.BarcodeOffsetMm = val;

            }

        }

        private void BtnPrintTestLabel_Click(object sender, RoutedEventArgs e)

        {

            ApplyBarcodeOffset();

            try

            {

                var testData = new SpecimenLabelData

                {

                    AccessionNo = "200586",

                    Mrn = 100031,

                    PatientNameAr = "زينب حيدر",

                    DateOfBirth = new DateTime(1995, 3, 10),

                    SpecimenTypeName = "EDTA Whole Blood"

                };

                string? printer = CmbSettingsLabelPrinter?.Text?.Trim();

                LabelRenderer.PrintLabel(testData, printer);

                string dir = LabelRenderer.BarcodeOffsetMm >= 0 ? "إلى اليمين ➡️" : "إلى اليسار ⬅️";

                LisMessageBox.Show($"تم إرسال ملصق تجريبي إلى الطابعة:\n{printer ?? "الافتراضية"}\n\nمقدار الإزاحة المطبقة: {Math.Abs(LabelRenderer.BarcodeOffsetMm)} مم {dir}.", "طباعة تجريبية", MessageBoxButton.OK, MessageBoxImage.Information);

            }

            catch (Exception ex)

            {

                LisMessageBox.Show($"تعذر إرسال الطباعة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Warning);

            }

        }

        private void BtnLogout_Click(object sender, RoutedEventArgs e)

        {

            var result = LisMessageBox.Show("هل أنت متأكد من رغبتك في تسجيل الخروج والعودة لشاشة الدخول؟", "تسجيل الخروج", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)

            {

                var loginWin = new LoginWindow();

                Application.Current.MainWindow = loginWin;

                loginWin.Show();

                this.Close();

            }

        }

        private bool _isSyncingTheme = false;

        private void SetThemeInCombos(string? theme)
        {
            if (string.IsNullOrWhiteSpace(theme)) theme = ThemeManager.ThemeNight;
            // Normalise legacy values
            if (theme.Equals("Night", StringComparison.OrdinalIgnoreCase) || theme.Equals("Light", StringComparison.OrdinalIgnoreCase))
                theme = theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? ThemeManager.ThemeCleanWhite : ThemeManager.ThemeBiotechDark;

            _isSyncingTheme = true;
            try
            {
                SelectComboByTag(CmbTopTheme, theme);
                SelectComboByTag(CmbSettingsTheme, theme);
            }
            finally
            {
                _isSyncingTheme = false;
            }
        }

        private static void SelectComboByTag(System.Windows.Controls.ComboBox? cmb, string tag)
        {
            if (cmb == null) return;
            foreach (var item in cmb.Items)
            {
                if (item is ComboBoxItem cbi &&
                    string.Equals(cbi.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                {
                    cmb.SelectedItem = cbi;
                    return;
                }
            }
            // fallback: select first item
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        private void SetBarcodeOffsetInCombo(double offsetMm)

        {

            if (CmbBarcodeOffset == null) return;

            foreach (var item in CmbBarcodeOffset.Items)

            {

                if (item is ComboBoxItem cbi && cbi.Tag != null)

                {

                    if (double.TryParse(cbi.Tag.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))

                    {

                        if (Math.Abs(val - offsetMm) < 0.1)

                        {

                            CmbBarcodeOffset.SelectedItem = cbi;

                            return;

                        }

                    }

                }

            }

            CmbBarcodeOffset.Text = $"+{offsetMm:0.#} مم";

        }

        private async void CmbTopTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (_isSyncingTheme || CmbTopTheme?.SelectedItem is not ComboBoxItem item) return;

            string theme = item.Tag?.ToString() ?? ThemeManager.ThemeNight;

            await ChangeThemeAsync(theme);

        }

        private async void CmbSettingsTheme_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (_isSyncingTheme || CmbSettingsTheme?.SelectedItem is not ComboBoxItem item) return;

            string theme = item.Tag?.ToString() ?? ThemeManager.ThemeNight;

            await ChangeThemeAsync(theme);

        }

        private async Task ChangeThemeAsync(string theme)

        {

            ThemeManager.ApplyTheme(theme);

            SetThemeInCombos(theme);

            try

            {

                var settings = await _labSettingsService.GetSettingsAsync();

                settings.Theme = theme;

                await _labSettingsService.SaveSettingsAsync(settings, _currentUser?.Id);

            }

            catch

            {

                // Silent catch on background theme persistence

            }

        }

        private void RbConnType_Checked(object sender, RoutedEventArgs e)

        {

            if (PanelTcpSettings == null || PanelSerialSettings == null) return;

            PanelTcpSettings.Visibility = RbConnTcp.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            PanelSerialSettings.Visibility = RbConnSerial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        }

        private async Task LoadInstrumentsGridAsync()

        {

            try

            {

                using var conn = _connectionFactory.CreateConnection();

                var list = (await conn.QueryAsync<dynamic>(@"

                    SELECT id, code, name, protocol, connection_type, operating_mode, tcp_mode,

                           host, port, com_port, baud_rate, data_bits, parity, stop_bits, flow_control, active,

                           CASE WHEN connection_type = 'TCP' THEN 'Port: ' || COALESCE(port::text, '5100') ELSE COALESCE(com_port, 'COM1') END AS port_display,

                           CASE WHEN active THEN 'نشط 🟢' ELSE 'معطل ⚪' END AS status_text

                    FROM instruments

                    ORDER BY id ASC;

                ")).AsList();

                GridInstruments.ItemsSource = list;

            }

            catch (Exception ex)

            {

                LisMessageBox.Show($"خطأ في استرجاع قائمة الأجهزة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

        private void GridInstruments_SelectionChanged(object sender, SelectionChangedEventArgs e)

        {

            if (GridInstruments.SelectedItem != null)

            {

                dynamic row = GridInstruments.SelectedItem;

                TxtInstCode.Text = (string?)row.code ?? "";

                TxtInstName.Text = (string?)row.name ?? "";

                string proto = (string?)row.protocol ?? "ASTM";

                foreach (ComboBoxItem item in CmbInstProtocol.Items)

                {

                    if ((string)item.Content == proto) { CmbInstProtocol.SelectedItem = item; break; }

                }

                string opMode = (string?)row.operating_mode ?? "Bidirectional";

                foreach (ComboBoxItem item in CmbInstOperatingMode.Items)

                {

                    if ((string)item.Content == opMode) { CmbInstOperatingMode.SelectedItem = item; break; }

                }

                string connType = (string?)row.connection_type ?? "TCP";

                if (connType == "RS232")

                {

                    RbConnSerial.IsChecked = true;

                }

                else

                {

                    RbConnTcp.IsChecked = true;

                }

                // TCP fields

                string tcpMode = (string?)row.tcp_mode ?? "Server";

                foreach (ComboBoxItem item in CmbInstTcpMode.Items)

                {

                    if ((string)item.Content == tcpMode) { CmbInstTcpMode.SelectedItem = item; break; }

                }

                TxtInstPort.Text = row.port != null ? row.port.ToString() : "5100";

                TxtInstHost.Text = (string?)row.host ?? "0.0.0.0";

                // Serial fields

                TxtInstComPort.Text = (string?)row.com_port ?? "COM1";

                string baud = row.baud_rate != null ? row.baud_rate.ToString() : "9600";

                foreach (ComboBoxItem item in CmbInstBaud.Items)

                {

                    if ((string)item.Content == baud) { CmbInstBaud.SelectedItem = item; break; }

                }

                TxtInstDataBits.Text = row.data_bits != null ? row.data_bits.ToString() : "8";

                string parity = (string?)row.parity ?? "None";

                foreach (ComboBoxItem item in CmbInstParity.Items)

                {

                    if ((string)item.Content == parity) { CmbInstParity.SelectedItem = item; break; }

                }

                string stopBits = (string?)row.stop_bits ?? "One";

                foreach (ComboBoxItem item in CmbInstStopBits.Items)

                {

                    if ((string)item.Content == stopBits) { CmbInstStopBits.SelectedItem = item; break; }

                }

                string flow = (string?)row.flow_control ?? "None";

                foreach (ComboBoxItem item in CmbInstFlowControl.Items)

                {

                    if ((string)item.Content == flow) { CmbInstFlowControl.SelectedItem = item; break; }

                }

                ChkInstActive.IsChecked = (bool?)row.active ?? true;

                BorderTestResult.Visibility = Visibility.Collapsed;

            }

        }

        private void BtnNewInstrument_Click(object sender, RoutedEventArgs e)

        {

            GridInstruments.SelectedItem = null;

            TxtInstCode.Clear();

            TxtInstName.Clear();

            CmbInstProtocol.SelectedIndex = 0;

            CmbInstOperatingMode.SelectedIndex = 0;

            RbConnTcp.IsChecked = true;

            CmbInstTcpMode.SelectedIndex = 0;

            TxtInstPort.Text = "5100";

            TxtInstHost.Text = "0.0.0.0";

            TxtInstComPort.Text = "COM1";

            CmbInstBaud.SelectedIndex = 0;

            TxtInstDataBits.Text = "8";

            CmbInstParity.SelectedIndex = 0;

            CmbInstStopBits.SelectedIndex = 0;

            CmbInstFlowControl.SelectedIndex = 0;

            ChkInstActive.IsChecked = true;

            BorderTestResult.Visibility = Visibility.Collapsed;

        }

        private async void BtnSaveInstrument_Click(object sender, RoutedEventArgs e)

        {

            string code = TxtInstCode.Text.Trim();

            string name = TxtInstName.Text.Trim();

            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))

            {

                LisMessageBox.Show("يرجى إدخال كود الجهاز واسمه.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);

                return;

            }

            string proto = (CmbInstProtocol.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ASTM";

            string opMode = (CmbInstOperatingMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Bidirectional";

            string connType = RbConnTcp.IsChecked == true ? "TCP" : "RS232";

            string tcpMode = (CmbInstTcpMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Server";

            int.TryParse(TxtInstPort.Text.Trim(), out var port);

            string host = TxtInstHost.Text.Trim();

            string comPort = TxtInstComPort.Text.Trim();

            int.TryParse((CmbInstBaud.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "9600", out var baud);

            int.TryParse(TxtInstDataBits.Text.Trim(), out var dataBits);

            string parity = (CmbInstParity.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";

            string stopBits = (CmbInstStopBits.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "One";

            string flow = (CmbInstFlowControl.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";

            bool active = ChkInstActive.IsChecked == true;

            try

            {

                using var conn = _connectionFactory.CreateConnection();

                const string sql = @"

                    INSERT INTO instruments (code, name, protocol, connection_type, operating_mode, tcp_mode,

                                            host, port, com_port, baud_rate, data_bits, parity, stop_bits, flow_control,

                                            active, updated_at)

                    VALUES (@Code, @Name, @Protocol, @ConnectionType, @OperatingMode, @TcpMode,

                            @Host, @Port, @ComPort, @BaudRate, @DataBits, @Parity, @StopBits, @FlowControl,

                            @Active, clock_timestamp())

                    ON CONFLICT (code) DO UPDATE

                    SET name = EXCLUDED.name,

                        protocol = EXCLUDED.protocol,

                        connection_type = EXCLUDED.connection_type,

                        operating_mode = EXCLUDED.operating_mode,

                        tcp_mode = EXCLUDED.tcp_mode,

                        host = EXCLUDED.host,

                        port = EXCLUDED.port,

                        com_port = EXCLUDED.com_port,

                        baud_rate = EXCLUDED.baud_rate,

                        data_bits = EXCLUDED.data_bits,

                        parity = EXCLUDED.parity,

                        stop_bits = EXCLUDED.stop_bits,

                        flow_control = EXCLUDED.flow_control,

                        active = EXCLUDED.active,

                        updated_at = clock_timestamp();

                ";

                await conn.ExecuteAsync(sql, new

                {

                    Code = code,

                    Name = name,

                    Protocol = proto,

                    ConnectionType = connType,

                    OperatingMode = opMode,

                    TcpMode = tcpMode,

                    Host = host,

                    Port = port > 0 ? (int?)port : null,

                    ComPort = comPort,

                    BaudRate = baud > 0 ? (int?)baud : null,

                    DataBits = dataBits > 0 ? (int?)dataBits : null,

                    Parity = parity,

                    StopBits = stopBits,

                    FlowControl = flow,

                    Active = active

                });

                await conn.ExecuteAsync("SELECT pg_notify('lis_instruments_channel', 'RELOAD');");

                await LoadInstrumentsGridAsync();

                LisMessageBox.Show("تم حفظ إعدادات الجهاز بنجاح في قاعدة البيانات.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information);

            }

            catch (Exception ex)

            {

                LisMessageBox.Show($"خطأ في حفظ إعدادات الجهاز: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);

            }

        }

        private async void BtnTestInstrumentConnection_Click(object sender, RoutedEventArgs e)

        {

            BorderTestResult.Visibility = Visibility.Visible;

            if (RbConnTcp.IsChecked == true)

            {

                int.TryParse(TxtInstPort.Text.Trim(), out var port);

                string host = TxtInstHost.Text.Trim();

                if (port <= 0)

                {

                    BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 29, 29));

                    TxtTestResult.Foreground = System.Windows.Media.Brushes.White;

                    TxtTestResult.Text = "رقم المنفذ (Port) غير صالح.";

                    return;

                }

                try

                {

                    string tcpMode = (CmbInstTcpMode.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Server";

                    if (tcpMode == "Server")

                    {

                        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);

                        try
                        {
                            listener.Start();
                            listener.Stop();
                            BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));
                            TxtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
                            TxtTestResult.Text = $"منفذ الاستماع TCP {port} متاح وجاهز لاستقبال اتصالات الجهاز.";
                        }
                        catch (Exception ex)
                        {
                            // If port is already bound, check if it's the Middleware actively listening
                            bool canConnect = false;
                            try
                            {
                                using var testClient = new System.Net.Sockets.TcpClient();
                                var connectTask = testClient.ConnectAsync("127.0.0.1", port);
                                if (await Task.WhenAny(connectTask, Task.Delay(1000)) == connectTask && testClient.Connected)
                                {
                                    canConnect = true;
                                }
                            }
                            catch { }

                            if (canConnect)
                            {
                                BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));
                                TxtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
                                TxtTestResult.Text = $"المنفذ {port} قيد الاستماع النشط حالياً وجاهز لاستقبال الأجهزة (بواسطة الميدلوير).";
                            }
                            else
                            {
                                BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 29, 29));
                                TxtTestResult.Foreground = System.Windows.Media.Brushes.White;
                                TxtTestResult.Text = $"المنفذ {port} قيد الاستخدام أو محجوز: {ex.Message}";
                            }
                        }

                    }

                    else

                    {

                        using var client = new System.Net.Sockets.TcpClient();

                        var connectTask = client.ConnectAsync(host == "0.0.0.0" ? "127.0.0.1" : host, port);

                        if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask)

                        {

                            BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));

                            TxtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));

                            TxtTestResult.Text = $"تم الاتصال بنجاح بالجهاز عبر TCP {host}:{port}.";

                        }

                        else

                        {

                            BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 29, 29));

                            TxtTestResult.Foreground = System.Windows.Media.Brushes.White;

                            TxtTestResult.Text = $"فشل الاتصال بـ {host}:{port}: تجاوز مهلة الانتظار.";

                        }

                    }

                }

                catch (Exception ex)

                {

                    BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 29, 29));

                    TxtTestResult.Foreground = System.Windows.Media.Brushes.White;

                    TxtTestResult.Text = $"فشل اختبار الاتصال: {ex.Message}";

                }

            }

            else

            {

                string comPort = TxtInstComPort.Text.Trim();

                try

                {

                    var portNames = System.IO.Ports.SerialPort.GetPortNames();

                    if (portNames.Contains(comPort, StringComparer.OrdinalIgnoreCase))

                    {

                        BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));

                        TxtTestResult.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));

                        TxtTestResult.Text = $"المنفذ التسلسلي {comPort} متوفر في النظام وجاهز.";

                    }

                    else

                    {

                        string available = portNames.Length > 0 ? string.Join(", ", portNames) : "لا توجد منافذ تسلسلية متصلة";

                        BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 29, 29));

                        TxtTestResult.Foreground = System.Windows.Media.Brushes.White;

                        TxtTestResult.Text = $"المنفذ {comPort} غير موجود. المنافذ المتوفرة: {available}";

                    }

                }

                catch (Exception ex)

                {

                    BorderTestResult.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(127, 29, 29));

                    TxtTestResult.Foreground = System.Windows.Media.Brushes.White;

                    TxtTestResult.Text = $"خطأ في فحص المنفذ التسلسلي: {ex.Message}";

                }

            }

        }

        private async void BtnDeleteInstrumentRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
            {
                dynamic row = fe.DataContext;
                long instId = (long)row.id;
                string instName = (string?)row.name ?? "الجهاز";
                string instCode = (string?)row.code ?? "";
                await DeleteInstrumentByIdAsync(instId, instName, instCode);
            }
        }

        private async void BtnDeleteInstrument_Click(object sender, RoutedEventArgs e)
        {
            if (GridInstruments.SelectedItem is not null)
            {
                dynamic row = GridInstruments.SelectedItem;
                long instId = (long)row.id;
                string instName = (string?)row.name ?? "الجهاز";
                string instCode = (string?)row.code ?? "";
                await DeleteInstrumentByIdAsync(instId, instName, instCode);
            }
            else
            {
                LisMessageBox.Show("يرجى اختيار جهاز من الجدول أولاً لحذفه.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            }
        }

        private async Task DeleteInstrumentByIdAsync(long instId, string instName, string instCode)
        {
            var prompt = LisMessageBox.Show(
                $"هل أنت متأكد من حذف الجهاز [{instName}] (كود: {instCode}) نهائياً من النظام؟\n\nتنبيه: سيتم إلغاء ربط جميع الفحوصات وقنوات الاستقبال المرتبطة بهذا الجهاز.",
                "تأكيد حذف الجهاز (Delete Instrument)",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                this);

            if (prompt != MessageBoxResult.Yes) return;

            try
            {
                using var conn = _connectionFactory.CreateConnection();
                await conn.ExecuteAsync("DELETE FROM instruments WHERE id = @Id;", new { Id = instId });
                await conn.ExecuteAsync("SELECT pg_notify('lis_instruments_channel', 'RELOAD');");
                await _authService.LogAuditAsync(_currentUser?.Id, "DELETE_INSTRUMENT", "instruments", instId.ToString(), null, new { id = instId, code = instCode, name = instName });

                BtnNewInstrument_Click(this, new RoutedEventArgs());
                await LoadInstrumentsGridAsync();
                LisMessageBox.Show($"تم حذف الجهاز [{instName}] بنجاح من النظام.", "تم الحذف", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء حذف الجهاز: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }




        private void StartNotificationListener()

        {

            _ = Task.Run(async () =>

            {

                while (true)

                {

                    try

                    {

                        using var conn = (Npgsql.NpgsqlConnection)_connectionFactory.CreateConnection();

                        await conn.OpenAsync();

                        using var cmd = new Npgsql.NpgsqlCommand("LISTEN lis_results_channel;", conn);

                        await cmd.ExecuteNonQueryAsync();

                        conn.Notification += (o, e) =>

                        {

                            Dispatcher.Invoke(async () =>
                            {
                                if (NavRegistration.IsChecked == true) await LoadOrdersGridAsync();
                                if (NavWorklist.IsChecked == true) await LoadWorklistGridAsync();
                                if (NavResults?.IsChecked == true) await LoadResultsOrdersListAsync();
                                if (NavInstruments.IsChecked == true) await LoadInstrumentsGridAsync();
                            });

                        };

                        while (conn.State == System.Data.ConnectionState.Open)

                        {

                            await conn.WaitAsync();

                        }

                    }
                    catch
                    {
                        await Task.Delay(5000);
                    }
                }
            });
        }

        // ── Results View Methods & Handlers ─────────────────────────────────────
        public async void BtnOrderViewResults_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
            {
                dynamic row = fe.DataContext;
                long orderId = (long)row.id;
                NavResults.IsChecked = true;
                await LoadResultsOrdersListAsync(orderId);
            }
        }

        public async void BtnWorklistViewResults_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
            {
                dynamic row = fe.DataContext;
                long orderId = (long)row.order_id;
                NavResults.IsChecked = true;
                await LoadResultsOrdersListAsync(orderId);
            }
        }

        private async Task LoadResultsOrdersListAsync(long? autoSelectOrderId = null)
        {
            if (_resultService == null || GridResultsOrders == null) return;

            try
            {
                string? status = (CmbResultsStatusFilter?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                if (string.IsNullOrWhiteSpace(status)) status = null;

                string? search = TxtResultsOrderSearch?.Text?.Trim();
                if (string.IsNullOrWhiteSpace(search)) search = null;

                _allResultsOrders = await _resultService.GetOrdersForResultsAsync(status, search);
                GridResultsOrders.ItemsSource = _allResultsOrders;

                if (autoSelectOrderId.HasValue)
                {
                    var target = _allResultsOrders.FirstOrDefault(o => o.OrderId == autoSelectOrderId.Value);
                    if (target != null)
                    {
                        GridResultsOrders.SelectedItem = target;
                        GridResultsOrders.ScrollIntoView(target);
                        await LoadSelectedOrderResultsAsync(target.OrderId);
                        return;
                    }
                }

                if (GridResultsOrders.SelectedItem == null && _allResultsOrders.Count > 0)
                {
                    GridResultsOrders.SelectedIndex = 0;
                }
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في تحميل قائمة الطلبات للنتائج: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRefreshResultsOrders_Click(object sender, RoutedEventArgs e) => _ = LoadResultsOrdersListAsync();

        private void TxtResultsOrderSearch_TextChanged(object sender, TextChangedEventArgs e) => _ = LoadResultsOrdersListAsync();

        private void CmbResultsStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => _ = LoadResultsOrdersListAsync();

        private async void GridResultsOrders_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridResultsOrders.SelectedItem is OrderWorklistHeaderDto header)
            {
                await LoadSelectedOrderResultsAsync(header.OrderId);
            }
            else
            {
                _currentOrderResults = null;
                PanelNoOrderSelected.Visibility = Visibility.Visible;
                PanelOrderResults.Visibility = Visibility.Collapsed;
            }
        }

        private async Task LoadSelectedOrderResultsAsync(long orderId)
        {
            if (_resultService == null) return;

            try
            {
                _currentOrderResults = await _resultService.GetOrderResultsAsync(orderId);
                if (_currentOrderResults == null)
                {
                    PanelNoOrderSelected.Visibility = Visibility.Visible;
                    PanelOrderResults.Visibility = Visibility.Collapsed;
                    return;
                }

                PanelNoOrderSelected.Visibility = Visibility.Collapsed;
                PanelOrderResults.Visibility = Visibility.Visible;

                // Bind Patient Header
                TxtResPatientName.Text = _currentOrderResults.FullNameAr;
                TxtResMrn.Text = $"P.ID: {_currentOrderResults.Mrn}";
                TxtResOrderId.Text = $"طلب #{_currentOrderResults.OrderId}";
                TxtResAgeGender.Text = $"العمر: {_currentOrderResults.AgeDisplay} | الجنس: {_currentOrderResults.GenderDisplay}";
                TxtResDoctor.Text = string.IsNullOrWhiteSpace(_currentOrderResults.ReferringDoctor) ? "الطبيب: غير محدد" : $"الطبيب: {_currentOrderResults.ReferringDoctor}";
                TxtResDate.Text = $"التاريخ: {_currentOrderResults.OrderDatetime:dd/MM/yyyy HH:mm}";

                // Bind Status Badge
                TxtResStatus.Text = _currentOrderResults.OrderStatus;
                BorderResStatus.Background = _currentOrderResults.OrderStatus switch
                {
                    "Validated"      => new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                    "ResultReceived" => new SolidColorBrush(Color.FromRgb(56, 189, 248)),
                    "InProgress"     => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                    "Collected"      => new SolidColorBrush(Color.FromRgb(139, 92, 246)),
                    "Rejected"       => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
                    _                => new SolidColorBrush(Color.FromRgb(100, 116, 139))
                };
                TxtResStatus.Foreground = Brushes.White;

                // Buttons state
                bool isValidated = _currentOrderResults.OrderStatus == "Validated";
                BtnValidateResults.Visibility = isValidated ? Visibility.Collapsed : Visibility.Visible;
                BtnUnvalidateResults.Visibility = isValidated ? Visibility.Visible : Visibility.Collapsed;

                // Bind Results DataGrid
                GridOrderResults.ItemsSource = _currentOrderResults.Results;
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء جلب نتائج التحاليل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TxtResultValue_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is ResultItemDto item)
            {
                item.IsModified = true;

                // Live recalculate flag if numeric
                if (!string.IsNullOrWhiteSpace(item.ValueText) && decimal.TryParse(item.ValueText, out decimal val))
                {
                    item.ValueNumeric = val;
                    if (item.Low.HasValue && val < item.Low.Value)
                    {
                        item.Flag = (item.CriticalLow.HasValue && val <= item.CriticalLow.Value) ? "LL" : "L";
                    }
                    else if (item.High.HasValue && val > item.High.Value)
                    {
                        item.Flag = (item.CriticalHigh.HasValue && val >= item.CriticalHigh.Value) ? "HH" : "H";
                    }
                    else
                    {
                        item.Flag = "N";
                    }
                }
                else
                {
                    item.Flag = null;
                }
            }
        }

        private async void BtnSaveManualResults_Click(object sender, RoutedEventArgs e)
        {
            if (_currentOrderResults == null || _resultService == null) return;

            try
            {
                var modified = _currentOrderResults.Results
                    .Where(r => r.IsModified || !string.IsNullOrWhiteSpace(r.ValueText))
                    .Select(r => (r.OrderTestId, r.ComponentId, r.ValueText))
                    .ToList();

                if (modified.Count == 0)
                {
                    LisMessageBox.Show("لا توجد نتائج معدلة لحفظها.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information, this);
                    return;
                }

                await _resultService.SaveOrderResultsBatchAsync(_currentOrderResults.OrderId, modified, _currentUser?.Id);

                LisMessageBox.Show("تم حفظ النتائج بنجاح وتحديث السجلات.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information, this);

                await LoadSelectedOrderResultsAsync(_currentOrderResults.OrderId);
                await LoadResultsOrdersListAsync(_currentOrderResults.OrderId);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء حفظ النتائج: {ex.InnerException?.Message ?? ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnValidateResults_Click(object sender, RoutedEventArgs e)
        {
            if (_currentOrderResults == null || _resultService == null) return;

            var confirm = LisMessageBox.Show(
                $"هل أنت متأكد من اعتماد نتائج التحاليل للطلب #{_currentOrderResults.OrderId} للمريض {_currentOrderResults.FullNameAr}؟\nبعد الاعتماد، تصبح النتائج جاهزة للطباعة والتسليم.",
                "تأكيد الاعتماد",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                this);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                // Auto-save any modified results first
                var modified = _currentOrderResults.Results
                    .Where(r => r.IsModified)
                    .Select(r => (r.OrderTestId, r.ComponentId, r.ValueText))
                    .ToList();

                if (modified.Count > 0)
                {
                    await _resultService.SaveOrderResultsBatchAsync(_currentOrderResults.OrderId, modified, _currentUser?.Id);
                }

                await _resultService.ValidateOrderResultsAsync(_currentOrderResults.OrderId, _currentUser?.Id);

                LisMessageBox.Show("تم اعتماد نتائج التحاليل بنجاح.", "تم الاعتماد", MessageBoxButton.OK, MessageBoxImage.Information, this);

                await LoadSelectedOrderResultsAsync(_currentOrderResults.OrderId);
                await LoadResultsOrdersListAsync(_currentOrderResults.OrderId);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء اعتماد النتائج: {ex.InnerException?.Message ?? ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnUnvalidateResults_Click(object sender, RoutedEventArgs e)
        {
            if (_currentOrderResults == null || _resultService == null) return;

            var confirm = LisMessageBox.Show(
                $"هل ترغب بفك اعتماد نتائج الطلب #{_currentOrderResults.OrderId} لإجراء تعديلات عليها؟",
                "تأكيد فك الاعتماد",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                this);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await _resultService.UnvalidateOrderResultsAsync(_currentOrderResults.OrderId, _currentUser?.Id);
                LisMessageBox.Show("تم فك الاعتماد بنجاح، يمكنك الآن تعديل النتائج.", "تم فك الاعتماد", MessageBoxButton.OK, MessageBoxImage.Information, this);

                await LoadSelectedOrderResultsAsync(_currentOrderResults.OrderId);
                await LoadResultsOrdersListAsync(_currentOrderResults.OrderId);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء فك الاعتماد: {ex.InnerException?.Message ?? ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnPreviewReport_Click(object sender, RoutedEventArgs e)
        {
            if (_currentOrderResults == null || _labSettingsService == null) return;

            try
            {
                var settings = await _labSettingsService.GetSettingsAsync();
                var previewWin = new ReportPreviewWindow(_currentOrderResults, settings)
                {
                    Owner = this
                };
                previewWin.ShowDialog();
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"تعذر فتح معاينة التقرير: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnPrintReportDirect_Click(object sender, RoutedEventArgs e)
        {
            if (_currentOrderResults == null || _labSettingsService == null) return;

            try
            {
                var settings = await _labSettingsService.GetSettingsAsync();
                string? printer = settings.ReportPrinter;

                ReportRenderer.PrintReport(_currentOrderResults, settings, printer);

                LisMessageBox.Show(
                    $"تم إرسال التقرير الطبي A4 للطباعة بنجاح.\nالمريض: {_currentOrderResults.FullNameAr} (#{_currentOrderResults.OrderId})\nالطابعة: {printer ?? "طابعة التقارير الافتراضية"}",
                    "طباعة التقرير",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"تعذر إرسال التقرير للطباعة: {ex.Message}", "خطأ في الطباعة", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }
    }
}
