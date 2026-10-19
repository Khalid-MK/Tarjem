using System;
using System.Reflection;
using SD = System.Drawing;
using WF = System.Windows.Forms;

namespace Tarjem
{
    /// <summary>System-tray icon + menu. It's the app's only permanent UI.</summary>
    internal sealed class TrayIcon : IDisposable
    {
        private readonly App _app;
        private readonly WF.NotifyIcon _icon;
        private readonly WF.ToolStripMenuItem _header, _enabled, _restore, _startup;

        public TrayIcon(App app)
        {
            _app = app;

            _header = new WF.ToolStripMenuItem("Tarjem") { Enabled = false };
            _enabled = new WF.ToolStripMenuItem("Hotkey enabled", null, (_, _) => app.ToggleEnabled());
            _restore = new WF.ToolStripMenuItem("Restore clipboard after grabbing", null, (_, _) => app.ToggleRestoreClipboard());
            _startup = new WF.ToolStripMenuItem("Start with Windows", null, (_, _) => app.ToggleStartup());

            var menu = new WF.ContextMenuStrip();
            menu.Items.AddRange(new WF.ToolStripItem[]
            {
                _header,
                new WF.ToolStripSeparator(),
                new WF.ToolStripMenuItem("Open translator", null, (_, _) => app.OpenManualFromTray()),
                _enabled,
                _restore,
                _startup,
                new WF.ToolStripSeparator(),
                new WF.ToolStripMenuItem("Settings…", null, (_, _) => app.OpenSettings()),
                new WF.ToolStripSeparator(),
                new WF.ToolStripMenuItem("Exit", null, (_, _) => app.ExitApp())
            });

            _icon = new WF.NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "Tarjem",
                ContextMenuStrip = menu,
                Visible = true
            };
            _icon.MouseClick += (_, e) =>
            {
                if (e.Button == WF.MouseButtons.Left) app.OpenManualFromTray();
            };

            Refresh();
        }

        public void Refresh()
        {
            var s = _app.Settings;
            _header.Text = $"Tarjem  —  {_app.HotkeyDisplay}";
            _enabled.Checked = s.Enabled;
            _restore.Checked = s.RestoreClipboard;
            _startup.Checked = s.StartWithWindows;

            string tip = s.Enabled ? $"Tarjem — {_app.HotkeyDisplay}" : "Tarjem — paused";
            _icon.Text = tip.Length > 63 ? tip[..63] : tip;
        }

        public void Notify(string message, bool warning = false)
        {
            _icon.ShowBalloonTip(3500, "Tarjem", message, warning ? WF.ToolTipIcon.Warning : WF.ToolTipIcon.Info);
        }

        private static SD.Icon LoadIcon()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tarjem.tarjem.ico");
                if (stream != null) return new SD.Icon(stream, WF.SystemInformation.SmallIconSize);
            }
            catch { }
            return (SD.Icon)SD.SystemIcons.Application.Clone();
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.ContextMenuStrip?.Dispose();
            _icon.Dispose();
        }
    }
}
