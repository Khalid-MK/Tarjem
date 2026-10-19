using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Tarjem.Interop
{
    internal static class Native
    {
        // ---- Window / hotkey -------------------------------------------------
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Dark / light native title bar (Windows 10 20H1+ and 11).</summary>
        public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
        {
            int v = dark ? 1 : 0;
            try { DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref v, sizeof(int)); }
            catch { /* older Windows */ }
        }

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOACTIVATE = 0x0010;

        // ---- Clipboard -------------------------------------------------------
        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();

        // ---- Keyboard --------------------------------------------------------
        [DllImport("user32.dll")]
        public static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        public const ushort VK_C = 0x43;
        public const ushort VK_V = 0x56;
        private const ushort VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1;
        private const ushort VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3;
        private const ushort VK_LMENU = 0xA4, VK_RMENU = 0xA5;
        private const ushort VK_LWIN = 0x5B, VK_RWIN = 0x5C;
        private const ushort VK_UNASSIGNED = 0xE8; // used to stop Alt-release from opening menus

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static readonly ushort[] NonCtrlModifiers =
            { VK_LMENU, VK_RMENU, VK_LSHIFT, VK_RSHIFT, VK_LWIN, VK_RWIN };

        public static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        /// <summary>True while the user is still physically holding Alt / Shift / Win.</summary>
        public static bool AnyNonCtrlModifierDown()
        {
            foreach (var vk in NonCtrlModifiers)
                if (IsDown(vk)) return true;
            return false;
        }

        /// <summary>
        /// Sends Ctrl+&lt;key&gt; to the foreground window, first releasing any
        /// Alt/Shift/Win the user may still be holding from the hotkey.
        /// </summary>
        public static string SendCtrlChord(ushort vk)
        {
            var inputs = new List<INPUT>();

            bool altOrWin = IsDown(VK_LMENU) || IsDown(VK_RMENU) || IsDown(VK_LWIN) || IsDown(VK_RWIN);
            if (altOrWin)
            {
                // A dummy keystroke between Alt-down and Alt-up prevents the
                // target app from treating the release as "open the menu bar".
                inputs.Add(Key(VK_UNASSIGNED, false));
                inputs.Add(Key(VK_UNASSIGNED, true));
            }
            foreach (var m in NonCtrlModifiers)
                if (IsDown(m)) inputs.Add(Key(m, true));

            bool ctrlHeld = IsDown(VK_LCONTROL) || IsDown(VK_RCONTROL);
            if (!ctrlHeld) inputs.Add(Key(VK_LCONTROL, false));
            inputs.Add(Key(vk, false));
            inputs.Add(Key(vk, true));
            if (!ctrlHeld) inputs.Add(Key(VK_LCONTROL, true));

            uint sent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
            int err = sent == 0 ? Marshal.GetLastWin32Error() : 0;
            return $"SendInput sent={sent}/{inputs.Count} err={err} ctrlHeld={ctrlHeld} altOrWin={altOrWin}";
        }

        private static INPUT Key(ushort vk, bool up)
        {
            uint flags = up ? KEYEVENTF_KEYUP : 0;
            if (vk is VK_RMENU or VK_RCONTROL or VK_LWIN or VK_RWIN)
                flags |= KEYEVENTF_EXTENDEDKEY;

            return new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = (ushort)MapVirtualKey(vk, 0),
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
        }

        // ---- Structs ---------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct GUITHREADINFO
        {
            public int cbSize;
            public uint flags;
            public IntPtr hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
            public RECT rcCaret;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        // MOUSEINPUT must be part of the union so the struct has the right size on x64.
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }
    }
}
