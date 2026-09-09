using Microsoft.Playwright;
using System.IO;

namespace QrLedgerReconciler.Infrastructure;

/// <summary>
/// Centralizes Playwright browser/context creation.
///
/// Strategy:
/// 1. Prefer Google Chrome installed on the machine.
/// 2. If Chrome cannot be launched, fallback to the bundled Playwright Chromium
///    located in "playwright-browsers" beside the application.
/// 3. Keeps all browser launch configuration in one place.
///
/// Browser priority:
///     Google Chrome -> Playwright Chromium
/// </summary>
internal static class BrowserContextFactory
{
    public const float DefaultTimeoutMs = 60_000;
    public const int BrowserSlowMoMs = 100;

    private const string ChromeChannel = "chrome";
    private const string BrowserDirectoryName = "playwright-browsers";

    /// <summary>
    /// Runs an operation using a new Playwright browser context.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        Func<IBrowserContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var playwright = await Playwright.CreateAsync();

        await using var browser = await LaunchBrowserAsync(playwright);

        var context = await browser.NewContextAsync(
            new BrowserNewContextOptions
            {
                IgnoreHTTPSErrors = true
            });

        context.SetDefaultTimeout(DefaultTimeoutMs);

        try
        {
            return await action(context);
        }
        finally
        {
            await context.CloseAsync();
        }
    }

    /// <summary>
    /// Runs an operation using a new Playwright browser context.
    /// </summary>
    public static Task RunAsync(
        Func<IBrowserContext, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return RunAsync(async context =>
        {
            await action(context);
            return true;
        });
    }

    /// <summary>
    /// Launches Google Chrome first.
    ///
    /// If Google Chrome cannot be launched, Playwright Chromium
    /// bundled with the application is used as a fallback.
    /// </summary>
    private static async Task<IBrowser> LaunchBrowserAsync(
        IPlaywright playwright)
    {
        try
        {
            return await LaunchChromeAsync(playwright);
        }
        catch (PlaywrightException chromeException)
        {
            try
            {
                return await LaunchBundledChromiumAsync(playwright);
            }
            catch (Exception chromiumException)
            {
                throw new InvalidOperationException(
                    BuildBrowserLaunchErrorMessage(
                        chromeException,
                        chromiumException),
                    chromiumException);
            }
        }
    }

    /// <summary>
    /// Launches the Google Chrome installed on Windows.
    ///
    /// Playwright resolves the "chrome" channel automatically.
    /// </summary>
    private static async Task<IBrowser> LaunchChromeAsync(
        IPlaywright playwright)
    {
        return await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = false,

                // IMPORTANT:
                // Use installed Google Chrome.
                Channel = ChromeChannel,

                SlowMo = BrowserSlowMoMs,

                Args = new[]
                {
                    "--force-renderer-accessibility",
                    "--kiosk-printing"
                }
            });
    }

    /// <summary>
    /// Launches the Playwright Chromium bundled with the application.
    ///
    /// Expected deployment structure:
    ///
    /// Application.exe
    /// playwright-browsers\
    ///     chromium-1187\
    ///     chromium_headless_shell-1187\
    ///     ffmpeg-1011\
    ///     winldd-1007\
    /// </summary>
    private static async Task<IBrowser> LaunchBundledChromiumAsync(
        IPlaywright playwright)
    {
        var browserDirectory = Path.Combine(
            AppContext.BaseDirectory,
            BrowserDirectoryName);

        if (!Directory.Exists(browserDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Không tìm thấy Chromium dự phòng của Playwright.\n\n" +
                $"Đường dẫn:\n{browserDirectory}\n\n" +
                "Hãy đảm bảo thư mục 'playwright-browsers' " +
                "được đóng gói cùng ứng dụng.");
        }

        // Tell Playwright to search for its browser binaries
        // inside the application directory.
        Environment.SetEnvironmentVariable(
            "PLAYWRIGHT_BROWSERS_PATH",
            browserDirectory);

        return await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = false,

                // No Channel here:
                // this launches the bundled Playwright Chromium.
                SlowMo = BrowserSlowMoMs,

                Args = new[]
                {
                    "--force-renderer-accessibility",
                    "--kiosk-printing"
                }
            });
    }

    /// <summary>
    /// Creates a useful error message when both Chrome
    /// and the bundled Chromium fail to launch.
    /// </summary>
    private static string BuildBrowserLaunchErrorMessage(
        Exception chromeException,
        Exception chromiumException)
    {
        return
            "Không thể khởi động trình duyệt cho HDBank QR Reconciler.\n\n" +

            "Ưu tiên 1 - Google Chrome:\n" +
            $"{chromeException.Message}\n\n" +

            "Ưu tiên 2 - Playwright Chromium:\n" +
            $"{chromiumException.Message}\n\n" +

            "Kiểm tra:\n" +
            "1. Google Chrome đã được cài đặt trên máy.\n" +
            "2. Nếu không có Chrome, thư mục 'playwright-browsers' " +
            "phải nằm cạnh file EXE.\n" +
            "3. Không xóa thư mục '.playwright' trong bộ cài.";
    }
}