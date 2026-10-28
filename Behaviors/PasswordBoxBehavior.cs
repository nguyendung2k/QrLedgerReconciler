using System.Windows;
using System.Windows.Controls;

namespace QrLedgerReconciler.Behaviors;

/// <summary>
/// Cho phép bind <c>PasswordBox.Password</c> (WPF không cho bind trực tiếp vì lý do bảo mật).
///
/// Lưu ý quan trọng: giá trị mặc định của DP là <c>null</c> (không phải chuỗi rỗng) để
/// lần bind đầu tiên vào <c>""</c> vẫn được coi là có thay đổi — nhờ vậy callback chạy
/// và handler <c>PasswordChanged</c> được gắn. Nếu để mặc định <c>string.Empty</c> thì
/// callback không bao giờ chạy lúc khởi tạo, handler không được gắn, người dùng gõ mật
/// khẩu mà ViewModel vẫn thấy chuỗi rỗng (nút Lưu báo "Nhập mật khẩu").
/// </summary>
public static class PasswordBoxBehavior
{
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.RegisterAttached(
            "Password",
            typeof(string),
            typeof(PasswordBoxBehavior),
            new FrameworkPropertyMetadata(null,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordChanged));

    public static string GetPassword(DependencyObject d)
        => (string?)d.GetValue(PasswordProperty) ?? string.Empty;

    public static void SetPassword(DependencyObject d, string value)
        => d.SetValue(PasswordProperty, value);

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox passwordBox) return;

        // Gắn handler trước mọi kiểm tra: lần đầu giá trị đi từ null → "" thì
        // vẫn phải có handler để bắt phím người dùng gõ về sau.
        passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
        passwordBox.PasswordChanged += PasswordBox_PasswordChanged;

        var newValue = (string?)e.NewValue ?? string.Empty;

        if (passwordBox.Password != newValue)
        {
            // Gán trong lúc handler đang tạm tháo nên không tạo vòng lặp.
            passwordBox.Password = newValue;
        }
    }

    private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
            SetPassword(passwordBox, passwordBox.Password);
    }
}
