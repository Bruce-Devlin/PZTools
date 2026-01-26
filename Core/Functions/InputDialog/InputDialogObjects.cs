using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace PZTools.Core.Functions.InputDialog
{
    public static class BooleanBoxes
    {
        public static IValueConverter VisibilityConverter { get; } = new BoolToVisibilityConverter();

        private sealed class BoolToVisibilityConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => value is Visibility v && v == Visibility.Visible;
        }
    }

    public sealed class BooleanNegationToVisibilityConverter : IValueConverter
    {
        public static BooleanNegationToVisibilityConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && !b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class StringNullOrEmptyToVisibilityConverter : IValueConverter
    {
        public static StringNullOrEmptyToVisibilityConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Enables binding PasswordBox.Password (which is not a dependency property) to a VM string.
    /// </summary>
    public static class PasswordBoxBinder
    {
        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached("BindPassword", typeof(bool), typeof(PasswordBoxBinder),
                new PropertyMetadata(false, OnBindPasswordChanged));

        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxBinder),
                new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

        private static readonly DependencyProperty IsUpdatingProperty =
            DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(PasswordBoxBinder),
                new PropertyMetadata(false));

        public static void SetBindPassword(DependencyObject dp, bool value) => dp.SetValue(BindPasswordProperty, value);
        public static bool GetBindPassword(DependencyObject dp) => (bool)dp.GetValue(BindPasswordProperty);

        public static void SetBoundPassword(DependencyObject dp, string value) => dp.SetValue(BoundPasswordProperty, value);
        public static string GetBoundPassword(DependencyObject dp) => (string)dp.GetValue(BoundPasswordProperty);

        private static void OnBindPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox pb) return;

            if ((bool)e.OldValue)
                pb.PasswordChanged -= HandlePasswordChanged;

            if ((bool)e.NewValue)
                pb.PasswordChanged += HandlePasswordChanged;
        }

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not PasswordBox pb) return;

            pb.PasswordChanged -= HandlePasswordChanged;

            var isUpdating = (bool)pb.GetValue(IsUpdatingProperty);
            if (!isUpdating)
                pb.Password = e.NewValue as string ?? string.Empty;

            pb.PasswordChanged += HandlePasswordChanged;
        }

        private static void HandlePasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is not PasswordBox pb) return;

            pb.SetValue(IsUpdatingProperty, true);
            SetBoundPassword(pb, pb.Password);
            pb.SetValue(IsUpdatingProperty, false);
        }
    }
}
