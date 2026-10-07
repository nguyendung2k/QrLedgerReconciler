using Microsoft.Playwright;

namespace QrLedgerReconciler.Infrastructure;

/// <summary>
/// Ép nội dung web vừa khổ giấy trước khi in qua --kiosk-printing.
///
/// Vì sao không chỉnh được "scale 80%" trực tiếp:
///   Chrome không có command-line switch hay lệnh CDP nào đặt tỉ lệ in khi
///   chạy --kiosk-printing. CDP Page.printToPDF có tham số scale nhưng chỉ
///   sinh file PDF rồi bỏ đi, không đổi được tỉ lệ cho lần in thực.
///   Hộp thoại in là nơi duy nhất có ô "Scale → Custom → 80%" nhưng kiosk
///   in thẳng nên hộp thoại không hiện ra.
///
/// Vậy ép bằng kích thước giấy. Đổi khổ giấy sang khổ 60% của khổ đang in
/// → cùng một khối layout nội dung nhưng trên tờ giấy nhỏ hơn 40%, nên
/// nội dung chiếm tỉ lệ in ra giống hệt scale 60%. Đây là cách duy nhất
/// chỉnh được tỉ lệ mà không bỏ luồng in tự động.
/// </summary>
internal static class PrintLayout
{
    /// <summary>
    /// Tỉ lệ thu nhỏ mong muốn.
    /// </summary>
    public const double Scale = 1;

    /// <summary>
    /// Khổ giấy A4 thật: 210mm x 297mm.
    /// </summary>
    private const double A4MmX = 210.0;
    private const double A4MmY = 297.0;

    public static Task ApplyAsync(ILocator root, double scale = Scale)
    {
        if (Math.Abs(scale - 1.0) < 0.0001)
        {
            return Task.CompletedTask;
        }

        var w = (int)Math.Round(A4MmX * scale);
        var h = (int)Math.Round(A4MmY * scale);

        var script = $$"""
            (wMm, hMm, scale) => {
                if (document.documentElement.dataset.printLayout === '1') return;

                const apply = () => {
                    // 1) Đổi khổ giấy @page sang khổ 60% của A4. Khi in, nội
                    //    dung bị ép vào tờ giấy nhỏ hơn 40% => tỉ lệ in ra
                    //    đúng bằng một phép scale 60%.
                    const style = document.createElement('style');
                    style.textContent = '@page { size: ' + wMm + 'mm ' + hMm + 'mm; margin: 0; }';
                    document.head.appendChild(style);

                    // 2) Co viewport về 60% để layout web khớp khổ giấy đã ép,
                    //    tránh nội dung tràn ra lề in.
                    const el = document.documentElement;
                    el.style.setProperty('zoom', String(scale), 'important');
                    el.style.setProperty('min-height', '0', 'important');
                    document.body.style.setProperty('min-height', '0', 'important');

                    document.documentElement.dataset.printLayout = '1';
                };

                if (document.readyState === 'complete') apply();
                else window.addEventListener('load', apply, { once: true });
            }
            """;

        try
        {
            return root.EvaluateAsync(script, new object?[] { w, h, scale });
        }
        catch
        {
            // Bỏ qua — không ép được vẫn in được, không làm hỏng luồng in.
            return Task.CompletedTask;
        }
    }
}