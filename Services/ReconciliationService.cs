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
        Action<string> status,
        RunControl control,
        bool headless = false)
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
            catch (Exception ex) when (!control.IsStopped)
            {
                // Đang dừng thì để lỗi gốc nổi lên: bọc thành "Không thể đăng nhập
                // EGAS." sẽ biến thao tác Dừng toàn bộ thành thông báo lỗi.
                throw new Exception("Không thể đăng nhập EGAS.", ex);
            }

            control.ThrowIfStopped();

            status("Đang lấy tổng sổ cái...");
            var ledgerTotal = await LedgerReportReader.GetTotalAsync(context, request, control);

            var qrTotal = await GetQrTotalAsync(context, egas, request, status, control.Token);

            return new ReconciliationResult(ledgerTotal, qrTotal);
        }, headless, control);
    }

    private static Task<decimal> GetQrTotalAsync(
        IBrowserContext context,
        IPage page,
        ReconciliationRequest request,
        Action<string> status,
        CancellationToken token)
    {
        status("Đang tra soát QR tĩnh HDBank...");
        return QrPaymentReader.GetStaticQrTotalAsync(context, page, request, token);
    }

    public Task<decimal> GetLedgerOnlyAsync(
        ReconciliationRequest request,
        Action<string> status,
        RunControl control,
        bool headless = false)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            var page = await context.NewPageAsync();

            status("Đang đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);

            control.ThrowIfStopped();

            status("Đang lấy dữ liệu sổ cái...");
            return await LedgerReportReader.GetTotalAsync(context, request, control);
        }, headless, control);
    }

    public Task<decimal> GetQrOnlyAsync(
        ReconciliationRequest request,
        Action<string> status,
        RunControl control,
        bool headless = false)
    {
        return BrowserContextFactory.RunAsync(async context =>
        {
            var page = await context.NewPageAsync();

            status("Đang đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);

            return await GetQrTotalAsync(context, page, request, status, control.Token);
        }, headless, control);
    }

    public Task ClearShiftAsync(
        ReconciliationRequest request,
        Action<string> status,
        RunControl control,
        bool headless = false)
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
                await control.CheckpointAsync();

                status($"Xử lý ngày {currentDay:dd/MM/yyyy}");

                await ShiftInvoiceProcessor.ClearOneDayAsync(context, page, currentDay, status, control);

                currentDay = currentDay.AddDays(1);
            }

            status("Hoàn thành.");
        }, headless, control);
    }
}
