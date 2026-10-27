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
    public async Task PrintDebtInvoicesAsync(
        ReconciliationRequest request,
        string customerCode,
        Action<string> status,
        PrintControl? control = null)
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

                await PrintAllInvoicesAsync(page, status, control);
            }

            status(control.IsStopped ? "⏹ Đã dừng theo yêu cầu." : "✅ Hoàn thành.");
        });
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

    // Link mở hoá đơn là thẻ <a>. Text của link bắt đầu bằng số hoá đơn
    // "411.xxxxx", nên neo regex ở đầu chuỗi để không bắt nhầm link khác
    // chỉ tình cờ chứa chuỗi "411." ở giữa.
    private static readonly Regex InvoiceTextPattern = new(@"^\s*411\.\d+");

    private static ILocator InvoiceLinks(IPage page)
        => page.Locator("a").Filter(new LocatorFilterOptions
        {
            HasTextRegex = InvoiceTextPattern
        });

    private static async Task PrintAllInvoicesAsync(IPage page, Action<string> status, PrintControl control)
    {
        status("=== BẮT ĐẦU IN HOÁ ĐƠN ===");

        int count = await InvoiceLinks(page).CountAsync();

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
                await InvoiceLinks(page).Nth(i).ClickAsync();
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

                // Click Print
                await page.ClickAsync("#printbtn, img[src*='Print2.gif']");
                await page.WaitForTimeoutAsync(200);

                var frame = page.Frame("saveTranFrm");
                if (frame == null)
                {
                    await page.WaitForSelectorAsync("iframe[name='saveTranFrm']",
                        new PageWaitForSelectorOptions { Timeout = 1500 });
                    frame = page.Frame("saveTranFrm");
                }

                if (frame != null)
                {
                    await frame.ClickAsync("input[value='Chọn mẫu in'], input.btn");
                    await frame.WaitForTimeoutAsync(100);

                    // kiosk-printing => in thẳng, không có dialog. Trang tự
                    // navigate về danh sách sau khi in — chờ đúng lúc đó.
                    status("Đang in và chờ trang tự quay lại...");
                    await frame.ClickAsync("input[value='Print']");
                    await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                        new PageWaitForLoadStateOptions { Timeout = 2000 });
                }

                await page.WaitForSelectorAsync("a:has-text('411.')",
                    new PageWaitForSelectorOptions { Timeout = 2000 });

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
            await page.WaitForTimeoutAsync(80);
        }

        status(control.IsStopped ? "=== ĐÃ DỪNG IN ===" : "=== HOÀN THÀNH IN TẤT CẢ ===");
    }
}