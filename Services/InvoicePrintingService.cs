using Microsoft.Playwright;
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

            await PrintAllInvoicesAsync(page, url, status, control);

            status(control.IsStopped ? "⏹ Đã dừng theo yêu cầu." : "✅ Hoàn thành.");
        });
    }

    private static async Task PrintAllInvoicesAsync(
        IPage page, string reportUrl, Action<string> status, PrintControl control)
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
                            await pdfPage.WaitForTimeoutAsync(1000);

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

                // In xong 1 hoá đơn: quay lại trang báo cáo (danh sách hoá đơn)
                // để lấy hoá đơn tiếp theo. Trước đây chỉ chờ selector "411."
                // mà không reload, nên trang danh sách có thể đã cũ → in nhầm
                // nội dung hoá đơn trước.
                await page.GotoAsync(reportUrl);
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await page.WaitForSelectorAsync("a:has-text('411.')",
                    new PageWaitForSelectorOptions { Timeout = 5000 });

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
            await page.WaitForTimeoutAsync(100);

            var cur = page.Frame("saveTranFrm");
            if (cur != null && !string.IsNullOrEmpty(cur.Url) && cur.Url != oldUrl)
            {
                // Chờ thêm một chút để trang mới dựng nội dung
                await page.WaitForTimeoutAsync(800);
                return cur.Url;
            }
        }

        status("Iframe không đổi URL sau 10 giây.");
        return null;
    }
}