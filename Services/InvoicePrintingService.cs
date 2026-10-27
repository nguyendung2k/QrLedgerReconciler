using Microsoft.Playwright;
using QrLedgerReconciler.Common;
using QrLedgerReconciler.Helpers;
using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;
using System;
using System.Text.RegularExpressions;

namespace QrLedgerReconciler.Services;

public class InvoicePrintingService
{
    /// <param name="documentType">
    /// Loại chứng từ cần in: "411", "415", ... Chỉ in các link có số chứng từ
    /// bắt đầu bằng loại này. Để null/"" (hoặc "Tất cả") thì in mọi chứng từ
    /// cùng dạng số 3 chữ số (411, 415, và các loại phát sinh sau này).
    /// </param>
    public async Task PrintDebtInvoicesAsync(
        ReconciliationRequest request,
        string customerCode,
        Action<string> status,
        PrintControl? control = null,
        string? documentType = null)
    {
        // Cho phép gọi không truyền control (ví dụ test) — khi đó không có
        // ai gọi Pause/Resume/Stop nên vòng lặp chạy như bình thường.
        control ??= new PrintControl();

        await BrowserContextFactory.RunAsync(async context =>
        {
            status("Đang khởi động trình duyệt...");

            var page = await context.NewPageAsync();

            status("Đang đăng nhập EGAS...");

            try
            {
                await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);
            }
            catch (Exception ex)
            {
                throw new Exception("Đăng nhập thất bại.", ex);
            }

            status("Đang mở báo cáo...");

            // In lần lượt từng ngày trong khoảng đã chọn: hết ngày 1 mới sang
            // ngày 2, hết ngày 2 mới sang ngày 3, ...
            var days = EnumerateDays(request.From, request.To).ToList();

            for (int d = 0; d < days.Count; d++)
            {
                control.ThrowIfStopped();
                await control.WaitIfPausedAsync(status);

                var day = days[d];
                var from = DayStart(request.From, day);
                var to = DayEnd(request.To, day);

                status($"=== Ngày {d + 1}/{days.Count}: {day:dd/MM/yyyy} ===");
                status($"Đang mở báo cáo ngày {day:dd/MM/yyyy}...");

                await page.GotoAsync(BuildReportUrl(customerCode, from, to));
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                if (await page.Locator("text=Không có dữ liệu").CountAsync() > 0)
                {
                    status($"Ngày {day:dd/MM/yyyy} không có dữ liệu.");
                    continue;
                }

                await PrintAllInvoicesAsync(page, status, control, documentType);
            }

            status(control.IsStopped ? "⏹ Đã dừng theo yêu cầu." : "✅ Hoàn thành.");
        });
    }

    private static string DescribeDocumentType(string? documentType)
        => string.IsNullOrWhiteSpace(documentType) ||
           documentType.Equals(AllDocumentTypes, StringComparison.OrdinalIgnoreCase)
            ? AllDocumentTypes
            : documentType.Trim();

    /// <summary>
    /// Chứng từ 415 in bằng mẫu "In chứng từ giấy HĐĐT"; các loại khác in bằng
    /// mẫu mặc định. Nhận số chứng từ của từng link (vd "415.01234") để xử lý
    /// đúng ngay cả khi in "Tất cả" (lẫn 411 và 415 trong cùng danh sách).
    /// </summary>
    private static bool RequiresPaperInvoiceTemplate(string? invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
            return false;

        // Bỏ ký tự trắng/thẻ xuống dòng, lấy phần số đầu tiên "415.01234"
        var first = invoiceNumber.Split(' ', '\n', '\t')[0].Trim();
        return first.StartsWith("415.", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildReportUrl(string customerCode, DateTime from, DateTime to)
        =>
            $"{EgasEndpoints.BaseUrl}/RPT/RPT.aspx?id=412Details" +
            $"&FRMNOPRINT1=" +
            $"&FROMDATE={from.ToEgasReport()}" +
            $"&TODATE={to.ToEgasReportEnd()}" +
            $"&CUST={customerCode}" +
            $"&PRODUCT=" +
            $"&GIAODICH=1" +
            $"&outputformat=1" +
            $"&noheader=" +
            $"&formison=1" +
            $"&rpttype=";

    /// <summary>Các ngày (00:00 mỗi ngày) từ ngày bắt đầu đến ngày kết thúc.</summary>
    private static IEnumerable<DateTime> EnumerateDays(DateTime from, DateTime to)
    {
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
            yield return day;
    }

    /// <summary>Ngày đầu lấy đúng giờ bắt đầu người dùng nhập, các ngày sau lấy từ 00:00.</summary>
    private static DateTime DayStart(DateTime from, DateTime day)
        => day.Date == from.Date ? from : day.Date;

    /// <summary>Ngày cuối lấy đúng giờ kết thúc người dùng nhập, các ngày trước lấy tới 23:59:59.</summary>
    private static DateTime DayEnd(DateTime to, DateTime day)
        => day.Date == to.Date ? to : day.Date.AddDays(1).AddSeconds(-1);

    // Link mở hoá đơn là thẻ <a>, text bắt đầu bằng số chứng từ dạng
    // "411.xxxxx" / "415.xxxxx". Các loại chứng từ khác về sau cũng cùng
    // định dạng 3 chữ số + dấu chấm, nên regex mặc định nhận mọi loại như
    // vậy (^: neo ở đầu chuỗi để không bắt nhầm link chỉ tình cờ chứa
    // "411." ở giữa). Khi người dùng chọn một loại cụ thể thì chỉ khớp loại đó.
    private const string AllDocumentTypes = "Tất cả";

    public static readonly IReadOnlyList<string> DocumentTypes = new[]
    {
        AllDocumentTypes, "411", "415"
    };

    private static Regex InvoiceTextPattern(string? documentType)
        => string.IsNullOrWhiteSpace(documentType) ||
           documentType.Equals(AllDocumentTypes, StringComparison.OrdinalIgnoreCase)
            ? new Regex(@"^\s*\d{3}\.\d+")
            : new Regex($@"^\s*{Regex.Escape(documentType.Trim())}\.\d+");

    private static ILocator InvoiceLinks(IPage page, string? documentType)
        => page.Locator("a").Filter(new LocatorFilterOptions
        {
            HasTextRegex = InvoiceTextPattern(documentType)
        });

    private static async Task PrintAllInvoicesAsync(
        IPage page,
        Action<string> status,
        PrintControl control,
        string? documentType)
    {
        status("=== BẮT ĐẦU IN HOÁ ĐƠN ===");

        status($"Loại chứng từ: {DescribeDocumentType(documentType)}");

        int count = await InvoiceLinks(page, documentType).CountAsync();

        status($"Tìm thấy {count} hóa đơn.");

        for (int i = 0; i < count; i++)
        {
            // Điểm kiểm tra Dừng / Tạm dừng — trước mỗi hoá đơn, kể cả khi
            // đang pause thì vẫn có thể Stop được ngay (WaitIfPausedAsync
            // liên tục kiểm tra IsStopped bên trong lúc chờ).
            control.ThrowIfStopped();
            await control.WaitIfPausedAsync(status);

            status($"In hóa đơn {i + 1}/{count}...");

            try
            {
                // Re-query lại mỗi lần vì trang danh sách được tải lại sau mỗi lần in
                var invoiceLink = InvoiceLinks(page, documentType).Nth(i);

                // Số chứng từ của link hiện tại (vd "415.01234"), dùng để chọn
                // đúng mẫu in riêng cho từng loại ngay cả khi in "Tất cả".
                var invoiceNumber = ((await invoiceLink.InnerTextAsync()).Trim())
                    .Split(' ', '\n', '\t')[0];

                await invoiceLink.ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                // Click Print
                await page.ClickAsync("#printbtn, img[src*='Print2.gif']");

                var frame = page.Frame("saveTranFrm");
                if (frame == null)
                {
                    await page.WaitForSelectorAsync("iframe[name='saveTranFrm']",
                        new PageWaitForSelectorOptions { Timeout = 1500 });
                    frame = page.Frame("saveTranFrm");
                }

                if (frame != null)
                {
                    // Chứng từ 415 cần chọn mẫu "In chứng từ giấy HĐĐT" trên
                    // ô select trước khi bấm "Chọn mẫu in". Quyết định theo số
                    // chứng từ của từng link để khi in "Tất cả" vẫn áp dụng
                    // đúng cho riêng các hoá đơn 415.
                    if (RequiresPaperInvoiceTemplate(invoiceNumber))
                    {
                        status("Đang chọn mẫu in chứng từ giấy HĐĐT...");
                        await frame.SelectOptionAsync(
                            "select",
                            new[] { new SelectOptionValue { Label = "In chứng từ giấy HĐĐT" } });
                        await frame.WaitForTimeoutAsync(100);
                    }

                    await frame.ClickAsync("input[value='Chọn mẫu in'], input.btn");

                    // Sau khi bấm "Chọn mẫu in", iframe saveTranFrm chuyển sang
                    // trang EINVPrint.aspx — chính là bản xem trước hoá đơn
                    // (PDF) mà người dùng thấy khi in. Headless không gọi được
                    // window.print(), nên ta lấy đúng URL trang đó rồi sinh PDF
                    // chuẩn qua CDP Page.printToPDF và in bằng HeadlessPrintHelper.
                    status("Đang lấy URL hoá đơn (EINVPrint)...");

                    // Chờ iframe thật sự điều hướng sang EINVPrint.aspx rồi mới
                    // đọc URL (click "Chọn mẫu in" nạp bất đồng bộ). Chờ tới khi
                    // URL đổi hẳn sang trang mới (khác trang cũ), tối đa 10 giây.
                    var eInvUrl = await WaitForInvoicePrintUrlAsync(page, frame, status);

                    if (string.IsNullOrWhiteSpace(eInvUrl))
                    {
                        status("Không lấy được URL hoá đơn — bỏ qua hoá đơn này.");
                    }
                    else
                    {
                        status($"URL hoá đơn: {eInvUrl}");

                        // Mở cùng URL hoá đơn trên một trang riêng để tạo PDF.
                        var pdfPage = await page.Context.NewPageAsync();
                        try
                        {
                            // Chờ DOMContentLoaded với WaitUntil để không treo ở
                            // ảnh nền/QR chưa tải xong (lỗi hoá đơn thứ 2 trở lên).
                            await pdfPage.GotoAsync(eInvUrl,
                                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

                            // Chờ nội dung hoá đơn (table dữ liệu) đã dựng xong thì
                            // sinh PDF ngay, thay vì chờ cố định 1000ms (nhanh hơn
                            // khi trang tải nhanh, vẫn đủ an toàn vì chờ đúng phần tử).
                            await pdfPage.WaitForSelectorAsync("table, .VATTEMP",
                                new PageWaitForSelectorOptions { Timeout = 5000 });

                            // Ẩn các thành phần thừa KHÔNG thuộc bản in chuẩn:
                            //   1. Nút hành động Print / Close (input.btn).
                            //   2. Overlay .nenhd_bg — là nền xám/đen phủ phía trên
                            //      trang, không nằm trong bản PDF thật của EGAS.
                            // Giữ nguyên ảnh QR (tra cứu) — là nội dung hoá đơn.
                            await pdfPage.AddStyleTagAsync(new PageAddStyleTagOptions
                            {
                                Content = @"
input.btn, body > br { display:none !important; }
.nenhd_bg { display:none !important; }
#main, .VATTEMP { margin: 0 !important; padding: 0 !important; border: none !important; box-shadow: none !important; }
body { margin: 0 auto !important; }
"
                            });

                            status("Đang sinh PDF hoá đơn...");

                            var pdfBytes = await pdfPage.PdfAsync(new PagePdfOptions
                            {
                                Format = "Letter",
                                PrintBackground = false,
                                Scale = 0.85f
                            });

                            status("Đang in (PDF hoá đơn)...");
                            await HeadlessPrintHelper.PrintPdfAsync(pdfBytes, status);
                        }
                        finally
                        {
                            await pdfPage.CloseAsync();
                        }
                    }
                }

                await InvoiceLinks(page, documentType).First.WaitForAsync(
                    new LocatorWaitForOptions { Timeout = 2000 });

                status("Đã quay lại danh sách.");
            }
            catch (OperationCanceledException)
            {
                // Do người dùng bấm Kết thúc trong lúc đang xử lý hoá đơn này —
                // ném tiếp ra ngoài để dừng hẳn, không log như một lỗi thường.
                throw;
            }
            catch (Exception ex)
            {
                status($"Lỗi: {ex.Message}");
            }

            await page.BringToFrontAsync();
        }

        status(control.IsStopped ? "=== ĐÃ DỪNG IN ===" : "=== HOÀN THÀNH IN TẤT CẢ ===");
    }

    /// <summary>
    /// Chờ iframe saveTranFrm điều hướng sang trang in của hoá đơn
    /// (URL khác URL trang cũ, không rỗng). Trước đây chỉ chờ cố định
    /// 1000ms nên hoá đơn 2+ hay bấm in khi iframe chưa nạp xong →
    /// trang PDF mở ra vẫn là trang cũ, sinh PDF sai nội dung.
    /// </summary>
    private static async Task<string?> WaitForInvoicePrintUrlAsync(
        IPage page, IFrame frame, Action<string> status)
    {
        var oldUrl = frame.Url;
        for (var t = 0; t < 100; t++)
        {
            await page.WaitForTimeoutAsync(50);

            var cur = page.Frame("saveTranFrm");
            if (cur != null && !string.IsNullOrEmpty(cur.Url) && cur.Url != oldUrl)
            {
                // Chờ thêm một chút để trang mới dựng nội dung
                await page.WaitForTimeoutAsync(400);
                return cur.Url;
            }
        }

        status("Iframe không đổi URL sau 10 giây.");
        return null;
    }
}