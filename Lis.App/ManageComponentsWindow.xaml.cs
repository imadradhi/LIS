using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Lis.Core.Services;

namespace Lis.App
{
    public partial class ManageComponentsWindow : Window
    {
        private readonly long _testId;
        private readonly ITestCatalogService _catalogService;
        private readonly UserSession? _currentUser;

        private TestCatalogDto? _currentTest;
        private List<ComponentDto> _components = new List<ComponentDto>();
        private ComponentDto? _selectedComponent;
        private ComponentDto? _editingComponent;
        private List<InstrumentSimpleDto> _instruments = new List<InstrumentSimpleDto>();

        public bool CatalogChanged { get; private set; }

        public ManageComponentsWindow(long testId, ITestCatalogService catalogService, UserSession? currentUser = null)
        {
            InitializeComponent();
            _testId = testId;
            _catalogService = catalogService;
            _currentUser = currentUser;

            Loaded += async (s, e) => await InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                _currentTest = await _catalogService.GetTestByIdAsync(_testId);
                if (_currentTest != null)
                {
                    TxtHeaderTestCode.Text = _currentTest.TestCode;
                    TxtHeaderTestDept.Text = _currentTest.DepartmentName ?? "عام (General)";
                    string arPart = !string.IsNullOrWhiteSpace(_currentTest.NameAr) ? $"{_currentTest.NameAr} - " : "";
                    TxtHeaderTestSubtitle.Text = $"{arPart}{_currentTest.NameEn} • أنبوب {_currentTest.SpecimenTypeName ?? "دم عام"} • السعر: {_currentTest.FormattedPrice}";
                }

                // Load Instruments
                _instruments = (await _catalogService.GetInstrumentsAsync()).ToList();
                var instList = new List<InstrumentSimpleDto>
                {
                    new InstrumentSimpleDto { Id = 0, Code = "", Name = "— بدون ربط بجهاز محدد —" }
                };
                instList.AddRange(_instruments);
                CmbCompInstrument.ItemsSource = instList;
                CmbCompInstrument.SelectedIndex = 0;

                CmbMappingInstrument.ItemsSource = _instruments;
                if (_instruments.Count > 0) CmbMappingInstrument.SelectedIndex = 0;

                await LoadComponentsAsync();
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء تحميل بيانات التحليل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async Task LoadComponentsAsync(long? selectCompId = null)
        {
            try
            {
                _components = (await _catalogService.GetComponentsForTestAsync(_testId)).ToList();
                ApplyComponentFilter(selectCompId);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في تحميل قائمة البارامترات: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private void TxtCompSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyComponentFilter();
        }

        private void ApplyComponentFilter(long? selectCompId = null)
        {
            string q = TxtCompSearch?.Text?.Trim() ?? string.Empty;
            List<ComponentDto> filtered;

            if (string.IsNullOrWhiteSpace(q))
            {
                filtered = _components;
            }
            else
            {
                filtered = _components.Where(c =>
                    (c.DisplayCode != null && c.DisplayCode.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (c.NameEn != null && c.NameEn.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (c.NameAr != null && c.NameAr.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (c.Unit != null && c.Unit.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            GridComponents.ItemsSource = filtered;
            TxtComponentsCountBadge.Text = $"{filtered.Count} بارامتر";

            if (filtered.Count > 0)
            {
                ComponentDto? toSelect = null;
                if (selectCompId.HasValue)
                {
                    toSelect = filtered.FirstOrDefault(c => c.Id == selectCompId.Value);
                }
                if (toSelect == null)
                {
                    toSelect = filtered.FirstOrDefault();
                }
                GridComponents.SelectedItem = toSelect;
            }
            else
            {
                _selectedComponent = null;
                GridReferenceRanges.ItemsSource = null;
                if (GridInstrumentMappings != null) GridInstrumentMappings.ItemsSource = null;
                TxtSelectedComponentHeader.Text = "🎯 القيم المرجعية الطبيعية (Normal Ranges)";
                TxtSelectedComponentSubtitle.Text = "لا توجد نتائج مطابقة لبحثك.";
            }
        }

        private async void GridComponents_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridComponents.SelectedItem is ComponentDto comp)
            {
                _selectedComponent = comp;
                TxtSelectedComponentHeader.Text = $"🎯 المدى المرجعي الطبيعي لـ: {comp.DisplayCode} ({comp.NameEn})";
                TxtSelectedComponentSubtitle.Text = $"الوحدة: {comp.Unit ?? "بدون"} • الكسور: {comp.Decimals} • اضغط حذف 🗑️ لإزالة أي مدى.";
                if (TxtSelectedComponentInstHeader != null)
                {
                    TxtSelectedComponentInstHeader.Text = $"📡 أجهزة التحليل المرتبطة بـ: {comp.DisplayCode} ({comp.NameEn})";
                }
                await LoadReferenceRangesAsync(comp.Id);
                await LoadInstrumentMappingsAsync(comp.Id);
            }
        }

        private async Task LoadReferenceRangesAsync(long componentId)
        {
            try
            {
                var ranges = await _catalogService.GetReferenceRangesAsync(componentId);
                GridReferenceRanges.ItemsSource = ranges;
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في تحميل القيم المرجعية: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private void BtnStartNewComponent_Click(object sender, RoutedEventArgs e)
        {
            ClearComponentForm();
            TxtCompDisplayCode.Focus();
        }

        private void BtnClearComponentForm_Click(object sender, RoutedEventArgs e)
        {
            ClearComponentForm();
        }

        private void ClearComponentForm()
        {
            _editingComponent = null;
            TxtComponentFormTitle.Text = "➕ إضافة بارامتر جديد (New Parameter)";
            TxtCompDisplayCode.Text = string.Empty;
            TxtCompNameEn.Text = string.Empty;
            TxtCompNameAr.Text = string.Empty;
            TxtCompUnit.Text = string.Empty;
            CmbCompDecimals.SelectedIndex = 1; // 1 decimal place
            CmbCompInstrument.SelectedIndex = 0;
            TxtCompInstrumentCode.Text = string.Empty;
        }

        private void BtnEditComponent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ComponentDto comp)
            {
                _editingComponent = comp;
                TxtComponentFormTitle.Text = $"✏️ تعديل بارامتر: {comp.DisplayCode} ({comp.NameEn})";
                TxtCompDisplayCode.Text = comp.DisplayCode;
                TxtCompNameEn.Text = comp.NameEn;
                TxtCompNameAr.Text = comp.NameAr ?? string.Empty;
                TxtCompUnit.Text = comp.Unit ?? string.Empty;

                int decIdx = Math.Clamp((int)comp.Decimals, 0, 3);
                CmbCompDecimals.SelectedIndex = decIdx;

                if (comp.InstrumentId.HasValue && comp.InstrumentId.Value > 0)
                {
                    CmbCompInstrument.SelectedValue = comp.InstrumentId.Value;
                }
                else
                {
                    CmbCompInstrument.SelectedIndex = 0;
                }
                TxtCompInstrumentCode.Text = comp.InstrumentTestCode ?? string.Empty;
                TxtCompDisplayCode.Focus();
            }
        }

        private async void BtnSaveComponent_Click(object sender, RoutedEventArgs e)
        {
            string displayCode = TxtCompDisplayCode.Text.Trim();
            string nameEn = TxtCompNameEn.Text.Trim();
            string nameAr = TxtCompNameAr.Text.Trim();
            string unit = TxtCompUnit.Text.Trim();

            if (string.IsNullOrWhiteSpace(displayCode))
            {
                LisMessageBox.Show("يرجى إدخال رمز البارامتر المختصر (Display Code) مثل WBC أو HGB.", "حقل مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtCompDisplayCode.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(nameEn))
            {
                LisMessageBox.Show("يرجى إدخال اسم البارامتر بالإنجليزية (Name En).", "حقل مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtCompNameEn.Focus();
                return;
            }

            short decimals = 1;
            if (CmbCompDecimals.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                short.TryParse(item.Tag.ToString(), out decimals);
            }

            long? instId = null;
            if (CmbCompInstrument.SelectedValue is long idVal && idVal > 0)
            {
                instId = idVal;
            }

            string instCode = TxtCompInstrumentCode.Text.Trim();

            try
            {
                var dto = _editingComponent ?? new ComponentDto
                {
                    TestId = _testId,
                    SortOrder = _components.Count + 1
                };

                dto.DisplayCode = displayCode;
                dto.NameEn = nameEn;
                dto.NameAr = string.IsNullOrWhiteSpace(nameAr) ? null : nameAr;
                dto.Unit = string.IsNullOrWhiteSpace(unit) ? null : unit;
                dto.Decimals = decimals;
                dto.InstrumentId = instId;
                dto.InstrumentTestCode = string.IsNullOrWhiteSpace(instCode) ? null : instCode;

                long savedId = await _catalogService.SaveComponentAsync(dto, _currentUser?.Id);
                CatalogChanged = true;

                ClearComponentForm();
                await LoadComponentsAsync(savedId);

                LisMessageBox.Show($"تم حفظ البارامتر [{displayCode}] بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في حفظ البارامتر: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnDeleteComponent_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ComponentDto comp)
            {
                var prompt = LisMessageBox.Show(
                    $"هل أنت متأكد من رغبتك في حذف البارامتر [{comp.DisplayCode} - {comp.NameEn}] مع جميع قيمه المرجعية؟",
                    "تأكيد الحذف",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    this);

                if (prompt != MessageBoxResult.Yes) return;

                try
                {
                    await _catalogService.DeleteComponentAsync(comp.Id, _currentUser?.Id);
                    CatalogChanged = true;
                    ClearComponentForm();
                    await LoadComponentsAsync();

                    LisMessageBox.Show($"تم حذف البارامتر [{comp.DisplayCode}] بنجاح.", "تم الحذف", MessageBoxButton.OK, MessageBoxImage.Information, this);
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"تعذر حذف البارامتر: {ex.Message}", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                }
            }
        }

        private void BtnClearRangeForm_Click(object sender, RoutedEventArgs e)
        {
            TxtRangeLow.Text = string.Empty;
            TxtRangeHigh.Text = string.Empty;
            TxtRangeCritLow.Text = string.Empty;
            TxtRangeCritHigh.Text = string.Empty;
            TxtRangeNote.Text = string.Empty;
        }

        private async void BtnSaveRange_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedComponent == null)
            {
                LisMessageBox.Show("يرجى اختيار بارامتر أولاً من الجدول الأيسر لإضافة المدى المرجعي له.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            string sex = (CmbRangeSex.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "B";
            string ageUnit = (CmbRangeAgeUnit.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Years";

            decimal.TryParse(TxtRangeAgeMin.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal ageMin);
            decimal? ageMax = null;
            if (decimal.TryParse(TxtRangeAgeMax.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal aMaxVal))
            {
                ageMax = aMaxVal;
            }

            decimal? low = null;
            if (decimal.TryParse(TxtRangeLow.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal lowVal))
            {
                low = lowVal;
            }

            decimal? high = null;
            if (decimal.TryParse(TxtRangeHigh.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal highVal))
            {
                high = highVal;
            }

            decimal? critLow = null;
            if (decimal.TryParse(TxtRangeCritLow.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal clVal))
            {
                critLow = clVal;
            }

            decimal? critHigh = null;
            if (decimal.TryParse(TxtRangeCritHigh.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal chVal))
            {
                critHigh = chVal;
            }

            string? note = string.IsNullOrWhiteSpace(TxtRangeNote.Text) ? null : TxtRangeNote.Text.Trim();

            if (!low.HasValue && !high.HasValue)
            {
                LisMessageBox.Show("يرجى إدخال الحد الأدنى أو الحد الأعلى الطبيعي (Min / Max).", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtRangeLow.Focus();
                return;
            }

            try
            {
                var rangeDto = new ReferenceRangeDto
                {
                    ComponentId = _selectedComponent.Id,
                    Sex = sex,
                    AgeMin = ageMin,
                    AgeMax = ageMax,
                    AgeUnit = ageUnit,
                    Low = low,
                    High = high,
                    CriticalLow = critLow,
                    CriticalHigh = critHigh,
                    Note = note
                };

                await _catalogService.SaveReferenceRangeAsync(rangeDto, _currentUser?.Id);
                CatalogChanged = true;

                BtnClearRangeForm_Click(sender, e);
                await LoadReferenceRangesAsync(_selectedComponent.Id);
                await LoadComponentsAsync(_selectedComponent.Id);

                LisMessageBox.Show($"تم حفظ المدى الطبيعي للبارامتر [{_selectedComponent.DisplayCode}] بنجاح.", "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في حفظ المدى المرجعي: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnDeleteRange_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ReferenceRangeDto range)
            {
                var prompt = LisMessageBox.Show("هل أنت متأكد من رغبتك في حذف هذا المدى الطبيعي؟", "تأكيد الحذف", MessageBoxButton.YesNo, MessageBoxImage.Question, this);
                if (prompt != MessageBoxResult.Yes) return;

                try
                {
                    await _catalogService.DeleteReferenceRangeAsync(range.Id, _currentUser?.Id);
                    CatalogChanged = true;
                    if (_selectedComponent != null)
                    {
                        await LoadReferenceRangesAsync(_selectedComponent.Id);
                        await LoadComponentsAsync(_selectedComponent.Id);
                    }
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"خطأ في حذف المدى: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private async Task LoadInstrumentMappingsAsync(long componentId)
        {
            if (GridInstrumentMappings == null) return;
            try
            {
                var mappings = (await _catalogService.GetInstrumentMappingsForComponentAsync(componentId)).ToList();
                GridInstrumentMappings.ItemsSource = mappings;
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في استرجاع قنوات الأجهزة: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnAddInstrumentMapping_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedComponent == null)
            {
                LisMessageBox.Show("يرجى اختيار بارامتر أولاً من الجدول الأيسر لربط الجهاز به.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            if (CmbMappingInstrument.SelectedItem is not InstrumentSimpleDto inst || inst.Id <= 0)
            {
                LisMessageBox.Show("يرجى اختيار جهاز تحليل من القائمة المنسدلة.", "حقل مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                CmbMappingInstrument.Focus();
                return;
            }

            string testCode = TxtMappingInstrumentTestCode.Text.Trim();
            if (string.IsNullOrWhiteSpace(testCode))
            {
                LisMessageBox.Show("يرجى إدخال الكود الذي يرسله هذا الجهاز (Test Code) مثل HbA1c أو A1c أو رقم القناة.", "حقل مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtMappingInstrumentTestCode.Focus();
                return;
            }

            try
            {
                await _catalogService.AddOrUpdateInstrumentMappingAsync(
                    inst.Id,
                    _testId,
                    _selectedComponent.Id,
                    testCode,
                    userId: _currentUser?.Id);

                CatalogChanged = true;
                TxtMappingInstrumentTestCode.Clear();
                await LoadInstrumentMappingsAsync(_selectedComponent.Id);

                LisMessageBox.Show($"تم ربط الجهاز [{inst.Name}] مع البارامتر [{_selectedComponent.DisplayCode}] بالكود [{testCode}] بنجاح.", "تم الربط", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ في حفظ ربط الجهاز: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
            }
        }

        private async void BtnDeleteInstrumentMapping_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is InstrumentMappingItemDto mapping)
            {
                var prompt = LisMessageBox.Show(
                    $"هل أنت متأكد من إلغاء ربط الجهاز [{mapping.InstrumentName}] مع الكود [{mapping.InstrumentTestCode}] لهذا البارامتر؟",
                    "تأكيد إلغاء الربط",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    this);

                if (prompt != MessageBoxResult.Yes) return;

                try
                {
                    await _catalogService.DeleteInstrumentMappingAsync(mapping.InstrumentId, mapping.ComponentId, _currentUser?.Id);
                    CatalogChanged = true;
                    if (_selectedComponent != null)
                    {
                        await LoadInstrumentMappingsAsync(_selectedComponent.Id);
                    }
                    LisMessageBox.Show("تم إلغاء ربط الجهاز بنجاح.", "تم الإلغاء", MessageBoxButton.OK, MessageBoxImage.Information, this);
                }
                catch (Exception ex)
                {
                    LisMessageBox.Show($"خطأ في إلغاء ربط الجهاز: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error, this);
                }
            }
        }

        private async void BtnUnlinkCompInstrument_Click(object sender, RoutedEventArgs e)
        {
            if (CmbCompInstrument.SelectedValue is long instId && instId > 0)
            {
                var instName = (CmbCompInstrument.SelectedItem as InstrumentSimpleDto)?.Name ?? "الجهاز";
                var prompt = LisMessageBox.Show(
                    $"هل تريد إزالة وحذف ربط [{instName}] من هذا البارامتر؟",
                    "تأكيد إزالة ربط الجهاز",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    this);

                if (prompt != MessageBoxResult.Yes) return;

                if (_editingComponent != null && _editingComponent.Id > 0)
                {
                    try
                    {
                        await _catalogService.DeleteInstrumentMappingAsync(instId, _editingComponent.Id, _currentUser?.Id);
                        CatalogChanged = true;
                        await LoadInstrumentMappingsAsync(_editingComponent.Id);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to delete mapping: {ex.Message}");
                    }
                }

                LisMessageBox.Show("تمت إزالة ربط الجهاز من هذا البارامتر بنجاح.", "تم الحذف", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }

            CmbCompInstrument.SelectedIndex = 0; // "— بدون ربط بجهاز محدد —"
            TxtCompInstrumentCode.Clear();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
