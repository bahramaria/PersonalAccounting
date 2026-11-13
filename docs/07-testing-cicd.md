# 07 — تست و CI/CD

## ۱. هرم تست
| لایه | ابزار | هدف |
|---|---|---|
| Domain/Application | xUnit، Shouldly، NSubstitute، FsCheck.Xunit | Invariant دفترداری، محاسبه وام، ماشین حالت چک، تکرارشونده |
| Localization | xUnit + جدول مرجع | شمسی ↔ میلادی، ارقام، پارس مبلغ/تاریخ |
| Infrastructure (یکپارچه) | xUnit + SQLite فایل موقت (و حالت SQLCipher) | Migration، Interceptor، Queryها، ایندکس |
| ViewModel | xUnit با Fake سرویس‌ها | منطق نمایش بدون UI |
| UI/RTL | (فاز بعد) اسنپ‌شات دستی/خودکار | بررسی RTL و Dark Mode |

> *چرا Shouldly/NSubstitute؟* FluentAssertions از v8 لایسنس تجاری دارد و Moq سابقه بحث SponsorLink؛ هر دو جایگزین ساده‌اند (ثبت در ADR).

## ۲. تست‌های الزامی
1. **Ledger Property-based**: برای هر دنباله تصادفی از عملیات معتبر، Σ همه Postingها به‌ازای هر ارز = ۰؛ تراز = مجموع Postingها.
2. **Money**: عملیات روی ارز متفاوت خطا می‌دهد؛ ریال/تومان رفت‌وبرگشت بدون خطا.
3. **Jalali**: ۱۰۰۰ تاریخ مرجع + رفت‌وبرگشت FsCheck برای ۱۲۰۰–۱۵۰۰؛ کبیسه‌ها؛ `AddMonths` با Clamp.
4. **Loan**: ≥ ۱۰ سناریو مرجع (Annuity/Custom، پرداخت جزئی، گرد کردن).
   **SMS Parser**: ≥ ۱۰ نمونه واقعی ناشناس‌شده از هر بانک اولویت‌دار؛ Fixtureها در `tests/Fixtures/sms/`.
5. **Recurring** (از ۱٫۱): Idempotency، Catch-up، ماه ۳۱ روزه.
6. **Cheque**: همه انتقال‌های مجاز/غیرمجاز، تولید Posting درست.
7. **Dedupe/نرمال‌سازی**: پیامک تکراری، ارقام/حروف عربی، تطبیق موجود.
8. **Backup**: Round-trip، گذرواژه اشتباه، FormatVersion قدیمی.
9. **Migration**: اجرای زنجیره Migrationها از نسخه اول تا آخر روی دیتابیس نمونه؛ تست فعلی Migration اولیه و بکاپ پیش از Migration را اجرا می‌کند.
10. **Security**: کلید خالی/اشتباه = عدم دسترسی؛ فایل پیوست دست‌کاری‌شده شناسایی می‌شود (GCM Tag).

## ۳. پوشش و کیفیت
Domain ≥ ۹۰٪، Application ≥ ۷۰٪ (Coverlet)؛ `TreatWarningsAsErrors`; Analyzers (.NET Analyzers، Roslynator اختیاری)؛ `dotnet format --verify-no-changes`; بررسی بسته‌های آسیب‌پذیر (`dotnet list package --vulnerable`).

## ۴. CI (GitHub Actions — اولین پیاده‌سازی)
**اصل: منطق Build در `build/*.sh|ps1`**؛ فایل Workflow فقط آن‌ها را صدا می‌زند تا انتقال به GitLab/Azure DevOps/Gitea (در صورت محدودیت دسترسی) ساده شود.
```
.github/workflows/ci.yml
  job test-core      : ubuntu-latest   → restore, build, test (Domain/Application/Infrastructure/Localization)
  job build-android  : ubuntu-latest   → install maui-android workload, restore and build net10.0-android
  job build-windows  : windows-latest  → install maui-windows workload, restore and build net10.0-windows10.0.19041.0
Trigger: PR و push به main و feature/**؛ تست‌های Core مستقل از MAUI workload اجرا می‌شوند. اجرای جدید روی همان branch، CI قدیمی همان branch را لغو می‌کند.
```
- نسخه‌دهی: `global.json` با roll-forward محدود به Feature Band .NET 10؛ `versionCode` اندروید از شماره Build.
- امضا و Secrets (Keystore/Provisioning) فقط در Secret Store؛ هرگز در مخزن.
- **CD** در این فاز: انتشار خودکار نیست؛ Artifact تولید نمی‌شود. (CD کامل = فاز بعدی در صورت نیاز.)
- مخزن: `main` محافظت‌شده، PR الزامی، Conventional Commits.
