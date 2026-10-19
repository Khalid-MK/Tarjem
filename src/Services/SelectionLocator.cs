using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using Tarjem.Interop;

namespace Tarjem.Services
{
    /// <summary>
    /// Finds where the selected text is on screen (physical pixels), so the popup can
    /// open right under it instead of at the mouse.
    ///   1. UI Automation TextPattern: real selection bounds (Chrome, Edge, Word, WPF, WinUI…)
    ///   2. Win32 caret position (Notepad, classic edit controls…)
    ///   3. null → caller falls back to the mouse cursor
    /// Runs on a worker thread with a hard timeout so a slow/hung app can't delay the popup.
    /// </summary>
    internal static class SelectionLocator
    {
        private const int TimeoutMs = 350;

        public static async Task<Native.RECT?> LocateAsync(IntPtr foreground)
        {
            var work = Task.Run(() => FromUiAutomation() ?? FromCaret(foreground));
            var finished = await Task.WhenAny(work, Task.Delay(TimeoutMs));
            if (finished != work) return null;
            try { return work.Result; }
            catch { return null; }
        }

        private static Native.RECT? FromUiAutomation()
        {
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element == null) return null;
                if (!element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern) ||
                    pattern is not TextPattern text)
                    return null;

                var ranges = text.GetSelection();
                if (ranges == null || ranges.Length == 0) return null;

                double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
                foreach (var range in ranges)
                {
                    foreach (var rc in range.GetBoundingRectangles())
                    {
                        if (rc.IsEmpty || rc.Width < 1 || rc.Height < 4) continue;
                        l = Math.Min(l, rc.Left);
                        t = Math.Min(t, rc.Top);
                        r = Math.Max(r, rc.Right);
                        b = Math.Max(b, rc.Bottom);
                    }
                }
                if (l == double.MaxValue) return null;
                return new Native.RECT((int)l, (int)t, (int)Math.Ceiling(r), (int)Math.Ceiling(b));
            }
            catch
            {
                return null; // element went away, pattern not supported, provider error…
            }
        }

        private static Native.RECT? FromCaret(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero) return null;
            uint thread = Native.GetWindowThreadProcessId(foreground, out _);
            if (thread == 0) return null;

            var info = new Native.GUITHREADINFO { cbSize = Marshal.SizeOf<Native.GUITHREADINFO>() };
            if (!Native.GetGUIThreadInfo(thread, ref info) || info.hwndCaret == IntPtr.Zero) return null;

            var rc = info.rcCaret;
            if (rc.Bottom - rc.Top < 4) return null;

            var topLeft = new Native.POINT { X = rc.Left, Y = rc.Top };
            var bottomRight = new Native.POINT { X = rc.Right, Y = rc.Bottom };
            Native.ClientToScreen(info.hwndCaret, ref topLeft);
            Native.ClientToScreen(info.hwndCaret, ref bottomRight);
            return new Native.RECT(topLeft.X, topLeft.Y, Math.Max(bottomRight.X, topLeft.X + 1), bottomRight.Y);
        }
    }
}
