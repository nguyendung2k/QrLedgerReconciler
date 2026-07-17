using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.Infrastructure;

internal static class LedgerReportReader
{
    private const string AccountCode = "112716";

    public static async Task<decimal> GetTotalAsync(
        IBrowserContext context,
        ReconciliationRequest request)
    {
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{EgasEndpoints.BaseUrl}/RPT/RPT.aspx?id=GLBookBangKe");

        await page.Locator("input[name='FROMDATE']")
            .FillAsync(request.From.ToString("d/M/yyyy HH:mm",
                CultureInfo.InvariantCulture));

        await page.Locator("input[name='TODATE']")
            .FillAsync(request.To.ToString("d/M/yyyy HH:mm",
                CultureInfo.InvariantCulture));

        await page.Locator("input[name='ACCT']")
            .FillAsync(AccountCode);

        await page.Locator("img[title='Ctrl-Enter']")
            .ClickAsync();

        await page.WaitForURLAsync(
            new Regex("formison=1", RegexOptions.IgnoreCase));

        var row = page.Locator("tr")
            .Filter(new() { HasText = "TỔNG CỘNG" });

        await row.WaitForAsync();

        return MoneyParser.Parse(await row.InnerTextAsync());
    }
}
