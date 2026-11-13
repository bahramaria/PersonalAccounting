# Phase 1 — Core and application foundation status

## Implemented

- .NET 10 solution foundation with Domain, Application, Infrastructure, and Localization projects.
- Currency-aware Money, CurrencyCode, Account, and balanced double-entry LedgerTransaction.
- Manual transactions are confirmed when created; SMS/import sources remain unconfirmed until explicitly confirmed.
- EF Core SQLite model and the initial `202611130001_InitialAccountingSchema` migration for Accounts, Transactions, and Postings.
- Design-time DbContext factory and pinned local `dotnet-ef` tool manifest.
- Database initialization service that applies pending migrations and creates a pre-migration SQLite backup when an existing file database is present.
- Migration integration tests use SQLite migrations rather than EnsureCreated.
- Account and transaction application services, EF repository, and existing core tests.

## Not yet implemented or validated

- Production SQLCipher provider, native libraries, key storage, and encrypted CRUD validation.
- Wiring the database initializer into MAUI startup after the secure encrypted provider is selected.
- Default Persian category/system-account seeding.
- MAUI RTL behavior on Android/Windows devices and complete S2 coverage.
- Persian calendar reference corpus and property-based validation.
- People/debt, cheque, loan, SMS, reminders, reports, export, and backup features beyond the pre-migration safety copy.

The current SQLite provider is an unencrypted development/test configuration. This branch is not a production-ready encrypted database.
