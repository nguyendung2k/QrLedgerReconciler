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

        _viewModel = new MainViewModel(
            message => MessageBox.Show(message, "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information),
            (title, message) => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question),
            dialogService
        );

        DataContext = _viewModel;

    }

    // PasswordBox không bind được Password (lý do bảo mật) nên đồng bộ tay.
    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box)
            _viewModel.Password = box.Password;
    }
}