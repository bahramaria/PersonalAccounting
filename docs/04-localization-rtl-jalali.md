# 04 — بومی‌سازی: شمسی، RTL، ارقام

## ۱. اصل‌ها
- ذخیره: UTC میلادی (`DateTimeOffset`) برای رویداد؛ `DateOnly` میلادی برای تاریخ حسابداری. **نمایش** به شمسی در لایه Localization.
- **به `CultureInfo("fa-IR")` برای شمسی تکیه نکنید**: Calendar پیش‌فرض آن در پلتفرم‌ها (ICU/Windows) یکسان نیست.
- منطقه زمانی: `TimeZoneInfo` با شناسه IANA/Windows (`Asia/Tehran`)؛ Offset هرگز Hard-code نشود (ایران ساعت تابستانی را در ۱۴۰۱ لغو کرد؛ داده Time Zone سیستم مرجع است).

## ۲. سرویس تقویم
```csharp
public readonly record struct PersianDate(int Year, int Month, int Day);
public interface ICalendarService
{
    PersianDate ToPersian(DateOnly gregorian);
    DateOnly ToGregorian(PersianDate p);                 // خطا برای تاریخ نامعتبر
    bool IsLeapYear(int persianYear);
    int DaysInMonth(int persianYear, int month);
    DayOfWeek FirstDayOfWeek { get; }                    // شنبه
    PersianDate AddMonths(PersianDate d, int n, MonthEndPolicy policy); // Clamp | Skip
}
public interface IDateFormatter
{
    string Format(DateOnly d, DateFormat f, CalendarKind cal, DigitStyle digits);  // "۱۴۰۵/۰۸/۲۲" | "۲۲ آبان ۱۴۰۵" | "یکشنبه ۲۲ آبان"
    bool TryParse(string input, CalendarKind cal, out DateOnly result);           // ارقام فارسی/عربی/لاتین
}
public enum CalendarKind { Persian, Gregorian /* آینده: HijriQamari */ }
public enum DigitStyle { Persian, Latin }
```
- پیاده‌سازی بر پایه `System.Globalization.PersianCalendar` **یا** الگوریتم داخلی؛ **الزامی**: جدول مرجع تست (سال‌های ۱۲۰۰–۱۵۰۰) + FsCheck رفت‌وبرگشت (`ToGregorian(ToPersian(d)) == d`) + بررسی کبیسه‌ها. اگر باگ/انحراف یافت شد، پیاده‌سازی داخلی جایگزین شود (ADR ثبت شود).
- ماه‌ها: فروردین … اسفند؛ نام ماه از Resource (برای زبان‌های آینده).
- **تعطیلات رسمی** وابسته به رویدادهای قمری و تصمیم رسمی‌اند: دادهٔ جدا و به‌روزرسانی‌پذیر (`holidays.json` با نسخه) — نه محاسبه ثابت.
- تکرار ماهانه شمسی با روز ۳۱ در ماه‌های ۳۰/۲۹ روزه: سیاست `Clamp` (آخرین روز ماه) پیش‌فرض.

## ۳. ارقام و جداکننده‌ها
```csharp
public interface INumberNormalizer
{
    string ToLatinDigits(string s);          // ۰-۹ و ٠-٩ → 0-9
    string NormalizeText(string s);          // ي→ی، ك→ک، حذف ZWNJ/Bidi کنترلی اضافی، trim
    bool TryParseAmount(string s, out decimal value); // «۱٬۲۳۴٫۵»، «1,234.5»، «۱۲۳۴ تومان»
}
public interface IMoneyFormatter { string Format(Money m, MoneyFormatOptions o); }
```
- جداکننده هزارگان نمایشی: `٬` (U+066C) یا `,` طبق تنظیم؛ اعشار `٫` (U+066B) یا `.`؛ پیش‌فرض فارسی: ارقام فارسی + `٬`.
- واحد نمایش: ریال/تومان با `DisplayDivisor`؛ برچسب «ریال»/«تومان»/«﷼» قابل تنظیم.
- ورودی مبلغ (`MoneyEntry`): هنگام تایپ فرمت زنده، پذیرش هر سه نوع رقم، و میان‌بر «هزار/میلیون» (`۵۰ه`، `۲م`) اختیاری.

## ۴. RTL در MAUI
1. `FlowDirection` ریشه (`Shell`/Page) از `ILocalizationService.FlowDirection` بایند شود؛ تغییر زبان = Rebuild صفحه ریشه.
2. به‌جای Left/Right: `Start/End` و `Margin/Padding` متقارن یا Converter جهت‌دار؛ `HorizontalTextAlignment=Start`.
3. آیکون‌های جهت‌دار (بازگشت، پیکان، ادامه) در RTL آینه شوند؛ آیکون‌های غیرجهت‌دار (ساعت، + ) نه.
4. **اعداد/مبالغ در متن RTL**: `AmountLabel` با Unicode Isolates (`U+2066 LRI … U+2069 PDI`) یا `LRM/RLM` تا علامت منفی/واحد جابه‌جا نشود. شماره تلفن/IBAN/صیاد همیشه LTR.
5. فونت: Vazirmatn (مجوز OFL؛ به‌صورت Embedded Font)، اندازه‌ها با `Font Scale` سیستم؛ جایگزین‌ها (Samim/IRANSans در صورت لایسنس) فقط پس از ثبت مجوز.
6. نقاط پرخطر شناخته‌شده که **باید تست دستی/اسنپ‌شات شوند**: Shell Flyout/TabBar، `CollectionView` (اسکرول افقی/Swipe)، `Entry` (مکان‌نما، ارقام)، `Picker`، `DatePicker`، `SearchBar`، نمودارها، PDF خروجی. هر انحراف ← Handler/Renderer سفارشی در `App/Platforms` و ثبت در ADR.
7. نمودار: کتابخانه نامزد (LiveChartsCore.SkiaSharpView.Maui یا Microcharts یا رسم سفارشی SkiaSharp) باید در Spike فاز ۰ برای **RTL، برچسب فارسی و Dark Mode** ارزیابی شود.

## ۵. متن و ترجمه
- `.resx` (fa پیش‌فرض، en)؛ کلیدها نام‌گذاری سلسله‌مراتبی (`Transactions.Add.Title`)؛ جمع/جنسیت با قالب‌های جدا.
- هیچ رشته‌ای در XAML/C# Hard-code نشود؛ تست ساده: اسکن برای حروف فارسی/انگلیسی literal.
- مرتب‌سازی/جستجوی فارسی: نرمال‌سازی (`NormalizeText`) روی نمایه جستجو؛ جستجوی بی‌حساس به `ی/ي`, `ک/ك`, ZWNJ و ارقام.
- خروجی Excel: `Worksheet.RightToLeft = true`، تاریخ به‌صورت متن شمسی + ستون میلادی ISO جدا (برای پردازش ماشینی). PDF: فونت Embedded، شکل‌دهی HarfBuzz و RTL.
