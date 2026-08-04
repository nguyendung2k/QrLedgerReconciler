namespace QrLedgerReconciler.Infrastructure;

/// <summary>
/// Địa chỉ/endpoint cố định của hệ thống EGAS nội bộ, dùng chung cho mọi
/// reader/service (LedgerReportReader, QrPaymentReader, ShiftInvoiceProcessor, ...).
/// </summary>
internal static class EgasEndpoints
{
    public const string BaseUrl = "http://192.168.1.101";

    /// <summary>
    /// Domain công khai dùng riêng cho chức năng in "Bảng phân chia sản phẩm"
    /// (ShiftProductReportService) — theo yêu cầu, chức năng này truy cập qua
    /// https://egas.petrolimex.com.vn/ thay vì IP nội bộ.
    /// </summary>
    public const string PublicBaseUrl = "https://egas.petrolimex.com.vn";
}