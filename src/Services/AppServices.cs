using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Tarjem.Services
{
    public sealed class AppSettings
    {
        /// <summary>e.g. "Ctrl+Alt+T", "Ctrl+Shift+Space", "Alt+Q", "F8".</summary>
        public string Hotkey { get; set; } = "Ctrl+Alt+T";
        public bool Enabled { get; set; } = true;
        /// <summary>Put back whatever was on your clipboard after grabbing the selection.</summary>
        public bool RestoreClipboard { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        /// <summary>Target when the selection is Arabic.</summary>
        public string ArabicTarget { get; set; } = "en";
        /// <summary>Target for anything that isn't Arabic (English, Russian, ...).</summary>
        public string OtherTarget { get; set; } = "ar";
        public double FontSize { get; set; } = 15;
        /// <summary>"system", "dark" or "light".</summary>
        public string Theme { get; set; } = "system";
        /// <summary>"selection" = open under the selected text, "cursor" = open at the mouse.</summary>
        public string AnchorMode { get; set; } = "selection";
        /// <summary>Width of the popup card in DIPs (360 / 420 / 520 in the settings UI).</summary>
        public double PopupWidth { get; set; } = 420;
        /// <summary>Show the original text above the translation.</summary>
        public bool ShowOriginal { get; set; } = true;

        private static readonly JsonSerializerOptions Json = new()
        {
            WriteIndented = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // keep "Ctrl+Alt+T" readable
        };

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tarjem");
        public static string FilePath => Path.Combine(Folder, "settings.json");

        public static AppSettings Load(out bool firstRun)
        {
            firstRun = !File.Exists(FilePath);
            if (!firstRun)
            {
                try
                {
                    var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json);
                    if (s != null) return s;
                }
                catch (Exception ex) { Log.Write(ex); }
            }
            var fresh = new AppSettings();
            fresh.Save();
            return fresh;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
            }
            catch (Exception ex) { Log.Write(ex); }
        }

        public void CopyFrom(AppSettings o)
        {
            Hotkey = o.Hotkey; Enabled = o.Enabled; RestoreClipboard = o.RestoreClipboard;
            StartWithWindows = o.StartWithWindows; ArabicTarget = o.ArabicTarget;
            OtherTarget = o.OtherTarget; FontSize = o.FontSize; Theme = o.Theme;
            AnchorMode = o.AnchorMode; PopupWidth = o.PopupWidth; ShowOriginal = o.ShowOriginal;
        }

        public AppSettings Clone()
        {
            var c = new AppSettings();
            c.CopyFrom(this);
            return c;
        }
    }

    internal static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "Tarjem";

        public static void Sync(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                if (key == null) return;
                if (enable && Environment.ProcessPath is { } exe)
                    key.SetValue(Name, $"\"{exe}\"");
                else if (key.GetValue(Name) != null)
                    key.DeleteValue(Name, throwOnMissingValue: false);
            }
            catch (Exception ex) { Log.Write(ex); }
        }
    }

    internal static class ThemeManager
    {
        public static bool IsDark { get; private set; } = true;

        public static void Apply(string mode)
        {
            bool light = mode.Equals("light", StringComparison.OrdinalIgnoreCase) ||
                         (mode.Equals("system", StringComparison.OrdinalIgnoreCase) && SystemUsesLightTheme());

            IsDark = !light;
            var r = Application.Current.Resources;
            if (light)
            {
                Set(r, "CardBg", "#FFFFFF");
                Set(r, "CardBorder", "#D9DDE3");
                Set(r, "Fg", "#1A1E23");
                Set(r, "SubFg", "#5F6873");
                Set(r, "HoverBg", "#EEF1F4");
                Set(r, "PressBg", "#E1E5EA");
                Set(r, "Accent", "#0E8A72");
                Set(r, "OnAccent", "#FFFFFF");
                Set(r, "BoxBg", "#F3F5F7");
                Set(r, "SegmentOn", "#FFFFFF");
                Set(r, "ErrorFg", "#C4362C");
            }
            else
            {
                Set(r, "CardBg", "#202327");
                Set(r, "CardBorder", "#383D44");
                Set(r, "Fg", "#ECEFF3");
                Set(r, "SubFg", "#98A1AC");
                Set(r, "HoverBg", "#2D3238");
                Set(r, "PressBg", "#373D44");
                Set(r, "Accent", "#4CC4A7");
                Set(r, "OnAccent", "#0B1F1A");
                Set(r, "BoxBg", "#17191C");
                Set(r, "SegmentOn", "#343A41");
                Set(r, "ErrorFg", "#FF7B70");
            }
        }

        private static void Set(ResourceDictionary r, string key, string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            r[key] = b;
        }

        private static bool SystemUsesLightTheme()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return k?.GetValue("AppsUseLightTheme") is int v && v == 1;
            }
            catch { return false; }
        }
    }

    internal static class Log
    {
        public static void Write(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                File.AppendAllText(Path.Combine(AppSettings.Folder, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }
    }
}
