using QrLedgerReconciler.Services;
using System.Windows;

namespace QrLedgerReconciler.Views;

/// <summary>Dialog đổi mật khẩu admin: nhập mật khẩu cũ + mới.</summary>
public partial class ChangeAdminPasswordWindow : Window
{
    private readonly AccountStore _store;

    public ChangeAdminPasswordWindow(AccountStore store)
    {
        InitializeComponent();
        _store = store;
        OldPasswordBox.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!_store.ValidateAdmin(OldPasswordBox.Password))
        {
            ErrorText.Text = "Mật khẩu hiện tại không đúng.";
            OldPasswordBox.Password = string.Empty;
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPasswordBox.Password)
            || NewPasswordBox.Password != ConfirmPasswordBox.Password)
        {
            ErrorText.Text = "Mật khẩu mới không khớp hoặc rỗng.";
            return;
        }

        _store.SetAdminPassword(NewPasswordBox.Password);
        _store.Save();

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}