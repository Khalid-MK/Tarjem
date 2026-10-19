using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using Tarjem.Interop;
using Tarjem.Services;

namespace Tarjem
{
    public partial class App : Application
    {
        private const string DefaultHotkey = "Ctrl+Alt+T";

        private Mutex? _mutex;
        private AppSettings _settings = null!;
        private HotkeyManager? _hotkeys;
        private TrayIcon? _tray;
        private PopupWindow? _popup;
        private bool _grabbing;

        internal AppSettings Settings => _settings;
        internal string HotkeyDisplay { get; private set; } = DefaultHotkey;

        protected override void OnStartup(StartupEventArgs e)
        {
            _mutex = new Mutex(true, @"Local\Tarjem.SingleInstance", out bool isNew);
            if (!isNew)
            {
                MessageBox.Show("Tarjem is already running — look for its icon in the system tray.", "Tarjem",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            base.OnStartup(e);
            DispatcherUnhandledException += (_, ex) => { Log.Write(ex.Exception); ex.Handled = true; };

            _settings = AppSettings.Load(out bool firstRun);
            ThemeManager.Apply(_settings.Theme);

            _popup = new PopupWindow(_settings);
            _hotkeys = new HotkeyManager();
            _hotkeys.Pressed += OnHotkey;
            _tray = new TrayIcon(this);

            ApplyHotkey(announce: firstRun);
            StartupManager.Sync(_settings.StartWithWindows);
        }

        // ---- Hotkey ------------------------------------------------------------

        private async void OnHotkey()
        {
            if (_grabbing || _popup == null) return;
            _grabbing = true;
            try
            {
                IntPtr fg = Native.GetForegroundWindow();

                // Pressing the hotkey while the popup is focused just closes it.
                if (_popup.IsVisible && fg == _popup.Handle)
                {
                    _popup.Hide();
                    return;
                }

                Native.GetCursorPos(out var cursor);
                string? text = await SelectionGrabber.GetSelectedTextAsync(_settings.RestoreClipboard);

                if (string.IsNullOrWhiteSpace(text))
                    _popup.OpenManual(cursor, "No selection — type or paste text, then Ctrl+Enter");
                else
                    _popup.OpenWithText(text, fg, cursor);
            }
            catch (Exception ex) { Log.Write(ex); }
            finally { _grabbing = false; }
        }

        internal void ApplyHotkey(bool announce)
        {
            if (_hotkeys == null || _tray == null) return;

            if (!Hotkey.TryParse(_settings.Hotkey, out var hk))
            {
                _tray.Notify($"\"{_settings.Hotkey}\" isn't a valid hotkey — using {DefaultHotkey}.", warning: true);
                Hotkey.TryParse(DefaultHotkey, out hk);
            }
            HotkeyDisplay = hk.Display;

            if (_settings.Enabled)
            {
                if (!_hotkeys.Register(hk))
                    _tray.Notify($"{hk.Display} is already taken by another app. Pick another one in settings.", warning: true);
                else if (announce)
                    _tray.Notify($"Running in the tray. Select any text and press {hk.Display}.");
            }
            else
            {
                _hotkeys.Unregister();
            }
            _tray.Refresh();
        }

        // ---- Tray actions ----------------------------------------------------------

        internal void ToggleEnabled()
        {
            _settings.Enabled = !_settings.Enabled;
            _settings.Save();
            ApplyHotkey(announce: false);
        }

        internal void ToggleRestoreClipboard()
        {
            _settings.RestoreClipboard = !_settings.RestoreClipboard;
            _settings.Save();
            _tray?.Refresh();
        }

        internal void ToggleStartup()
        {
            _settings.StartWithWindows = !_settings.StartWithWindows;
            _settings.Save();
            StartupManager.Sync(_settings.StartWithWindows);
            _tray?.Refresh();
        }

        internal void OpenManualFromTray()
        {
            Native.GetCursorPos(out var cursor);
            _popup?.OpenManual(cursor, null);
        }

        internal void OpenSettingsFile()
        {
            _settings.Save(); // make sure the file exists
            try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.FilePath}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Write(ex); }
        }

        internal void ReloadSettings()
        {
            var fresh = AppSettings.Load(out _);
            _settings.CopyFrom(fresh);
            ThemeManager.Apply(_settings.Theme);
            _popup?.ApplySettings();
            StartupManager.Sync(_settings.StartWithWindows);
            ApplyHotkey(announce: false);
            _tray?.Notify($"Settings reloaded. Hotkey: {HotkeyDisplay}");
        }

        internal void ExitApp()
        {
            _tray?.Dispose();
            _tray = null;
            _hotkeys?.Dispose();
            _hotkeys = null;
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _tray?.Dispose();
            _hotkeys?.Dispose();
            if (_mutex != null)
            {
                try { _mutex.ReleaseMutex(); } catch { }
                _mutex.Dispose();
            }
            base.OnExit(e);
        }
    }
}
