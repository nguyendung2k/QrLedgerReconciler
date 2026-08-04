using Microsoft.Playwright;
using QrLedgerReconciler.Infrastructure;
using System.Globalization;
using System.Text.RegularExpressions;

namespace QrLedgerReconciler.Services;

internal static class ShiftInvoiceProcessor
{
    private const int ShortWaitMs = 300;
    private const int MediumWaitMs = 600;
    private const int ActionWaitMs = 1200;   // Giảm nhẹ

    private record ShiftInfo(string Shift, string Color);

    public static async Task ClearOneDayAsync(
        IBrowserContext context,
        IPage page,
        DateTime day,
        Action<string> status)
    {
        status($"Ngày {day:dd/MM/yyyy}");

        await OpenHomeAsync(page, day, status);

        var shifts = await GetShiftListWithColorAsync(page);

        var targetShifts = shifts.Where(s => IsTargetColor(s.Color)).ToList();

        if (targetShifts.Count == 0)
        {
            status($"Không có ca #36C cần xử lý ngày {day:dd/MM/yyyy} (Tổng: {shifts.Count})");
            return;
        }

        status($"Xử lý {targetShifts.Count}/{shifts.Count} ca (#36C)");

        foreach (var shiftInfo in targetShifts)
        {
            await ProcessShiftAsync(context, page, shiftInfo.Shift, status);
        }
    }

    private static async Task ProcessShiftAsync(
        IBrowserContext context,
        IPage page,
        string shift,
        Action<string> status)
    {
        status($"Mở ca: {shift}");
        await OpenShiftAsync(page, shift);

        await OpenShiftMenuAsync(page);

        var popup = await OpenInvoiceWindowAsync(context, page);
        await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        // Xử lý lần lượt theo yêu cầu
        status("Tick & Gửi lại");
        await CheckResendAsync(popup);
        await SendAgainAsync(popup);

        status("Tick & Cập nhật");
        await CheckUpdateAsync(popup);
        await UpdateInvoiceAsync(context, popup, status);

        await popup.CloseAsync();
        await page.BringToFrontAsync();
        status($"Hoàn thành ca {shift}");
    }

    // ====================== TỐI ƯU POPUP ======================

    private static async Task<IPage> OpenInvoiceWindowAsync(IBrowserContext context, IPage page)
    {
        var invoiceLink = page.Locator("#wsctrl4div a").Filter(new() { HasText = "Hoá đơn NMKLHD theo lô" });

        // Cách tối ưu & ổn định nhất
        var popupTask = context.RunAndWaitForPageAsync(async () =>
        {
            await invoiceLink.ClickAsync(new() { Timeout = 10000 });
        }, new() { Timeout = 15000 });

        var popup = await popupTask;
        await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        return popup;
    }

    private static async Task UpdateInvoiceAsync(
        IBrowserContext context,
        IPage popup,
        Action<string> status)
    {
        status("Đang bấm Cập nhật...");

        

        var updateBtn = popup.Locator("input[value='Cập nhật']");

     

        // Tối ưu: Dùng RunAndWaitForPageAsync cho popup thứ 2
        var newPageTask = context.RunAndWaitForPageAsync(async () =>
        {
            await updateBtn.ClickAsync(new() { Timeout = 8000 });
        }, new() { Timeout = 5000 });


        status($"Đang bấm Cập nhật newWindow... {popup.Url}");

        try
        {
            var newWindow = await newPageTask;

            status($"Đang bấm Cập nhật newWindow... {popup}");
            await newWindow.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            status($"Đã mở cửa sổ cập nhật: {newWindow.Url}");
        }
        catch (TimeoutException)
        {
            status("Không mở được cửa sổ cập nhật mới (timeout).");
        }
    }

    // ====================== LẤY CA + MÀU (đã tối ưu) ======================
    private static async Task<List<ShiftInfo>> GetShiftListWithColorAsync(IPage page)
    {
        var shiftPattern = new Regex(@"^\d{8}\s*-\s*\d+$");

        var links = await page.Locator("table a, tr a").AllAsync();

        var tasks = links.Select(async link =>
        {
            var textTask = link.InnerTextAsync();
            var colorTask = link.EvaluateAsync<string>("el => window.getComputedStyle(el).color");

            await Task.WhenAll(textTask, colorTask);

            var text = Regex.Replace(await textTask, @"\s+", " ").Trim();
            return shiftPattern.IsMatch(text) ? new ShiftInfo(text, await colorTask) : null;
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(x => x != null)
                      .DistinctBy(x => x!.Shift)
                      .ToList()!;
    }

    private static bool IsTargetColor(string color)
    {
        if (string.IsNullOrWhiteSpace(color)) return false;

        bool is36C = color.Contains("51, 102, 204") ||
                     color.Contains("#3366cc", StringComparison.OrdinalIgnoreCase) ||
                     color.Contains("#36c", StringComparison.OrdinalIgnoreCase);



        return is36C;
    }

    // ====================== HỖ TRỢ KHÁC ======================
    private static async Task OpenHomeAsync(IPage page, DateTime day, Action<string> status)
    {
        await page.GotoAsync($"{EgasEndpoints.BaseUrl}/UHome/UHome.aspx",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });

        var dateInput = page.Locator("input[name='viewdate']");
        await dateInput.FillAsync(day.ToString("d/M/yyyy"));
        await dateInput.PressAsync("Enter");

        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
    }

    private static async Task OpenShiftAsync(IPage page, string shift)
    {
        var ca = page.Locator($"a:has-text('{shift}')").First;
        await ca.ClickAsync(new() { Timeout = 10000 });
        await page.WaitForTimeoutAsync(MediumWaitMs);
    }

    private static async Task OpenShiftMenuAsync(IPage page)
    {
        var arrow = page.Locator("#wsctrl4btn");
        await arrow.ClickAsync(new() { Timeout = 8000 });
        await page.WaitForTimeoutAsync(ShortWaitMs);
    }

    private static async Task SendAgainAsync(IPage popup)
    {
        await popup.Locator("input[value='Gửi lại']").ClickAsync();
        await popup.WaitForTimeoutAsync(ActionWaitMs);
    }

    private static async Task CheckResendAsync(IPage popup)
    {
        var cb = popup.Locator("//input[@value='Gửi lại']/following-sibling::input[@type='checkbox']");
        if (!await cb.IsCheckedAsync())
            await cb.CheckAsync();
    }

    private static async Task CheckUpdateAsync(IPage popup)
    {
        var cb = popup.Locator("//input[@value='Cập nhật']/following-sibling::input[@type='checkbox']");
        if (!await cb.IsCheckedAsync())
            await cb.CheckAsync();
    }

}