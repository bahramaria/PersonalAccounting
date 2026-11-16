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
public interface IDatabaseKeyProvider
{
    string GetDatabasePassword();
}

public interface IDatabaseKeyInitializer
{
    Task InitializeAsync(string migrationBackupDirectory, CancellationToken ct);
}

public interface IDeviceSecretStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
}
```
- اولین اجرا: تولید کلید تصادفی ۲۵۶ بیتی (CSPRNG) → ذخیره در SecureStorage سیستم‌عامل از طریق `MauiSecureStorageAdapter` → بازکردن SQLite3 Multiple Ciphers در حالت سازگار SQLCipher v4.
- Provider از `Microsoft.EntityFrameworkCore.Sqlite.Core` و `SQLite3MC.PCLRaw.bundle` استفاده می‌کند؛ bundle پیش‌فرض SQLite هم‌زمان بارگذاری نمی‌شود.
- منطق مستقل `DatabaseKeyProvider` از طریق `IDeviceSecretStore` به SecureStorage وصل شده و در تست‌های Core قابل جایگزینی است.
- نبود کلید برای دیتابیس رمزنگاری‌شده خطای بسته می‌دهد؛ کلید جدید جایگزین کلید گم‌شده نمی‌شود و دیتابیس قبلی دست‌نخورده می‌ماند.
- دیتابیس‌های plaintext نسخه‌های قبلی در اولین اجرا با `VACUUM INTO` به SQLCipher v4 تبدیل می‌شوند؛ بکاپ ایمنی رمزنگاری‌شده پیش از جایگزینی حفظ می‌شود.
- تعویض Provider نباید به Schema دست بزند؛ E2EE و چرخش کلید خارج از دامنه فعلی‌اند.
- فایل‌های پیوست (از ۱٫۱): رمزنگاری AES-256-GCM جدا؛ SQLCipher فایل‌ها را پوشش نمی‌دهد.
- الگوریتم و پارامترها (تعداد تکرار KDF و …) را از مستندات روز/OWASP تأیید کنید؛ از حافظه عدد ننویسید.

## ۳. قفل برنامه
- PIN (۴–۸ رقم، Hash با Salt + KDF، شمارنده تلاش و قفل تصاعدی) + بیومتریک بومی سیستم‌عامل؛ در Android از `BiometricPrompt` و در Windows از `UserConsentVerifier` استفاده می‌شود. در صورت نبود سخت‌افزار/قابلیت یا شکست احراز هویت، ورود با PIN در دسترس می‌ماند.
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
- زمان‌سنجی محدودیت تلاش ناموفق از `TimeProvider` تزریق‌شده استفاده می‌کند تا رفتار قفل قابل‌آزمون و مستقل از ساعت سیستم باشد.
- پیاده‌سازی بیومتریک Android از `BiometricPrompt` بومی (Android 9/API 28 به بالا) و مجوز `USE_BIOMETRIC` استفاده می‌کند؛ پیاده‌سازی Windows از `UserConsentVerifier` بهره می‌برد. در هر دو پلتفرم، PIN مسیر جایگزین است.
- قفل PIN جایگزین رمزنگاری دیتابیس نیست. رمزنگاری فعال دیتابیس با SQLite3MC در حالت سازگار SQLCipher v4 پیاده‌سازی شده است؛ Build اندروید و ویندوز در CI سبز شده و اعتبارسنجی Runtime روی دستگاه واقعی هنوز لازم است.
- مرجع پارامتر PBKDF2: [OWASP Password Storage Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).

## ۴. بکاپ و ریستور
فرمت `.hbak` (ZIP):
```
manifest.json   { FormatVersion=2, AppVersion, SchemaVersion, CreatedAtUtc, DatabaseSizeBytes, DatabaseSha256, DatabaseEncrypted, DeviceBound, DatabaseKey? }
data.db         (SQLCipher v4 compatible encrypted database snapshot)

بیرون از ZIP: header نسخه‌دار AES-GCM و پارامترهای KDF در صورت گذرواژه
attachments/    (در نسخه ۱٫۱؛ فایل‌های رمزنگاری‌شده)
```
- دیتابیس داخل بسته در هر دو حالت رمزنگاری‌شده است. با گذرواژه کاربر، کل آرشیو با AES-GCM + PBKDF2 رمزنگاری می‌شود و کلید دستگاه مبدأ فقط داخل همین آرشیو رمزنگاری‌شده قرار می‌گیرد. بدون گذرواژه، کلید داخل آرشیو قرار نمی‌گیرد و فایل فقط روی دستگاه مبدأ قابل بازیابی است.
- بکاپ خودکار محلی دوره‌ای (نگهداری N نسخه) + «خروجی به» Share Sheet/پوشه (SAF در Android، Files در iOS، پوشه در Windows).
- ریستور: اعتبارسنجی Checksum و `FormatVersion`؛ Migration بکاپ‌های قدیمی؛ بکاپ خودکار از وضعیت فعلی پیش از جایگزینی.
- اشتراک‌گذاری از Share Sheet (تلگرام/بله/ایتا/فایل‌ها)؛ بکاپ ابری مستقیم فعلاً ندارد.


### وضعیت پیاده‌سازی بکاپ و بازیابی در نسخه داخلی

- فرمت فایل `.hbak` با سربرگ نسخه‌دار و بسته ZIP شامل `manifest.json` و `data.db` است.
- Snapshot پایگاه داده با `VACUUM INTO` ساخته می‌شود و در تمام مسیرها رمزنگاری‌شده باقی می‌ماند؛ WAL و تراکنش‌های ثبت‌شده در Snapshot سازگار لحاظ می‌شوند.
- بکاپ قابل‌انتقال با گذرواژه کاربر محافظت می‌شود: AES-256-GCM و PBKDF2-HMAC-SHA256 با ۶۰۰٬۰۰۰ تکرار؛ Salt شانزده‌بایتی، Nonce دوازده‌بایتی و Tag شانزده‌بایتی.
- گذرواژه بکاپ حداقل ۸ نویسه است و در ورودی مخفی دریافت می‌شود؛ امکان بازیابی گذرواژه وجود ندارد. بکاپ بدون گذرواژه همچنان رمزنگاری‌شده است، اما Device-bound خواهد بود.
- اندازه فایل بکاپ خروجی و ورودی حداکثر ۵۱۲ مگابایت است؛ ساخت بکاپ بزرگ‌تر پیش از نوشتن فایل نهایی رد می‌شود.
- قبل از بازیابی، ساختار فایل، نسخه فرمت، Checksum، صحت SQLite و نسخه Schema بررسی می‌شوند؛ جزئیات بکاپ و وضعیت رمزنگاری پیش از جایگزینی نمایش داده می‌شوند.
- پیش از جایگزینی دیتابیس، یک بکاپ ایمنی محلی از وضعیت فعلی ساخته می‌شود. بازیابی ناموفق نباید دیتابیس جاری را تغییر دهد.
- Snapshot پایگاه داده با `VACUUM INTO` ساخته می‌شود و در تمام مسیرها رمزنگاری‌شده باقی می‌ماند. بکاپ دارای گذرواژه، کلید دستگاه مبدأ را داخل آرشیو AES-GCM رمزنگاری‌شده نگه می‌دارد تا روی دستگاه دیگر با کلید محلی جدید بازرمزنگاری شود.
- بکاپ بدون گذرواژه همچنان دیتابیس رمزنگاری‌شده دارد، اما کلید مبدأ در آرشیو قرار نمی‌گیرد؛ بنابراین فقط روی همان دستگاه قابل بازیابی است. این وضعیت در رابط کاربری به‌عنوان Device-bound نمایش داده می‌شود.
- بکاپ خودکار روزانه در فضای خصوصی برنامه با کلید دستگاه و رمزنگاری AES-GCM محافظت می‌شود و هفت نسخه نگهداری می‌شوند. بکاپ دستی با گذرواژه، قابلیت انتقال بین دستگاه‌ها را دارد.
- بکاپ شامل دیتابیس است؛ پیوست‌ها تا زمان اضافه‌شدن قابلیت پیوست در نسخه ۱٫۱ خارج از دامنه‌اند.
- بکاپ رمزنگاری‌شده با گذرواژه کاربر و رمزنگاری دیتابیس فعال دو لایه مستقل‌اند.
- وضعیت S1: ۳۷ تست Infrastructure، شامل رمزنگاری، مهاجرت کلید و بکاپ بین دو کلید دستگاه، موفق‌اند؛ Build اندروید و ویندوز نیز سبز شده است. اعتبارسنجی Runtime روی دستگاه واقعی همچنان باز است. iOS در TargetFramework فعلی پروژه فعال نیست.

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
