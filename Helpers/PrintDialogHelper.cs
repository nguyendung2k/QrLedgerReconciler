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
    internal static class PrintDialogHelper
    {
        public static bool ClickPrint(int timeoutMs = 100)
        {
            var sw = Stopwatch.StartNew();

            using var automation = new UIA3Automation();

            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                try
                {
                    var desktop = automation.GetDesktop();

                    // Lấy tất cả cửa sổ
                    var windows = desktop.FindAllChildren(
                        cf => cf.ByControlType(ControlType.Window));

                    foreach (var window in windows)
                    {
                        System.Diagnostics.Debug.WriteLine($"Window: {window.Name}");

                        // Tìm tất cả Button trong cửa sổ
                        var buttons = window.FindAllDescendants(
                            cf => cf.ByControlType(ControlType.Button));

                        foreach (var btn in buttons)
                        {
                            System.Diagnostics.Debug.WriteLine($"Button: {btn.Name}");

                            if (btn.Name.Equals("Print", StringComparison.OrdinalIgnoreCase) ||
                                btn.Name.Equals("In", StringComparison.OrdinalIgnoreCase))
                            {
                                btn.AsButton().Invoke();

                                return true;
                            }
                        }
                    }
                }
                catch
                {
                }

                Thread.Sleep(300);
            }

            return false;
        }
    }
}