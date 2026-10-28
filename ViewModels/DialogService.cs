using System;
using System.Windows;
using QrLedgerReconciler.Services;

namespace QrLedgerReconciler.ViewModels;

public interface IDialogService
{
    DateTime? ShowDatePicker(DateTime initialDate);

    /// <summary>Hỏi mật khẩu admin; true nếu đúng.</summary>
    bool ShowAdminLogin(AccountStore store);

    /// <summary>Mở màn hình quản lý tài khoản (modal, theo owner truyền vào).</summary>
    void ShowAccountManager(AccountStore store, Window owner,
                            Action<string> showMessage,
                            Func<string, string, MessageBoxResult> showConfirm);
}

public class DialogService : IDialogService
{
    public DateTime? ShowDatePicker(DateTime initialDate)
    {
        var picker = new DateTimePickerWindow(initialDate);
        return picker.ShowDialog() == true ? picker.SelectedDateTime : null;
    }

    public bool ShowAdminLogin(AccountStore store)
    {
        var dialog = new Views.AdminPasswordWindow(store)
        {
            Owner = Application.Current.MainWindow,
        };

        return dialog.ShowDialog() == true && dialog.IsAuthenticated;
    }

    public void ShowAccountManager(AccountStore store, Window owner,
                                   Action<string> showMessage,
                                   Func<string, string, MessageBoxResult> showConfirm)
    {
        var window = new Views.AccountManagerWindow(store, showMessage, showConfirm)
        {
            Owner = owner,
        };

        window.ShowDialog();
    }
}