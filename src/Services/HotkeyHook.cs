using System;
using System.Runtime.InteropServices;
using System.Threading;
using Tarjem.Interop;

namespace Tarjem.Services
{
    /// <summary>
    /// "Block the key's own action" mode.
    ///
    /// RegisterHotKey only swallows the key-down. The key-up still reaches the app
    /// underneath, and some keys act on key-up (the Menu key opens the context menu,
    /// a lone Alt/Win tap opens the menu bar / Start). This low-level keyboard hook
    /// swallows the shortcut's key-down, its auto-repeats and its key-up, so the app
    /// never sees it at all.
    ///
    /// It runs on its own thread with its own message loop, so nothing the UI thread
    /// does (translating, drawing) can ever delay your typing. For every other key the
    /// callback is a single compare and returns immediately.
    /// </summary>
    internal sealed class HotkeyHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        private const uint WM_QUIT = 0x0012;

        private readonly Action _onPressed;
        private readonly LowLevelKeyboardProc _proc;   // kept alive: the hook holds a pointer to it

        private volatile uint _vk, _mods;
        private volatile bool _armed;
        private bool _swallowUp;                        // only touched on the hook thread

        private Thread? _thread;
        private uint _threadId;
        private IntPtr _hook;

        public HotkeyHook(Action onPressed)
        {
            _onPressed = onPressed;
            _proc = Callback;
        }

        /// <summary>Starts (or retargets) the hook. Returns false if Windows refused to install it.</summary>
        public bool Start(Hotkey hk)
        {
            _vk = hk.Vk;
            _mods = hk.Modifiers;
            _armed = true;
            if (_thread != null) return _hook != IntPtr.Zero;

            using var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Tarjem keyboard hook" };
            _thread.Start();
            ready.Wait(2000);
            if (_hook != IntPtr.Zero) return true;

            Stop();
            return false;
        }

        public void Stop()
        {
            _armed = false;
            if (_thread == null) return;
            if (_threadId != 0) PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(1000);
            _thread = null;
            _threadId = 0;
        }

        private void Run(ManualResetEventSlim ready)
        {
            _threadId = GetCurrentThreadId();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
            ready.Set();
            if (_hook == IntPtr.Zero) return;

            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { /* pump: required for LL hooks */ }

            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _armed)
            {
                var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                if (k.vkCode == _vk && k.dwExtraInfo != Native.InjectMarker)
                {
                    int msg = wParam.ToInt32();
                    if (msg is WM_KEYDOWN or WM_SYSKEYDOWN)
                    {
                        if (_swallowUp) return (IntPtr)1;               // auto-repeat while held
                        if (CurrentModifiers() == _mods)
                        {
                            _swallowUp = true;
                            bool altOrWin = (_mods & (Hotkey.MOD_ALT | Hotkey.MOD_WIN)) != 0;
                            ThreadPool.QueueUserWorkItem(_ =>
                            {
                                // Releasing Alt/Win with nothing in between would open the
                                // menu bar / Start menu, since the app never saw our key.
                                if (altOrWin) Native.SendDummyKey();
                                _onPressed();
                            });
                            return (IntPtr)1;
                        }
                    }
                    else if ((msg is WM_KEYUP or WM_SYSKEYUP) && _swallowUp)
                    {
                        _swallowUp = false;
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private static uint CurrentModifiers()
        {
            uint m = 0;
            if (Native.IsDown(0x11)) m |= Hotkey.MOD_CONTROL;
            if (Native.IsDown(0x12)) m |= Hotkey.MOD_ALT;
            if (Native.IsDown(0x10)) m |= Hotkey.MOD_SHIFT;
            if (Native.IsDown(0x5B) || Native.IsDown(0x5C)) m |= Hotkey.MOD_WIN;
            return m;
        }

        public void Dispose() => Stop();

        // ---- P/Invoke --------------------------------------------------------------

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int ptX, ptY;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
