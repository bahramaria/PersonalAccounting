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
- اولین اجرا: تولید کلید تصادفی ۲۵۶ بیتی (CSPRNG) → ذخیره در `SecureStorage` → باز کردن SQLCipher.
- پیاده‌سازی MVP: `DeviceKeyProvider`. **آماده E2EE**: `PassphraseWrappedKeyProvider` (گذرواژه → KDF مثل Argon2id/PBKDF2 → KEK → Wrap(DEK)). تعویض Provider نباید به Schema دست بزند.
- فایل‌های پیوست (از ۱٫۱): رمزنگاری AES-256-GCM جدا؛ SQLCipher فایل‌ها را پوشش نمی‌دهد.
- الگوریتم و پارامترها (تعداد تکرار KDF و …) را از مستندات روز/OWASP تأیید کنید؛ از حافظه عدد ننویسید.

## ۳. قفل برنامه
- PIN (۴–۸ رقم، Hash با Salt + KDF، شمارنده تلاش و قفل تصاعدی) + بیومتریک (افزونه سازگار با MAUI؛ ارزیابی در Spike).
- قفل خودکار: فوری/۳۰ثانیه/۱دقیقه/۵دقیقه پس از Background.
- مخفی‌سازی مبالغ (Toggle سراسری) و Widget/Notification بدون مبلغ (تنظیم).
- فراموشی PIN = بازیابی فقط از بکاپ (اطلاع‌رسانی صریح)؛ بدون Backdoor.

## ۴. بکاپ و ریستور
فرمت `.hbak` (ZIP):
```
manifest.json   { FormatVersion, AppVersion, SchemaVersion, CreatedAtUtc, DeviceId, WorkspaceIds, Encryption{Alg,KdfParams,Salt}, Checksums }
data.db         (دیتابیس SQLCipher یا Export لاجیکال JSON/NDJSON)
attachments/    (فایل‌های رمزنگاری‌شده)
```
- رمزنگاری کل بسته با گذرواژه کاربر (AES-GCM + KDF)؛ بدون گذرواژه فقط با هشدار.
- بکاپ خودکار محلی دوره‌ای (نگهداری N نسخه) + «خروجی به» Share Sheet/پوشه (SAF در Android، Files در iOS، پوشه در Windows).
- ریستور: اعتبارسنجی Checksum و `FormatVersion`؛ Migration بکاپ‌های قدیمی؛ بکاپ خودکار از وضعیت فعلی پیش از جایگزینی.
- اشتراک‌گذاری از Share Sheet (تلگرام/بله/ایتا/فایل‌ها)؛ بکاپ ابری مستقیم فعلاً ندارد.

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
