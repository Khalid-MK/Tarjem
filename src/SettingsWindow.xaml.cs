using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Tarjem.Interop;
using Tarjem.Services;

namespace Tarjem
{
    public partial class SettingsWindow : Window
    {
        public const string DefaultHotkey = "Ctrl+Alt+T";

        private static readonly (string Code, string Name)[] Languages =
        {
            ("ar", "العربية  ·  Arabic"),
            ("en", "English"),
            ("ru", "Русский  ·  Russian"),
            ("fr", "Français  ·  French"),
            ("de", "Deutsch  ·  German"),
            ("es", "Español  ·  Spanish"),
            ("it", "Italiano  ·  Italian"),
            ("pt", "Português  ·  Portuguese"),
            ("tr", "Türkçe  ·  Turkish"),
            ("fa", "فارسی  ·  Persian"),
            ("ur", "اردو  ·  Urdu"),
            ("hi", "हिन्दी  ·  Hindi"),
            ("bn", "বাংলা  ·  Bengali"),
            ("id", "Bahasa Indonesia"),
            ("ms", "Bahasa Melayu  ·  Malay"),
            ("zh-CN", "中文  ·  Chinese"),
            ("ja", "日本語  ·  Japanese"),
            ("ko", "한국어  ·  Korean"),
        };

        private const double WidthCompactValue = 360, WidthNormalValue = 420, WidthWideValue = 540;
        private const double MinFont = 11, MaxFont = 26;

        private readonly App _app;
        private readonly AppSettings _edit;   // working copy; only applied on Save
        private bool _loading, _saved, _recording;

        internal SettingsWindow(App app, AppSettings current)
        {
            InitializeComponent();
            _app = app;
            _edit = current.Clone();

            HeaderIcon.Source = LoadIcon(64);
            Icon = LoadIcon(32);

            LoadValues();

            SourceInitialized += (_, _) => UpdateTitleBar();
            HotkeyBox.GotKeyboardFocus += (_, _) => StartRecording();
            HotkeyBox.LostKeyboardFocus += (_, _) => StopRecording();
            HotkeyBox.PreviewKeyDown += HotkeyBox_PreviewKeyDown;
            HotkeyBox.PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (!HotkeyBox.IsKeyboardFocused) { HotkeyBox.Focus(); e.Handled = true; }
            };
        }

        // ---- Load / gather ----------------------------------------------------

        private void LoadValues()
        {
            _loading = true;

            HotkeyBox.Text = _edit.Hotkey;
            EnabledSwitch.IsChecked = _edit.Enabled;

            FillLanguages(ArabicTargetBox, _edit.ArabicTarget, exclude: "ar");
            FillLanguages(OtherTargetBox, _edit.OtherTarget, exclude: null);

            (_edit.AnchorMode == "cursor" ? AnchorCursor : AnchorSelection).IsChecked = true;

            if (_edit.PopupWidth <= (WidthCompactValue + WidthNormalValue) / 2) WidthCompact.IsChecked = true;
            else if (_edit.PopupWidth >= (WidthNormalValue + WidthWideValue) / 2) WidthWide.IsChecked = true;
            else WidthNormal.IsChecked = true;

            FontSizeText.Text = _edit.FontSize.ToString("0");

            switch (_edit.Theme.ToLowerInvariant())
            {
                case "dark": ThemeDark.IsChecked = true; break;
                case "light": ThemeLight.IsChecked = true; break;
                default: ThemeSystem.IsChecked = true; break;
            }

            ShowOriginalSwitch.IsChecked = _edit.ShowOriginal;
            RestoreClipboardSwitch.IsChecked = _edit.RestoreClipboard;
            StartupSwitch.IsChecked = _edit.StartWithWindows;

            _loading = false;
        }

        private static void FillLanguages(ComboBox box, string selected, string? exclude)
        {
            box.Items.Clear();
            var list = Languages.Where(l => l.Code != exclude).ToList();
            if (!list.Any(l => l.Code == selected))
                list.Add((selected, selected)); // keep a custom code from settings.json

            foreach (var (code, name) in list)
            {
                var item = new ComboBoxItem { Content = name, Tag = code };
                box.Items.Add(item);
                if (code == selected) box.SelectedItem = item;
            }
        }

        private void Gather()
        {
            _edit.Enabled = EnabledSwitch.IsChecked == true;
            _edit.ArabicTarget = (ArabicTargetBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "en";
            _edit.OtherTarget = (OtherTargetBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "ar";
            _edit.AnchorMode = AnchorCursor.IsChecked == true ? "cursor" : "selection";
            _edit.PopupWidth = WidthCompact.IsChecked == true ? WidthCompactValue
                             : WidthWide.IsChecked == true ? WidthWideValue
                             : WidthNormalValue;
            _edit.Theme = SelectedTheme();
            _edit.ShowOriginal = ShowOriginalSwitch.IsChecked == true;
            _edit.RestoreClipboard = RestoreClipboardSwitch.IsChecked == true;
            _edit.StartWithWindows = StartupSwitch.IsChecked == true;
        }

        private string SelectedTheme() =>
            ThemeDark.IsChecked == true ? "dark" : ThemeLight.IsChecked == true ? "light" : "system";

        // ---- Hotkey recorder ----------------------------------------------------

        private void StartRecording()
        {
            if (_recording) return;
            _recording = true;
            _app.SuspendHotkey();   // otherwise pressing the current shortcut would fire it
            HotkeyBox.Text = "Press a shortcut…";
            HideError();
        }

        private void StopRecording()
        {
            if (!_recording) return;
            _recording = false;
            _app.ResumeHotkey();
            HotkeyBox.Text = _edit.Hotkey;
        }

        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Escape)
            {
                EndRecordingFocus();
                return;
            }

            var mods = Keyboard.Modifiers;
            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            if (IsModifier(key))
            {
                // Show the combination as it's being built.
                HotkeyBox.Text = parts.Count > 0 ? string.Join(" + ", parts) + " + …" : "Press a shortcut…";
                return;
            }

            parts.Add(KeyName(key));
            string combo = string.Join("+", parts);

            if (!Hotkey.TryParse(combo, out _, out string? error))
            {
                HotkeyBox.Text = "Not allowed — try again";
                ShowError(error ?? "That shortcut can't be used.");
                return;
            }

            HideError();
            _edit.Hotkey = combo;
            EndRecordingFocus();   // ends recording and shows the new value
        }

        private void EndRecordingFocus()
        {
            if (!FocusSink.Focus()) Keyboard.ClearFocus();
        }

        private static bool IsModifier(Key k) => k is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

        private static string KeyName(Key k) => k switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
            Key.Apps => "Menu",
            _ => k.ToString()
        };

        private void ResetHotkey_Click(object sender, RoutedEventArgs e)
        {
            _edit.Hotkey = DefaultHotkey;
            HotkeyBox.Text = DefaultHotkey;
            HideError();
        }

        // ---- Other controls --------------------------------------------------------

        private void FontDown_Click(object sender, RoutedEventArgs e) => StepFont(-1);
        private void FontUp_Click(object sender, RoutedEventArgs e) => StepFont(+1);

        private void StepFont(int delta)
        {
            _edit.FontSize = Math.Clamp(Math.Round(_edit.FontSize) + delta, MinFont, MaxFont);
            FontSizeText.Text = _edit.FontSize.ToString("0");
        }

        private void Theme_Checked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            ThemeManager.Apply(SelectedTheme());   // live preview; reverted on Cancel
            UpdateTitleBar();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppSettings.FilePath}\"") { UseShellExecute = true });
            }
            catch (Exception ex) { Log.Write(ex); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Gather();
            string? error = _app.SaveSettings(_edit);
            if (error != null)
            {
                ShowError(error);
                return;
            }
            _saved = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosed(EventArgs e)
        {
            if (_recording) StopRecording();
            if (!_saved) ThemeManager.Apply(_app.Settings.Theme);   // undo the theme preview
            base.OnClosed(e);
        }

        // ---- Helpers ----------------------------------------------------------------

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBar.Visibility = Visibility.Visible;
        }

        private void HideError() => ErrorBar.Visibility = Visibility.Collapsed;

        private void UpdateTitleBar()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero) Native.SetDarkTitleBar(hwnd, ThemeManager.IsDark);
        }

        internal static BitmapSource? LoadIcon(int size)
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Tarjem.tarjem.ico");
                if (stream == null) return null;
                var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - size)).First();
            }
            catch { return null; }
        }
    }
}
