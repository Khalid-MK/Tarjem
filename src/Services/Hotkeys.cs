using System;
using System.Windows.Input;
using System.Windows.Interop;
using Tarjem.Interop;

namespace Tarjem.Services
{
    /// <summary>A parsed global hotkey such as "Ctrl+Alt+T".</summary>
    public readonly struct Hotkey
    {
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

        public uint Modifiers { get; }
        public uint Vk { get; }
        public string Display { get; }

        private Hotkey(uint mods, uint vk, string display) { Modifiers = mods; Vk = vk; Display = display; }

        public static bool TryParse(string? text, out Hotkey hotkey) => TryParse(text, out hotkey, out _);

        /// <summary>
        /// Parses and validates a shortcut. RegisterHotKey takes the key away from every
        /// other app, so a combination you'd normally type (a bare letter, Shift+letter…)
        /// would stop you from typing it anywhere. Those are rejected with a reason.
        /// </summary>
        public static bool TryParse(string? text, out Hotkey hotkey, out string? error)
        {
            hotkey = default;
            error = "Empty shortcut.";
            if (string.IsNullOrWhiteSpace(text)) return false;

            uint mods = 0;
            Key key = Key.None;
            string keyText = "";
            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var part in parts)
            {
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= MOD_CONTROL; break;
                    case "alt": mods |= MOD_ALT; break;
                    case "shift": mods |= MOD_SHIFT; break;
                    case "win": case "windows": mods |= MOD_WIN; break;
                    default:
                        error = $"\"{text}\" isn't a valid shortcut.";
                        if (key != Key.None) return false;
                        if (!TryParseKey(part, out key)) return false;
                        keyText = part;
                        break;
                }
            }

            if (key == Key.None) { error = "Add a key after the modifiers."; return false; }

            // On its own: only keys that never type anything.
            if (mods == 0 && !IsStandaloneKey(key))
            {
                error = $"{keyText} on its own would stop you typing it anywhere. Add Ctrl, Alt or Win, " +
                        "or use a key that types nothing: F1–F24, Pause, Scroll Lock or Insert.";
                return false;
            }

            // Shift only changes what a typing key produces (T → capital T, 1 → !, arrows → select),
            // so Shift + that key would hijack normal typing too.
            if (mods == MOD_SHIFT && !IsStandaloneKey(key))
            {
                error = $"Shift+{keyText} is something you type or use to select text. Add Ctrl, Alt or Win instead.";
                return false;
            }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            if (vk == 0) { error = $"\"{text}\" isn't a valid shortcut."; return false; }

            hotkey = new Hotkey(mods, vk, string.Join("+", parts));
            error = null;
            return true;
        }

        /// <summary>Keys that produce no text, so they're safe as a shortcut on their own (or with Shift).</summary>
        public static bool IsStandaloneKey(Key k) =>
            (k >= Key.F1 && k <= Key.F24) || k is Key.Pause or Key.Scroll or Key.Insert;

        private static bool TryParseKey(string s, out Key key)
        {
            key = Key.None;
            if (s.Length == 1 && char.IsDigit(s[0])) { key = Key.D0 + (s[0] - '0'); return true; }
            try
            {
                var k = new KeyConverter().ConvertFromInvariantString(s);
                if (k is Key parsed && parsed != Key.None) { key = parsed; return true; }
            }
            catch { /* invalid key name */ }
            return false;
        }
    }

    /// <summary>
    /// Registers one system-wide hotkey on a hidden message-only window.
    /// RegisterHotKey costs nothing while idle: no keyboard hook, no polling.
    /// </summary>
    public sealed class HotkeyManager : IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HotkeyId = 0xB17E;

        private readonly HwndSource _sink;
        private bool _registered;

        public event Action? Pressed;

        public HotkeyManager()
        {
            var p = new HwndSourceParameters("TarjemHotkeySink")
            {
                ParentWindow = new IntPtr(-3), // HWND_MESSAGE
                WindowStyle = 0,
                Width = 0,
                Height = 0
            };
            _sink = new HwndSource(p);
            _sink.AddHook(WndProc);
        }

        public bool Register(Hotkey hk)
        {
            Unregister();
            _registered = Native.RegisterHotKey(_sink.Handle, HotkeyId, hk.Modifiers | Hotkey.MOD_NOREPEAT, hk.Vk);
            return _registered;
        }

        public void Unregister()
        {
            if (_registered)
            {
                Native.UnregisterHotKey(_sink.Handle, HotkeyId);
                _registered = false;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                handled = true;
                Pressed?.Invoke();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            Unregister();
            _sink.RemoveHook(WndProc);
            _sink.Dispose();
        }
    }
}
