# CLAUDE.md — قوانین الزامی برای عامل‌های کدنویس

پروژه: اپ حسابداری شخصی فارسی با .NET MAUI، **کاربردی و ساده برای کاربر ایرانی** (ثبت از پیامک، چک، اقساط، بدهی/طلب). قبل از هر کار، `README.md` و سند مرتبط در `docs/` را بخوان. معیار تصمیم: «آیا اکثریت کاربران ایرانی به این نیاز دارند و آیا ساده می‌ماند؟»

## دستورهای اصلی

```bash
# Core-only: Linux/Windows, no MAUI workload required
dotnet build Hesabyar.Core.slnx
dotnet test Hesabyar.Core.slnx

# Full solution: requires the MAUI workloads for the target platforms
dotnet build Hesabyar.slnx

# Platform-specific app builds
dotnet build src/Hesabyar.App/Hesabyar.App.csproj -p:MauiTargetFramework=net10.0-android
dotnet build src/Hesabyar.App/Hesabyar.App.csproj -p:MauiTargetFramework=net10.0-windows10.0.19041.0

# EF migrations: run on a machine with the selected MAUI startup TFM workload installed
dotnet ef migrations add <Name> -p src/Hesabyar.Infrastructure -s src/Hesabyar.App
```

تست‌های Domain/Application/Infrastructure/Localization باید بدون MAUI Workload روی Linux اجرا شوند. برای CI و تست‌های Core از `Hesabyar.Core.slnx` استفاده کن، نه Full Solution.

**وضعیت امنیت دیتابیس:** Provider فعلی Microsoft EF Core SQLite بدون رمزنگاری است و فقط برای توسعه/تست و پیشبرد Migration استفاده می‌شود. تا تکمیل Spike S1، SQLCipher، کلید امن یا رمزنگاری دیتابیس را ادعا یا فعال نکن.

## قاعده وابستگی (نقض = Reject)
`Domain` ← `Application` ← (`Infrastructure`, `App`). `Localization` مستقل است.
- Domain: صفر وابستگی به EF/MAUI/Newtonsoft و هر کتابخانه UI.
- Application: Serviceهای هر Feature و Interface؛ پیاده‌سازی فنی (EF، فایل، SMS) ممنوع.
- ViewModel فقط Serviceهای Application را صدا می‌زند، هرگز DbContext را.
- کد وابسته به پلتفرم فقط در `App/Platforms` یا پشت Interface در Infrastructure.

## قوانین سخت (Hard Rules)
1. **پول**: همیشه `Money` (`long` Minor Units + `CurrencyCode`). `double/float` برای پول ممنوع. `decimal` فقط برای نرخ و محاسبه میانی.
2. **زمان**: `DateTime.Now/UtcNow` ممنوع؛ از `TimeProvider` تزریق‌شده استفاده کن. زمان رویداد = `DateTimeOffset` UTC. «تاریخ حسابداری» = `DateOnly` میلادی.
3. **شمسی**: هرگز به `CultureInfo`/`DateTime.ToString` برای شمسی تکیه نکن؛ فقط `ICalendarService` و `IDateFormatter`. رشته تاریخ شمسی هرگز ذخیره نمی‌شود.
4. **RTL**: `Left/Right` و `HorizontalOptions=Start/End` را با دقت؛ از `Start/End` استفاده کن، نه `Left/Right`. همه صفحات باید با `FlowDirection` از `ILocalizationService` کار کنند. هر رشته UI در `.resx`، هیچ رشته hard-coded.
5. **ارقام ورودی**: همه ورودی‌های عددی/تاریخ از `INumberNormalizer` عبور کنند (ارقام فارسی/عربی→لاتین، `٬ ٫` → جداکننده‌ها، `ي ك`→`ی ک`).
6. **دفترداری**: هر `Transaction` باید Invariant مجموع Postingها = صفر به ازای هر ارز را داشته باشد؛ در Aggregate اعمال شود نه در UI.
7. **حذف**: فقط Soft Delete (`IsDeleted`); Hard Delete فقط در Purge Job مستند.
8. **شناسه‌ها**: `Guid` نسخه 7 (`Guid.CreateVersion7()`); `int` auto-increment برای موجودیت دامنه ممنوع. فقط `Id/CreatedAtUtc/UpdatedAtUtc/IsDeleted` در پایه؛ ستون‌های Sync/Workspace قبل از نیاز اضافه نشوند.
9. **Async**: sync-over-async (`.Result`, `.Wait()`) ممنوع؛ `CancellationToken` در همه APIهای I/O.
10. **EF Core**: `AsNoTracking` برای Queryهای خواندنی؛ جلوگیری از N+1 (`Include`/Projection)؛ هر Migration بازبینی شود. نوشتن‌ها از طریق یک نویسنده (SQLite تک‌نویسنده).
11. **امنیت**: هیچ داده مالی/PII در Log. کلید دیتابیس فقط در `SecureStorage` و از طریق `IKeyProvider`. Provider فعلی رمزنگاری ندارد.
12. **لایسنس بسته‌ها**: MediatR، FluentAssertions (v8+)، AutoMapper و Moq استفاده نشوند (لایسنس/ریسک). جایگزین: سرویس دستی، Shouldly، Mapperly/دستی، NSubstitute. قبل از افزودن هر بسته جدید، لایسنس و سازگاری با MAUI بررسی و در `docs/09-decision-log.md` ثبت شود.
13. **Feature Flag**: فقط `IEntitlementService` ساده؛ در MVP همه فعال‌اند. قبل از ساخت هر Interface/جدول/ماژول «برای آینده» بپرس: آیا در `docs/06` MVP است؟ اگر نه، نساز (YAGNI).
14. **نسخه بسته‌ها**: از حافظه نسخه ننویس؛ از NuGet واقعی بخوان و در `Directory.Packages.props` (Central Package Management) قفل کن.
15. **پیامک**: متن خام پیامک و مبالغ هرگز Log نشود؛ مجوز SMS فقط پس از رضایت صریح؛ همه چیز بدون مجوز هم کار کند؛ پارسر پیامک داده‌محور و با Fixture واقعی تست شود.
16. **UX چک/اشخاص**: ویرایش و حذف همیشه ممکن؛ لیست‌ها پیش‌فرض بر اساس تاریخ سررسید؛ بایگانی خودکار موارد نهایی‌شده.

## سبک کد
- C# مدرن: nullable فعال، `record` برای Value Object/DTO، Primary Constructor، `sealed` پیش‌فرض.
- SOLID: در تغییرات ساختاری کوتاه بنویس کدام اصل (SRP/OCP/DIP) انگیزه بود.
- Service هر Feature با متدهای async و خروجی `Result<T>` (بدون Exception برای خطای دامنه)؛ سرویس بزرگ‌تر از ~۸ متد شکسته شود.
- نام‌گذاری: انگلیسی؛ متن کاربر فقط از Resource.

## گردش کار هر Task
1. سند مرتبط را بخوان؛ اگر ابهام بود، فرض را در توضیح PR ثبت کن.
2. برای منطق Domain/Application **اول تست** بنویس (xUnit).
3. پیاده‌سازی کوچک و مستقل؛ Migration در Commit جدا.
4. اجرای Build و Test مناسب پلتفرم؛ هیچ Warning جدیدی نباید اضافه شود.
5. اگر رابط کاربری تغییر کرد: بررسی RTL، Dark Mode و فونت بزرگ؛ تست دستگاه را از Build CI جدا گزارش کن.
6. اگر تصمیم معماری گرفتی: `docs/09-decision-log.md` را به‌روز کن.

## Definition of Done
Build سبز، تست‌های جدید، پوشش Domain ≥ 90٪ و Application ≥ 70٪، بدون رشته hard-coded، RTL تأییدشده روی پلتفرم هدف، مستندات مرتبط به‌روز.

## ممنوعیت‌های قطعی
- افزودن وابستگی به سرویس‌های خارجی (Firebase، Analytics، Crash SaaS) بدون Interface و Opt-in.
- ذخیره رمز، کلید یا PIN به‌صورت متن ساده.
- تغییر قالب فایل بکاپ بدون افزایش `FormatVersion` و Migration بکاپ.
