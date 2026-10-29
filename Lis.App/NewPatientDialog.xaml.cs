using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Lis.Core.Data;
using Lis.Core.Models;
using Lis.Core.Services;

namespace Lis.App
{
    public partial class NewPatientDialog : Window
    {
        private readonly IPatientService _patientService;
        private readonly IOrderService _orderService;
        private readonly IBillingService _billingService;
        private readonly ILabSettingsService _labSettingsService;
        private readonly IDbConnectionFactory _connectionFactory;
        private readonly UserSession? _currentUser;

        private Patient? _selectedPatient;

        public NewPatientDialog(
            IPatientService patientService,
            IOrderService orderService,
            IBillingService billingService,
            ILabSettingsService labSettingsService,
            IDbConnectionFactory connectionFactory,
            UserSession? currentUser)
        {
            InitializeComponent();

            _patientService = patientService;
            _orderService = orderService;
            _billingService = billingService;
            _labSettingsService = labSettingsService;
            _connectionFactory = connectionFactory;
            _currentUser = currentUser;

            Loaded += (s, e) => TxtPatientNameAr.Focus();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private async void TxtPatientSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = TxtPatientSearch.Text.Trim();
            BtnClearSearch.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

            if (query.Length < 2)
            {
                BorderSearchResults.Visibility = Visibility.Collapsed;
                ListSearchResults.ItemsSource = null;
                return;
            }

            try
            {
                var patients = await _patientService.SearchPatientsAsync(query, 10);
                var list = patients.ToList();
                if (list.Count > 0)
                {
                    ListSearchResults.ItemsSource = list;
                    BorderSearchResults.Visibility = Visibility.Visible;
                }
                else
                {
                    BorderSearchResults.Visibility = Visibility.Collapsed;
                }
            }
            catch
            {
                BorderSearchResults.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtPatientSearch.Clear();
            BorderSearchResults.Visibility = Visibility.Collapsed;
            ListSearchResults.ItemsSource = null;
        }

        private void ListSearchResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListSearchResults.SelectedItem is Patient patient)
            {
                SelectExistingPatient(patient);
                BorderSearchResults.Visibility = Visibility.Collapsed;
                TxtPatientSearch.Clear();
            }
        }

        private void SelectExistingPatient(Patient patient)
        {
            _selectedPatient = patient;

            TxtPatientNameAr.Text = patient.FullNameAr;
            CmbPatientSex.SelectedIndex = string.Equals(patient.Sex, "F", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            DpDateOfBirth.SelectedDate = patient.DateOfBirth;
            TxtPatientPhone.Text = patient.Phone ?? string.Empty;

            TxtBannerName.Text = patient.FullNameAr;
            TxtBannerMrn.Text = $"رقم الملف: {patient.Mrn} | الجنس: {(patient.Sex == "F" ? "أنثى" : "ذكر")}";
            BorderSelectedPatientBanner.Visibility = Visibility.Visible;

            TxtAssignedMrn.Text = $"🟢 مسجل (MRN: {patient.Mrn})";
            if (TryFindResource("SuccessBrush") is SolidColorBrush successBrush)
            {
                TxtAssignedMrn.Foreground = successBrush;
            }
        }

        private void BtnUnselectPatient_Click(object sender, RoutedEventArgs e)
        {
            _selectedPatient = null;
            BorderSelectedPatientBanner.Visibility = Visibility.Collapsed;

            TxtAssignedMrn.Text = "تلقائي جديد (Auto)";
            if (TryFindResource("AccentBrush") is SolidColorBrush accentBrush)
            {
                TxtAssignedMrn.Foreground = accentBrush;
            }

            TxtPatientNameAr.Clear();
            CmbPatientSex.SelectedIndex = 0;
            DpDateOfBirth.SelectedDate = null;
            TxtCalculatedAge.Text = "-- سنة";
            TxtPatientPhone.Clear();
            TxtReferringDoctor.Clear();

            TxtPatientNameAr.Focus();
        }

        private void DpDateOfBirth_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DpDateOfBirth.SelectedDate.HasValue)
            {
                try
                {
                    var ageInfo = _patientService.CalculateAge(DpDateOfBirth.SelectedDate.Value);
                    TxtCalculatedAge.Text = ageInfo.FormattedText;
                }
                catch
                {
                    TxtCalculatedAge.Text = "-- سنة";
                }
            }
            else
            {
                TxtCalculatedAge.Text = "-- سنة";
            }
        }

        private async void BtnProceedToTests_Click(object sender, RoutedEventArgs e)
        {
            string nameAr = TxtPatientNameAr.Text.Trim();
            if (string.IsNullOrWhiteSpace(nameAr))
            {
                LisMessageBox.Show("يرجى إدخال اسم المراجع كاملاً بالعربية.", "حقل مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtPatientNameAr.Focus();
                return;
            }

            if (!DpDateOfBirth.SelectedDate.HasValue)
            {
                LisMessageBox.Show("يرجى اختيار تاريخ ميلاد المراجع لحساب المديات الطبيعية بدقة.", "حقل مطلوب", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // 1. Create patient if not existing
                Patient? patient = _selectedPatient;
                if (patient == null)
                {
                    patient = new Patient
                    {
                        FullNameAr = nameAr,
                        Sex = CmbPatientSex.SelectedIndex == 1 ? "F" : "M",
                        DateOfBirth = DpDateOfBirth.SelectedDate.Value,
                        Phone = string.IsNullOrWhiteSpace(TxtPatientPhone.Text) ? null : TxtPatientPhone.Text.Trim()
                    };

                    patient = await _patientService.CreatePatientAsync(patient, _currentUser?.Id);
                }

                if (patient == null)
                {
                    LisMessageBox.Show("تعذر إنشاء أو تحديد المراجع.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string? refDoc = string.IsNullOrWhiteSpace(TxtReferringDoctor.Text) ? null : TxtReferringDoctor.Text.Trim();
                string? notes = null;

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
                {
                    Owner = this
                };

                bool? dlgResult = addTestsDlg.ShowDialog();

                if (dlgResult == true)
                {
                    DialogResult = true;
                    Close();
                }
            }
            catch (Exception ex)
            {
                LisMessageBox.Show($"خطأ أثناء معالجة بيانات المراجع: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

