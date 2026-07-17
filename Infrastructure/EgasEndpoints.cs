namespace QrLedgerReconciler.Infrastructure;

/// <summary>
/// Địa chỉ/endpoint cố định của hệ thống EGAS nội bộ, dùng chung cho mọi
/// reader/service (LedgerReportReader, QrPaymentReader, ShiftInvoiceProcessor, ...).
/// </summary>
internal static class EgasEndpoints
{
    public const string BaseUrl = "http://192.168.1.101";
}
