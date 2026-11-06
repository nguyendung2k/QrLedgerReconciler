using QrLedgerReconciler.Services;
using QrLedgerReconciler.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace QrLedgerReconciler.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var dialogService = new DialogService();
        var accountStore = AccountStore.Load();
        var settings = SettingsStore.Load();

        _viewModel = new MainViewModel(
            message => Controls.ModernDialog.ShowInfo(this, "Thông báo", message),
            (title, message) => Controls.ModernDialog.ShowConfirm(this, title, message)
                ? MessageBoxResult.Yes
                : MessageBoxResult.No,
            dialogService,
            accountStore,
            settings,
            this
        );

        DataContext = _viewModel;
    }
}