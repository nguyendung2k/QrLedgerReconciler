using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.Service;

internal static class QrPaymentReader
{
    public static async Task<decimal> GetStaticQrTotalAsync(
        IBrowserContext context,
        IPage egasHome,
        ReconciliationRequest request)
    {
        await egasHome.GotoAsync($"{EgasEndpoints.BaseUrl}/UHome/KTMHomePage.aspx");

        var management = await context.NewPageAsync();

        await management.GotoAsync(
            $"{EgasEndpoints.BaseUrl}/Utils/SvrHub.aspx?pageindex=KTM_TransCheck");

        await management.WaitForURLAsync(
            new Regex("cards\\.hdbank\\.com\\.vn",
                RegexOptions.IgnoreCase));

        if (management.Url.Contains("errorlogin",
                StringComparison.OrdinalIgnoreCase))
        {
            await management.GetByRole(
                AriaRole.Link,
                new() { Name = "Click vào đây để thoát" })
                .ClickAsync();

            await management.GotoAsync(
                $"{EgasEndpoints.BaseUrl}/Utils/SvrHub.aspx?pageindex=KTM_TransCheck");
        }

        await management.WaitForURLAsync(
            new Regex("QuanLyYeuCau", RegexOptions.IgnoreCase));

        var qrIndex = await context.RunAndWaitForPageAsync(async () =>
            await management.GetByRole(
                AriaRole.Link,
                new() { Name = "Quản lý QR Code" })
                .ClickAsync());

        await qrIndex.WaitForURLAsync(
            new Regex("PLXPaymentByQR/Index",
                RegexOptions.IgnoreCase));

        var result = await context.RunAndWaitForPageAsync(async () =>
            await qrIndex.GetByRole(
                AriaRole.Link,
                new() { Name = "Xem kết quả thanh toán" })
                .ClickAsync());

        await result.WaitForURLAsync(
            new Regex("PLXPaymentByQR/ViewResult",
                RegexOptions.IgnoreCase));

        var dates = result.GetByPlaceholder(
            "DD-MM-YYYY HH:MM:SS*",
            new() { Exact = true });

        await dates.Nth(0).FillAsync(
            request.From.ToString(
                "dd-MM-yyyy HH:mm:00",
                CultureInfo.InvariantCulture));

        await dates.Nth(1).FillAsync(
            request.To.ToString(
                "dd-MM-yyyy HH:mm:59",
                CultureInfo.InvariantCulture));

        await result.GetByRole(
            AriaRole.Button,
            new() { Name = "Tìm kiếm KQGD" })
            .ClickAsync();

        var total = (await result.Locator("h5").AllInnerTextsAsync())
            .FirstOrDefault(x =>
                x.Contains("VND)", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "Không tìm thấy tổng QR tĩnh HDBank.");

        return MoneyParser.Parse(total);
    }
}
