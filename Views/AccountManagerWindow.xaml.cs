using QrLedgerReconciler.Models;
using QrLedgerReconciler.Services;
using QrLedgerReconciler.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace QrLedgerReconciler.Views;

public partial class AccountManagerWindow : Window
{
    private readonly AccountManagerViewModel _viewModel;

    public AccountManagerWindow(AccountStore store,
                               SettingsStore settings,
                               Action<string> showMessage,
                               Func<string, string, MessageBoxResult> showConfirm)
    {
        InitializeComponent();
        _viewModel = new AccountManagerViewModel(store, settings, showMessage, showConfirm);
        DataContext = _viewModel;
    }

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountTab is null || HeadlessTab is null || SettingsTab is null) return;

        var control = (TabControl)sender;
        AccountTab.Visibility = control.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        HeadlessTab.Visibility = control.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        SettingsTab.Visibility = control.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ChangeAdminPassword_Click(object sender, RoutedEventArgs e)
    {
        var store = ((AccountManagerViewModel)DataContext).Store;
        var dialog = new ChangeAdminPasswordWindow(store);

        if (dialog.ShowDialog() == true)
            Controls.ModernDialog.ShowInfo(this, "Thông báo", "Đã đổi mật khẩu admin.");
    }

    /// <summary>Nút "⋯": mở menu ngữ cảnh bằng chuột trái (ContextMenu mặc định chỉ mở bằng chuột phải).</summary>
    private void DotsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (button.ContextMenu is null) return;

        // Gán ngữ cảnh dòng cho menu (menu mở thủ công nên không tự thừa hưởng DataContext).
        button.ContextMenu.DataContext = button.DataContext;

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.DataContext is not UserAccount account) return;

        ((AccountManagerViewModel)DataContext).DeleteAccount(account);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Có thay đổi chưa lưu → hỏi trước khi đóng để tránh mất dữ liệu.
        if (_viewModel.IsDirty)
        {
            var result = Controls.ModernDialog.ShowConfirm(
                this,
                "Thay đổi chưa lưu",
                "Có thay đổi chưa được lưu.\n\nBạn có muốn lưu trước khi đóng?");

            if (!result)
            {
                e.Cancel = true;
                return;
            }

            if (!_viewModel.SaveCommand.CanExecute(null))
            {
                // Có thay đổi ở tab Chạy ẩn nhưng chưa lưu → tự lưu cấu hình này.
                _viewModel.SaveHeadlessCommand.Execute(null);
            }
        }
    }
}