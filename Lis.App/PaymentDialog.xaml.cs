using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Lis.App
{
    public partial class PaymentDialog : Window
    {
        public bool Confirmed { get; private set; }
        public decimal PaidAmount { get; private set; }
        public string PaymentMethod { get; private set; } = "Cash";

        private readonly decimal _maxBalance;

        public PaymentDialog(long invoiceNo, string patientName, decimal balance, string cashierName)
        {
            InitializeComponent();

            _maxBalance = balance;
            TxtInvoiceInfo.Text = $"فاتورة رقم #{invoiceNo}";
            TxtPatientInfo.Text = $"المريض: {patientName} | المتبقي المطلوب: {balance:#,##0} IQD";
            TxtAmount.Text = balance.ToString("0");
            TxtCashierTill.Text = $"قاصة: {cashierName}";

            Loaded += (s, e) =>
            {
                TxtAmount.Focus();
                TxtAmount.SelectAll();
            };
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void RbCash_Checked(object sender, RoutedEventArgs e)
        {
            PaymentMethod = "Cash";
        }

        private void RbCard_Checked(object sender, RoutedEventArgs e)
        {
            PaymentMethod = "Card";
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (!decimal.TryParse(TxtAmount.Text?.Trim(), out var amount) || amount <= 0)
            {
                LisMessageBox.Show("يرجى إدخال مبلغ دفع صالح أكبر من الصفر.", "قيمة غير صالحة", MessageBoxButton.OK, MessageBoxImage.Warning, this);
                TxtAmount.Focus();
                TxtAmount.SelectAll();
                return;
            }

            if (_maxBalance > 0 && amount > _maxBalance)
            {
                var ask = LisMessageBox.Show(
                    $"المبلغ المدخل ({amount:#,##0} IQD) أكبر من المتبقي المطلوب ({_maxBalance:#,##0} IQD).\nهل ترغب بالمتابعة وقبض كامل المتبقي ({_maxBalance:#,##0} IQD) فقط؟",
                    "تأكيد المبلغ",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    this);

                if (ask != MessageBoxResult.Yes)
                {
                    return;
                }
                amount = _maxBalance;
            }

            PaidAmount = amount;
            Confirmed = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = false;
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                BtnCancel_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                BtnConfirm_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
