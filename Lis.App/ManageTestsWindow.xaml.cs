using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Lis.Core.Models;
using Lis.Core.Services;

namespace Lis.App
{
    public partial class ManageTestsWindow : Window
    {
        private readonly ITestCatalogService _catalogService;
        private readonly UserSession? _currentUser;
        private List<TestCatalogDto> _allCatalogTests = new List<TestCatalogDto>();
        private TestCatalogDto? _editingTest = null;

        public bool CatalogChanged { get; private set; } = false;

        public ManageTestsWindow(ITestCatalogService catalogService, UserSession? currentUser = null)
        {
            InitializeComponent();
            _catalogService = catalogService;
            _currentUser = currentUser;

            Loaded += async (s, e) => await InitializeCatalogAsync();
        }

        private async Task InitializeCatalogAsync()
        {
            try
            {
                // 1. Load departments
                var depts = (await _catalogService.GetDepartmentsAsync()).ToList();
                CmbFormDepartment.ItemsSource = depts;
                if (depts.Count > 0) CmbFormDepartment.SelectedIndex = 0;

                // 2. Load specimen types
                var specTypes = (await _catalogService.GetSpecimenTypesAsync()).ToList();
                CmbFormSpecimenType.ItemsSource = specTypes;
                if (specTypes.Count > 0) CmbFormSpecimenType.SelectedIndex = 0;

                // 3. Load all tests
                await LoadTestsGridAsync();
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء تحميل بيانات دليل الفحوصات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async Task LoadTestsGridAsync(string? search = null)
        {
            try
            {
                var tests = (await _catalogService.GetAllTestsAsync(search)).ToList();
                _allCatalogTests = tests;
                GridCatalogTests.ItemsSource = tests;
                TxtTestsCountBadge.Text = $"{tests.Count} فحص";
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في جلب قائمة الفحوصات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private void TxtSearchTests_TextChanged(object sender, TextChangedEventArgs e)
        {
            string filter = TxtSearchTests.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filter))
            {
                GridCatalogTests.ItemsSource = _allCatalogTests;
                TxtTestsCountBadge.Text = $"{_allCatalogTests.Count} فحص";
            }
            else
            {
                var filtered = _allCatalogTests.Where(t =>
                    t.TestCode.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    t.NameEn.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    (t.NameAr != null && t.NameAr.Contains(filter, StringComparison.OrdinalIgnoreCase))).ToList();

                GridCatalogTests.ItemsSource = filtered;
                TxtTestsCountBadge.Text = $"{filtered.Count} فحص";
            }
        }

        private void BtnStartNewTest_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
            TxtFormTestCode.Focus();
        }

        private void BtnClearForm_Click(object sender, RoutedEventArgs e)
        {
            ClearForm();
        }

        private void ClearForm()
        {
            _editingTest = null;
            TxtFormTitle.Text = "➕ إضافة فحص جديد (New Test)";
            TxtFormTestCode.Clear();
            TxtFormTestCode.IsEnabled = true;
            TxtFormNameAr.Clear();
            TxtFormNameEn.Clear();
            TxtFormPrice.Text = "0";
            ChkFormActive.IsChecked = true;
            if (BtnFormManageComponents != null) BtnFormManageComponents.Visibility = Visibility.Collapsed;
            GridCatalogTests.SelectedItem = null;
        }

        private void GridCatalogTests_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridCatalogTests.SelectedItem is TestCatalogDto item)
            {
                PopulateFormForEdit(item);
            }
        }

        private void BtnEditTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TestCatalogDto item)
            {
                PopulateFormForEdit(item);
            }
        }

        private void PopulateFormForEdit(TestCatalogDto item)
        {
            _editingTest = item;
            TxtFormTitle.Text = $"✏️ تعديل الفحص: {item.TestCode}";
            TxtFormTestCode.Text = item.TestCode;
            TxtFormTestCode.IsEnabled = false; // Code is unique identifier
            TxtFormNameAr.Text = item.NameAr ?? string.Empty;
            TxtFormNameEn.Text = item.NameEn;
            TxtFormPrice.Text = item.Price.ToString("0");
            ChkFormActive.IsChecked = item.Active;

            if (BtnFormManageComponents != null)
            {
                BtnFormManageComponents.Visibility = Visibility.Visible;
                BtnFormManageComponents.Content = $"🔬 تفاصيل وبارامترات: {item.TestCode} (Parameters)";
            }

            if (item.DepartmentId.HasValue)
            {
                CmbFormDepartment.SelectedValue = item.DepartmentId.Value;
            }

            if (item.SpecimenTypeId.HasValue)
            {
                CmbFormSpecimenType.SelectedValue = item.SpecimenTypeId.Value;
            }
        }

        private void BtnManageComponents_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TestCatalogDto item)
            {
                OpenComponentsWindow(item.Id);
            }
        }

        private void BtnFormManageComponents_Click(object sender, RoutedEventArgs e)
        {
            if (_editingTest != null && _editingTest.Id > 0)
            {
                OpenComponentsWindow(_editingTest.Id);
            }
        }

        private void OpenComponentsWindow(long testId)
        {
            var win = new ManageComponentsWindow(testId, _catalogService, _currentUser) { Owner = this };
            win.ShowDialog();
            if (win.CatalogChanged)
            {
                CatalogChanged = true;
            }
        }

        private async void BtnSaveTest_Click(object sender, RoutedEventArgs e)
        {
            string code = TxtFormTestCode.Text?.Trim() ?? string.Empty;
            string nameAr = TxtFormNameAr.Text?.Trim() ?? string.Empty;
            string nameEn = TxtFormNameEn.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(code))
            {
                LisMessageBox.Show("يرجى إدخال كود التحليل (مثال: T_TSH).", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtFormTestCode.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(nameEn))
            {
                LisMessageBox.Show("يرجى إدخال اسم التحليل بالإنجليزية.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtFormNameEn.Focus();
                return;
            }

            if (!decimal.TryParse(TxtFormPrice.Text?.Trim(), out var price) || price < 0)
            {
                LisMessageBox.Show("يرجى إدخال سعر صحيح للتحليل.", "قيمة غير صالحة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtFormPrice.Focus();
                TxtFormPrice.SelectAll();
                return;
            }

            long? deptId = CmbFormDepartment.SelectedValue as long?;
            long? specTypeId = CmbFormSpecimenType.SelectedValue as long?;

            try
            {
                var dto = new TestCatalogDto
                {
                    Id = _editingTest?.Id ?? 0,
                    TestCode = code,
                    NameAr = !string.IsNullOrWhiteSpace(nameAr) ? nameAr : nameEn,
                    NameEn = nameEn,
                    DepartmentId = deptId,
                    SpecimenTypeId = specTypeId,
                    Price = price,
                    Active = ChkFormActive.IsChecked == true,
                    SortOrder = 10
                };

                await _catalogService.SaveTestAsync(dto, _currentUser?.Id);
                CatalogChanged = true;

                LisMessageBox.Show(
                    $"تم حفظ بيانات التحليل [{code} - {dto.NameAr}] بنجاح!",
                    "تم الحفظ بنجاح",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information,
                    this);

                ClearForm();
                await LoadTestsGridAsync(TxtSearchTests.Text?.Trim());
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء حفظ التحليل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnDeleteTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TestCatalogDto item)
            {
                var confirm = LisMessageBox.Show(
                    $"هل أنت متأكد من رغبتك في حذف أو إلغاء تفعيل الفحص [{item.TestCode} - {item.NameAr}]؟\n(ملاحظة: إذا كان الفحص مرتبطاً بطلبات أو عينات سابقة، فسيتم تعطيله فقط للحفاظ على سجلات المرضى).",
                    "تأكيد الحذف أو التعطيل",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    this);

                if (confirm == MessageBoxResult.Yes)
                {
                    try
                    {
                        bool deletedPermanently = await _catalogService.DeleteTestAsync(item.Id, _currentUser?.Id);
                        CatalogChanged = true;

                        if (deletedPermanently)
                        {
                            LisMessageBox.Show($"تم حذف التحليل [{item.TestCode}] نهائياً من قاعدة البيانات.", "تم الحذف", MessageBoxButton.OK, MessageBoxImage.Information, this);
                        }
                        else
                        {
                            LisMessageBox.Show($"نظراً لوجود طلبات وفواتير سابقة لهذا التحليل، تم إيقاف تفعيله (Inactive) بنجاح لحماية سجلات المرضى.", "تم التعطيل", MessageBoxButton.OK, MessageBoxImage.Information, this);
                        }

                        if (_editingTest?.Id == item.Id) ClearForm();
                        await LoadTestsGridAsync(TxtSearchTests.Text?.Trim());
                    }
                    catch (Exception ex)
                    {
                        LisMessageBox.Show($"خطأ أثناء حذف التحليل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                    }
                }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
