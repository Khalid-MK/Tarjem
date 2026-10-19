using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;

namespace Tarjem.Services
{
    internal sealed record VoiceOption(string Id, string Name, string Lang);

    /// <summary>
    /// Reads text aloud.
    ///   • Windows voices through SAPI (COM, late-bound). The modern "OneCore" voices
    ///     (e.g. Microsoft Hoda – Arabic) are listed by pointing SAPI's token category at
    ///     the OneCore registry key, so no Windows SDK / WinRT projection is needed.
    ///   • Google's online voice (translate_tts) when chosen, or as a fallback when
    ///     Windows has no voice for the language.
    /// All playback happens on the UI thread; only downloads run in the background.
    /// </summary>
    internal sealed class SpeechService
    {
        private const string OneCoreVoices = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices";
        private const string ClassicVoices = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech\Voices";
        private const int SVSFlagsAsync = 1, SVSFPurgeBeforeSpeak = 2, SVSFIsNotXML = 16;
        private const int SRSEDone = 1;

        private static readonly HttpClient Http = CreateClient();

        private readonly AppSettings _settings;
        private readonly DispatcherTimer _poll;
        private readonly Dictionary<string, object> _tokens = new();
        private List<VoiceOption>? _voices;
        private object? _sapi;               // SAPI.SpVoice
        private MediaPlayer? _player;
        private CancellationTokenSource? _cts;

        public bool IsSpeaking { get; private set; }
        /// <summary>Raised on the UI thread when speaking starts or stops.</summary>
        public event Action? StateChanged;
        /// <summary>A message worth showing the user (missing voice, network error…).</summary>
        public event Action<string>? Message;

        public SpeechService(AppSettings settings)
        {
            _settings = settings;
            _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _poll.Tick += (_, _) => PollSapi();
        }

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Tarjem/1.1");
            return c;
        }

        // ---- Voices -----------------------------------------------------------------

        public IReadOnlyList<VoiceOption> Voices => _voices ??= LoadVoices();

        public bool HasWindowsVoice(string lang) => Voices.Any(v => v.Lang == Base(lang));

        private List<VoiceOption> LoadVoices()
        {
            var list = new List<VoiceOption>();
            foreach (var category in new[] { OneCoreVoices, ClassicVoices })
            {
                try
                {
                    dynamic cat = Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpObjectTokenCategory", true)!)!;
                    cat.SetId(category, false);
                    dynamic tokens = cat.EnumerateTokens();
                    int count = tokens.Count;
                    for (int i = 0; i < count; i++)
                    {
                        dynamic token = tokens.Item(i);
                        string id = token.Id;
                        string name = token.GetDescription();
                        if (list.Any(v => v.Name == name)) continue;
                        _tokens[id] = token;
                        list.Add(new VoiceOption(id, name, LanguageOf(token)));
                    }
                }
                catch (Exception ex) { Log.Write(ex); }
            }
            return list;
        }

        private static string LanguageOf(dynamic token)
        {
            try
            {
                string attr = token.GetAttribute("Language");          // e.g. "C01" or "409;9"
                int lcid = Convert.ToInt32(attr.Split(';')[0], 16);
                return CultureInfo.GetCultureInfo(lcid).TwoLetterISOLanguageName;
            }
            catch { return ""; }
        }

        private static string Base(string lang) => lang.Split('-')[0].ToLowerInvariant();

        private VoiceOption? PickVoice(string lang, AppSettings cfg)
        {
            string b = Base(lang);
            string preferred = b == "ar" ? cfg.ArabicVoice : b == "en" ? cfg.LatinVoice : "";
            var match = Voices.FirstOrDefault(v => v.Id == preferred && v.Lang == b);
            return match ?? Voices.FirstOrDefault(v => v.Lang == b);
        }

        // ---- Speak / stop ------------------------------------------------------------

        /// <summary>Speaks <paramref name="text"/> in <paramref name="lang"/>. Pass <paramref name="cfg"/> to preview unsaved settings.</summary>
        public void Speak(string text, string lang, AppSettings? cfg = null)
        {
            cfg ??= _settings;
            Stop();
            text = text.Trim();
            if (text.Length == 0) return;
            if (string.IsNullOrEmpty(lang) || lang == "auto") lang = LanguageDetector.IsArabicDominant(text) ? "ar" : "en";

            bool useGoogle = cfg.SpeechEngine == "google";
            VoiceOption? voice = useGoogle ? null : PickVoice(lang, cfg);

            if (!useGoogle && voice == null)
            {
                if (!cfg.SpeechOnlineFallback)
                {
                    Message?.Invoke($"No {LanguageDetector.DisplayName(lang)} voice in Windows. Add one: Settings → Time & language → Speech.");
                    return;
                }
                useGoogle = true;
            }

            if (!useGoogle && SpeakWindows(text, voice!, cfg)) return;
            if (!useGoogle && !cfg.SpeechOnlineFallback)
            {
                Message?.Invoke("The Windows voice couldn't start.");
                return;
            }
            _ = SpeakGoogleAsync(text, lang, cfg);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _cts = null;
            _poll.Stop();
            try { if (_sapi != null) ((dynamic)_sapi).Speak(string.Empty, SVSFPurgeBeforeSpeak); } catch { }
            try { _player?.Stop(); } catch { }
            SetSpeaking(false);
        }

        private bool SpeakWindows(string text, VoiceOption voice, AppSettings cfg)
        {
            try
            {
                _sapi ??= Activator.CreateInstance(Type.GetTypeFromProgID("SAPI.SpVoice", true)!);
                var t = _sapi!.GetType();
                // Voice is a "putref" property: set it through IDispatch explicitly.
                t.InvokeMember("Voice", BindingFlags.PutRefDispProperty, null, _sapi, new[] { _tokens[voice.Id] });
                t.InvokeMember("Rate", BindingFlags.SetProperty, null, _sapi, new object[] { cfg.SpeechRate * 3 });
                ((dynamic)_sapi).Speak(text, SVSFlagsAsync | SVSFPurgeBeforeSpeak | SVSFIsNotXML);
                SetSpeaking(true);
                _poll.Start();
                return true;
            }
            catch (Exception ex)
            {
                Log.Write(ex);
                return false;
            }
        }

        private void PollSapi()
        {
            try
            {
                if ((int)((dynamic)_sapi!).Status.RunningState != SRSEDone) return;
            }
            catch { /* treat as finished */ }
            _poll.Stop();
            SetSpeaking(false);
        }

        // ---- Google online voice ---------------------------------------------------------

        private async Task SpeakGoogleAsync(string text, string lang, AppSettings cfg)
        {
            var cts = _cts = new CancellationTokenSource();
            var ct = cts.Token;
            SetSpeaking(true);
            var files = new List<string>();
            try
            {
                var chunks = Translator.Chunk(text, 180).Where(c => c.Trim().Length > 0).ToList();
                if (chunks.Count == 0) return;

                Task<string> next = DownloadAsync(chunks[0], lang, ct);
                for (int i = 0; i < chunks.Count; i++)
                {
                    string file = await next;
                    files.Add(file);
                    if (i + 1 < chunks.Count) next = DownloadAsync(chunks[i + 1], lang, ct);   // prefetch while playing
                    await PlayAsync(file, cfg, ct);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Write(ex);
                if (!ct.IsCancellationRequested) Message?.Invoke("Couldn't play the online voice — check your connection.");
            }
            finally
            {
                foreach (var f in files) { try { File.Delete(f); } catch { } }
                if (ReferenceEquals(_cts, cts)) { _cts = null; SetSpeaking(false); }
            }
        }

        private static async Task<string> DownloadAsync(string chunk, string lang, CancellationToken ct)
        {
            string url = "https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=" +
                         Uri.EscapeDataString(lang) + "&q=" + Uri.EscapeDataString(chunk.Trim());
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 100) throw new InvalidOperationException("Empty audio");

            string dir = Path.Combine(Path.GetTempPath(), "Tarjem");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"tts-{Guid.NewGuid():N}.mp3");
            await File.WriteAllBytesAsync(file, bytes, ct).ConfigureAwait(false);
            return file;
        }

        private Task PlayAsync(string file, AppSettings cfg, CancellationToken ct)
        {
            _player ??= new MediaPlayer();
            var player = _player;
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void Ended(object? s, EventArgs e) => tcs.TrySetResult();
            void Failed(object? s, ExceptionEventArgs e) => tcs.TrySetException(e.ErrorException);

            player.MediaEnded += Ended;
            player.MediaFailed += Failed;
            var reg = ct.Register(() => player.Dispatcher.InvokeAsync(() => { player.Stop(); tcs.TrySetCanceled(); }));

            player.Open(new Uri(file));
            player.Play();
            player.SpeedRatio = cfg.SpeechRate switch { < 0 => 0.8, > 0 => 1.25, _ => 1.0 };

            return tcs.Task.ContinueWith(t =>
            {
                player.MediaEnded -= Ended;
                player.MediaFailed -= Failed;
                reg.Dispose();
                player.Close();
                return t;
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext()).Unwrap();
        }

        private void SetSpeaking(bool on)
        {
            if (IsSpeaking == on) return;
            IsSpeaking = on;
            StateChanged?.Invoke();
        }
    }
}
