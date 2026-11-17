# 02 — معماری

## ۱. ساختار Solution
```
Hesabyar.slnx
Hesabyar.Core.slnx           # Core-only solution for Linux tests without MAUI workloads
Directory.Build.props        # RootNamespace, Nullable, TreatWarningsAsErrors, LangVersion
Directory.Packages.props     # Central Package Management
global.json                  # .NET 10 SDK band
src/
  Hesabyar.Domain/           # Entities, ValueObjects, Domain Services, Events — بدون وابستگی
  Hesabyar.Application/      # Feature Services, Interfaces (Ports), DTOs, Validation
  Hesabyar.Infrastructure/   # EF Core + SQLite development provider, SMS, Backup, export, KeyProvider
  Hesabyar.Localization/     # ICalendarService, ILocalizationService, Jalali, NumberNormalizer, .resx
  Hesabyar.App/              # MAUI Android/Windows: Views, ViewModels, Shell, Resources, Platforms
  Hesabyar.Sync.Contracts/   # قراردادهای نسخه‌دار و مستقل از UI برای Sync کلاینت/سرور
  Hesabyar.Server/           # ASP.NET Core Sync API (بسته بعدی دهک ۹)
tests/
  Hesabyar.Domain.Tests/  Hesabyar.Application.Tests/
  Hesabyar.Infrastructure.Tests/  Hesabyar.Localization.Tests/
build/                       # اسکریپت‌های مستقل از CI (build.ps1 / build.sh)
```
**قاعده وابستگی**: `Domain ← Application ← Infrastructure/App`. `Localization` مستقل. (DIP: App و Infrastructure به Interfaceهای Application وابسته‌اند.)

## ۲. سازمان‌دهی داخلی Application (سرویس هر Feature)
```
Application/
  Accounts/ Transactions/ People/ Cheques/ Loans/ Sms/ Reminders/ Reports/ Backup/ Settings/
  Common/  (Result<T>, Errors, IClock)
```
هر Feature یک یا چند Service کوچک دارد (`TransactionService`, `ChequeService`…) با متدهای async و خروجی `Result<T>`. اگر یک سرویس از ~۳۰۰ خط یا ~۸ متد بزرگ‌تر شد، به Use Caseهای جدا شکسته می‌شود (SRP). *چرا نه Handler جدا از ابتدا؟* مراسم زیاد برای تیم تک‌نفره؛ MediatR هم لایسنس تجاری دارد.

## ۳. پورت‌های لازم در MVP
```csharp
public interface IClock { DateTimeOffset UtcNow { get; } }               // یا TimeProvider
public interface IKeyProvider { ValueTask<byte[]> GetDatabaseKeyAsync(CancellationToken ct); }
public interface ISmsSource { IAsyncEnumerable<RawSms> ReadInboxAsync(DateTimeOffset since, CancellationToken ct); }  // Android؛ سایر: no-op
public interface ISmsParser { bool CanParse(string sender, string body); ParsedSms? Parse(string sender, string body); }
public interface IReminderScheduler { Task RescheduleAllAsync(CancellationToken ct); }
public interface IBackupService { Task<BackupInfo> CreateAsync(BackupOptions o, CancellationToken ct); Task RestoreAsync(Stream s, string? password, CancellationToken ct); }
public interface IEntitlementService { bool IsEnabled(FeatureKey key); }   // MVP: همه فعال
```
سایر Interfaceها (`IFileStore`, `IOcrEngine`, `IReportExporter`, `IPurchaseProvider`, `ICrashReporter`…) **فقط وقتی ساخته می‌شوند که دومین پیاده‌سازی یا نیاز واقعی پیدا شد.**

## ۴. Presentation (MAUI)
- **MVVM**: `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`); ViewModelها `partial` و بدون ارجاع به View.
- **Shell**: ناوبری با Route ثبت‌شده؛ تب‌های خانه، تراکنش‌ها، حساب‌ها، گزارش‌ها و بیشتر. در این مرحله فقط خانه عملیاتی است و تب‌های دیگر Placeholder هستند.
- **DI**: `MauiProgram` نقطه Composition Root است؛ ثبت سرویس‌های زیرساخت از `AddHesabyarInfrastructure` انجام می‌شود.
- **RTL**: `ILocalizationService.FlowDirection` جهت Shell و هر Page را تعیین می‌کند؛ متن‌ها از `.resx` می‌آیند. Android با `supportsRtl=true` ساخته می‌شود. این اسکلت، رفتار RTL روی دستگاه را تأیید نمی‌کند.
- **DesignTokens**: `ResourceDictionary` برای رنگ و سبک پایه؛ فونت Vazirmatn و Light/Dark کامل هنوز باید اضافه و ارزیابی شوند.
- **State**: ViewModelها `Transient`؛ سرویس‌های پایدار در صورت نیاز `Singleton`.

## ۵. Persistence
- `AppDbContext` با `IDbContextFactory<AppDbContext>` و Migrationهای EF Core.
- در اسکلت فعلی، Provider برابر Microsoft EF Core SQLite و **بدون رمزنگاری** است؛ SQLCipher و کلید امن تا تکمیل Spike S1 فعال نمی‌شوند.
- `StartupPage` پیش از نمایش Shell، `DatabaseInitializer` را اجرا می‌کند: وجود Migration معوق را بررسی، در صورت وجود دیتابیس فایل‌محور از آن نسخه پشتیبان می‌گیرد و سپس `MigrateAsync` را اجرا می‌کند.
- تا قبل از پیاده‌سازی بکاپ رمزنگاری‌شده، Android `allowBackup=false` دارد تا دیتابیس محلیِ فعلاً بدون رمزنگاری وارد Android Auto Backup نشود.
- `SqliteConnectionPragmaInterceptor`: `journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout=5000`.
- زمان Audit از `TimeProvider` تزریق‌شده گرفته می‌شود؛ تاریخ‌های حسابداری همچنان `DateOnly` میلادی‌اند.
- **یک نویسنده**: صف نوشتن (`IWriteQueue`/SemaphoreSlim) باید پیش از هم‌زمانی نوشتن از چند Feature اضافه شود.
- **Interceptorها**: Soft Delete و قواعد Audit توسعه‌یافته در مرحله بعد؛ فعلاً فیلدهای پایه هنگام SaveChanges مقداردهی می‌شوند.
- تراز حساب با SUM روی Postingهای ایندکس‌شده؛ Snapshot ماهانه (بهینه‌سازی بعدی، نه حالا).

## ۶. جریان‌های متقاطع
| موضوع | راهکار |
|---|---|
| خطا | `Result<T>` + کدهای خطای ثابت (`Errors.Ledger.Unbalanced`); ترجمه در UI |
| اعتبارسنجی | در Service (قوانین کاربردی) و Aggregate (Invariant دامنه) |
| Logging | `ILogger`؛ بدون PII؛ سطح قابل تغییر |
| کار پس‌زمینه | MAUI قابلیت قابل‌اتکا ندارد؛ کارهای دوره‌ای (تکرارشونده، یادآور) **در شروع برنامه و Resume** اجرا + اعلان محلی |
| Concurrency | SQLite تک‌نویسنده؛ صف نوشتن هنوز پیاده نشده است |
| Eventها | Domain Event درون‌فرایندی (ساده)، برای بروزرسانی گزارش/یادآور |

## ۷. گزینه‌های بررسی‌شده
- *Prism*: ماژولار قوی ولی هزینه یادگیری/وابستگی بالاتر؛ Shell+DI کافی است.
- *Hexagonal کامل با CQRS Event Store*: برای نیاز فعلی Over-engineering؛ دفترداری دوطرفه خودش Audit می‌دهد.


## ۸. معماری همگام‌سازی (دهک ۹)

- قراردادهای مشترک در پروژه مستقل `Hesabyar.Sync.Contracts` قرار دارند؛ این پروژه به MAUI، EF Core، دیتابیس و روش احراز هویت وابسته نیست.
- هر تغییر با `ChangeId` یکتا، شناسه Aggregate، نوع عملیات، نسخه Schema و مهر نسخه شامل زمان UTC، شناسه دستگاه و شماره ترتیبی همان دستگاه منتقل می‌شود.
- تغییرات یک Aggregate به‌صورت یک واحد همگام می‌شوند؛ به‌ویژه تراکنش مالی و تمام Postingهای آن نباید جداگانه اعمال شوند.
- ادغام فیلدها مستقل است و بر اساس مهر نسخه با ترتیب قطعی انجام می‌شود؛ حذف با Tombstone مستقل از حذف فیزیکی نمایش داده می‌شود.
- Push دارای شناسه تغییر idempotent و ترتیب افزایشی شماره دستگاه است؛ Pull با Cursor و صفحه‌بندی محدود انجام می‌شود.
- احراز هویت و مالکیت داده از Claim معتبر سرور به دست می‌آید؛ هیچ `UserId` ارسالی از کلاینت مرجع مالکیت نیست.
- قرارداد فعلی صرفاً پروتکل و اعتبارسنجی مستقل را پیاده‌سازی می‌کند؛ انتقال شبکه، ذخیره ChangeLog سمت سرور، احراز هویت و ادغام پایگاه داده در بسته‌های بعدی دهک ۹ اضافه می‌شوند.
