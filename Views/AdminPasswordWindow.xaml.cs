using System.Windows;
using System.Windows.Input;
using QrLedgerReconciler.Services;

namespace QrLedgerReconciler.Views;

/// <summary>Cửa sổ nhập mật khẩu admin để mở màn hình quản lý tài khoản.</summary>
public partial class AdminPasswordWindow : Window
{
    private readonly AccountStore _store;
    private int _attempts;

    public bool IsAuthenticated { get; private set; }

    public AdminPasswordWindow(AccountStore store)
    {
        InitializeComponent();
        _store = store;

        if (_store.IsDefaultAdminPassword)
            HintText.Visibility = Visibility.Visible;

        Loaded += (_, _) => PasswordInput.Focus();
    }

    private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Ok_Click(sender, e);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_store.ValidateAdmin(PasswordInput.Password))
        {
            IsAuthenticated = true;
            DialogResult = true;
            return;
        }

        _attempts++;
        PasswordInput.Password = string.Empty;

        if (_attempts >= 3)
        {
            MessageBox.Show("Sai mật khẩu quá 3 lần.", "Thông báo",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            DialogResult = false;
            return;
        }

        ErrorText.Text = $"Sai mật khẩu (còn {3 - _attempts} lần thử).";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}