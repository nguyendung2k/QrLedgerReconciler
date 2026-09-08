using Microsoft.Playwright;
using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;
using QrLedgerReconciler.Service;

namespace QrLedgerReconciler.Services;

/// <summary>
/// Điều phối luồng đối chiếu sổ cái với QR tĩnh HDBank. Toàn bộ chi tiết
/// triển khai (đăng nhập, đọc sổ cái, đọc QR, xử lý ca) nằm ở các class
/// chuyên trách trong Infrastructure / Ledger / Qr / Shifts.
/// </summary>
public sealed class ReconciliationService
{
    public Task<ReconciliationResult> RunAsync(
        ReconciliationRequest request,
        Action<string> status)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            status("Đang khởi động trình duyệt...");

            var egas = await context.NewPageAsync();

            status("Đang đăng nhập EGAS...");

            try
            {
                await EgasAuthenticator.LoginAsync(egas, EgasEndpoints.BaseUrl, request);
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new Exception("Không thể đăng nhập EGAS.", ex);
            }

            status("Đang lấy tổng sổ cái...");
            var ledgerTotal = await LedgerReportReader.GetTotalAsync(context, request);

            var qrTotal = await GetQrTotalAsync(context, egas, request, status);

            return new ReconciliationResult(ledgerTotal, qrTotal);
        });
    }

    private static Task<decimal> GetQrTotalAsync(
        IBrowserContext context,
        IPage page,
        ReconciliationRequest request,
        Action<string> status)
    {
        status("Đang tra soát QR tĩnh HDBank...");
        return QrPaymentReader.GetStaticQrTotalAsync(context, page, request);
    }

    public Task<decimal> GetLedgerOnlyAsync(
        ReconciliationRequest request,
        Action<string> status)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            var page = await context.NewPageAsync();

            status("Đang đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);

            status("Đang lấy dữ liệu sổ cái...");
            return await LedgerReportReader.GetTotalAsync(context, request);
        });
    }

    public Task<decimal> GetQrOnlyAsync(
        ReconciliationRequest request,
        Action<string> status)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            var page = await context.NewPageAsync();

            status("Đang đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);

            return await GetQrTotalAsync(context, page, request, status);
        });
    }

    public Task ClearShiftAsync(
        ReconciliationRequest request,
        Action<string> status)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            var page = await context.NewPageAsync();

            status("Đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);

            var currentDay = request.From.Date;
            var lastDay = request.To.Date;

            while (currentDay <= lastDay)
            {
                status($"Xử lý ngày {currentDay:dd/MM/yyyy}");

                await ShiftInvoiceProcessor.ClearOneDayAsync(context, page, currentDay, status);

                currentDay = currentDay.AddDays(1);
            }

            status("Hoàn thành.");
        });
    }
}
