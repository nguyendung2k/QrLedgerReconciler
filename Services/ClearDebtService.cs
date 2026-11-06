using Microsoft.Playwright;
using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace QrLedgerReconciler.Services;

/// <summary>
/// Tự động clear công nợ tháng trên trang AR_DueDate_Prepare.
/// Lặp qua từng khách: mở link ">>>>" (tab mới) → xử lý dialog alert (nếu có)
/// → nhấn Lưu → đóng tab → reload danh sách
/// → lặp cho đến khi danh sách trống.
///
/// Thời điểm clear tự tính: 23:58 ngày cuối tháng trước (không dùng
/// request.From/Request.To) — port từ EgasTelegramBot/ClearDebtService.
/// </summary>
public class ClearDebtService
{
    private const string AccountCode = "131202";
    private const int PopupTimeoutMs = 15_000;
    private const int ClickTimeoutMs = 10_000;
    private const int AfterSaveWaitMs = 2_000;
    private const int AfterReloadWaitMs = 1_000;

    public async Task<ClearDebtResult> RunAsync(
        ReconciliationRequest request,
        Action<string> status,
        RunControl control,
        bool headless = false)
    {
        return await BrowserContextFactory.RunAsync(async context =>
        {
            status("Khởi động trình duyệt...");

            var page = await context.NewPageAsync();

            status("Đăng nhập EGAS...");
            await EgasAuthenticator.LoginAsync(page, EgasEndpoints.BaseUrl, request);

            var toDate = GetLastDayOfPreviousMonth();
            status($"Mở báo cáo AR_DueDate_Prepare (tháng {toDate:MM/yyyy})...");
            await OpenReportAsync(page, toDate);

            var processedCount = 0;
            var errors = new List<string>();

            while (true)
            {
                await control.CheckpointAsync(status);

                var customers = await ParseCustomerListAsync(page, status);

                if (customers.Count == 0)
                {
                    status("✅ Danh sách trống — đã clear xong tất cả khách.");
                    break;
                }

                status($"Còn {customers.Count} khách. Đang xử lý khách tiếp theo...");

                var current = customers[0];

                try
                {
                    await ProcessOneCustomerAsync(context, page, current, status, control);
                    processedCount++;
                }
                catch (OperationCanceledException)
                {
                    // Người dùng bấm Dừng toàn bộ — ném tiếp ra ngoài để dừng hẳn,
                    // không ghi thành "Lỗi khách X".
                    throw;
                }
                catch (Exception ex) when (!control.IsStopped)
                {
                    var errMsg = $"Lỗi khách {current.CustomerCode}: {ex.Message}";
                    errors.Add(errMsg);
                    status($"⚠️ {errMsg}");
                }

                await ReloadListPageAsync(page, toDate, status);
            }

            status($"Hoàn thành: {processedCount} khách đã clear, {errors.Count} lỗi.");
            return new ClearDebtResult(processedCount + errors.Count, processedCount, errors);
        }, headless, control);
    }

    /// <summary>
    /// 23:58 ngày cuối tháng trước — cùng logic với GetLastDayOfPreviousMonth
    /// của EgasTelegramBot.
    /// </summary>
    private static DateTime GetLastDayOfPreviousMonth()
    {
        var today = DateTime.Today;
        var lastMonth = today.AddMonths(-1);
        return new DateTime(lastMonth.Year, lastMonth.Month,
            DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month),
            23, 58, 0);
    }

    // ====================== MỞ BÁO CÁO ======================

    private static string BuildReportUrl(DateTime toDate)
    {
        var toDateText = toDate.ToString("d/M/yyyy HH:mm", CultureInfo.InvariantCulture);
        var escaped = Uri.EscapeDataString(toDateText);

        return $"{EgasEndpoints.BaseUrl}/RPT/RPT.aspx?id=AR_DueDate_Prepare" +
               $"&TODATE={escaped}" +
               $"&ACCT={AccountCode}" +
               $"&CREDITAREA=" +
               $"&outputformat=1" +
               $"&noheader=" +
               $"&formison=1" +
               $"&rpttype=";
    }

    private static async Task OpenReportAsync(IPage page, DateTime toDate)
    {
        await page.GotoAsync(BuildReportUrl(toDate), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 90_000
        });

        await page.WaitForURLAsync(
            new Regex("formison=1", RegexOptions.IgnoreCase),
            new PageWaitForURLOptions
            {
                Timeout = 90_000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

        await page.WaitForTimeoutAsync(AfterReloadWaitMs);
    }

    // ====================== PARSE DANH SÁCH KHÁCH ======================

    private static async Task<List<CustomerInfo>> ParseCustomerListAsync(IPage page, Action<string> status)
    {
        // Luôn chụp screenshot để debug nếu parse gặp vấn đề.
        await SaveDebugScreenshotAsync(page);

        // Lấy các dòng dữ liệu (bỏ header). Ưu tiên tbody, fallback tr:not(.trhdr).
        var rows = page.Locator("table tbody tr");
        if (await rows.CountAsync() == 0)
        {
            rows = page.Locator("table tr:not(.trhdr)");
        }

        var rowCount = await rows.CountAsync();
        if (rowCount == 0)
        {
            // Không có dòng dữ liệu nào. Nếu trang vẫn có header bảng
            // ("Mã khách" / "Clear chứng từ") thì coi là danh sách trống —
            // thành công (hết khách để clear). Nếu không có header → trang
            // lỗi thật, vẫn throw để người dùng kiểm tra screenshot.
            var hasHeader =
                await page.Locator("text=Mã khách").CountAsync() > 0 ||
                await page.Locator("text=Clear chứng từ").CountAsync() > 0;

            if (hasHeader)
            {
                status("Trang có header nhưng không còn dòng dữ liệu — coi như danh sách trống.");
                return new List<CustomerInfo>();
            }

            throw new InvalidOperationException(
                "Không tìm thấy dòng nào trong bảng danh sách khách. Kiểm tra screenshot clear-debt-page.png");
        }

        var customers = new List<CustomerInfo>();

        for (var i = 0; i < rowCount; i++)
        {
            var row = rows.Nth(i);

            // Tìm link ">>>>" (href chứa navtr hoặc nội dung >>>>).
            var link = row.Locator("a[href*='navtr']").First;
            if (await link.CountAsync() == 0)
            {
                link = row.Locator("a:has-text('>>>>')").First;
            }

            if (await link.CountAsync() == 0)
            {
                // Dòng không có link clear — bỏ qua (có thể là dòng tổng hoặc dòng khác).
                continue;
            }

            var href = await link.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var parsed = TryParseNavtrHref(href, out var customerCode, out var customerName);
            if (!parsed)
            {
                status($"⚠️ Không parse được href: {href}");
                continue;
            }

            customers.Add(new CustomerInfo(customerCode, customerName, href));
        }

        if (customers.Count == 0)
        {
            // Danh sách có dòng nhưng không còn dòng nào có link clear —
            // hết khách cần clear → thành công (fix V2: không throw nữa).
            status("0 khách có link clear — coi như đã clear xong tất cả.");
            return customers;
        }

        status($"Tìm thấy {customers.Count} khách cần clear.");
        return customers;
    }

    /// <summary>
    /// Parse href dạng: javascript:navtr('253120067,&gt;221.003,&gt;Công ty...');
    /// → customerCode = "221.003", customerName = "Công ty..."
    /// </summary>
    private static bool TryParseNavtrHref(string href, out string customerCode, out string customerName)
    {
        customerCode = "";
        customerName = "";

        var match = Regex.Match(href, @"navtr\('([^']*)'\)");
        if (!match.Success) return false;

        var raw = match.Groups[1].Value;
        // Decode HTML entities: &gt; → >, &lt; → <, &amp; → &
        raw = System.Net.WebUtility.HtmlDecode(raw);

        // Tách theo dấu phẩy: [shiftId, customerCode, customerName, ...]
        var parts = raw.Split(',');
        if (parts.Length < 3) return false;

        customerCode = parts[1].Trim();
        customerName = string.Join(",", parts.Skip(2)).Trim();

        if (string.IsNullOrWhiteSpace(customerCode)) return false;

        return true;
    }

    private static async Task SaveDebugScreenshotAsync(IPage page)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(dir, "clear-debt-page.png"),
                FullPage = true
            });
        }
        catch { }
    }

    // ====================== XỬ LÝ TỪNG KHÁCH ======================

    private static async Task ProcessOneCustomerAsync(
        IBrowserContext context,
        IPage page,
        CustomerInfo customer,
        Action<string> status,
        RunControl control)
    {
        await control.CheckpointAsync(status);

        status($"Đang xử lý khách {customer.CustomerCode} ({customer.CustomerName})...");

        // Setup dialog handler TRƯỚC khi click link — alert có thể không xuất hiện
        // với một số khách, handler chỉ fire khi có dialog.
        //
        // Handler phải là một biến cục bộ có tên để còn gỡ được ở cuối hàm:
        // page.Dialog -= (_, _) => { } tạo delegate MỚI (khác target) nên không
        // gỡ gì cả, khiến mỗi khách lại cộng thêm một handler.
        //
        // Thân handler là async void: nếu "Dừng toàn bộ" đóng trình duyệt đúng
        // lúc dialog đang xử lý, AcceptAsync ném PlaywrightException và sẽ làm
        // sập ứng dụng (không có DispatcherUnhandledException ở App.xaml.cs).
        EventHandler<IDialog>? dialogHandler = async (_, dialog) =>
        {
            try
            {
                status($"Dialog: {dialog.Message}");
                await dialog.AcceptAsync();
            }
            catch (Exception ex) when (!control.IsStopped)
            {
                // Lỗi thật khi xử lý dialog — báo cho người dùng biết, nhưng KHÔNG
                // để lọt ra ngoài: đây là async void.
                status($"⚠️ Không xử lý được dialog: {ex.Message}");
            }
            catch (Exception)
            {
                // Context đã bị đóng (Dừng toàn bộ) — bỏ qua, luồng chính đã dừng.
                // Vẫn phải bắt: lỗi thoát khỏi async void sẽ làm sập ứng dụng
                // (App không có DispatcherUnhandledException).
            }
        };

        page.Dialog += dialogHandler;

        // Click link ">>>>" → mở tab mới (TR.aspx).
        var popupTask = context.RunAndWaitForPageAsync(async () =>
        {
            var link = page.Locator($"a[href*='navtr']").First;
            await link.ClickAsync(new() { Timeout = ClickTimeoutMs });
        }, new() { Timeout = PopupTimeoutMs });

        IPage detailPage;
        try
        {
            detailPage = await popupTask;
            await detailPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await detailPage.WaitForTimeoutAsync(500); // Chờ dialog alert xuất hiện

            // Nhấn Lưu (Ctrl-S). Chỉ cần nhấn Lưu là đã hoàn thành — không cần
            // đợi modal "Đã cập nhập chứng từ"; đóng tab luôn rồi làm khách tiếp theo.
            await detailPage.ClickAsync("#savebtn");
            await detailPage.WaitForTimeoutAsync(AfterSaveWaitMs);
            status($"✅ Khách {customer.CustomerCode} đã lưu chứng từ.");

            // Đóng tab detail ngay sau khi Lưu.
            await detailPage.CloseAsync();
            await page.BringToFrontAsync();
        }
        finally
        {
            // Gỡ đúng handler đã đăng ký — nếu để lại, mỗi khách sẽ cộng dồn
            // một handler trên cùng page và cùng tranh AcceptAsync một dialog.
            page.Dialog -= dialogHandler;
        }
    }

    // ====================== RELOAD DANH SÁCH ======================

    private static async Task ReloadListPageAsync(IPage page, DateTime toDate, Action<string> status)
    {
        await page.GotoAsync(BuildReportUrl(toDate), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 90_000
        });

        await page.WaitForURLAsync(
            new Regex("formison=1", RegexOptions.IgnoreCase),
            new PageWaitForURLOptions
            {
                Timeout = 90_000,
                WaitUntil = WaitUntilState.DOMContentLoaded
            });

        await page.WaitForTimeoutAsync(AfterReloadWaitMs);
    }

    private sealed record CustomerInfo(string CustomerCode, string CustomerName, string Href);
}

public sealed record ClearDebtResult(int TotalCustomers, int SuccessCount, List<string> Errors);
