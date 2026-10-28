using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace QrLedgerReconciler.Controls;

/// <summary>
/// Ô nhập mật khẩu có nút con mắt để hiện/ẩn nội dung.
///
/// WPF tách <see cref="PasswordBox"/> (ẩn) và <see cref="TextBox"/> (hiện) nên control
/// này giữ hai control chồng nhau ở cùng vị trí, đồng bộ text giữa chúng và coi
/// <see cref="Password"/> là nguồn sự thật duy nhất cho ViewModel.
/// </summary>
public partial class PasswordInput : UserControl
{
    public static readonly DependencyProperty PasswordProperty =
        DependencyProperty.Register(
            nameof(Password),
            typeof(string),
            typeof(PasswordInput),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnPasswordChanged));

    public static readonly DependencyProperty IsRevealedProperty =
        DependencyProperty.Register(
            nameof(IsRevealed),
            typeof(bool),
            typeof(PasswordInput),
            new FrameworkPropertyMetadata(false,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnIsRevealedChanged));

    /// <summary>Chống vòng lặp đồng bộ giữa hai control.</summary>
    private bool _syncing;

    private bool _isLoaded;

    public PasswordInput()
    {
        InitializeComponent();

        Loaded += (_, _) =>
        {
            _isLoaded = true;
            SyncBothFromPassword();
        };
    }

    public string Password
    {
        get => (string?)GetValue(PasswordProperty) ?? string.Empty;
        set => SetValue(PasswordProperty, value);
    }

    public bool IsRevealed
    {
        get => (bool)GetValue(IsRevealedProperty);
        set => SetValue(IsRevealedProperty, value);
    }

    /// <summary>Focus vào ô đang hiển thị (ẩn hoặc hiện).</summary>
    public new void Focus()
    {
        if (IsRevealed) VisibleBox.Focus();
        else HiddenBox.Focus();
    }

    /// <summary>Đặt con trỏ ở cuối văn bản.</summary>
    public void SelectAllText()
    {
        if (IsRevealed) VisibleBox.SelectAll();
        else HiddenBox.SelectAll();
    }

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordInput input || input._syncing) return;

        input.SyncBothFromPassword();
    }

    private static void OnIsRevealedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PasswordInput input)
            input.ApplyRevealState();
    }

    /// <summary>Ghi giá trị từ ViewModel xuống cả hai control (không kích hoạt ghi ngược).</summary>
    private void SyncBothFromPassword()
    {
        var value = Password;

        _syncing = true;
        try
        {
            if (HiddenBox.Password != value) HiddenBox.Password = value;
            if (VisibleBox.Text != value) VisibleBox.Text = value;
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Đổi control hiển thị; giữ nguyên nội dung và vị trí con trỏ.</summary>
    private void ApplyRevealState()
    {
        var revealed = IsRevealed;

        EyeOpenIcon.Visibility = revealed ? Visibility.Collapsed : Visibility.Visible;
        EyeOffIcon.Visibility = revealed ? Visibility.Visible : Visibility.Collapsed;
        RevealToggle.ToolTip = revealed ? "Ẩn mật khẩu" : "Hiện mật khẩu";

        if (revealed)
        {
            VisibleBox.Text = HiddenBox.Password;
            VisibleBox.Visibility = Visibility.Visible;
            HiddenBox.Visibility = Visibility.Collapsed;

            if (_isLoaded)
            {
                VisibleBox.Focus();
                VisibleBox.CaretIndex = VisibleBox.Text.Length;
            }
        }
        else
        {
            HiddenBox.Password = VisibleBox.Text;
            HiddenBox.Visibility = Visibility.Visible;
            VisibleBox.Visibility = Visibility.Collapsed;

            if (_isLoaded)
            {
                HiddenBox.Focus();
                HiddenBox.SelectAll();
            }
        }
    }

    private void RevealToggle_Checked(object sender, RoutedEventArgs e) => IsRevealed = true;

    private void RevealToggle_Unchecked(object sender, RoutedEventArgs e) => IsRevealed = false;

    private void HiddenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;

        _syncing = true;
        try
        {
            if (VisibleBox.Text != HiddenBox.Password)
                VisibleBox.Text = HiddenBox.Password;
        }
        finally
        {
            _syncing = false;
        }

        Password = HiddenBox.Password;
    }

    private void VisibleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;

        _syncing = true;
        try
        {
            if (HiddenBox.Password != VisibleBox.Text)
                HiddenBox.Password = VisibleBox.Text;
        }
        finally
        {
            _syncing = false;
        }

        Password = VisibleBox.Text ?? string.Empty;
    }
}
