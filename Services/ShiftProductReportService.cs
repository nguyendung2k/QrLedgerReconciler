using Microsoft.Playwright;
using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QrLedgerReconciler.Services;

/// <summary>
/// Tự động lặp qua các ca trong ngày (từ ô "Ngày mở ca" trên UHome) và in
/// "Bảng phân chia sản phẩm" cho từng ca tại trang report Sal01.
/// </summary>
public class ShiftProductReportService
{
    private const int AfterSearchWaitMs = 800;
    private const int AfterPrintWaitMs = 1000;

    public Task RunAsync(
        ReconciliationRequest request,
        Action<string> status)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            status("Khởi động trình duyệt...");

            var homePage = await context.NewPageAsync();

            status("Đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(homePage, EgasEndpoints.PublicBaseUrl, request);

            var date = request.From.Date;
            var lastDate = request.To.Date;

            while (date <= lastDate)
            {
                status($"Đang xử lý ngày {date:dd/MM/yyyy}");

                await ProcessOneDayAsync(context, homePage, date, status);

                date = date.AddDays(1);
            }

            status("Hoàn thành in Bảng phân chia sản phẩm.");
        });
    }

    private static async Task ProcessOneDayAsync(
        IBrowserContext context,
        IPage homePage,
        DateTime date,
        Action<string> status)
    {
        var shifts = await GetShiftListForDateAsync(context, homePage, date, status);

        if (shifts.Count == 0)
        {
            status($"Không có ca ngày {date:dd/MM/yyyy}.");
            return;
        }

        status($"Có {shifts.Count} ca ngày {date:dd/MM/yyyy}.");

        // Ưu tiên xử lý từ ca cuối cùng trong danh sách lên dần.
        shifts.Reverse();

        foreach (var shift in shifts)
        {
            status($"In Bảng phân chia sản phẩm - ca {shift}");

            try
            {
                await PrintShiftProductReportAsync(context, shift, status);
            }
            catch (Exception ex)
            {
                status($"Lỗi khi in ca {shift}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Lấy danh sách mã ca cho 1 ngày trực tiếp từ ô "Ngày mở ca" trên trang
    /// UHome — không qua menu "Sổ giao ca" nữa.
    /// </summary>
    private static async Task<List<string>> GetShiftListForDateAsync(
        IBrowserContext context,
        IPage homePage,
        DateTime date,
        Action<string> status)
    {
        await homePage.GotoAsync($"{EgasEndpoints.PublicBaseUrl}/UHome/UHome.aspx");

        var dateInput = homePage.Locator("input[name='viewdate']");
        await dateInput.FillAsync(date.ToString("d/M/yyyy", CultureInfo.InvariantCulture));
        await dateInput.PressAsync("Enter");

        await homePage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await homePage.WaitForTimeoutAsync(600);

        var shiftPattern = new Regex(@"^\d{8}\s*-\s*\d+$");

        var links = await homePage.Locator("a").AllInnerTextsAsync();

        var shifts = links
            .Select(text => text.Trim())
            .Where(text => shiftPattern.IsMatch(text))
            .Distinct()
            .ToList();

        return shifts;
    }

    /// <summary>
    /// Mở report Sal01, điền cùng 1 mã ca vào cả "Từ ca" và "Đến ca",
    /// Ctrl+Enter để tìm, đợi kết quả rồi bấm In.
    /// </summary>
    private static async Task PrintShiftProductReportAsync(
        IBrowserContext context,
        string shiftCode,
        Action<string> status)
    {
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{EgasEndpoints.PublicBaseUrl}/RPT/RPT.aspx?APP=CENTER&id=Sal01");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Chỉ điền phần trước dấu "-" (ví dụ "26080102 - 2" → "26080102"),
        // không điền hậu tố ca.
        var shiftPrefix = shiftCode.Split('-')[0].Trim();

        var fromInput = page.Locator("input[name='actb_FROMWS']");
        var toInput = page.Locator("input[name='actb_TOWS']");

        // Gõ từng ký tự để ô autocomplete hiện gợi ý, rồi Enter để chọn.
        await fromInput.ClickAsync();
        await fromInput.PressSequentiallyAsync(shiftPrefix, new() { Delay = 60 });
        await page.WaitForTimeoutAsync(500);
        await fromInput.PressAsync("Enter");

        await toInput.ClickAsync();
        await toInput.PressSequentiallyAsync(shiftPrefix, new() { Delay = 60 });
        await page.WaitForTimeoutAsync(500);
        await toInput.PressAsync("Enter");

        // Nút tìm kiếm thật sau khi đã điền xong điều kiện.
        status($"Đang tìm ca {shiftPrefix}...");
        await page.Locator("img.imggo[title='Ctrl-Enter']").ClickAsync();

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await page.WaitForTimeoutAsync(AfterSearchWaitMs);

        // Đợi tiêu đề báo cáo xuất hiện = tìm kiếm đã ra kết quả.
        await page.Locator("text=BẢNG PHÂN CHIA SẢN PHẨM")
            .WaitForAsync(new() { Timeout = 10_000 });

        // In bằng PDF: headless không gọi được window.print(), nên sinh PDF
        // chuẩn qua CDP Page.printToPDF (đúng nội dung báo cáo đang hiện) rồi
        // in qua HeadlessPrintHelper.
        status($"Đang sinh PDF báo cáo...");
        var pdfBytes = await page.PdfAsync(new PagePdfOptions
        {
            Format = "A4",
            PrintBackground = true
        });

        status($"Đang in (PDF báo cáo)...");
        await HeadlessPrintHelper.PrintPdfAsync(pdfBytes, status);
        await page.WaitForTimeoutAsync(AfterPrintWaitMs);

        await page.CloseAsync();

        status($"Đã in ca {shiftCode}");
    }
}