using Microsoft.Playwright;

namespace QrLedgerReconciler.Infrastructure;

/// <summary>
/// Centralizes Playwright browser/context creation and disposal. Both
/// ReconciliationService and ClearShiftService used to have an identical
/// copy of this setup code — now there is exactly one place to change
/// launch options (headless, slow-mo, timeout, ...).
/// </summary>
internal static class BrowserContextFactory
{
    public const float DefaultTimeoutMs = 60_000;
    public const int BrowserSlowMoMs = 100;

    public static async Task<T> RunAsync<T>(Func<IBrowserContext, Task<T>> action)
    {
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = false,
            SlowMo = BrowserSlowMoMs,
            Args = new[] { "--force-renderer-accessibility" , "--kiosk-printing" }
        });

        var context = await browser.NewContextAsync(new()
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
            
        }
    }

    public static Task RunAsync(Func<IBrowserContext, Task> action) =>
        RunAsync(async context =>
        {
            await action(context);
            return true;
        });
}
