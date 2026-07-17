using Microsoft.Playwright;
using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QrLedgerReconciler.Services;

public class ClearShiftService
{
    private const int ActionFinishWaitMs = 1_500;

    public Task RunAsync(
        ReconciliationRequest request,
        Action<string> status)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            status("Khởi động trình duyệt...");

            var page = await context.NewPageAsync();

            status("Đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);

            status("Bắt đầu xử lý...");
            await ProcessDaysAsync(context, page, request, status);
        });
    }

    #region Day / shift processing

    private static async Task ProcessDaysAsync(
        IBrowserContext context,
        IPage homePage,
        ReconciliationRequest request,
        Action<string> status)
    {
        var date = request.From.Date;
        var lastDate = request.To.Date;

        while (date <= lastDate)
        {
            status($"Đang xử lý {date:dd/MM/yyyy}");

            await ProcessOneDayAsync(context, homePage, date, status);

            date = date.AddDays(1);
        }
    }

    private static async Task ProcessOneDayAsync(
        IBrowserContext context,
        IPage homePage,
        DateTime date,
        Action<string> status)
    {
        await homePage.GotoAsync($"{EgasEndpoints.BaseUrl}/UHome/UHome.aspx");

        status($"Mở màn hình ngày {date:dd/MM/yyyy}...");

        // Mở menu Sổ giao ca
        await homePage.ClickAsync("text=Sổ giao ca");

        // Mở Hóa đơn NMKLHD theo lô
        var report = await context.RunAndWaitForPageAsync(
            async () => await homePage.ClickAsync("text=Hóa đơn NMKLHD theo lô"));

        await report.WaitForLoadStateAsync();

        await FilterShiftByDateAsync(report, date);

        var shifts = await GetShiftListAsync(report);

        status($"Có {shifts.Count} ca.");

        foreach (var shift in shifts)
        {
            status($"Đang xử lý ca {shift}");

            await ProcessShiftAsync(report, shift, status);
        }

        await report.CloseAsync();
    }

    private static async Task FilterShiftByDateAsync(
        IPage page,
        DateTime date)
    {
        var txtDate = page.Locator("input").First;

        await txtDate.FillAsync(
            date.ToString("d/M/yyyy", CultureInfo.InvariantCulture));

        await page.GetByText("Lọc").ClickAsync();

        await page.WaitForLoadStateAsync();
    }

    private static async Task<List<string>> GetShiftListAsync(IPage page)
    {
        var shiftPattern = new Regex(@"^\d{8}\s*-\s*\d+$");

        var links = await page.Locator("a").AllInnerTextsAsync();

        return links
            .Select(text => text.Trim())
            .Where(text => shiftPattern.IsMatch(text))
            .Distinct()
            .ToList();
    }

    private static async Task ProcessShiftAsync(
        IPage page,
        string shift,
        Action<string> status)
    {
        status($"Đang xử lý {shift}");

        // Mở ca
        await page.GetByRole(AriaRole.Link, new() { Name = shift })
            .ClickAsync();

        await page.WaitForLoadStateAsync();

        await ExecuteActionAsync(page, "Gửi lại", status);
        await ExecuteActionAsync(page, "Cập nhật", status);

        // Quay lại danh sách ca
        await page.GoBackAsync();
        await page.WaitForLoadStateAsync();
    }

    private static async Task ExecuteActionAsync(
        IPage page,
        string actionName,
        Action<string> status)
    {
        status(actionName);

        await ScrollToRightAsync(page);

        await page.GetByRole(AriaRole.Checkbox, new() { Name = actionName })
            .CheckAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Gửi" })
            .ClickAsync();

        await WaitForActionToFinishAsync(page);

        status($"{actionName} xong");
    }

    private static async Task ScrollToRightAsync(IPage page)
    {
        await page.EvaluateAsync(@"
            () => {
                const div = document.querySelector('.dxgvCSD');

                if (div) {
                    div.scrollLeft = div.scrollWidth;
                }
            }");
    }

    private static async Task WaitForActionToFinishAsync(IPage page)
    {
        await page.WaitForLoadStateAsync();
        await page.WaitForTimeoutAsync(ActionFinishWaitMs);

        var okButton = page.GetByRole(AriaRole.Button, new() { Name = "OK" });

        if (await okButton.CountAsync() > 0)
        {
            await okButton.ClickAsync();
        }
    }

    #endregion
}
