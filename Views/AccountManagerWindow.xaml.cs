using QrLedgerReconciler.Services;
using QrLedgerReconciler.ViewModels;
using System.Windows;

namespace QrLedgerReconciler.Views;

public partial class AccountManagerWindow : Window
{
    public AccountManagerWindow(AccountStore store,
                               Action<string> showMessage,
                               Func<string, string, MessageBoxResult> showConfirm)
    {
        InitializeComponent();
        DataContext = new AccountManagerViewModel(store, showMessage, showConfirm);
    }

    private void ChangeAdminPassword_Click(object sender, RoutedEventArgs e)
    {
        var store = ((AccountManagerViewModel)DataContext).Store;
        var dialog = new ChangeAdminPasswordWindow(store);

        if (dialog.ShowDialog() == true)
            MessageBox.Show("Đã đổi mật khẩu admin.", "Thông báo",
                MessageBoxButton.OK, MessageBoxImage.Information);
    }
}