using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace QrLedgerReconciler.Models;

/// <summary>
/// Các chức năng trong app. Dùng làm key phân quyền cho từng tài khoản EGAS.
/// Thứ tự liệt kê chính là thứ tự hiển thị checkbox trên màn hình quản lý.
/// </summary>
public enum AppFunction
{
    GetLedger,             // Lấy Sổ cái
    GetQr,                 // Lấy QR
    Reconcile,             // Đối chiếu
    ClearShift,            // Clear ca
    PrintDebtInvoice,      // In công nợ
    PrintShiftProductReport, // In Bảng phân chia sản phẩm
    ClearDebt,             // Clear công nợ tháng
}

public static class AppFunctionExtensions
{
    /// <summary>Nhãn tiếng Việt hiển thị cho từng chức năng.</summary>
    public static string DisplayName(this AppFunction function) => function switch
    {
        AppFunction.GetLedger => "Lấy Sổ cái",
        AppFunction.GetQr => "Lấy QR",
        AppFunction.Reconcile => "Đối chiếu",
        AppFunction.ClearShift => "Clear ca",
        AppFunction.PrintDebtInvoice => "In công nợ",
        AppFunction.PrintShiftProductReport => "In Bảng phân chia sản phẩm",
        AppFunction.ClearDebt => "Clear công nợ tháng",
        _ => function.ToString(),
    };

    /// <summary>Danh sách đầy đủ các chức năng theo thứ tự hiển thị cố định.</summary>
    public static IReadOnlyList<AppFunction> AllFunctions { get; } =
        (AppFunction[])Enum.GetValues(typeof(AppFunction));
}

public sealed class UserAccount : INotifyPropertyChanged
{
    private string _userName = string.Empty;
    private string _password = string.Empty;
    private string _displayName = string.Empty;
    private bool _isEnabled = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string UserName
    {
        get => _userName;
        set => SetProperty(ref _userName, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    /// <summary>Tên hiển thị / ghi chú (ví dụ "Tài khoản ca sáng").</summary>
    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    /// <summary>Tắt = ẩn khỏi dropdown form chính (không xoá dữ liệu).</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    /// <summary>Các chức năng được cấp quyền. Là HashSet để khi xoá tài khoản
    /// khoá quyền thì không ảnh hưởng dữ liệu khác.</summary>
    public HashSet<AppFunction> Functions { get; set; } = new();

    /// <summary>Tài khoản có được dùng chức năng này không.</summary>
    public bool CanUse(AppFunction function) => IsEnabled && Functions.Contains(function);

    /// <summary>Số chức năng được cấp. Bỏ qua khi (de)serialize JSON.</summary>
    [JsonIgnore]
    public int FunctionCount => Functions.Count;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}