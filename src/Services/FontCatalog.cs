using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Tarjem.Services
{
    /// <summary>Curated font lists, filtered to what's actually installed on this PC.</summary>
    internal static class FontCatalog
    {
        public const string Default = "Segoe UI";

        private static readonly string[] ArabicCandidates =
        {
            "Segoe UI", "Tahoma", "Arial",
            // ship with Windows
            "Sakkal Majalla", "Traditional Arabic", "Simplified Arabic", "Arabic Typesetting",
            "Aldhabi", "Urdu Typesetting", "Andalus", "Microsoft Uighur", "Times New Roman",
            // popular free fonts (show up once installed)
            "Cairo", "Tajawal", "Almarai", "IBM Plex Sans Arabic", "Noto Naskh Arabic", "Noto Sans Arabic",
            "Noto Kufi Arabic", "Amiri", "Scheherazade New", "Readex Pro", "Rubik", "Changa",
            "El Messiri", "Lateef", "Harmattan", "Markazi Text", "Vazirmatn", "Lalezar",
        };

        private static readonly string[] LatinCandidates =
        {
            "Segoe UI", "Segoe UI Variable Text", "Calibri", "Arial", "Verdana", "Tahoma",
            "Trebuchet MS", "Bahnschrift", "Georgia", "Cambria", "Times New Roman", "Candara",
            "Corbel", "Constantia", "Consolas", "Cascadia Code", "Cascadia Mono",
            // popular free fonts (show up once installed)
            "Inter", "Roboto", "Open Sans", "Noto Sans", "Lato", "Source Sans Pro", "Source Sans 3",
            "IBM Plex Sans", "Poppins", "Montserrat", "Nunito", "JetBrains Mono", "Fira Code",
        };

        private static HashSet<string>? _installed;

        private static HashSet<string> Installed =>
            _installed ??= new HashSet<string>(
                Fonts.SystemFontFamilies.Select(f => f.Source), StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<string> Arabic(string current) => Build(ArabicCandidates, current);
        public static IReadOnlyList<string> Latin(string current) => Build(LatinCandidates, current);

        private static List<string> Build(IEnumerable<string> candidates, string current)
        {
            var list = candidates.Where(Installed.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!string.IsNullOrWhiteSpace(current) && !list.Contains(current, StringComparer.OrdinalIgnoreCase))
                list.Insert(0, current); // keep a custom choice from settings.json
            return list;
        }

        /// <summary>The chosen font with Segoe UI behind it for any glyph it lacks.</summary>
        public static FontFamily Family(string name) =>
            new FontFamily(string.IsNullOrWhiteSpace(name) || name == Default ? Default : $"{name}, {Default}");
    }
}
