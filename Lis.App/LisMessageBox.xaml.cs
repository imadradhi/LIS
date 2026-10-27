using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Lis.App
{
    public partial class LisMessageBox : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;
        private readonly MessageBoxButton _buttonType;

        public LisMessageBox(string message, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            InitializeComponent();

            _buttonType = button;
            TxtMessage.Text = message ?? string.Empty;
            TxtTitle.Text = string.IsNullOrWhiteSpace(caption) ? "تنبيه" : caption;

            ApplyThemeAndIcon(icon, caption, button);
            ConfigureButtons(button);

            Loaded += (s, e) =>
            {
                if (BtnOK.Visibility == Visibility.Visible)
                {
                    BtnOK.Focus();
                }
                else if (BtnYes.Visibility == Visibility.Visible)
                {
                    BtnYes.Focus();
                }
            };
        }

        private void ApplyThemeAndIcon(MessageBoxImage icon, string? caption, MessageBoxButton button)
        {
            var isSuccess = caption != null && (caption.Contains("نجاح") || caption.Contains("تم") || caption.Contains("حفظ"));
            
            SolidColorBrush accentBrush;
            SolidColorBrush bgBrush;
            string headerEmoji;
            string bodyEmoji;

            switch (icon)
            {
                case MessageBoxImage.Error: // or MessageBoxImage.Hand / MessageBoxImage.Stop
                    accentBrush = (SolidColorBrush)FindResource("DangerBrush");
                    bgBrush = new SolidColorBrush(Color.FromArgb(45, 239, 68, 68));
                    headerEmoji = "⛔";
                    bodyEmoji = "✕";
                    break;

                case MessageBoxImage.Warning: // or MessageBoxImage.Exclamation
                    accentBrush = (SolidColorBrush)FindResource("WarningBrush");
                    bgBrush = new SolidColorBrush(Color.FromArgb(45, 245, 158, 11));
                    headerEmoji = "⚠️";
                    bodyEmoji = "⚠️";
                    break;

                case MessageBoxImage.Question:
                    accentBrush = (SolidColorBrush)FindResource("AccentBrush");
                    bgBrush = new SolidColorBrush(Color.FromArgb(45, 56, 189, 248));
                    headerEmoji = "❓";
                    bodyEmoji = "❓";
                    break;

                case MessageBoxImage.Information:
                default:
                    if (isSuccess)
                    {
                        accentBrush = (SolidColorBrush)FindResource("SuccessBrush");
                        bgBrush = new SolidColorBrush(Color.FromArgb(45, 16, 185, 129));
                        headerEmoji = "✓";
                        bodyEmoji = "✓";
                    }
                    else
                    {
                        accentBrush = (SolidColorBrush)FindResource("PrimaryBrush");
                        bgBrush = new SolidColorBrush(Color.FromArgb(45, 2, 132, 199));
                        headerEmoji = "ℹ️";
                        bodyEmoji = "ℹ️";
                    }
                    break;
            }

            AccentLine.Background = accentBrush;
            IconContainer.BorderBrush = accentBrush;
            IconContainer.Background = bgBrush;
            TxtHeaderIcon.Text = headerEmoji;
            TxtBodyIcon.Text = bodyEmoji;

            // If warning/destructive question (e.g. Cancel Order, Reject specimen), style Yes button as Danger
            if (icon == MessageBoxImage.Warning && button == MessageBoxButton.YesNo)
            {
                if (TryFindResource("DangerButtonStyle") is Style dangerStyle)
                {
                    BtnYes.Style = dangerStyle;
                }
            }
        }

        private void ConfigureButtons(MessageBoxButton button)
        {
            switch (button)
            {
                case MessageBoxButton.OK:
                    BtnOK.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Collapsed;
                    BtnYes.Visibility = Visibility.Collapsed;
                    BtnNo.Visibility = Visibility.Collapsed;
                    break;

                case MessageBoxButton.OKCancel:
                    BtnOK.Visibility = Visibility.Visible;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnYes.Visibility = Visibility.Collapsed;
                    BtnNo.Visibility = Visibility.Collapsed;
                    break;

                case MessageBoxButton.YesNo:
                    BtnOK.Visibility = Visibility.Collapsed;
                    BtnCancel.Visibility = Visibility.Collapsed;
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    break;

                case MessageBoxButton.YesNoCancel:
                    BtnOK.Visibility = Visibility.Collapsed;
                    BtnCancel.Visibility = Visibility.Visible;
                    BtnYes.Visibility = Visibility.Visible;
                    BtnNo.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Result = (_buttonType == MessageBoxButton.YesNo) ? MessageBoxResult.No : MessageBoxResult.Cancel;
            DialogResult = false;
            Close();
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.OK;
            DialogResult = true;
            Close();
        }

        private void BtnYes_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Yes;
            DialogResult = true;
            Close();
        }

        private void BtnNo_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.No;
            DialogResult = false;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = MessageBoxResult.Cancel;
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                BtnClose_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (BtnOK.Visibility == Visibility.Visible)
                {
                    BtnOK_Click(sender, e);
                    e.Handled = true;
                }
                else if (BtnYes.Visibility == Visibility.Visible)
                {
                    BtnYes_Click(sender, e);
                    e.Handled = true;
                }
            }
        }

        #region Static Show Helper Methods

        public static MessageBoxResult Show(
            string message,
            string caption = "تنبيه",
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.Information,
            Window? owner = null)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess())
            {
                return Application.Current.Dispatcher.Invoke(() => Show(message, caption, button, icon, owner));
            }

            var dialog = new LisMessageBox(message, caption, button, icon);

            if (owner != null && owner.IsVisible)
            {
                dialog.Owner = owner;
            }
            else if (Application.Current != null)
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w.IsActive && w != dialog)
                    {
                        dialog.Owner = w;
                        break;
                    }
                }

                if (dialog.Owner == null && Application.Current.MainWindow != null && Application.Current.MainWindow.IsVisible)
                {
                    dialog.Owner = Application.Current.MainWindow;
                }
            }

            if (dialog.Owner != null)
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog.Result;
        }

        public static MessageBoxResult Show(Window owner, string message)
            => Show(message, "تنبيه", MessageBoxButton.OK, MessageBoxImage.Information, owner);

        public static MessageBoxResult Show(Window owner, string message, string caption)
            => Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information, owner);

        public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton button)
            => Show(message, caption, button, MessageBoxImage.Information, owner);

        public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton button, MessageBoxImage icon)
            => Show(message, caption, button, icon, owner);

        #endregion
    }
}
