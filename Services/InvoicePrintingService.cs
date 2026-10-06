using Microsoft.Playwright;
using QrLedgerReconciler.Helpers;
using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;
using System;

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

            var url =
                $"http://192.168.1.101/RPT/RPT.aspx?id=412Details" +
                $"&FRMNOPRINT1=" +
                $"&FROMDATE={request.From:dd/M/yyyy HH:mm}" +
                $"&TODATE={request.To:dd/M/yyyy HH:mm}" +
                $"&CUST={customerCode}" +
                $"&PRODUCT=" +
                $"&GIAODICH=1" +
                $"&outputformat=1" +
                $"&noheader=" +
                $"&formison=1" +
                $"&rpttype=";

            await page.GotoAsync(url);

            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            if (await page.Locator("text=Không có dữ liệu").CountAsync() > 0)
            {
                status("Không có dữ liệu.");
                return;
            }

            await PrintAllInvoicesAsync(page, status, control);

            status(control.IsStopped ? "⏹ Đã dừng theo yêu cầu." : "✅ Hoàn thành.");
        });
    }

    private static async Task PrintAllInvoicesAsync(IPage page, Action<string> status, PrintControl control)
    {
        status("=== BẮT ĐẦU IN HOÁ ĐƠN ===");

        var invoiceSelector = page.Locator("a").Filter(new LocatorFilterOptions { HasText = "411." });
        int count = await invoiceSelector.CountAsync();

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
                var invoices = page.Locator("a").Filter(new LocatorFilterOptions { HasText = "411." });
                await invoices.Nth(i).ClickAsync();
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

                    // kiosk-printing => in thẳng, không có hộp thoại để chọn tỉ lệ.
                    // Nội dung được ép vừa khổ giấy để in đúng tỉ lệ mong muốn.
                    status($"Đang in (vừa khổ 60%)...");
                    await PrintLayout.ApplyAsync(frame.Locator("html"));
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