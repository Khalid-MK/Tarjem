# Tarjem — ترجم

أداة صغيرة للـ Windows: تحدد أي نص في أي برنامج، تدوس **Ctrl+Alt+T**، يطلعلك popup صغير جنب الماوس فيه الترجمة.

- عربي → English، وأي لغة تانية → عربي (تلقائي).
- الأداة مش بتراقب الـ selection خالص. بتشتغل بس لما تدوس الزرار، فمفيش أي lag.
- قاعدة في الـ system tray. تقدر توقفها مؤقتاً من هناك.

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

من الـ tray: **Edit settings…** ثم **Reload settings**.
الملف: `%AppData%\Tarjem\settings.json`

```json
{
  "Hotkey": "Ctrl+Alt+T",      // e.g. "Ctrl+Shift+Space", "Alt+Q", "F8"
  "Enabled": true,
  "RestoreClipboard": true,     // يرجّع الـ clipboard زي ما كان بعد ما ياخد النص
  "StartWithWindows": false,
  "ArabicTarget": "en",         // النص العربي يتترجم لـ
  "OtherTarget": "ar",          // أي نص تاني يتترجم لـ
  "FontSize": 15,
  "Theme": "system"             // system | dark | light
}
```

## How it works

1. `RegisterHotKey`: hotkey على مستوى الـ system. مفيش keyboard hook ولا polling.
2. لما تدوس الـ hotkey: يستنى لحد ما تشيل إيدك من Alt/Shift، يبعت `Ctrl+C` مرة واحدة للبرنامج اللي قدامك، ويقرا النص من الـ clipboard. بعدها يرجّع الـ clipboard القديم.
3. الترجمة من Google Translate public endpoint (`translate.googleapis.com`، من غير API key). لو فشل، بيجرب MyMemory كـ fallback.

## Notes

- الـ endpoint ده unofficial. شغال كويس للاستخدام الشخصي، بس ممكن Google تحط عليه limit لو استخدامك تقيل جداً.
- البرامج اللي شغالة as Administrator مش هتستقبل الـ Ctrl+C من برنامج عادي. ده من Windows نفسه (UIPI). لو احتجت تترجم من جواها، شغّل Tarjem هو كمان as Admin.
- الـ errors بتتسجل في `%AppData%\Tarjem\error.log`.
