using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Lis.Core.Services;

namespace Lis.App
{
    public class SelectableTestItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public long Id { get; set; }
        public string TestCode { get; set; } = string.Empty;
        public string NameEn { get; set; } = string.Empty;
        public string? NameAr { get; set; }
        public string DisplayName => !string.IsNullOrWhiteSpace(NameAr) ? $"{NameAr} ({NameEn})" : NameEn;
        public string? DepartmentName { get; set; }
        public string? SpecimenTypeName { get; set; }
        public decimal Price { get; set; }
        public string FormattedPrice => $"{Price:#,##0} IQD";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class SelectTestsDialog : Window
    {
        private readonly List<SelectableTestItem> _allTests;
        private readonly ObservableCollection<SelectableTestItem> _filteredView = new();

        public List<long> SelectedTestIds => _allTests.Where(t => t.IsSelected).Select(t => t.Id).ToList();

        public SelectTestsDialog(IEnumerable<TestCatalogDto> tests)
        {
            InitializeComponent();

            _allTests = tests.Select(t =>
            {
                var item = new SelectableTestItem
                {
                    Id = t.Id,
                    TestCode = t.TestCode,
                    NameEn = t.NameEn,
                    NameAr = t.NameAr,
                    DepartmentName = t.DepartmentName,
                    SpecimenTypeName = t.SpecimenTypeName,
                    Price = t.Price,
                    IsSelected = false
                };
                item.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(SelectableTestItem.IsSelected))
                    {
                        UpdateSummary();
                    }
                };
                return item;
            }).OrderBy(t => t.TestCode).ToList();

            GridCatalogTests.ItemsSource = _filteredView;
            ApplyFilter(string.Empty);
            UpdateSummary();
        }

        private void ApplyFilter(string search)
        {
            _filteredView.Clear();
            var trimmed = search.Trim();

            IEnumerable<SelectableTestItem> source = _allTests;
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                source = _allTests.Where(t =>
                    t.TestCode.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    t.NameEn.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(t.NameAr) && t.NameAr.Contains(trimmed, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(t.DepartmentName) && t.DepartmentName.Contains(trimmed, StringComparison.OrdinalIgnoreCase)));
            }

            foreach (var item in source)
            {
                _filteredView.Add(item);
            }

            BtnClearSearch.Visibility = string.IsNullOrWhiteSpace(trimmed) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void UpdateSummary()
        {
            var selected = _allTests.Where(t => t.IsSelected).ToList();
            int count = selected.Count;
            decimal total = selected.Sum(t => t.Price);
            TxtSelectionSummary.Text = $"المحدد: {count} فحص | إجمالي القيمة: {total:#,##0} IQD";
            BtnConfirm.IsEnabled = count > 0;
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter(TxtSearch.Text);
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = string.Empty;
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredView)
            {
                item.IsSelected = true;
            }
            UpdateSummary();
        }

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredView)
            {
                item.IsSelected = false;
            }
            UpdateSummary();
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!SelectedTestIds.Any())
            {
                LisMessageBox.Show("يرجى تحديد فحص واحد على الأقل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
