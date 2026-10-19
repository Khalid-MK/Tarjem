using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Tarjem.Interop;

namespace Tarjem.Services
{
    internal static class ClipboardUtil
    {
        /// <summary>The clipboard is often briefly locked by the app that just wrote to it.</summary>
        public static T? Retry<T>(Func<T> action, int attempts = 10)
        {
            for (int i = 0; i < attempts; i++)
            {
                try { return action(); }
                catch (ExternalException) { Thread.Sleep(25); }
            }
            return default;
        }

        public static void Retry(Action action, int attempts = 10) =>
            Retry<bool>(() => { action(); return true; }, attempts);

        public static string? TryGetText() =>
            Retry(() => Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null);

        public static void SetText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Retry(() => Clipboard.SetDataObject(new DataObject(DataFormats.UnicodeText, text), true));
        }
    }

    /// <summary>Best-effort copy of the common clipboard formats so they can be put back.</summary>
    internal sealed class ClipboardSnapshot
    {
        private static readonly string[] Formats =
        {
            DataFormats.UnicodeText, DataFormats.Text, DataFormats.Rtf,
            DataFormats.Html, DataFormats.Bitmap, DataFormats.FileDrop
        };

        private readonly List<KeyValuePair<string, object>> _items = new();

        public static ClipboardSnapshot Capture()
        {
            var snap = new ClipboardSnapshot();
            ClipboardUtil.Retry(() =>
            {
                snap._items.Clear();
                var data = Clipboard.GetDataObject();
                if (data == null) return;
                foreach (var f in Formats)
                {
                    try
                    {
                        if (!data.GetDataPresent(f, false)) continue;
                        var value = data.GetData(f, false);
                        if (value != null) snap._items.Add(new(f, value));
                    }
                    catch { /* some formats can't be read; skip them */ }
                }
            });
            return snap;
        }

        public void Restore()
        {
            if (_items.Count == 0) return; // nothing we know how to put back
            var obj = new DataObject();
            foreach (var kv in _items)
            {
                try { obj.SetData(kv.Key, kv.Value); } catch { }
            }
            ClipboardUtil.Retry(() => Clipboard.SetDataObject(obj, true));
        }
    }

    /// <summary>
    /// Reads the current selection of whatever app is in front by sending Ctrl+C
    /// once, only when the hotkey is pressed. Nothing runs in the background.
    /// </summary>
    internal static class SelectionGrabber
    {
        public static async Task<string?> GetSelectedTextAsync(bool restoreClipboard)
        {
            // Give the user a moment to let go of Alt/Shift/Win, otherwise the
            // target app would receive Ctrl+Alt+C instead of Ctrl+C.
            var wait = Stopwatch.StartNew();
            while (Native.AnyNonCtrlModifierDown() && wait.ElapsedMilliseconds < 700)
                await Task.Delay(15);

            var snapshot = restoreClipboard ? ClipboardSnapshot.Capture() : null;
            uint before = Native.GetClipboardSequenceNumber();

            Native.SendCtrlChord(Native.VK_C);

            string? text = null;
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 650)
            {
                await Task.Delay(20);
                if (Native.GetClipboardSequenceNumber() != before)
                {
                    await Task.Delay(25); // let the source app finish writing all formats
                    text = ClipboardUtil.TryGetText();
                    break;
                }
            }

            // Only restore if we actually changed the clipboard.
            if (text != null && snapshot != null)
                snapshot.Restore();

            return text;
        }
    }
}
