# Phase 1 — Core and application foundation status

## Implemented

- .NET 10 solution foundation with Domain, Application, Infrastructure, and Localization projects.
- Currency-aware Money, CurrencyCode, Account, and balanced double-entry LedgerTransaction.
- Manual transactions are confirmed when created; SMS/import sources remain unconfirmed until explicitly confirmed.
- EF Core SQLite model and initial migration for Accounts, Transactions, and Postings.
- Design-time DbContext factory, pinned dotnet-ef tool manifest, and migration startup service with pre-migration SQLite backup.
- Migration integration tests run against SQLite migrations; the backup test verifies that pre-existing data is retained in the safety copy and that repeated startup is idempotent.
- MAUI Android/Windows shell scaffold with MVVM, DI, Persian resources, and page-level FlowDirection from ILocalizationService.
- MAUI startup invokes DatabaseInitializer before displaying Shell; SQLite WAL, foreign keys, and busy timeout are configured per connection.
- Android explicitly enables native RTL and disables OS Auto Backup while the database is still unencrypted.
- CI separates Core tests from platform-specific Android and Windows builds.

## Not yet implemented or validated

- Production SQLCipher provider, native libraries, key generation/storage, and encrypted CRUD validation.
- Default Persian category/system-account seeding.
- MAUI RTL behavior on Android/Windows devices, screenshots, and complete S2 coverage.
- Persian calendar reference corpus and property-based validation.
- Jalali/Gregorian calendar selection and fiscal-period/numbering workflows are documented decisions but not yet implemented in UI or persistence.
- People/debt, cheque, loan, SMS, reminders, reports, export, and backup features beyond the pre-migration safety copy.

The current MAUI startup uses unencrypted Microsoft EF Core SQLite. It is not a production encrypted database, and the Android/Windows CI jobs are build checks only, not device-level behavior validation.
