using System.Windows;
using System.Windows.Controls;

namespace QrLedgerReconciler.Behaviors;

public static class PasswordBoxBehavior
{
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.RegisterAttached(
            "Password",
            typeof(string),
            typeof(PasswordBoxBehavior),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordChanged));

    public static string GetPassword(DependencyObject d)
        => (string)d.GetValue(PasswordProperty);

    public static void SetPassword(DependencyObject d, string value)
        => d.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordBox passwordBox)
        {
            passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;

            if (passwordBox.Password != (string)e.NewValue)
            {
                passwordBox.Password = (string)e.NewValue;
            }

            passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
        }
    }

    private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
        {
            SetPassword(passwordBox, passwordBox.Password);
        }
    }
}