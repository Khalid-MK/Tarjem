using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Tarjem.Services
{
    public sealed record TranslationResult(string Text, string SourceLang, string TargetLang);

    public static class LanguageDetector
    {
        public static bool IsArabicChar(char c) =>
            (c >= '؀' && c <= 'ۿ') || (c >= 'ݐ' && c <= 'ݿ') ||
            (c >= 'ࢠ' && c <= 'ࣿ') || (c >= 'ﭐ' && c <= '﷿') ||
            (c >= 'ﹰ' && c <= '﻿');

        /// <summary>
        /// True when at least ~40% of the letters are Arabic script, so mixed text like
        /// "الـ API ده بيرجع error" still counts as Arabic.
        /// </summary>
        public static bool IsArabicDominant(string text)
        {
            int arabic = 0, other = 0;
            foreach (char c in text)
            {
                if (IsArabicChar(c)) { if (char.IsLetter(c)) arabic++; }
                else if (char.IsLetter(c)) other++;
            }
            return arabic > 0 && arabic * 3 >= other * 2;
        }

        public static string PickTarget(string text, AppSettings s) =>
            IsArabicDominant(text) ? s.ArabicTarget : s.OtherTarget;

        public static bool IsRtl(string? lang) =>
            lang is "ar" or "fa" or "ur" or "he" or "iw" or "ps" or "yi" or "ckb" or "sd" or "ug";

        public static string DisplayName(string? code) => code switch
        {
            "ar" => "العربية",
            "en" => "English",
            "ru" => "Русский",
            "fr" => "Français",
            "de" => "Deutsch",
            "es" => "Español",
            "tr" => "Türkçe",
            "fa" => "فارسی",
            "ur" => "اردو",
            null or "" or "auto" => "Auto",
            _ => code.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Free, key-less translation: Google's public "gtx" endpoint, with MyMemory as a fallback.
    /// </summary>
    public static class Translator
    {
        private static readonly HttpClient Http = CreateClient();
        private static readonly Dictionary<string, TranslationResult> Cache = new();
        private static readonly Queue<string> CacheOrder = new();
        private const int CacheSize = 64;

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Tarjem/1.1");
            return c;
        }

        public static async Task<TranslationResult> TranslateAsync(string text, string target, CancellationToken ct)
        {
            string key = target + "\u0001" + text;
            lock (Cache)
                if (Cache.TryGetValue(key, out var cached)) return cached;

            TranslationResult result;
            try
            {
                result = await GoogleAsync(text, target, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                result = await MyMemoryAsync(text, target, ct).ConfigureAwait(false);
            }

            lock (Cache)
            {
                if (!Cache.ContainsKey(key))
                {
                    Cache[key] = result;
                    CacheOrder.Enqueue(key);
                    while (CacheOrder.Count > CacheSize) Cache.Remove(CacheOrder.Dequeue());
                }
            }
            return result;
        }

        // ---- Google (unofficial public endpoint) ------------------------------
        private static async Task<TranslationResult> GoogleAsync(string text, string target, CancellationToken ct)
        {
            var sb = new StringBuilder();
            string detected = "auto";

            foreach (var chunk in Chunk(text, 800))
            {
                string url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl="
                             + Uri.EscapeDataString(target) + "&dt=t&q=" + Uri.EscapeDataString(chunk);
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                var (translated, lang) = ParseGoogle(json);
                sb.Append(translated);
                if (detected == "auto" && !string.IsNullOrEmpty(lang)) detected = lang;
            }

            if (sb.Length == 0) throw new InvalidOperationException("Empty translation");
            return new TranslationResult(sb.ToString(), detected, target);
        }

        /// <summary>Response shape: [[["translated","original",...],...], null, "detectedLang", ...]</summary>
        public static (string text, string? lang) ParseGoogle(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var sb = new StringBuilder();

            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 &&
                root[0].ValueKind == JsonValueKind.Array)
            {
                foreach (var seg in root[0].EnumerateArray())
                {
                    if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0 &&
                        seg[0].ValueKind == JsonValueKind.String)
                        sb.Append(seg[0].GetString());
                }
            }

            string? lang = null;
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 2 &&
                root[2].ValueKind == JsonValueKind.String)
                lang = root[2].GetString();

            return (sb.ToString(), lang);
        }

        // ---- MyMemory fallback ----------------------------------------------
        private static async Task<TranslationResult> MyMemoryAsync(string text, string target, CancellationToken ct)
        {
            // MyMemory needs an explicit source language.
            string source = LanguageDetector.IsArabicDominant(text) ? "ar" : (target == "en" ? "ru" : "en");
            var sb = new StringBuilder();

            foreach (var chunk in Chunk(text, 450))
            {
                string url = "https://api.mymemory.translated.net/get?q=" + Uri.EscapeDataString(chunk) +
                             "&langpair=" + source + "|" + target;
                using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                var t = doc.RootElement.GetProperty("responseData").GetProperty("translatedText").GetString();
                sb.Append(t);
                if (chunk.EndsWith('\n') && !(t ?? "").EndsWith('\n')) sb.Append('\n');
            }
            return new TranslationResult(sb.ToString(), source, target);
        }

        /// <summary>Split long text on line / sentence boundaries, keeping the separators.</summary>
        public static IEnumerable<string> Chunk(string text, int max)
        {
            int start = 0;
            while (start < text.Length)
            {
                int len = Math.Min(max, text.Length - start);
                if (start + len < text.Length)
                {
                    int cut = LastBreak(text, start, len);
                    if (cut > 0) len = cut;
                }
                yield return text.Substring(start, len);
                start += len;
            }
        }

        private static int LastBreak(string text, int start, int len)
        {
            int nl = text.LastIndexOf('\n', start + len - 1, len);
            if (nl > start + len / 3) return nl - start + 1;
            for (int i = start + len - 1; i > start + len / 3; i--)
            {
                char c = text[i];
                if (c is '.' or '!' or '?' or '؟' or '。' or '؛' or ';') return i - start + 1;
            }
            int sp = text.LastIndexOf(' ', start + len - 1, len);
            return sp > start ? sp - start + 1 : 0;
        }
    }
}
