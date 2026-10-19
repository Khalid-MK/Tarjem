# Tarjem — ترجم

أداة صغيرة للـ Windows: تحدد أي نص في أي برنامج، تدوس **Ctrl+Alt+T**، يطلعلك popup صغير جنب الماوس فيه الترجمة.

- عربي → English، وأي لغة تانية → عربي (تلقائي).
- الأداة مش بتراقب الـ selection خالص. بتشتغل بس لما تدوس الزرار، فمفيش أي lag.
- قاعدة في الـ system tray. تقدر توقفها مؤقتاً من هناك.

## Download

من صفحة [Releases](https://github.com/Khalid-MK/Tarjem/releases/latest):

- **`Tarjem.exe`** (حوالي 0.3MB): محتاج [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). لو مش متسطب، Windows هيطلب منك تنزله أول مرة.
- **`Tarjem-standalone.exe`** (حوالي 63MB): بيشتغل على أي Windows 10/11 x64 من غير ما تسطب أي حاجة.

شغّل الـ exe وهيقعد في الـ tray.

## Build

محتاج .NET 8 SDK.

```
build.bat              :: exe صغير (محتاج .NET 8 Desktop Runtime)
build.bat standalone   :: exe أكبر، يشتغل على أي جهاز
```

الناتج: `publish\Tarjem.exe`. شغّله وهيقعد في الـ tray.

## Popup shortcuts

| Key | Action |
|---|---|
| `Ctrl+Alt+T` | ترجم الـ selection (ولو الـ popup مفتوح ومتفوكس: يقفله) |
| `Esc` | Close |
| `Ctrl+Enter` | Copy & close — ولو بتعدل النص الأصلي: ترجم |
| `Ctrl+R` | Replace: يلصق الترجمة مكان النص اللي كنت محدده |
| `Ctrl+S` | اقلب الاتجاه (عربي ⇄ English) |
| `Ctrl+E` | عدّل النص الأصلي وترجمه تاني |

الترجمة نفسها editable، و Copy بينسخ الجزء المتحدد منها بس لو انت محدد حاجة.
زرار الـ 📌 بيخلي الـ popup مفتوح لما تضغط برا.
لو دوست الـ hotkey من غير ما تحدد حاجة، بيفتح الـ popup فاضي تكتب فيه أو تعمل paste.

## Settings

من الـ tray اختار **Settings…**، أو من زرار ⚙ في الـ popup:

- **Shortcut**: دوس على الخانة واضغط الـ combination الجديدة. لو الـ shortcut ده واخده برنامج تاني، هيقولك قبل ما يحفظ.
  - لازم يبقى فيه Ctrl أو Alt أو Win، لأن الـ hotkey بيتسحب من كل البرامج.
  - الزراير اللي مش بتكتب حاجة تنفع لوحدها أو مع Shift: F1–F24 و Pause و Scroll Lock و Insert وزرار الـ Menu.
  - Shift + حرف أو رقم أو أسهم مش مسموح، لأنه بيبوظ الكتابة والـ selection.
- **Translation**: النص العربي يتترجم لأنهي لغة، وأي لغة تانية تتترجم لأنهي لغة.
- **Popup**:
  - المكان: تحت الـ selection أو عند الماوس
  - العرض: Compact / Normal / Wide
  - حجم خط الترجمة
  - خط للعربي وخط للإنجليزي، مع preview. بيظهر بس الخطوط اللي متسطبة عندك، فلو سطّبت Cairo أو Tajawal مثلاً هيظهروا في القايمة
  - الـ Theme
  - إظهار النص الأصلي
- **Behavior**: ترجيع الـ clipboard بعد ما ياخد النص، والتشغيل مع Windows.

الملف نفسه موجود في `%AppData%\Tarjem\settings.json`. لو حطيت فيه `"DebugLog": true` هيكتب trace لكل ضغطة hotkey في `trace.log` جنب الملف.

### Popup placement

الـ popup بيفتح تحت النص المتحدد مباشرة. بيعرف مكانه بالترتيب ده:

1. UI Automation `TextPattern`: Chrome، Edge، Word، WPF/WinUI، Notepad
2. الـ Win32 caret: الـ edit controls القديمة
3. لو الاتنين فشلوا: مكان الماوس

## How it works

1. `RegisterHotKey`: hotkey على مستوى الـ system. مفيش keyboard hook ولا polling.
2. لما تدوس الـ hotkey: يستنى لحد ما تشيل إيدك من Alt/Shift، يبعت `Ctrl+C` مرة واحدة للبرنامج اللي قدامك، ويقرا النص من الـ clipboard. بعدها يرجّع الـ clipboard القديم.
3. الترجمة من Google Translate public endpoint (`translate.googleapis.com`، من غير API key). لو فشل، بيجرب MyMemory كـ fallback.

## Notes

- الـ endpoint ده unofficial. شغال كويس للاستخدام الشخصي، بس ممكن Google تحط عليه limit لو استخدامك تقيل جداً.
- البرامج اللي شغالة as Administrator مش هتستقبل الـ Ctrl+C من برنامج عادي. ده من Windows نفسه (UIPI). لو احتجت تترجم من جواها، شغّل Tarjem هو كمان as Admin.
- الـ errors بتتسجل في `%AppData%\Tarjem\error.log`.
