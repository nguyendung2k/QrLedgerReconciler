using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QrLedgerReconciler.Controls;

/// <summary>
/// Hộp thoại xác nhận / thông báo theo chuẩn UI mới (thay cho MessageBox mặc định).
/// </summary>
public partial class ModernDialog : Window
{
    private ModernDialog()
    {
        InitializeComponent();
    }

    /// <summary>Hộp thoại thông tin, chỉ có nút "Đóng".</summary>
    public static void ShowInfo(Window? owner, string title, string message)
        => ShowSimple(owner, title, message, "AccentSoftBrush");

    /// <summary>Hộp thoại cảnh báo, chỉ có nút "Đóng".</summary>
    public static void ShowWarning(Window? owner, string title, string message)
        => ShowSimple(owner, title, message, "AccentSoftBrush");

    /// <summary>Hộp thoại lỗi, chỉ có nút "Đóng".</summary>
    public static void ShowError(Window? owner, string title, string message)
        => ShowSimple(owner, title, message, "AccentSoftBrush");

    /// <summary>
    /// Hộp thoại xác nhận 2 nút: "Hủy" + "Xác nhận". Trả về true khi bấm Xác nhận.
    /// </summary>
    public static bool ShowConfirm(Window? owner, string title, string message)
    {
        var dialog = new ModernDialog
        {
            TitleText = { Text = title },
            MessageText = { Text = message },
        };

        AddButton(dialog, "Hủy", "AccentSoftBrush", "TextPrimaryBrush", false, () => dialog.DialogResult = false);
        AddButton(dialog, "Xác nhận", "AccentBrush", "White", true, () => dialog.DialogResult = true);
        return Show(dialog, owner) == true;
    }

    private static void ShowSimple(Window? owner, string title, string message, string accentKey)
    {
        var dialog = new ModernDialog
        {
            TitleText = { Text = title },
            MessageText = { Text = message },
        };

        AddButton(dialog, "Đóng", accentKey, "TextPrimaryBrush", true, () => dialog.DialogResult = true);
        Show(dialog, owner);
    }

    private static void AddButton(ModernDialog dialog, string text,
                                  string bgKey, string fgKey, bool isDefault,
                                  Action onClick)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)dialog.FindResource("DialogButton"),
            Background = (Brush)dialog.FindResource(bgKey),
            Foreground = fgKey == "White"
                ? Brushes.White
                : (Brush)dialog.FindResource(fgKey),
            IsDefault = isDefault,
            IsCancel = false,
        };

        button.Click += (_, _) => onClick();
        dialog.ButtonRow.Children.Add(button);
    }

    private static bool? Show(ModernDialog dialog, Window? owner)
    {
        dialog.Owner = owner ?? Application.Current?.MainWindow;
        return dialog.ShowDialog();
    }
}
