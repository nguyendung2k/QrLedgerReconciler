using Microsoft.Playwright;
using QrLedgerReconciler.Models;
using System.Text.RegularExpressions;

namespace QrLedgerReconciler.Infrastructure;

public static class EgasAuthenticator
{
    public static async Task LoginAsync(IPage page, string baseUrl, string userName, string password)
    {
        await page.GotoAsync($"{baseUrl}/login.aspx");

        await page.Locator("input[name='UserID']").FillAsync(userName);
        await page.Locator("input[name='UserPassword']").FillAsync(password);

        await page.Locator("img[src='img/go.gif']").ClickAsync();

        await page.WaitForURLAsync(new Regex("(errcode=203|/UHome/)", RegexOptions.IgnoreCase));

        if (page.Url.Contains("errcode=203", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Sai tài khoản hoặc mật khẩu EGAS.");
        }

        var invalidLoginText = page.Locator("text=Thông tin đăng nhập không hợp lệ.");
        if (await invalidLoginText.CountAsync() > 0)
        {
            throw new InvalidOperationException("Sai tài khoản hoặc mật khẩu EGAS.");
        }
    }

    public static Task LoginAsync(IPage page, string baseUrl, ReconciliationRequest request)
        => LoginAsync(page, baseUrl, request.UserName, request.Password);
}