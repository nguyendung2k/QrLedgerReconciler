using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QrLedgerReconciler.Infrastructure;

namespace QrLedgerReconciler.Services;

internal static class ShiftInvoiceProcessor
{
    private const int ShortWaitMs = 500;
    private const int MediumWaitMs = 1_000;
    private const int SendAgainWaitMs = 1_500;
    private const int PopupWaitMs = 2000;  

    public static async Task ClearOneDayAsync(
        IBrowserContext context,
        IPage page,
        DateTime day,
        Action<string> status)
    {
        status($"Ngày {day:dd/MM/yyyy}");

        await OpenHomeAsync(page, day, status);

        var shifts = await GetShiftListAsync(page);

        if (shifts.Count == 0)
        {
            status($"Không có ca ngày {day:dd/MM/yyyy}");
            return;
        }

        status($"Có {shifts.Count} ca.");

        foreach (var shift in shifts)
        {
            status($"Đang xử lý {shift}");
            await ProcessShiftAsync(context, page, shift, status);
        }
    }

    private static async Task ProcessShiftAsync(
        IBrowserContext context,
        IPage page,
        string shift,
        Action<string> status)
    {
        status($"Đang mở ca: {shift}");
        await OpenShiftAsync(page, shift);

        status("Đang mở menu ▼");
        await OpenShiftMenuAsync(page);

        status("Đang mở Hóa đơn NMKLHD theo lô");
        var popup = await OpenInvoiceWindowAsync(context, page);

        status("Đã mở popup hóa đơn");
        await popup.WaitForLoadStateAsync(LoadState.NetworkIdle);

        status("Tick gửi lại");
        await CheckResendAsync(popup);

        status("Bấm gửi");
        await SendAgainAsync(popup);

        status("Tick cập nhật");
        await CheckUpdateAsync(popup);

        status("Bấm cập nhật");
        await UpdateInvoiceAsync(context, popup, status);

        status("Đã hoàn tất cập nhật hóa đơn.");

        await popup.CloseAsync();
        await page.BringToFrontAsync();

        status($"Hoàn thành ca {shift}");
    }

    private static async Task UpdateInvoiceAsync(
        IBrowserContext context,
        IPage popup,
        Action<string> status)
    {
        status("Đang cập nhật hóa đơn NMKLHD theo lô...");

        var updateBtn = popup.Locator("input[value='Cập nhật']");
        await updateBtn.ClickAsync();

        // Chờ popup/tab mới
        IPage? updatePage = null;
        try
        {
            updatePage = await context.WaitForPageAsync(new BrowserContextWaitForPageOptions { Timeout = 8000 });
            await updatePage.WaitForLoadStateAsync();
        }
        catch
        {
            updatePage = popup;
        }

        // Poll trạng thái
        await PollUpdateStatusAsync(updatePage ?? popup, status);
    }

    /// <summary>
    /// Poll API cập nhật trực tiếp
    /// </summary>
    private static async Task PollUpdateStatusAsync(IPage page, Action<string> status)
    {
        var maxAttempts = 60;
        var attempt = 0;

        var recIDs = await page.EvaluateAsync<string>("() => document.querySelector('input[name=\"recIDs\"]')?.value || ''");

        while (attempt < maxAttempts)
        {
            try
            {
                var response = await page.EvaluateAsync<string>($@"
                async () => {{
                    try {{
                        const res = await fetch('http://192.168.1.101/EINV/EInv_Batch_Fix.aspx?displayMode=full&mode=getinv', {{
                            method: 'POST',
                            headers: {{ 'Content-Type': 'application/x-www-form-urlencoded' }},
                            body: 'recIDs=' + encodeURIComponent('{recIDs}')
                        }});
                        return await res.text();
                    }} catch(e) {{ return 'error: ' + e.message; }}
                }}
            ");

                if (response.Contains("cập nhật thành công", StringComparison.OrdinalIgnoreCase) ||
                    response.Contains("Cần gửi thông tin để cập nhật dữ liệu", StringComparison.OrdinalIgnoreCase))
                {
                    status("✅ Cập nhật hóa đơn NMKLHD theo lô thành công.");
                    return;
                }

                status($"Đang cập nhật... ({attempt + 1}/{maxAttempts})");
            }
            catch (Exception ex)
            {
                status($"Poll API lỗi: {ex.Message}");
            }

            await Task.Delay(1000);
            attempt++;
        }

        status("⏰ Hết thời gian chờ cập nhật.");
    }

    // ==================== Các method hỗ trợ khác giữ nguyên ====================

    private static async Task OpenShiftMenuAsync(IPage page)
    {
        var arrow = page.Locator("#wsctrl4btn");
        await arrow.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await arrow.ClickAsync();
        await page.WaitForTimeoutAsync(ShortWaitMs);
    }

    private static async Task<IPage> OpenInvoiceWindowAsync(IBrowserContext context, IPage page)
    {
        var invoiceLink = page.Locator("#wsctrl4div a").Filter(new() { HasText = "Hoá đơn NMKLHD theo lô" });

        await invoiceLink.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });

        try
        {
            var popup = await context.RunAndWaitForPageAsync(async () => await invoiceLink.ClickAsync(), new() { Timeout = PopupWaitMs });
            await popup.WaitForLoadStateAsync();
            return popup;
        }
        catch (TimeoutException)
        {
            return page;
        }
    }

    private static async Task SendAgainAsync(IPage popup)
    {
        await popup.Locator("input[type='button'][value='Gửi lại']").ClickAsync();
        await popup.WaitForTimeoutAsync(SendAgainWaitMs);
    }

    private static async Task CheckUpdateAsync(IPage popup)
    {
        var checkbox = popup.Locator("//input[@value='Cập nhật']/following-sibling::input[@type='checkbox']");
        await checkbox.WaitForAsync();
        if (!await checkbox.IsCheckedAsync()) await checkbox.CheckAsync();
    }

    private static async Task CheckResendAsync(IPage popup)
    {
        var checkbox = popup.Locator("//input[@value='Gửi lại']/following-sibling::input[@type='checkbox']");
        await checkbox.WaitForAsync();
        if (!await checkbox.IsCheckedAsync()) await checkbox.CheckAsync();
    }

    private static async Task<List<string>> GetShiftListAsync(IPage page)
    {
        var shiftPattern = new Regex(@"^\d{8}\s*-\s*\d+$");
        var result = new List<string>();

        var links = await page.Locator("a").AllAsync();

        foreach (var link in links)
        {
            var text = Regex.Replace(await link.InnerTextAsync(), @"\s+", " ").Trim();
            if (shiftPattern.IsMatch(text))
                result.Add(text);
        }

        return result.Distinct().ToList();
    }

    private static async Task OpenShiftAsync(IPage page, string shift)
    {
        var ca = page.Locator("a").Filter(new() { HasText = shift }).First;
        await ca.WaitForAsync();
        await ca.ClickAsync();
        await page.WaitForTimeoutAsync(MediumWaitMs);
    }

    private static async Task OpenHomeAsync(IPage page, DateTime day, Action<string> status)
    {
        await page.GotoAsync($"{EgasEndpoints.BaseUrl}/UHome/UHome.aspx");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        status($"Đang nhập Ngày mở ca {day:dd/MM/yyyy}");

        var dateInput = page.Locator("input[name='viewdate']");
        await dateInput.WaitForAsync();
        await dateInput.FillAsync(day.ToString("d/M/yyyy"));
        await dateInput.PressAsync("Enter");

        await page.WaitForTimeoutAsync(MediumWaitMs);
    }
}