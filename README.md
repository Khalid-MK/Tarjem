# Tarjem — ترجم

**[English](#english) · [العربية](#العربية)**

---

## English

A tiny Windows tool: select text in any app, press **Ctrl+Alt+T**, and a small popup opens right under your selection with the translation.

- Arabic → English, and any other language → Arabic, automatically.
- It doesn't watch your selection. It only runs when you press the shortcut, so it never slows your PC down.
- Lives in the system tray. You can pause it from there.

### Download

From the [Releases](https://github.com/Khalid-MK/Tarjem/releases/latest) page:

- **`Tarjem.exe`** (~0.3 MB): needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). If it's missing, Windows offers to download it on first run.
- **`Tarjem-standalone.exe`** (~63 MB): runs on any Windows 10/11 x64 with nothing to install.

Run the exe and it sits in the tray. The exe isn't code-signed, so the first time Windows SmartScreen may show "Windows protected your PC". Click **More info → Run anyway**.

### Popup shortcuts

| Key | Action |
|---|---|
| `Ctrl+Alt+T` | Translate the selection (closes the popup if it's focused) |
| `Esc` | Close |
| `Ctrl+Enter` | Copy & close. While editing the original text: translate |
| `Ctrl+R` | Replace: paste the translation over the text you selected |
| `Ctrl+S` | Flip direction (Arabic ⇄ English) |
| `Ctrl+E` | Edit the original text and translate again |

- The translation is editable. **Copy** copies only the selected part if you selected something.
- 📌 keeps the popup open when you click elsewhere.
- Pressing the shortcut with nothing selected opens an empty popup to type or paste into.

### Settings

Open **Settings…** from the tray, or the ⚙ button in the popup.

- **Shortcut**: click the box and press the new combination. If another app already uses it, you're told before saving.
  - It must include Ctrl, Alt or Win, because the shortcut is taken away from every other app.
  - Keys that type nothing work on their own or with Shift: F1–F24, Pause, Scroll Lock, Insert and the Menu key.
  - Shift + a letter, digit or arrow isn't allowed: it would break typing and text selection.
  - **Block the key's own action** (on by default): the app you're in won't also react to the shortcut. For example, the Menu key won't open a right-click menu at the same time.
- **Translation**: which language Arabic text goes to, and which language everything else goes to.
- **Popup**:
  - Position: under the selection, or at the mouse
  - Width: Compact / Normal / Wide
  - Translation text size
  - Separate Arabic and English fonts, with a live preview. Only installed fonts are listed, so fonts like Cairo or Tajawal appear once you install them.
  - Theme: System / Dark / Light
  - Show or hide the original text
- **Behavior**: put your clipboard back after grabbing the selection, and start with Windows.

The settings file is `%AppData%\Tarjem\settings.json`. Setting `"DebugLog": true` writes a trace of every shortcut press to `trace.log` next to it.

### How it works

1. **Shortcut.** By default a small low-level keyboard hook on its own thread watches for your shortcut and swallows it, so the app underneath never sees it. With *Block the key's own action* off, it uses `RegisterHotKey` instead: no hook at all, but some keys (like Menu) may also trigger their normal action.
2. **Grabbing the text.** When you press the shortcut, Tarjem waits for you to release Alt/Shift, sends `Ctrl+C` once to the app in front, reads the clipboard, then puts your old clipboard back.
3. **Placement.** The popup opens right under the selected text. It finds it using, in order:
   1. UI Automation `TextPattern` (Chrome, Edge, Word, WPF/WinUI, Notepad)
   2. the Win32 caret (classic edit controls)
   3. the mouse position, if both fail
4. **Translation.** Google Translate's public endpoint (`translate.googleapis.com`, no API key), with MyMemory as a fallback.

### Build

Needs the .NET 8 SDK (or newer).

```
build.bat              :: small exe (needs the .NET 8 Desktop Runtime)
build.bat standalone   :: bigger exe that runs anywhere
```

Output: `publish\Tarjem.exe`.

### Notes

- The translation endpoint is unofficial. It works well for personal use, but Google may rate-limit very heavy use.
- Apps running as Administrator won't accept `Ctrl+C` from a normal app. That's Windows itself (UIPI). To translate inside them, run Tarjem as Administrator too.
- Errors are logged to `%AppData%\Tarjem\error.log`.

---

<div dir="rtl">

## العربية

أداة صغيرة لنظام Windows: حدِّد أي نص في أي برنامج، واضغط **Ctrl+Alt+T**، فتظهر نافذة صغيرة أسفل النص المحدد مباشرةً وفيها الترجمة.

- العربية ← الإنجليزية، وأي لغة أخرى ← العربية، تلقائيًا.
- الأداة لا تراقب النص الذي تحدده. تعمل فقط عند ضغط الاختصار، لذلك لا تُبطئ جهازك أبدًا.
- تعمل من شريط النظام (System Tray)، ويمكنك إيقافها مؤقتًا من هناك.

### التحميل

من صفحة [Releases](https://github.com/Khalid-MK/Tarjem/releases/latest):

- **`Tarjem.exe`** (حوالي 0.3 ميجابايت): يحتاج إلى [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). إذا لم يكن مثبتًا، سيعرض Windows تنزيله عند أول تشغيل.
- **`Tarjem-standalone.exe`** (حوالي 63 ميجابايت): يعمل على أي Windows 10/11 بنظام x64 دون تثبيت أي شيء.

شغِّل الملف وسيظهر في شريط النظام. الملف غير موقَّع رقميًا، لذلك قد يُظهر Windows SmartScreen في أول مرة رسالة "Windows protected your PC". اضغط **More info ← Run anyway**.

### اختصارات النافذة

| الاختصار | الوظيفة |
|---|---|
| `Ctrl+Alt+T` | ترجمة النص المحدد (ويغلق النافذة إن كانت هي النشطة) |
| `Esc` | إغلاق |
| `Ctrl+Enter` | نسخ ثم إغلاق. وأثناء تعديل النص الأصلي: ترجمة |
| `Ctrl+R` | استبدال: يلصق الترجمة مكان النص الذي حددته |
| `Ctrl+S` | عكس الاتجاه (عربي ⇄ إنجليزي) |
| `Ctrl+E` | تعديل النص الأصلي وإعادة ترجمته |

- يمكنك تعديل الترجمة نفسها. زر **Copy** ينسخ الجزء المحدد فقط إن كنت قد حددت جزءًا منها.
- زر 📌 يُبقي النافذة مفتوحة عند الضغط خارجها.
- إذا ضغطت الاختصار دون تحديد أي نص، تُفتح نافذة فارغة لتكتب فيها أو تلصق نصًا.

### الإعدادات

افتح **Settings…** من شريط النظام، أو من زر ⚙ في النافذة.

- **الاختصار**: اضغط على الخانة ثم اضغط التركيبة الجديدة. إذا كان برنامج آخر يستخدمها، ستُبلَّغ بذلك قبل الحفظ.
  - يجب أن يحتوي على Ctrl أو Alt أو Win، لأن الاختصار يُسحب من جميع البرامج الأخرى.
  - المفاتيح التي لا تكتب شيئًا تعمل وحدها أو مع Shift: F1–F24 و Pause و Scroll Lock و Insert ومفتاح القائمة (Menu).
  - Shift مع حرف أو رقم أو سهم غير مسموح، لأنه سيعطِّل الكتابة وتحديد النصوص.
  - **منع الوظيفة الأصلية للمفتاح** (مفعَّل افتراضيًا): البرنامج الذي تعمل فيه لن يتفاعل مع الاختصار أيضًا. مثلًا، مفتاح Menu لن يفتح قائمة الزر الأيمن في الوقت نفسه.
- **الترجمة**: اللغة التي يُترجم إليها النص العربي، واللغة التي يُترجم إليها أي نص آخر.
- **النافذة**:
  - الموضع: أسفل النص المحدد، أو عند مؤشر الفأرة
  - العرض: Compact / Normal / Wide
  - حجم خط الترجمة
  - خط للعربية وخط للإنجليزية، مع معاينة فورية. تظهر الخطوط المثبتة فقط، لذا تظهر خطوط مثل Cairo أو Tajawal بمجرد تثبيتها.
  - المظهر: System / Dark / Light
  - إظهار النص الأصلي أو إخفاؤه
- **السلوك**: إرجاع محتوى الحافظة بعد أخذ النص، والتشغيل مع بدء Windows.

ملف الإعدادات هو `%AppData%\Tarjem\settings.json`. إذا وضعت فيه `"DebugLog": true` فسيُسجِّل تتبعًا لكل ضغطة اختصار في ملف `trace.log` بجواره.

### كيف تعمل

1. **الاختصار.** افتراضيًا، تراقب خطافة لوحة مفاتيح صغيرة (low-level keyboard hook) في خيط مستقل اختصارك فقط وتحجبه، فلا يصل إلى البرنامج الذي تعمل فيه. وعند إيقاف خيار *منع الوظيفة الأصلية للمفتاح* تُستخدم `RegisterHotKey` بدلًا منها: بلا أي خطافة، لكن بعض المفاتيح (مثل Menu) قد تؤدي وظيفتها المعتادة أيضًا.
2. **أخذ النص.** عند ضغط الاختصار، تنتظر الأداة حتى ترفع إصبعك عن Alt/Shift، ثم ترسل `Ctrl+C` مرة واحدة إلى البرنامج الظاهر، وتقرأ الحافظة، ثم تعيد محتواها القديم.
3. **الموضع.** تظهر النافذة أسفل النص المحدد مباشرةً، وتحدد مكانه بهذا الترتيب:
   1. UI Automation `TextPattern` (Chrome و Edge و Word و WPF/WinUI و Notepad)
   2. مؤشر الكتابة في Win32 (حقول الإدخال التقليدية)
   3. موضع مؤشر الفأرة إذا فشل الاثنان
4. **الترجمة.** عبر الواجهة العامة لترجمة Google (`translate.googleapis.com`، دون مفتاح API)، مع MyMemory بديلًا عند الفشل.

### البناء من المصدر

يحتاج إلى .NET 8 SDK (أو أحدث).

```
build.bat              :: ملف صغير (يحتاج .NET 8 Desktop Runtime)
build.bat standalone   :: ملف أكبر يعمل على أي جهاز
```

الناتج: `publish\Tarjem.exe`.

### ملاحظات

- واجهة الترجمة غير رسمية. تعمل جيدًا للاستخدام الشخصي، لكن قد تحدّ Google منها عند الاستخدام الكثيف جدًا.
- البرامج التي تعمل بصلاحيات المسؤول (Administrator) لا تقبل `Ctrl+C` من برنامج عادي. هذا من Windows نفسه (UIPI). لترجمة النص داخلها، شغِّل Tarjem أيضًا بصلاحيات المسؤول.
- تُسجَّل الأخطاء في `%AppData%\Tarjem\error.log`.

</div>
