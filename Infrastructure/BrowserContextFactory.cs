using Microsoft.Playwright;
using System;
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
    /// Thư mục profile cố định của ứng dụng. Dùng persistent context để cookie,
    /// session đăng nhập EGAS và các thiết lập trình duyệt được giữ lại giữa các
    /// lần chạy. Cố ý KHÔNG dùng profile Chrome của người dùng
    /// (%LOCALAPPDATA%\Google\Chrome\User Data) để tránh xung đột khoá profile
    /// khi người dùng đang mở Chrome.
    /// </summary>
    private static string ProfileDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QrLedgerReconciler",
        "chrome-profile");

    /// <summary>
    /// Runs an operation using the application's persistent browser profile.
    /// </summary>
    public static async Task<T> RunAsync<T>(
        Func<IBrowserContext, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        using var playwright = await Playwright.CreateAsync();

        Directory.CreateDirectory(ProfileDirectory);

        var context = await LaunchPersistentContextAsync(playwright);

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
    /// Launches Chrome/Chromium with a persistent profile directory.
    /// </summary>
    private static async Task<IBrowserContext> LaunchPersistentContextAsync(
        IPlaywright playwright)
    {
        try
        {
            return await playwright.Chromium.LaunchPersistentContextAsync(
                ProfileDirectory,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    Channel = ChromeChannel,
                    SlowMo = BrowserSlowMoMs,
                    IgnoreHTTPSErrors = true,
                    Args = new[]
                    {
                        "--force-renderer-accessibility",
                        "--kiosk-printing"
                    }
                });
        }
        catch (PlaywrightException chromeException)
        {
            try
            {
                return await LaunchBundledChromiumPersistentAsync(playwright);
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
    /// Launches the Playwright Chromium bundled with the application
    /// using a persistent profile directory.
    /// </summary>
    private static async Task<IBrowserContext> LaunchBundledChromiumPersistentAsync(
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

        Environment.SetEnvironmentVariable(
            "PLAYWRIGHT_BROWSERS_PATH",
            browserDirectory);

        return await playwright.Chromium.LaunchPersistentContextAsync(
            ProfileDirectory,
            new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false,
                SlowMo = BrowserSlowMoMs,
                IgnoreHTTPSErrors = true,
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