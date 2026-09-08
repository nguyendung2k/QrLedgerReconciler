using System.Globalization;
using Microsoft.Playwright;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.Infrastructure;

internal static class LedgerReportReader
{
    private const string AccountDesc = "112716 - Bán hàng thanh toán QRCode tĩnh";

    public static async Task<decimal> GetTotalAsync(
        IBrowserContext context,
        ReconciliationRequest request)
    {
        var page = await context.NewPageAsync();

        var url =
            $"{EgasEndpoints.BaseUrl}/RPT/RPT.aspx" +
            $"?id=GLBookBangKe" +
            $"&FROMDATE={Uri.EscapeDataString(request.From.ToString("d/M/yyyy", CultureInfo.InvariantCulture))}" +
            $"&TODATE={Uri.EscapeDataString(request.To.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture))}" +
            $"&ACCT={Uri.EscapeDataString(AccountDesc)}" +
            $"&outputformat=1" +
            $"&formison=1";

        await page.GotoAsync(url);

        var row = page.Locator("tr")
            .Filter(new() { HasText = "TỔNG CỘNG" });

        // Chờ nội dung dòng tổng thực sự có chữ số — dữ liệu load async,
        // WaitForAsync() trả về ngay khi element tồn tại dù số chưa render.
        await page.WaitForFunctionAsync(
            @"() => {
                const tr = document.querySelectorAll('tr');
                for (const row of tr) {
                    if (row.textContent.includes('TỔNG CỘNG') && /\d/.test(row.textContent))
                        return true;
                }
                return false;
            }");

        return MoneyParser.Parse(await row.InnerTextAsync());
    }
}
