using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Tarjem.Interop;
using Tarjem.Services;

namespace Tarjem
{
    public partial class PopupWindow : Window
    {
        private const string PinGlyph = "", PinnedGlyph = "";
        private const double ShadowMargin = 14;   // must match Card's Margin in XAML
        private const double Gap = 6;             // space between the text and the card

        private readonly AppSettings _settings;
        private readonly DispatcherTimer _statusTimer;

        private IntPtr _targetHwnd;
        private Native.POINT _cursor;
        private Native.RECT? _selection;   // screen bounds of the selected text, when known
        private string _targetLang = "ar";
        private bool _autoTarget = true;   // false once the user flips direction manually
        private bool _pinned, _editing, _userMoved, _suppressHide;
        private CancellationTokenSource? _cts;

        public PopupWindow(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            ApplySettings();

            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
            _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); StatusText.Text = ""; };

            Deactivated += (_, _) => { if (!_pinned && !_suppressHide) Hide(); };
            IsVisibleChanged += (_, _) => { if (!IsVisible) _cts?.Cancel(); };
            SizeChanged += (_, _) => { if (IsVisible && !_userMoved) Place(); };
            PreviewKeyDown += OnPreviewKeyDown;
            SourceBox.TextChanged += (_, _) => ApplyDirection(SourceBox, LanguageDetector.IsArabicDominant(SourceBox.Text));

            // The text boxes swallow the wheel even when they can't scroll; route it to the outer viewer.
            Body.PreviewMouseWheel += (_, e) =>
            {
                if (Body.ScrollableHeight <= 0) return;
                Body.ScrollToVerticalOffset(Body.VerticalOffset - e.Delta / 3.0);
                e.Handled = true;
            };

            Header.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState != MouseButtonState.Pressed) return;
                _userMoved = true;
                try { DragMove(); } catch { /* mouse released early */ }
            };
        }

        public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

        public void ApplySettings()
        {
            TranslationBox.FontSize = _settings.FontSize;
            Width = Math.Clamp(_settings.PopupWidth, 300, 900) + ShadowMargin * 2;
            UpdateSourceVisibility();
        }

        // ---- Entry points -----------------------------------------------------

        /// <summary>Translate text that was selected in another window.</summary>
        internal void OpenWithText(string text, IntPtr sourceWindow, Native.POINT cursor, Native.RECT? selection)
        {
            _targetHwnd = sourceWindow;
            _cursor = cursor;
            _selection = selection;
            _userMoved = false;
            _autoTarget = true;
            SetEditing(false);

            SourceBox.Text = text.Trim();
            TranslationBox.Text = "";
            ShowStatus("");
            ReplaceBtn.Visibility = Visibility.Visible;

            Present();
            TranslationBox.Focus();
            _ = TranslateAsync();
        }

        /// <summary>Empty popup to type / paste text into (tray click, or hotkey with no selection).</summary>
        internal void OpenManual(Native.POINT cursor, string? hint)
        {
            _targetHwnd = IntPtr.Zero;
            _cursor = cursor;
            _selection = null;
            _userMoved = false;
            _autoTarget = true;
            _cts?.Cancel();

            SourceBox.Text = "";
            TranslationBox.Text = "";
            LangLabel.Text = "Auto  →  العربية / English";
            ReplaceBtn.Visibility = Visibility.Collapsed;
            SetEditing(true);
            ShowStatus(hint ?? "Ctrl+Enter to translate", sticky: true);

            Present();
            SourceBox.Focus();
        }

        // ---- Translation --------------------------------------------------------------

        private async Task TranslateAsync()
        {
            string text = SourceBox.Text.Trim();
            if (text.Length == 0) return;

            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();

            if (_autoTarget) _targetLang = LanguageDetector.PickTarget(text, _settings);
            SetLangLabel(null);
            Busy.Visibility = Visibility.Visible;
            ShowStatus("");

            try
            {
                var result = await Translator.TranslateAsync(text, _targetLang, cts.Token);
                if (cts.IsCancellationRequested) return;

                TranslationBox.Text = result.Text;
                ApplyDirection(TranslationBox, LanguageDetector.IsRtl(_targetLang));
                Body.ScrollToHome();
                SetLangLabel(result.SourceLang);
            }
            catch (Exception ex) when (!cts.IsCancellationRequested)
            {
                Log.Write(ex);
                ShowStatus("Translation failed — check your connection", error: true, sticky: true);
            }
            catch { /* cancelled by a newer request */ }
            finally
            {
                if (ReferenceEquals(_cts, cts)) Busy.Visibility = Visibility.Collapsed;
            }
        }

        private void SetLangLabel(string? detected)
        {
            string from = detected == null ? "…" : LanguageDetector.DisplayName(detected);
            LangLabel.Text = $"{from}  →  {LanguageDetector.DisplayName(_targetLang)}";
        }

        // ---- Buttons ------------------------------------------------------------------

        private void Copy_Click(object sender, RoutedEventArgs e) => CopyTranslation(close: false);

        private void CopyTranslation(bool close)
        {
            string text = TranslationBox.SelectionLength > 0 ? TranslationBox.SelectedText : TranslationBox.Text;
            if (string.IsNullOrEmpty(text)) return;
            ClipboardUtil.SetText(text);
            if (close) Hide();
            else ShowStatus("Copied ✓");
        }

        private async void Replace_Click(object sender, RoutedEventArgs e) => await ReplaceSelectionAsync();

        private async Task ReplaceSelectionAsync()
        {
            string text = TranslationBox.Text;
            if (string.IsNullOrEmpty(text) || _targetHwnd == IntPtr.Zero || !Native.IsWindow(_targetHwnd))
                return;

            _suppressHide = true;
            try
            {
                Hide();
                ClipboardUtil.SetText(text);
                Native.SetForegroundWindow(_targetHwnd);
                await Task.Delay(120);
                Native.SendCtrlChord(Native.VK_V);
            }
            finally { _suppressHide = false; }
        }

        private void Swap_Click(object sender, RoutedEventArgs e) => FlipDirection();

        private void FlipDirection()
        {
            _autoTarget = false;
            _targetLang = _targetLang == "ar"
                ? (_settings.ArabicTarget == "ar" ? "en" : _settings.ArabicTarget)
                : "ar";
            _ = TranslateAsync();
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinned = !_pinned;
            PinBtn.Content = _pinned ? PinnedGlyph : PinGlyph;
            Highlight(PinBtn, _pinned);
            PinBtn.ToolTip = _pinned ? "Pinned — click to unpin" : "Keep open";
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            Hide();
            ((App)Application.Current).OpenSettings();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Hide();

        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            SetEditing(!_editing);
            if (_editing)
            {
                SourceBox.Focus();
                SourceBox.CaretIndex = SourceBox.Text.Length;
            }
        }

        private void Translate_Click(object sender, RoutedEventArgs e) => _ = TranslateAsync();

        private void SetEditing(bool on)
        {
            _editing = on;
            SourceBox.IsReadOnly = !on;
            SourceBox.Padding = on ? new Thickness(6, 4, 6, 4) : new Thickness(0);
            if (on) SourceBox.SetResourceReference(BackgroundProperty, "BoxBg");
            else SourceBox.ClearValue(BackgroundProperty);
            SourceBox.SetResourceReference(ForegroundProperty, on ? "Fg" : "SubFg");
            TranslateBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Highlight(EditBtn, on);
            UpdateSourceVisibility();
        }

        private void UpdateSourceVisibility() =>
            SourceBox.Visibility = _editing || _settings.ShowOriginal ? Visibility.Visible : Visibility.Collapsed;

        private static void Highlight(Button b, bool on)
        {
            if (on) b.SetResourceReference(ForegroundProperty, "Accent");
            else b.ClearValue(ForegroundProperty);
        }

        // ---- Keyboard ---------------------------------------------------------------

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.Enter)
            {
                if (_editing && SourceBox.IsKeyboardFocusWithin) _ = TranslateAsync();
                else CopyTranslation(close: true);
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.R && ReplaceBtn.IsVisible)
            {
                _ = ReplaceSelectionAsync();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.S)
            {
                FlipDirection();
                e.Handled = true;
            }
            else if (ctrl && e.Key == Key.E)
            {
                Edit_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ---- Placement -----------------------------------------------------------------

        private void Present()
        {
            if (!IsVisible)
            {
                Left = -32000;
                Top = -32000;
                Show();
            }
            Place();
            UpdateLayout();
            Place();          // second pass: correct size/DPI now that we're on the right monitor
            Activate();
            Native.SetForegroundWindow(Handle);
        }

        /// <summary>
        /// Opens the card just under the selected text (or under the mouse when the
        /// selection's position isn't known), flipping above it when there's no room,
        /// and keeps it inside the monitor's work area.
        /// </summary>
        private void Place()
        {
            IntPtr h = Handle;
            if (!Native.GetWindowRect(h, out var r)) return;
            int w = r.Right - r.Left, ht = r.Bottom - r.Top;
            if (w <= 0 || ht <= 0) return;

            // What the card should hug, in physical pixels.
            Native.RECT target = _selection ?? new Native.RECT(_cursor.X, _cursor.Y - 10, _cursor.X + 1, _cursor.Y + 14);

            var probe = _selection.HasValue
                ? new System.Drawing.Point(target.Left, target.Bottom)
                : new System.Drawing.Point(_cursor.X, _cursor.Y);
            var wa = System.Windows.Forms.Screen.FromPoint(probe).WorkingArea;

            double scale = VisualTreeHelper.GetDpi(this).DpiScaleY;
            int margin = (int)Math.Round(ShadowMargin * scale);
            int gap = (int)Math.Round(Gap * scale);

            // Never taller than the screen: past that, the body scrolls as one block.
            double maxBody = Math.Max(120, wa.Height / scale - 150);
            if (Math.Abs(Body.MaxHeight - maxBody) > 1) Body.MaxHeight = maxBody;

            // Card's top-left sits at (target.Left, target.Bottom + gap); the window is `margin` bigger on each side.
            int x = target.Left - margin;
            int y = target.Bottom + gap - margin;

            if (y + ht - margin > wa.Bottom)                    // no room below
            {
                int above = target.Top - gap - ht + margin;     // card's bottom just above the text
                y = above + margin >= wa.Top ? above : wa.Bottom - ht + margin;
            }

            x = Math.Min(x, wa.Right - w + margin);
            x = Math.Max(x, wa.Left - margin);
            y = Math.Max(y, wa.Top - margin);

            Native.SetWindowPos(h, Native.HWND_TOPMOST, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        // ---- Helpers --------------------------------------------------------------------

        private static void ApplyDirection(TextBox box, bool rtl)
        {
            box.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }

        private void ShowStatus(string text, bool error = false, bool sticky = false)
        {
            _statusTimer.Stop();
            StatusText.Text = text;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, error ? "ErrorFg" : "SubFg");
            if (!sticky && text.Length > 0) _statusTimer.Start();
        }
    }
}
