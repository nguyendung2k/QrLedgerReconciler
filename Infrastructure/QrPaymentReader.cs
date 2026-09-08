using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Microsoft.Playwright;

using QrLedgerReconciler.Infrastructure;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.Service;

public sealed class QrPaymentReader
{
    // ============================================================
    // CONSTANTS
    // ============================================================

    private const int DefaultTimeoutMs = 60_000;
    private const int NewPageTimeoutMs = 30_000;

    // ============================================================
    // LOG FILE
    // ============================================================

    private static readonly string LogFile =
        Path.Combine(
            AppContext.BaseDirectory,
            "qr-debug.log");

    // ============================================================
    // REGEX
    // ============================================================

    private static readonly Regex HdbankRegex =
        new(
            @"cards\.hdbank\.com\.vn",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    private static readonly Regex QuanLyYeuCauRegex =
        new(
            @"cards\.hdbank\.com\.vn/trasoat/QuanLyYeuCau",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    private static readonly Regex QrIndexRegex =
        new(
            @"cards\.hdbank\.com\.vn/trasoat/HDBPortal/PLXPaymentByQR/Index",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    private static readonly Regex ViewResultRegex =
        new(
            @"cards\.hdbank\.com\.vn/trasoat/HDBPortal/PLXPaymentByQR/ViewResult",
            RegexOptions.IgnoreCase |
            RegexOptions.Compiled);

    // ============================================================
    // MAIN STATIC METHOD
    // ============================================================

    public static async Task<decimal> GetStaticQrTotalAsync(
        IBrowserContext context,
        IPage egasHome,
        ReconciliationRequest request)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        if (egasHome == null)
            throw new ArgumentNullException(nameof(egasHome));

        if (request == null)
            throw new ArgumentNullException(nameof(request));

        Log("============================================================");
        Log("QR PAYMENT READER - START");
        Log("============================================================");

        Log($"From = {request.From:dd/MM/yyyy HH:mm:ss}");
        Log($"To   = {request.To:dd/MM/yyyy HH:mm:ss}");

        try
        {
            // ====================================================
            // 1. KIỂM TRA EGAS HOME
            // ====================================================

            Log("[1] EGAS home hiện tại:");
            Log($"    URL = {egasHome.Url}");

            if (string.IsNullOrWhiteSpace(egasHome.Url))
            {
                throw new InvalidOperationException(
                    "EGAS home chưa có URL.");
            }

            // ====================================================
            // 2. MỞ KTM HOME
            // ====================================================

            var ktmHomeUrl =
                $"{EgasEndpoints.BaseUrl}/KTMHomePage.aspx";

            Log("[2] Mở KTM Home:");
            Log($"    {ktmHomeUrl}");

            await egasHome.GotoAsync(
                ktmHomeUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = DefaultTimeoutMs
                });

            await WaitDomReadyAsync(egasHome);

            Log($"[2] KTM Home URL = {egasHome.Url}");

            // ====================================================
            // 3. TẠO PAGE QUẢN LÝ
            // ====================================================

            Log("[3] Tạo page quản lý...");

            var managementPage =
                await context.NewPageAsync();

            await AttachPageDiagnosticsAsync(
                managementPage,
                "MANAGEMENT");

            // ====================================================
            // 4. MỞ SVRHUB
            //
            // Không mở trực tiếp QuanLyYeuCau.
            //
            // Flow:
            //
            // KTMHomePage
            //      ↓
            // SvrHub.aspx
            //      ↓
            // Login/logintoken
            //      ↓
            // QuanLyYeuCau
            // ====================================================

            var svrHubUrl =
                $"{EgasEndpoints.BaseUrl}/Utils/SvrHub.aspx?pageindex=KTM_TransCheck";

            Log("[4] Mở SvrHub:");
            Log($"    {svrHubUrl}");

            await managementPage.GotoAsync(
                svrHubUrl,
                new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = DefaultTimeoutMs
                });

            Log("[4] Sau SvrHub:");
            Log($"    URL = {managementPage.Url}");

            await WaitForUrlAsync(
                managementPage,
                QuanLyYeuCauRegex,
                DefaultTimeoutMs,
                "QuanLyYeuCau");

            await WaitDomReadyAsync(managementPage);

            Log("[4] Đã vào QuanLyYeuCau:");
            Log($"    URL = {managementPage.Url}");

            // ====================================================
            // 5. KIỂM TRA LOGIN
            // ====================================================

            if (IsErrorLogin(managementPage.Url))
            {
                Log("[ERROR] HDBank trả về errorlogin.");

                await HandleHdbankErrorLoginAsync(
                    managementPage);

                throw new InvalidOperationException(
                    "HDBank trả về errorlogin khi mở QuanLyYeuCau.");
            }

            await LogPageStateAsync(
                managementPage,
                "QUANLYYEUCAU");

            await LogHdbankCookiesAsync(context);

            // ====================================================
            // 6. TÌM "QUẢN LÝ QR CODE"
            // ====================================================

            Log("[5] Tìm link 'Quản lý QR Code'...");

            ILocator qrManagementLink =
                managementPage.GetByRole(
                    AriaRole.Link,
                    new PageGetByRoleOptions
                    {
                        Name = "Quản lý QR Code",
                        Exact = true
                    });

            var qrLinkCount =
                await qrManagementLink.CountAsync();

            Log(
                $"[5] Số link 'Quản lý QR Code' = {qrLinkCount}");

            // Fallback
            if (qrLinkCount == 0)
            {
                qrManagementLink =
                    managementPage.GetByText(
                        "Quản lý QR Code",
                        new PageGetByTextOptions
                        {
                            Exact = true
                        });

                qrLinkCount =
                    await qrManagementLink.CountAsync();

                Log(
                    $"[5] Fallback GetByText = {qrLinkCount}");
            }

            if (qrLinkCount == 0)
            {
                Log(
                    "[ERROR] Không tìm thấy 'Quản lý QR Code'.");

                await LogPageStateAsync(
                    managementPage,
                    "KHONG_TIM_THAY_QUAN_LY_QR");

                throw new InvalidOperationException(
                    "Không tìm thấy link 'Quản lý QR Code' trên trang QuanLyYeuCau.");
            }

            // ====================================================
            // 7. DEBUG HREF
            // ====================================================

            try
            {
                var href =
                    await qrManagementLink.First
                        .GetAttributeAsync("href");

                var target =
                    await qrManagementLink.First
                        .GetAttributeAsync("target");

                Log("[6] QR Management link:");
                Log($"    href   = {href}");
                Log($"    target = {target}");
            }
            catch (Exception ex)
            {
                Log(
                    $"[WARN] Không đọc được href/target: {ex.Message}");
            }

            // ====================================================
            // 8. SNAPSHOT PAGE TRƯỚC CLICK
            // ====================================================

            var pagesBeforeQrClick =
                context.Pages.ToHashSet();

            Log(
                $"[7] Số page trước click 'Quản lý QR Code' = {pagesBeforeQrClick.Count}");

            LogAllPages(
                context,
                "BEFORE CLICK QUAN LY QR");

            // ====================================================
            // 9. CLICK "QUẢN LÝ QR CODE"
            // ====================================================

            await qrManagementLink.First.ClickAsync();

            Log(
                "[8] Đã click 'Quản lý QR Code'.");

            // ====================================================
            // 10. BẮT PAGE MỚI - QR INDEX
            // ====================================================

            var qrIndexPage =
                await WaitForNewPageByUrlAsync(
                    context,
                    pagesBeforeQrClick,
                    QrIndexRegex,
                    "Quản lý QR Code",
                    NewPageTimeoutMs);

            await AttachPageDiagnosticsAsync(
                qrIndexPage,
                "QR-INDEX");

            Log("[8] Đã bắt được tab QR Index:");
            Log($"    URL = {qrIndexPage.Url}");

            await WaitDomReadyAsync(qrIndexPage);

            LogAllPages(
                context,
                "AFTER QR INDEX OPEN");

            // ====================================================
            // 11. VALIDATE QR INDEX
            // ====================================================

            if (!QrIndexRegex.IsMatch(qrIndexPage.Url))
            {
                throw new InvalidOperationException(
                    $"Tab QR Code không đúng URL. URL hiện tại: {qrIndexPage.Url}");
            }

            // ====================================================
            // 12. KIỂM TRA LOGIN
            // ====================================================

            if (IsErrorLogin(qrIndexPage.Url))
            {
                Log(
                    "[ERROR] QR Index trả về errorlogin.");

                await HandleHdbankErrorLoginAsync(
                    qrIndexPage);

                throw new InvalidOperationException(
                    "HDBank trả về errorlogin khi mở QR Index.");
            }

            // ====================================================
            // 13. TÌM "XEM KẾT QUẢ THANH TOÁN"
            // ====================================================

            Log(
                "[9] Tìm 'Xem kết quả thanh toán'...");

            ILocator viewResultLink =
                qrIndexPage.GetByRole(
                    AriaRole.Link,
                    new PageGetByRoleOptions
                    {
                        Name = "Xem kết quả thanh toán",
                        Exact = true
                    });

            var viewResultCount =
                await viewResultLink.CountAsync();

            Log(
                $"[9] Số link 'Xem kết quả thanh toán' = {viewResultCount}");

            // Fallback
            if (viewResultCount == 0)
            {
                viewResultLink =
                    qrIndexPage.GetByText(
                        "Xem kết quả thanh toán",
                        new PageGetByTextOptions
                        {
                            Exact = true
                        });

                viewResultCount =
                    await viewResultLink.CountAsync();

                Log(
                    $"[9] Fallback GetByText = {viewResultCount}");
            }

            if (viewResultCount == 0)
            {
                Log(
                    "[ERROR] Không tìm thấy 'Xem kết quả thanh toán'.");

                await LogPageStateAsync(
                    qrIndexPage,
                    "KHONG_TIM_THAY_XEM_KET_QUA");

                throw new InvalidOperationException(
                    "Không tìm thấy link 'Xem kết quả thanh toán' trên trang Quản lý QR Code.");
            }

            // ====================================================
            // 14. DEBUG HREF
            // ====================================================

            try
            {
                var href =
                    await viewResultLink.First
                        .GetAttributeAsync("href");

                var target =
                    await viewResultLink.First
                        .GetAttributeAsync("target");

                Log("[10] View Result link:");
                Log($"    href   = {href}");
                Log($"    target = {target}");
            }
            catch (Exception ex)
            {
                Log(
                    $"[WARN] Không đọc được href/target: {ex.Message}");
            }

            // ====================================================
            // 15. SNAPSHOT PAGE TRƯỚC CLICK VIEW RESULT
            // ====================================================

            var pagesBeforeResultClick =
                context.Pages.ToHashSet();

            Log(
                $"[11] Số page trước click 'Xem kết quả thanh toán' = {pagesBeforeResultClick.Count}");

            LogAllPages(
                context,
                "BEFORE CLICK VIEW RESULT");

            // ====================================================
            // 16. CLICK VIEW RESULT
            // ====================================================

            await viewResultLink.First.ClickAsync();

            Log(
                "[12] Đã click 'Xem kết quả thanh toán'.");

            // ====================================================
            // 17. BẮT PAGE VIEW RESULT
            // ====================================================

            var resultPage =
                await WaitForNewPageByUrlAsync(
                    context,
                    pagesBeforeResultClick,
                    ViewResultRegex,
                    "Xem kết quả thanh toán",
                    NewPageTimeoutMs);

            await AttachPageDiagnosticsAsync(
                resultPage,
                "VIEW-RESULT");

            Log("[12] Đã bắt được tab ViewResult:");
            Log($"     URL = {resultPage.Url}");

            await WaitDomReadyAsync(resultPage);

            LogAllPages(
                context,
                "AFTER VIEW RESULT OPEN");

            // ====================================================
            // 18. VALIDATE VIEW RESULT
            // ====================================================

            if (!ViewResultRegex.IsMatch(resultPage.Url))
            {
                throw new InvalidOperationException(
                    $"Tab ViewResult không đúng URL. URL hiện tại: {resultPage.Url}");
            }

            // ====================================================
            // 19. KIỂM TRA LOGIN
            // ====================================================

            if (IsErrorLogin(resultPage.Url))
            {
                Log(
                    "[ERROR] ViewResult trả về errorlogin.");

                await HandleHdbankErrorLoginAsync(
                    resultPage);

                throw new InvalidOperationException(
                    "HDBank trả về errorlogin khi mở ViewResult.");
            }

            await LogPageStateAsync(
                resultPage,
                "VIEW_RESULT");

            // ====================================================
            // 20. TÌM INPUT NGÀY
            // ====================================================

            Log("[13] Tìm các ô ngày giờ...");

            var dateInputs =
                resultPage.GetByPlaceholder(
                    "DD-MM-YYYY HH:MM:SS*",
                    new PageGetByPlaceholderOptions
                    {
                        Exact = true
                    });

            var dateInputCount =
                await dateInputs.CountAsync();

            Log(
                $"[13] Số input ngày giờ = {dateInputCount}");

            if (dateInputCount < 2)
            {
                Log(
                    "[ERROR] Không tìm thấy đủ 2 ô ngày giờ.");

                await LogPageStateAsync(
                    resultPage,
                    "THIEU_DATE_INPUT");

                throw new InvalidOperationException(
                    $"Không tìm thấy đủ 2 ô ngày giờ. Tìm thấy: {dateInputCount}.");
            }

            // ====================================================
            // 21. FORMAT FROM / TO
            // ====================================================

            var fromText =
                request.From.ToString(
                    "dd-MM-yyyy HH:mm:00",
                    CultureInfo.InvariantCulture);

            var toText =
                request.To.ToString(
                    "dd-MM-yyyy HH:mm:59",
                    CultureInfo.InvariantCulture);

            Log("[14] Khoảng thời gian:");
            Log($"     FROM = {fromText}");
            Log($"     TO   = {toText}");

            // ====================================================
            // 22. FILL FROM
            // ====================================================

            await dateInputs
                .Nth(0)
                .FillAsync(fromText);

            Log(
                "[15] Đã nhập From.");

            // ====================================================
            // 23. FILL TO
            // ====================================================

            await dateInputs
                .Nth(1)
                .FillAsync(toText);

            Log(
                "[16] Đã nhập To.");

            // ====================================================
            // 24. TÌM BUTTON TÌM KIẾM
            // ====================================================

            Log(
                "[17] Tìm nút 'Tìm kiếm KQGD'...");

            ILocator searchButton =
                resultPage.GetByRole(
                    AriaRole.Button,
                    new PageGetByRoleOptions
                    {
                        Name = "Tìm kiếm KQGD",
                        Exact = true
                    });

            var searchButtonCount =
                await searchButton.CountAsync();

            Log(
                $"[17] Số button = {searchButtonCount}");

            // Fallback
            if (searchButtonCount == 0)
            {
                searchButton =
                    resultPage.GetByText(
                        "Tìm kiếm KQGD",
                        new PageGetByTextOptions
                        {
                            Exact = true
                        });

                searchButtonCount =
                    await searchButton.CountAsync();

                Log(
                    $"[17] Fallback GetByText = {searchButtonCount}");
            }

            if (searchButtonCount == 0)
            {
                throw new InvalidOperationException(
                    "Không tìm thấy nút 'Tìm kiếm KQGD'.");
            }

            // ====================================================
            // 25. CLICK TÌM KIẾM
            // ====================================================

            Log(
                "[18] Click 'Tìm kiếm KQGD'...");

            await searchButton.First.ClickAsync();

            Log(
                "[18] Đã click tìm kiếm.");

            // ====================================================
            // 26. CHỜ AJAX
            // ====================================================

            await Task.Delay(1_500);

            await WaitDomReadyAsync(
                resultPage);

            Log(
                "[19] Đọc kết quả...");

            // ====================================================
            // 27. ĐỌC H5
            // ====================================================

            var headings =
                resultPage.Locator("h5");

            var headingCount =
                await headings.CountAsync();

            Log(
                $"[19] Số h5 = {headingCount}");

            var allH5Texts =
                new List<string>();

            for (var i = 0; i < headingCount; i++)
            {
                try
                {
                    var text =
                        (await headings
                            .Nth(i)
                            .InnerTextAsync())
                        .Trim();

                    allH5Texts.Add(text);

                    Log(
                        $"[H5 {i}] {text}");
                }
                catch (Exception ex)
                {
                    Log(
                        $"[WARN] Không đọc được h5[{i}]: {ex.Message}");
                }
            }

            // ====================================================
            // 28. TÌM TOTAL
            // ====================================================

            var totalText =
                allH5Texts.FirstOrDefault(
                    x =>
                        x.Contains(
                            "VND)",
                            StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrWhiteSpace(totalText))
            {
                Log(
                    "[ERROR] Không tìm thấy h5 chứa 'VND)'.");

                await LogPageStateAsync(
                    resultPage,
                    "KHONG_TIM_THAY_TOTAL");

                throw new InvalidOperationException(
                    "Không tìm thấy tổng thanh toán trong kết quả HDBank.");
            }

            Log(
                $"[20] TOTAL TEXT = {totalText}");

            // ====================================================
            // 29. PARSE MONEY
            // ====================================================

            var total =
                MoneyParser.Parse(totalText);

            Log(
                $"[21] TOTAL DECIMAL = {total:N0}");

            Log("============================================================");
            Log("QR PAYMENT READER - SUCCESS");
            Log($"TOTAL = {total:N0}");
            Log("============================================================");

            return total;
        }
        catch (Exception ex)
        {
            Log("============================================================");
            Log("QR PAYMENT READER - ERROR");
            Log($"TYPE    = {ex.GetType().FullName}");
            Log($"MESSAGE = {ex.Message}");
            Log($"STACK   = {ex.StackTrace}");
            Log("============================================================");

            LogAllPages(
                context,
                "EXCEPTION");

            throw;
        }
    }

    // ============================================================
    // WAIT NEW PAGE BY URL
    // ============================================================

    private static async Task<IPage> WaitForNewPageByUrlAsync(
        IBrowserContext context,
        IReadOnlyCollection<IPage> existingPages,
        Regex expectedUrlRegex,
        string actionName,
        int timeoutMs)
    {
        var stopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        var alreadyLoggedPages =
            new HashSet<IPage>();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var currentPages =
                context.Pages;

            foreach (var page in currentPages)
            {
                // ------------------------------------------------
                // CHỈ NHẬN PAGE THỰC SỰ MỚI
                // ------------------------------------------------

                if (existingPages.Contains(page))
                    continue;

                string url;

                try
                {
                    url = page.Url;
                }
                catch
                {
                    url = string.Empty;
                }

                if (!alreadyLoggedPages.Contains(page))
                {
                    alreadyLoggedPages.Add(page);

                    Log(
                        $"[NEW PAGE][{actionName}] Page mới: {url}");
                }

                // ------------------------------------------------
                // Có thể page mới đang ở about:blank.
                // Tiếp tục chờ redirect.
                // ------------------------------------------------

                if (expectedUrlRegex.IsMatch(url))
                {
                    Log(
                        $"[NEW PAGE][{actionName}] ĐÚNG URL: {url}");

                    try
                    {
                        await WaitDomReadyAsync(page);
                    }
                    catch (Exception ex)
                    {
                        Log(
                            $"[WARN] Wait DOM Ready lỗi: {ex.Message}");
                    }

                    return page;
                }
            }

            await Task.Delay(200);
        }

        Log(
            $"[NEW PAGE][TIMEOUT] {actionName} sau {timeoutMs / 1000} giây.");

        LogAllPages(
            context,
            $"TIMEOUT {actionName}");

        throw new TimeoutException(
            $"Không phát hiện TAB mới đúng URL sau thao tác '{actionName}' trong {timeoutMs / 1000} giây.");
    }

    // ============================================================
    // WAIT URL
    // ============================================================

    private static async Task WaitForUrlAsync(
        IPage page,
        Regex expectedUrlRegex,
        int timeoutMs,
        string description)
    {
        var stopwatch =
            System.Diagnostics.Stopwatch.StartNew();

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            var url =
                page.Url;

            if (expectedUrlRegex.IsMatch(url))
            {
                Log(
                    $"[WAIT URL] {description}: OK -> {url}");

                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Timeout khi chờ URL '{description}'. URL hiện tại: {page.Url}");
    }

    // ============================================================
    // WAIT DOM READY
    // ============================================================

    private static async Task WaitDomReadyAsync(
        IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(
                LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions
                {
                    Timeout = 15_000
                });
        }
        catch (TimeoutException)
        {
            Log(
                $"[WARN] DOMContentLoaded timeout: {page.Url}");
        }
        catch (Exception ex)
        {
            Log(
                $"[WARN] WaitDomReadyAsync: {ex.Message}");
        }
    }

    // ============================================================
    // LOGIN CHECK
    // ============================================================

    private static bool IsErrorLogin(
        string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (url.Contains(
                "errorlogin",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (url.Contains(
                "/Login/",
                StringComparison.OrdinalIgnoreCase))
        {
            if (!url.Contains(
                    "QuanLyYeuCau",
                    StringComparison.OrdinalIgnoreCase)
                &&
                !url.Contains(
                    "PLXPaymentByQR",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // ============================================================
    // HANDLE LOGIN ERROR
    // ============================================================

    private static async Task HandleHdbankErrorLoginAsync(
        IPage page)
    {
        try
        {
            Log(
                $"[HDB ERROR LOGIN] URL = {page.Url}");

            var title =
                await page.TitleAsync();

            Log(
                $"[HDB ERROR LOGIN] TITLE = {title}");

            var body =
                page.Locator("body");

            if (await body.CountAsync() > 0)
            {
                var text =
                    await body.InnerTextAsync();

                if (text.Length > 3_000)
                {
                    text =
                        text[..3_000];
                }

                Log(
                    $"[HDB ERROR LOGIN] BODY:\n{text}");
            }
        }
        catch (Exception ex)
        {
            Log(
                $"[WARN] HandleHdbankErrorLoginAsync: {ex.Message}");
        }
    }

    // ============================================================
    // PAGE DIAGNOSTICS
    // ============================================================

    private static async Task AttachPageDiagnosticsAsync(
        IPage page,
        string name)
    {
        try
        {
            page.Request += (_, request) =>
            {
                try
                {
                    if (IsInterestingUrl(request.Url))
                    {
                        Log(
                            $"[REQUEST][{name}] {request.Method} {request.Url}");
                    }
                }
                catch
                {
                    // Diagnostics không được làm crash automation.
                }
            };

            page.Response += (_, response) =>
            {
                try
                {
                    if (IsInterestingUrl(response.Url))
                    {
                        Log(
                            $"[RESPONSE][{name}] {(int)response.Status} {response.Url}");
                    }
                }
                catch
                {
                    // Ignore diagnostics errors.
                }
            };

            page.FrameNavigated += (_, frame) =>
            {
                try
                {
                    Log(
                        $"[FRAME][{name}] {frame.Url}");
                }
                catch
                {
                    // Ignore.
                }
            };

            page.Popup += (_, popup) =>
            {
                try
                {
                    Log(
                        $"[POPUP][{name}] {popup.Url}");
                }
                catch
                {
                    // Ignore.
                }
            };

            page.Close += (_, _) =>
            {
                try
                {
                    Log(
                        $"[PAGE CLOSED][{name}]");
                }
                catch
                {
                    // Ignore.
                }
            };
        }
        catch (Exception ex)
        {
            Log(
                $"[WARN] AttachPageDiagnosticsAsync({name}): {ex.Message}");
        }

        await Task.CompletedTask;
    }

    // ============================================================
    // INTERESTING URL
    // ============================================================

    private static bool IsInterestingUrl(
        string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return
            HdbankRegex.IsMatch(url)
            ||
            url.Contains(
                "SvrHub.aspx",
                StringComparison.OrdinalIgnoreCase)
            ||
            url.Contains(
                "KTMHomePage.aspx",
                StringComparison.OrdinalIgnoreCase);
    }

    // ============================================================
    // LOG ALL PAGES
    // ============================================================

    private static void LogAllPages(
        IBrowserContext context,
        string title)
    {
        try
        {
            Log(
                $"---------------- {title} ----------------");

            var pages =
                context.Pages;

            Log(
                $"PAGE COUNT = {pages.Count}");

            for (var i = 0; i < pages.Count; i++)
            {
                var page =
                    pages[i];

                string url;

                try
                {
                    url = page.Url;
                }
                catch
                {
                    url = "<unknown>";
                }

                Log(
                    $"PAGE[{i}] = {url}");
            }

            Log(
                "------------------------------------------------");
        }
        catch (Exception ex)
        {
            Log(
                $"[WARN] LogAllPages: {ex.Message}");
        }
    }

    // ============================================================
    // LOG PAGE STATE
    // ============================================================

    private static async Task LogPageStateAsync(
        IPage page,
        string name)
    {
        try
        {
            Log(
                $"[PAGE STATE][{name}] URL = {page.Url}");

            var title =
                await page.TitleAsync();

            Log(
                $"[PAGE STATE][{name}] TITLE = {title}");

            var body =
                page.Locator("body");

            if (await body.CountAsync() == 0)
                return;

            var text =
                await body.InnerTextAsync();

            text =
                text.Trim();

            if (text.Length > 8_000)
            {
                text =
                    text[..8_000];
            }

            Log(
                $"[PAGE STATE][{name}] BODY:\n{text}");
        }
        catch (Exception ex)
        {
            Log(
                $"[WARN] LogPageStateAsync({name}): {ex.Message}");
        }
    }

    // ============================================================
    // LOG HDBANK COOKIES
    // ============================================================

    private static async Task LogHdbankCookiesAsync(
        IBrowserContext context)
    {
        try
        {
            var cookies =
                await context.CookiesAsync();

            var hdbCookies =
                cookies
                    .Where(x =>
                        x.Domain.Contains(
                            "hdbank.com.vn",
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

            Log(
                $"[COOKIES] HDBank cookie count = {hdbCookies.Count}");

            foreach (var cookie in hdbCookies)
            {
                // Không log cookie value.
                Log(
                    $"[COOKIE] {cookie.Name} | Domain={cookie.Domain} | Path={cookie.Path}");
            }
        }
        catch (Exception ex)
        {
            Log(
                $"[WARN] LogHdbankCookiesAsync: {ex.Message}");
        }
    }

    // ============================================================
    // REDIRECT CHAIN
    // ============================================================

    private static async Task LogRedirectChainAsync(
        IResponse? response,
        string name)
    {
        if (response == null)
            return;

        try
        {
            Log(
                $"[REDIRECT][{name}] Final = {response.Url}");

            var request =
                response.Request;

            var chain =
                request.RedirectedFrom;

            var chainUrls =
                new List<string>();

            while (chain != null)
            {
                chainUrls.Add(
                    chain.Url);

                chain =
                    chain.RedirectedFrom;
            }

            chainUrls.Reverse();

            foreach (var url in chainUrls)
            {
                Log(
                    $"[REDIRECT][{name}] {url}");
            }
        }
        catch (Exception ex)
        {
            Log(
                $"[WARN] LogRedirectChainAsync: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    // ============================================================
    // FILE LOG
    // ============================================================

    private static void Log(
        string message)
    {
        try
        {
            var line =
                $"[{DateTime.Now:dd/MM/yyyy HH:mm:ss.fff}] {message}";

            File.AppendAllText(
                LogFile,
                line + Environment.NewLine);
        }
        catch
        {
            // Không để lỗi logging làm crash chương trình.
        }
    }
}