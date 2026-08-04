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

        await ClickLoginButtonAsync(page);

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

    /// <summary>
    /// Trang nội bộ (IP 192.168.1.101) dùng nút ảnh img[src='img/go.gif'].
    /// Trang public (egas.petrolimex.com.vn) dùng nút chữ "Đăng nhập"
    /// (khác với "Đăng nhập qua AD" nên phải match text chính xác).
    /// Thử ảnh trước, không có thì fallback sang nút chữ.
    /// </summary>
    private static async Task ClickLoginButtonAsync(IPage page)
    {
        var imageButton = page.Locator("img[src='img/go.gif']");

        if (await imageButton.CountAsync() > 0)
        {
            await imageButton.First.ClickAsync();
            return;
        }

        var textButton = page.GetByText("Đăng nhập", new() { Exact = true });
        await textButton.First.ClickAsync();
    }

    public static Task LoginAsync(IPage page, string baseUrl, ReconciliationRequest request)
        => LoginAsync(page, baseUrl, request.UserName, request.Password);
}