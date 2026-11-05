using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.ViewModels;

/// <summary>
/// Một dòng trong menu chức năng bên trái của cửa sổ chính.
/// Chỉ chứa những chức năng tài khoản đang chọn được cấp quyền.
/// </summary>
public sealed class FunctionItem
{
    public FunctionItem(AppFunction function) => Function = function;

    public AppFunction Function { get; }

    public string DisplayName => Function.DisplayName();

    /// <summary>
    /// Khoá màu của nút chạy — XAML dùng DataTrigger trên chuỗi này để chọn nền
    /// (Primary / Warning / Print / Secondary). Không ném ra Brush để giữ màu
    /// toàn bộ trong XAML.
    /// </summary>
    public string AccentKey => Function switch
    {
        AppFunction.Reconcile => "Primary",
        AppFunction.ClearShift or AppFunction.ClearDebt => "Warning",
        AppFunction.PrintDebtInvoice or AppFunction.PrintShiftProductReport => "Print",
        _ => "Secondary",
    };

    /// <summary>Mô tả ngắn hiển thị ở khung chi tiết bên phải.</summary>
    public string Description => Function switch
    {
        AppFunction.GetLedger =>
            "Lấy tổng tiền Sổ cái trong khoảng Từ ngày → Đến ngày.",
        AppFunction.GetQr =>
            "Lấy tổng tiền thanh toán QR tĩnh HDBank trong khoảng Từ ngày → Đến ngày.",
        AppFunction.Reconcile =>
            "Chạy Lấy Sổ cái và Lấy QR, sau đó so sánh chênh lệch giữa hai nguồn.",
        AppFunction.ClearShift =>
            "Clear sổ giao ca cho các ngày trong khoảng Từ ngày → Đến ngày.",
        AppFunction.PrintDebtInvoice =>
            "In hoá đơn công nợ của một khách hàng. Có thể tạm dừng / tiếp tục trong lúc in.",
        AppFunction.PrintShiftProductReport =>
            "In Bảng phân chia sản phẩm theo ca trong khoảng Từ ngày → Đến ngày.",
        AppFunction.ClearDebt =>
            "Clear công nợ của tất cả khách trong tháng trước (tự tính 23:58 ngày cuối tháng trước). Không dùng Từ ngày / Đến ngày.",
        _ => string.Empty,
    };
}
