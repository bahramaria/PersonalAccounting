# 03 — مدل داده (نسخه ۲: ساده‌شده)

## ۱. اصول
1. **دفترداری دوطرفه پشت صحنه:** هر تراکنش = Postingهایی با مجموع صفر. کاربر فقط «دریافت/پرداخت/انتقال» می‌بیند.
2. پول = `long` (Minor Unit) + کد ارز. هرگز `double`.
3. فقط چیزی ساخته می‌شود که در MVP لازم است. جدول‌های آینده (بودجه، هدف، دارایی، چندکاربره، Sync) **ساخته نمی‌شوند**؛ افزودن‌شان بعداً یک Migration است.
4. حداقل‌های Sync-Ready که بعداً گران‌اند: `Guid` v7، `CreatedAtUtc`، `UpdatedAtUtc`، `IsDeleted` (Soft Delete).
5. تراز هیچ‌وقت ذخیره نمی‌شود؛ از Postingها محاسبه می‌شود.

## ۲. پایه (`EntityBase`)
`Id (Guid v7)`, `CreatedAtUtc`, `UpdatedAtUtc`, `IsDeleted`. (بدون `WorkspaceId/Version/DeviceId`؛ فیلتر سراسری `IsDeleted` کافی است.)

## ۳. Value Objectها
```csharp
public readonly record struct CurrencyCode(string Value);              // MVP: فقط "IRR"
public readonly record struct Money(long Minor, CurrencyCode Currency); // ریال = کوچک‌ترین واحد
```
- ذخیره همیشه ریال؛ «تومان» فقط تنظیم نمایش (`÷10`).
- حساب‌ها فیلد `CurrencyCode` دارند (آماده چندارزی)، ولی MVP رابط انتخاب ارز ندارد و جدول نرخ ارز ساخته نمی‌شود.

## ۴. موجودیت‌های MVP

**Account** — `Name, Type{Asset,Liability,Income,Expense,Equity}, Kind{Cash,Bank,Card,Wallet,CreditCard,Category,System}, ParentId?, CurrencyCode, Icon, Color, SortOrder, IsArchived, IncludeInTotal` + اختیاری برای بانک: `BankName, CardLast4, AccountNumberMasked`.
دسته‌بندی‌ها خودشان Account از نوع Income/Expense هستند (حداکثر ۲ سطح).
حساب‌های **System** (خودکار): `Opening` (Equity)، `Receivables` (طلب از اشخاص)، `Payables` (بدهی به اشخاص)، `ChequesReceivable`، `ChequesPayable`، `Uncategorized`، `Adjustment`.

**Transaction** — `BusinessDate (DateOnly میلادی), Kind{Expense,Income,Transfer,Opening,Adjustment,PersonDebt,PersonRepay,LoanPayment,ChequeEvent}, Description?, PartyId?, Source{Manual,Sms,Recurring...}, SmsMessageId?, IsConfirmed(bool)`.
**Posting** — `TransactionId, AccountId, AmountMinor (بدهکار مثبت، بستانکار منفی), CurrencyCode, PartyId?, Memo?`.
`PartyId` روی Posting اجازه می‌دهد مانده هر شخص (طلب/بدهی) با `SUM` روی حساب‌های Receivables/Payables به‌دست بیاید.

**Party** — `Name, Phone?, Note?`.

**نگاشت عملیات → Posting**
| عملیات | Postingها |
|---|---|
| پرداخت ۵۰۰ از بانک (غذا) | `+Expense:Food`, `−Asset:Bank` |
| دریافت ۱۰۰۰ | `+Asset:Bank`, `−Income:Salary` |
| انتقال | `+Dest`, `−Source` |
| **قرض دادم به علی** | `+Receivables(Party=Ali)`, `−Asset:Bank` |
| **علی پس داد** | `+Asset:Bank`, `−Receivables(Party=Ali)` |
| **قرض گرفتم از رضا** | `+Asset:Bank`, `−Payables(Party=Reza)` |
| پرداخت قسط وام | `+Payables/LoanLiability (اصل)`, `+Expense:Interest (سود)`, `−Asset:Bank` |
| دریافت چک | `+ChequesReceivable`, `−(Income/Receivables)` |
| وصول چک دریافتی | `+Asset:Bank`, `−ChequesReceivable` |
| پاس شدن چک پرداختی | `+ChequesPayable`, `−Asset:Bank` |
قاعده: Σ Posting به‌ازای هر ارز = ۰؛ حداقل ۲ Posting؛ اعمال در Aggregate.
*MVP فقط تراکنش دو-پایه + تراکنش وام (۲–۳ Posting) دارد؛ Split بعداً.*

**Cheque** — `Direction{Received,Issued}, AmountMinor, BankName, SerialNo?, SayadId? (۱۶ رقم), DueDate (DateOnly), PartyId?, Status, StatusDate, LinkedAccountId?, Note?, IsArchived`.
وضعیت‌ها: گیرنده `Received → Cleared | Bounced | Endorsed | Cancelled`؛ پرداختی `Issued → Cleared | Bounced | Cancelled`. (مرحله «Deposited» اختیاری؛ MVP ساده: ۴ وضعیت.)
`ChequeStatusHistory(ChequeId, From, To, AtUtc, TransactionId?)` — سبک، برای نمایش تاریخچه.
**قوانین UX (از شکایت‌های بازار):** ویرایش و حذف همیشه مجاز (با تصحیح تراکنش‌های مرتبط)؛ بایگانی خودکار چک‌های وصول‌شده؛ فیلد یادداشت؛ مرتب‌سازی پیش‌فرض بر اساس سررسید.

**Loan / LoanInstallment** — `Loan(Name, Direction{Borrowed,Lent}, PartyId?, PrincipalMinor, AnnualRatePercent?, Method{Annuity,Custom}, InstallmentCount, FirstDueDate, AccountId (حساب بدهی/طلب), Status)`، `LoanInstallment(LoanId, Number, DueDate, PrincipalMinor, InterestMinor, PaidMinor, PaidDate?, Status{Upcoming,Paid,Partial,Overdue})`.
روش‌های «اصل مساوی»، «کارمزد ثابت» و جریمه دیرکرد بعداً اضافه می‌شوند (v1.1+)؛ دیرکرد در MVP فقط به‌صورت «روزهای تأخیر» نمایش داده می‌شود.

**Reminder** — `OwnerType, OwnerId, FireAtUtc, Title, Status`.

**SmsMessage** (برای MVP) — `Sender, Body, ReceivedAtUtc, ParsedKind?, ParsedAmountMinor?, ParsedBalanceMinor?, AccountId?, TransactionId?, Status{New,Matched,Ignored,Posted}, DedupeHash`.
**CategoryMemory (حافظه دسته)** — `MatchKey (متن نرمال‌شده/فروشنده), CategoryId, PartyId?, Hits` — ساده‌ترین نسخه «MerchantRule».

**Setting** — `Key, ValueJson`. **SchemaInfo** برای نسخه Migration.

## ۵. بعد از MVP (فقط وقتی لازم شد)
`RecurringRule/Occurrence` (۱٫۱)، `Attachment` (۱٫۱)، `Budget` (۱٫۱)، `Tag` (۱٫۱)، `ExchangeRate`/`AssetUnit` (در صورت تقاضا)، `ChangeLog/DeviceId/Version` + `Workspace` (همراه Sync/چندحسابی).
**هزینه افزودن بعدی:** یک Migration + یک فیلتر سراسری؛ دلیلی برای پیاده‌سازی پیش‌دستانه نیست.

## ۶. ایندکس‌ها و محدودیت‌ها
`Posting(AccountId, TransactionId)`, `Posting(PartyId)`, `Transaction(BusinessDate DESC)`, `Cheque(DueDate, Status)`, `LoanInstallment(DueDate, Status)`, `SmsMessage(DedupeHash)` یکتا.
`CHECK`: `SayadId` ۱۶ رقم عددی؛ ارز Posting = ارز حساب.

## ۷. Seed
دسته‌های فارسی: خوراک، مسکن/اجاره، حمل‌ونقل، سلامت، آموزش، تفریح، قبوض، اقساط، پوشاک، مناسبت/هدیه، حقوق، سایر.
