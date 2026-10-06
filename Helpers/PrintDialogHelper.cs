using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace QrLedgerReconciler.Helpers
{
    /// <summary>
    /// Điều khiển hộp thoại in của Chrome bằng FlaUI (UI Automation).
    ///
    /// Luồng: đợi hộp thoại "In"/"Print" xuất hiện → mở "Cài đặt khác"
    /// ("More settings") → chọn "Scale"/"Tỉ lệ" = "Custom"/"Tùy chỉnh"
    /// → gõ 80 vào ô tỉ lệ → bấm "Print"/"In".
    ///
    /// Ứng dụng KHÔNG chạy --kiosk-printing nữa, vì kiosk in thẳng không có
    /// hộp thoại để đặt tỉ lệ. Hộp thoại in của Chrome là bản địa hoá theo
    /// ngôn ngữ hệ thống nên các nhãn tìm theo cả tiếng Anh lẫn tiếng Việt.
    /// </summary>
    internal static class PrintDialogHelper
    {
        // Thời gian tối đa đợi toàn bộ luồng (mở dialog → set tỉ lệ → bấm in).
        private const int TotalTimeoutMs = 25_000;

        // Tỉ lệ in mong muốn.
        public const double ScalePercent = 80.0;

        public static string ScalePercentPercent => ScalePercent.ToString("0") + "%";

        // Khoảng nghỉ giữa các lần quét UI Automation.
        private const int PollDelayMs = 300;

        public static bool SetScaleAndPrint(
            int timeoutMs = TotalTimeoutMs)
        {
            var sw = Stopwatch.StartNew();

            using var automation = new UIA3Automation();

            AutomationElement? printDialog = null;

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var desktop = automation.GetDesktop();

                printDialog = FindPrintDialog(desktop);

                if (printDialog != null)
                {
                    break;
                }

                Thread.Sleep(PollDelayMs);
            }

            if (printDialog == null)
            {
                return false;
            }

            // Mở "More settings" nếu chưa mở — phần Scale thường nằm trong đó.
            TryClickMoreSettings(printDialog);

            // Chọn Combobox "Scale" → "Custom", rồi gõ 80 vào ô bên cạnh.
            if (!SetScaleCustom(printDialog))
            {
                return false;
            }

            // Bấm nút Print/In.
            return TryClickPrint(printDialog);
        }

        private static AutomationElement? FindPrintDialog(AutomationElement desktop)
        {
            var windows = desktop.FindAllChildren(
                cf => cf.ByControlType(ControlType.Window));

            foreach (var window in windows)
            {
                var name = window.Name ?? string.Empty;

                if (name.Contains("Print", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("In", StringComparison.OrdinalIgnoreCase))
                {
                    return window;
                }
            }

            return null;
        }

        private static void TryClickMoreSettings(AutomationElement dialog)
        {
            var buttons = dialog.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Button));

            foreach (var btn in buttons)
            {
                var name = btn.Name ?? string.Empty;

                if (name.Contains("More settings", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Cài đặt khác", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Cài đặt thêm", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Cài đặt", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        btn.AsButton().Invoke();
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool SetScaleCustom(AutomationElement dialog)
        {
            // Tìm Combobox có nhãn/giá trị liên quan đến Scale/Tỉ lệ.
            var combos = dialog.FindAllDescendants(
                cf => cf.ByControlType(ControlType.ComboBox));

            AutomationElement? scaleCombo = null;

            foreach (var combo in combos)
            {
                var name = combo.Name ?? string.Empty;

                if (name.Contains("Scale", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tỉ lệ", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tỷ lệ", StringComparison.OrdinalIgnoreCase))
                {
                    scaleCombo = combo;
                    break;
                }
            }

            if (scaleCombo == null)
            {
                // Chrome dùng một nút có thể là ComboBox hoặc Button chứa giá
                // trị tỉ lệ hiện tại, ví dụ "Default"/"Mặc định". Thử tìm theo
                // giá trị hiển thị.
                var buttons = dialog.FindAllDescendants(
                    cf => cf.ByControlType(ControlType.Button));

                foreach (var btn in buttons)
                {
                    var name = btn.Name ?? string.Empty;

                    if (name.Contains("Default", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Mặc định", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Scale", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Tỉ lệ", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Tỷ lệ", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            btn.AsButton().Invoke();
                            scaleCombo = btn;
                            break;
                        }
                        catch
                        {
                        }
                    }
                }
            }

            if (scaleCombo == null)
            {
                return false;
            }

            // Mở dropdown rồi chọn "Custom"/"Tùy chỉnh".
            try
            {
                scaleCombo.AsComboBox().Expand();
                Thread.Sleep(PollDelayMs);
            }
            catch
            {
            }

            SelectCustomOption(dialog);

            // Gõ giá trị 80 vào ô Edit chứa số phần trăm.
            return SetScalePercentValue(dialog, ScalePercent);
        }

        private static bool SelectCustomOption(AutomationElement dialog)
        {
            // Sau khi mở dropdown, các mục là ListItem chứa "Custom"/"Tùy chỉnh".
            var items = dialog.FindAllDescendants(
                cf => cf.ByControlType(ControlType.ListItem));

            foreach (var item in items)
            {
                var name = item.Name ?? string.Empty;

                if (name.Contains("Custom", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tùy chỉnh", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tuỳ chỉnh", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        item.Click();
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static bool SetScalePercentValue(AutomationElement dialog, double percent)
        {
            // Ô nhập số % thường là một Edit. Sau khi chọn Custom, Chrome hiện
            // một ô nhập với giá trị mặc định (ví dụ "100"). Gõ đè giá trị mới.
            var edits = dialog.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Edit));

            foreach (var edit in edits)
            {
                var name = edit.Name ?? string.Empty;
                var value = edit.AsTextBox()?.Text ?? string.Empty;

                bool looksLikeScale =
                    name.Contains("Scale", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tỉ lệ", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Tỷ lệ", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("%", StringComparison.OrdinalIgnoreCase);

                if (looksLikeScale)
                {
                    try
                    {
                        var textBox = edit.AsTextBox();
                        textBox.Text = percent.ToString("0");
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static bool TryClickPrint(AutomationElement dialog)
        {
            var buttons = dialog.FindAllDescendants(
                cf => cf.ByControlType(ControlType.Button));

            foreach (var btn in buttons)
            {
                var name = btn.Name ?? string.Empty;

                if (name.Equals("Print", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("In", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        btn.AsButton().Invoke();
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }
    }
}