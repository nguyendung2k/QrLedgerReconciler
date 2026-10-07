using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;

namespace QrLedgerReconciler.Infrastructure;

/// <summary>
/// In PDF trong chế độ headless (không mở cửa sổ trình duyệt).
///
/// Vì sao cần helper này:
///   Chrome headless KHÔNG in được ra máy in vật lý — window.print() bị vô
///   hiệu nên cách duy nhất là sinh PDF chuẩn qua CDP Page.printToPDF rồi
///   tự giao file đó cho spooler. Nhưng GDI+ (System.Drawing) không đọc
///   được PDF và máy không có Sumatra/Acrobat để in PDF trực tiếp.
///
/// Cách hoạt động (không cần cài thêm công cụ ngoài):
///   1. Nhận byte[] PDF (sinh sẵn từ CDP Page.printToPDF ở service).
///   2. Raster hoá từng trang PDF thành ảnh PNG độ phân giải cao
///      bằng Windows.Data.Pdf (WinRT) — API raster PDF của chính Windows.
///   3. In từng trang PNG bằng PrintDocument (System.Drawing.Printing).
///
/// Yêu cầu xây dựng: TFM net8.0-windows10.0.19041.0 để dùng được
/// Windows.Data.Pdf (WinRT) và gói System.Drawing.Common cho System.Drawing.
/// </summary>
internal static class HeadlessPrintHelper
{
    /// <summary>
    /// In PDF <paramref name="pdfBytes"/> ra máy in mặc định.
    /// </summary>
    public static async Task PrintPdfAsync(byte[] pdfBytes, Action<string> status)
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
        {
            throw new ArgumentException("Không có dữ liệu PDF để in.", nameof(pdfBytes));
        }

        var tempPdf = Path.Combine(Path.GetTempPath(), $"egas_{Guid.NewGuid():N}.pdf");

        try
        {
            await File.WriteAllBytesAsync(tempPdf, pdfBytes);

            status("Đang raster hoá PDF (nội dung hoá đơn)...");

            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tempPdf);
            var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);

            var pageCount = (int)doc.PageCount;

            for (uint i = 0; i < pageCount; i++)
            {
                status(pageCount > 1
                    ? $"Đang in trang {i + 1}/{pageCount}..."
                    : "Đang gửi tới máy in...");

                using var page = doc.GetPage(i);
                using var image = await RasterizePdfPageAsync(page);

                PrintImage(image);
            }

            status("✅ Đã gửi lệnh in.");
        }
        finally
        {
            // Windows.Data.Pdf giữ file cho tới khi đối tượng tự được release;
            // xoá thất bại (file đang bận) thì bỏ qua, lần sau sẽ ghi đè/thay thế.
            try
            {
                if (File.Exists(tempPdf))
                {
                    File.Delete(tempPdf);
                }
            }
            catch (IOException)
            {
                // bỏ qua: file đang bận, lần in sau sẽ ghi file mới
            }
            catch (UnauthorizedAccessException)
            {
                // bỏ qua
            }
        }
    }

    /// <summary>
    /// Raster hoá một trang PDF thành ảnh bitmap 300 DPI (độ rõ ngang máy in).
    /// </summary>
    private static async Task<Bitmap> RasterizePdfPageAsync(
        Windows.Data.Pdf.PdfPage page)
    {
        using (var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
        {
            double width = page.Size.Width;
            double height = page.Size.Height;

            const int targetDpi = 300;
            var destinationWidth = (uint)(width / 72.0 * targetDpi);
            var destinationHeight = (uint)(height / 72.0 * targetDpi);

            await page.RenderToStreamAsync(
                stream,
                new Windows.Data.Pdf.PdfPageRenderOptions
                {
                    DestinationWidth = destinationWidth,
                    DestinationHeight = destinationHeight
                });

            var size = (long)stream.Size;
            var buffer = new byte[size];
            await stream.AsStreamForRead().ReadAsync(buffer, 0, buffer.Length);

            using (var ms = new System.IO.MemoryStream(buffer))
            {
                return new Bitmap(ms);
            }
        }
    }

    /// <summary>
    /// In ảnh ra máy in mặc định, lấp đầy trang giấy (giữ tỉ lệ ảnh).
    /// </summary>
    private static void PrintImage(Image image)
    {
        using var pd = new PrintDocument();

        pd.PrinterSettings.PrintToFile = false;

        // Driver "Remote Desktop Easy Print" trả về MarginBounds rất lớn
        // (lề mặc định Windows), làm ảnh bị co nhỏ và lệch lề. Ép lề về 0 để
        // toàn bộ khổ giấy (Letter) dùng hết cho ảnh — máy in tự trừ phần lề
        // vật lý tối thiểu (hardware margin) sau.
        pd.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

        pd.PrintPage += (_, e) =>
        {
            if (e.Graphics == null) return;

            // Với lề bằng 0, dùng e.PageBounds (toàn bộ vùng giấy thực in được,
            // không phụ thuộc lề driver). KHÔNG co thêm (inset = 0): hoá đơn đã
            // được EGAS canh lề đều 4 góc trong bản PDF chuẩn, in nguyên khổ để
            // đúng như file mẫu "Bán công nợ kiêm xuất hóa đơn.pdf".
            var target = FitInto(image.Width, image.Height, e.PageBounds, 0f);
            e.Graphics.DrawImage(image, target);
            e.HasMorePages = false;
        };

        // OriginAtMargins=false: Graphics gốc (0,0) nằm ở mép giấy, PageBounds
        // có X/Y là khoảng cách mép -> vùng in được, nên FitInto canh đều 4 cạnh.
        pd.OriginAtMargins = false;

        pd.Print();
    }

    /// <summary>
    /// Fit ảnh vào chính xác một vùng chữ nhật (đã có sẵn X/Y là toạ độ
    /// tuyệt đối trên trang, như MarginBounds) — giữ tỉ lệ, canh giữa.
    /// </summary>
    private static RectangleF FitInto(int srcW, int srcH, RectangleF area, float insetRatio = 0f)
    {
        // Thu vùng vẽ vào insetRatio ở mỗi phía để nội dung lọt gọn trong
        // vùng an toàn của máy in, vẫn giữ tỉ lệ ảnh và canh giữa.
        var ax = area.X + area.Width * insetRatio;
        var ay = area.Y + area.Height * insetRatio;
        var aw = area.Width * (1 - 2 * insetRatio);
        var ah = area.Height * (1 - 2 * insetRatio);

        var scale = Math.Min(aw / srcW, ah / srcH);
        var w = srcW * scale;
        var h = srcH * scale;
        var x = ax + (aw - w) / 2;
        var y = ay + (ah - h) / 2;
        return new RectangleF(x, y, w, h);
    }
}