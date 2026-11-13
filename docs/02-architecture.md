# 02 — معماری

## ۱. ساختار Solution
```
Hesabyar.sln
Directory.Build.props        # RootNamespace, Nullable, TreatWarningsAsErrors, LangVersion
Directory.Packages.props     # Central Package Management
global.json                  # قفل SDK
src/
  Hesabyar.Domain/           # Entities, ValueObjects, Domain Services, Events — بدون وابستگی
  Hesabyar.Application/      # Feature Services, Interfaces (Ports), DTOs, Validation
  Hesabyar.Infrastructure/   # EF Core+SQLCipher, SMS (Android), Backup, Excel/CSV export, KeyProvider
  Hesabyar.Localization/     # ICalendarService, Jalali, NumberNormalizer, Formatters, .resx
  Hesabyar.App/              # MAUI: Views, ViewModels, Shell, Resources, Platforms
  Hesabyar.Sync.Contracts/   # (فاز بعد) DTOهای مشترک کلاینت/سرور
  Hesabyar.Server/           # (فاز بعد) ASP.NET Core Sync API
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
- **MVVM**: `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)؛ ViewModelها `partial` و بدون ارجاع به View.
- **Shell**: ناوبری با Route ثبت‌شده و پارامتر Type-safe (`INavigationService` پوششی).
- **تب‌ها**: خانه، تراکنش‌ها، حساب‌ها، گزارش‌ها، بیشتر (وام، چک، تکرارشونده، واردات، تنظیمات). دکمه شناور افزودن سریع.
- **DI**: `MauiProgram` فقط ثبت می‌کند؛ ثبت هر ماژول در `AddXxxModule(this IServiceCollection)`.
- **DesignTokens**: `ResourceDictionary` برای رنگ، فاصله، تایپوگرافی (فونت Vazirmatn)، Light/Dark.
- **کنترل‌های سفارشی**: `JalaliDatePicker`, `MoneyEntry`, `AmountLabel` (Bidi-safe), `PersianCalendarView`.
- **State**: ViewModelها `Transient`؛ سرویس‌های حالت (Workspace فعال، تنظیمات) `Singleton` با `WeakReferenceMessenger`.

## ۵. Persistence
- `AppDbContext` (EF Core, SQLite + SQLCipher) با `IDbContextFactory<AppDbContext>`.
- **یک نویسنده**: صف نوشتن (`IWriteQueue`/SemaphoreSlim) چون SQLite تک‌نویسنده است؛ `PRAGMA journal_mode=WAL`, `foreign_keys=ON`, `busy_timeout`.
- **Migrations** هنگام راه‌اندازی با اتصال کلید؛ قبل از Migrate، بکاپ خودکار فایل (محافظ).
- **Interceptor ها**: یک `AuditInterceptor` ساده (CreatedAt/UpdatedAt) و `SoftDeleteInterceptor`.
- **Value Converter**: `Money` ← دو ستون (`AmountMinor` INTEGER, `Currency` TEXT)؛ `DateOnly`/`DateTimeOffset` ← ISO/Unix ms.
- تراز حساب با SUM روی Postingهای ایندکس‌شده؛ در صورت نیاز Snapshot ماهانه (بهینه‌سازی بعدی، نه حالا).

## ۶. جریان‌های متقاطع
| موضوع | راهکار |
|---|---|
| خطا | `Result<T>` + کدهای خطای ثابت (`Errors.Ledger.Unbalanced`)؛ ترجمه در UI |
| اعتبارسنجی | در Service (قوانین کاربردی) و Aggregate (Invariant دامنه) |
| Logging | `ILogger` + فایل چرخشی محلی؛ بدون PII؛ سطح قابل تغییر |
| کار پس‌زمینه | MAUI قابلیت قابل‌اتکا ندارد؛ کارهای دوره‌ای (تکرارشونده، یادآور) **در شروع برنامه و Resume** اجرا + اعلان محلی |
| Concurrency | یک نویسنده؛ Optimistic با `Version` |
| Eventها | Domain Event درون‌فرایندی (ساده)، برای بروزرسانی گزارش/یادآور |

## ۷. گزینه‌های بررسی‌شده
- *Prism*: ماژولار قوی ولی هزینه یادگیری/وابستگی بالاتر؛ Shell+DI کافی است.
- *Hexagonal کامل با CQRS Event Store*: برای نیاز فعلی Over-engineering؛ دفترداری دوطرفه خودش Audit می‌دهد.
