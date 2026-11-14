# 05 — امنیت، بکاپ، Sync و Feature Flag

## ۱. مدل تهدید (خلاصه)
| تهدید | کنترل |
|---|---|
| دستگاه گم/دزدیده‌شده | قفل برنامه، SQLCipher، کلید در Keystore/Keychain/DPAPI |
| نگاه از روی شانه / اسکرین‌شات | حالت مخفی‌سازی مبالغ، `FLAG_SECURE` (Android)، پوشش تار در App Switcher (iOS) |
| بکاپ لو رفته | بکاپ رمزنگاری‌شده با گذرواژه کاربر |
| لاگ/Crash حاوی داده مالی | بدون PII در Log؛ Crash Reporter پیش‌فرض محلی و Opt-in |
| سرور (فاز بعد) | احراز هویت، TLS، (در آینده) E2EE |

## ۲. مدیریت کلید
```csharp
public interface IKeyProvider
{
    ValueTask<byte[]> GetDatabaseKeyAsync(CancellationToken ct);   // 256-bit
    Task RotateAsync(CancellationToken ct);
}
```
- اولین اجرا: تولید کلید تصادفی ۲۵۶ بیتی (CSPRNG) → ذخیره در `SecureStorage` → باز کردن SQLite3 Multiple Ciphers با حالت سازگار SQLCipher v4.
- Provider از `Microsoft.EntityFrameworkCore.Sqlite.Core` و `SQLite3MC.PCLRaw.bundle` استفاده می‌کند؛ bundle پیش‌فرض SQLite نباید هم‌زمان بارگذاری شود.
- کلید فقط در SecureStorage سیستم‌عامل نگهداری می‌شود؛ نبود کلید برای دیتابیس رمزنگاری‌شده نباید با تولید کلید جدید پنهان شود.
- دیتابیس plaintext نسخه‌های قبلی در اولین اجرا به فایل رمزنگاری‌شده تبدیل می‌شود و نسخه پشتیبان رمزنگاری‌شده پیش از جایگزینی نگهداری می‌شود.
- پیاده‌سازی MVP: `DeviceKeyProvider`. **آماده E2EE**: `PassphraseWrappedKeyProvider` (گذرواژه → KDF مثل Argon2id/PBKDF2 → KEK → Wrap(DEK)). تعویض Provider نباید به Schema دست بزند.
- فایل‌های پیوست (از ۱٫۱): رمزنگاری AES-256-GCM جدا؛ SQLCipher فایل‌ها را پوشش نمی‌دهد.
- الگوریتم و پارامترها (تعداد تکرار KDF و …) را از مستندات روز/OWASP تأیید کنید؛ از حافظه عدد ننویسید.

## ۳. قفل برنامه
- PIN (۴–۸ رقم، Hash با Salt + KDF، شمارنده تلاش و قفل تصاعدی) + بیومتریک (افزونه سازگار با MAUI؛ ارزیابی در Spike).
- قفل خودکار: فوری/۳۰ثانیه/۱دقیقه/۵دقیقه پس از Background.
- مخفی‌سازی مبالغ (Toggle سراسری) و Widget/Notification بدون مبلغ (تنظیم).
- فراموشی PIN = بازیابی فقط از بکاپ (اطلاع‌رسانی صریح)؛ بدون Backdoor.

### وضعیت پیاده‌سازی قفل در نسخه داخلی

- قفل PIN در نسخه داخلی آزمایشی است؛ فعال‌سازی آن تا آماده‌شدن بکاپ/بازیابی نیازمند تأیید هشدار کاربر است.
- PIN بین ۴ تا ۸ رقم است؛ ارقام فارسی و عربی پیش از اعتبارسنجی به لاتین نرمال می‌شوند.
- اعتبارسنجی با PBKDF2-HMAC-SHA256، تعداد ۶۰۰٬۰۰۰ تکرار، Salt تصادفی ۱۶ بایتی و خروجی ۳۲ بایتی انجام می‌شود؛ مقایسه Hash به‌صورت constant-time است.
- Credential و وضعیت محدودیت تلاش در MAUI SecureStorage نگهداری می‌شوند و fallback ناامن وجود ندارد.
- پس از ۵ تلاش ناموفق، قفل افزایشی ۳۰ ثانیه، ۱ دقیقه، ۵ دقیقه و سپس ۱۵ دقیقه اعمال می‌شود.
- قفل هنگام شروع و پس از بازگشت از پس‌زمینه، با انتخاب فوری/۳۰ ثانیه/۱ دقیقه/۵ دقیقه فعال می‌شود.
- این قفل جایگزین رمزنگاری دیتابیس نیست؛ SQLite فعلی رمزنگاری نشده است. فراموشی PIN تا آماده‌شدن بکاپ/بازیابی به معنی از دست‌رفتن دسترسی برنامه است.
- مرجع پارامتر PBKDF2: [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).

## ۴. بکاپ و ریستور
فرمت `.hbak` (ZIP):
```
manifest.json   { FormatVersion, AppVersion, SchemaVersion, CreatedAtUtc, DatabaseSizeBytes, DatabaseSha256 }
data.db         (SQLite database snapshot)

بیرون از ZIP: header نسخه‌دار شامل وضعیت رمزنگاری، پارامتر KDF و Salt/Nonce/Tag در صورت رمزنگاری
attachments/    (فایل‌های رمزنگاری‌شده)
```
- رمزنگاری کل بسته با گذرواژه کاربر (AES-GCM + KDF)؛ بدون گذرواژه فقط با هشدار.
- بکاپ خودکار محلی دوره‌ای (نگهداری N نسخه) + «خروجی به» Share Sheet/پوشه (SAF در Android، Files در iOS، پوشه در Windows).
- ریستور: اعتبارسنجی Checksum و `FormatVersion`؛ Migration بکاپ‌های قدیمی؛ بکاپ خودکار از وضعیت فعلی پیش از جایگزینی.
- اشتراک‌گذاری از Share Sheet (تلگرام/بله/ایتا/فایل‌ها)؛ بکاپ ابری مستقیم فعلاً ندارد.


### وضعیت پیاده‌سازی بکاپ و بازیابی در نسخه داخلی

- فرمت فایل `.hbak` با سربرگ نسخه‌دار و بسته ZIP شامل `manifest.json` و `data.db` است.
- Snapshot پایگاه داده با SQLite3MC VACUUM INTO snapshot تهیه می‌شود تا داده‌های WAL نیز در نسخه پشتیبان لحاظ شوند.
- فایل دستی می‌تواند با گذرواژه کاربر رمزنگاری شود: AES-256-GCM و PBKDF2-HMAC-SHA256 با ۶۰۰٬۰۰۰ تکرار؛ Salt شانزده‌بایتی، Nonce دوازده‌بایتی و Tag شانزده‌بایتی.
- گذرواژه بکاپ حداقل ۸ نویسه است و در ورودی مخفی دریافت می‌شود؛ امکان بازیابی گذرواژه وجود ندارد. فایل بدون گذرواژه فقط پس از هشدار روشن درباره قابل‌خواندن‌بودن اطلاعات مالی ساخته می‌شود.
- اندازه فایل بکاپ خروجی و ورودی حداکثر ۵۱۲ مگابایت است؛ ساخت بکاپ بزرگ‌تر پیش از نوشتن فایل نهایی رد می‌شود.
- قبل از بازیابی، ساختار فایل، نسخه فرمت، Checksum، صحت SQLite و نسخه Schema بررسی می‌شوند؛ جزئیات بکاپ و وضعیت رمزنگاری پیش از جایگزینی نمایش داده می‌شوند.
- پیش از جایگزینی دیتابیس، یک بکاپ ایمنی محلی از وضعیت فعلی ساخته می‌شود. بازیابی ناموفق نباید دیتابیس جاری را تغییر دهد.
- Snapshot پایگاه داده با `VACUUM INTO` ساخته می‌شود تا WAL و تراکنش‌های ثبت‌شده در Snapshot سازگار لحاظ شوند؛ مقصد با URI و کلید مناسب به‌صورت plaintext یا SQLCipher v4 ساخته می‌شود.
- بکاپ خودکار روزانه در فضای خصوصی برنامه با کلید دستگاه رمزنگاری می‌شود و هفت نسخه نگهداری می‌شوند. این نسخه‌ها برای انتقال دستگاه قابل استفاده نیستند؛ برای انتقال باید بکاپ دستی با گذرواژه کاربر ساخته شود.
- بکاپ شامل دیتابیس است؛ پیوست‌ها تا زمان اضافه‌شدن قابلیت پیوست در نسخه ۱٫۱ خارج از دامنه‌اند.
- بکاپ رمزنگاری‌شده با گذرواژه کاربر و رمزنگاری دیتابیس فعال دو لایه مستقل‌اند.
- وضعیت S1: پیاده‌سازی SQLite3 Multiple Ciphers با cipher سازگار SQLCipher v4 در حال انجام است؛ تا عبور تست‌های رمزنگاری، مهاجرت داده و Build هر دو پلتفرم، رمزنگاری دیتابیس فعال تأییدشده محسوب نمی‌شود.

## ۵. Sync (نسخه ۱٫۲؛ فقط یادداشت طراحی)
هنگام شروع: سرور ASP.NET Core، `ChangeLog` + `DeviceId` + `Version` در Migration مربوط به Sync اضافه می‌شود (نه قبل از آن)، احراز هویت JWT، تعارض فیلد-سطح LWW، Tombstone از همان `IsDeleted`. E2EE با `PassphraseWrappedKeyProvider` در صورت تقاضا. جزئیات کامل در زمان شروع فاز نوشته می‌شود.

## ۶. Feature Flag و Pro (حداقلی)
```csharp
public enum FeatureKey { MultiWorkspace, CloudSync, E2EE, AdvancedReports, ExcelPdfExport, OcrReceipts,
                         SmsImport, StatementImport, Cheques, LoansInstallments, RecurringRules, AttachmentsUnlimited, Budgets, AiAssistant }
public interface IEntitlementService { bool IsEnabled(FeatureKey k); IObservable<EntitlementChanged> Changes { get; } }
```
- MVP: `AllEnabledEntitlementService` (همه فعال). آینده: `LicenseEntitlementService` (توکن امضاشده + بررسی آفلاین با مهلت).
- محدودیت‌های Free (تعداد حساب/پیوست/Workspace…) در `FreeTierLimits` (Config)، نه پراکنده در کد.
- **UI**: `IsVisible/IsEnabled` از Binding به `EntitlementService`؛ **Application** هم در Handler بررسی می‌کند (فقط UI کافی نیست).
- پرداخت: نیمه‌دوم ۱٫۲؛ **آزمون رایگان بلند** (شواهد بازار: کاربران از «اول پول بده» ناراضی‌اند).

## ۷. حریم خصوصی
- Telemetry پیش‌فرض خاموش؛ Opt-in شفاف؛ بدون داده مالی.
- «پاک‌سازی کامل داده» و «خروجی تمام داده» در تنظیمات (انطباق با اصول حریم خصوصی).
- مجوزهای پلتفرم فقط در لحظه نیاز و با توضیح فارسی (دوربین، اعلان، SMS اندروید، Exact Alarm).
