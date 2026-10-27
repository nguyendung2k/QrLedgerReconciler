using System.Globalization;

namespace QrLedgerReconciler.Common;

/// <summary>
/// Định dạng ngày giờ dùng khi automation điền vào ô trên trang EGAS / HDBank.
/// Giữ một chỗ duy nhất để mọi luồng nhập đều có cả ngày lẫn giờ, tránh việc
/// mỗi nơi tự cắt mất phần giờ như trước.
/// </summary>
public static class DateTimeExtensions
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Ô "Ngày mở ca" (viewdate) trên UHome — chỉ chọn ngày của ca.</summary>
    private const string EgasShiftDay = "d/M/yyyy";

    /// <summary>Tham số FROMDATE / TODATE trên RPT.aspx.</summary>
    public const string EgasReport = "d/M/yyyy HH:mm:ss";

    /// <summary>Hai ô DD-MM-YYYY HH:MM:SS trên trang tra soát HDBank.</summary>
    public const string HdbankInput = "dd-MM-yyyy HH:mm:ss";

    /// <summary>Định dạng hiển thị trên form chính và hộp thoại xác nhận.</summary>
    public const string Display = "dd/MM/yyyy HH:mm";

    /// <summary>Mốc bắt đầu của một ngày: 00:00.</summary>
    public static DateTime StartOfDay(DateTime value) => value.Date;

    /// <summary>
    /// Mốc kết thúc của một ngày: 23:59:59, để không bỏ sót giao dịch phát
    /// sinh trong phút cuối cùng.
    /// </summary>
    public static DateTime EndOfDay(DateTime value)
        => value.Date.AddDays(1).AddSeconds(-1);

    /// <summary>Ngày của ca (viewdate) — trang ca chỉ nhận ngày.</summary>
    public static string ToEgasShiftDay(this DateTime value)
        => value.ToString(EgasShiftDay, Culture);

    /// <summary>
    /// FROMDATE trên báo cáo EGAS. Giữ đúng giờ phút người dùng nhập.
    /// </summary>
    public static string ToEgasReport(this DateTime value)
        => value.ToString(EgasReport, Culture);

    /// <summary>
    /// TODATE trên báo cáo EGAS. Báo cáo lọc theo "nhỏ hơn hoặc bằng", nên mốc
    /// kết thúc được đẩy tới giây 59 của phút để không mất giao dịch cuối phút.
    /// </summary>
    public static string ToEgasReportEnd(this DateTime value)
        => value.ToString("d/M/yyyy HH:mm:59", Culture);

    /// <summary>Ô From trên HDBank — giữ đúng giờ phút, giây về 00.</summary>
    public static string ToHdbankStart(this DateTime value)
        => value.ToString("dd-MM-yyyy HH:mm:00", Culture);

    /// <summary>Ô To trên HDBank — giữ đúng giờ phút, giây về 59.</summary>
    public static string ToHdbankEnd(this DateTime value)
        => value.ToString("dd-MM-yyyy HH:mm:59", Culture);
}
