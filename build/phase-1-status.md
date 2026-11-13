# Phase 1 — Core implementation status

## Implemented

- .NET 10 solution foundation with Domain, Application, Infrastructure, and Localization projects.
- Currency-aware Money, CurrencyCode, Account, and balanced double-entry LedgerTransaction.
- Manual transactions are confirmed when created; SMS/import sources remain unconfirmed until explicitly confirmed.
- EF Core SQLite model for Accounts, Transactions, and Postings.
- Account application service:
  - Create asset, liability credit-card, income-category, and expense-category accounts.
  - Support a maximum of two category levels.
  - Create an opening-balance transaction atomically with the account.
  - Rename and archive accounts.
  - Calculate account balances from postings in a grouped query.
- Transaction application service:
  - Expense, income, and transfer.
  - Validate account status, account/category types, currency equality, positive amount, and transfer source/destination inequality.
  - Persist every operation as a balanced ledger transaction.
- EF repository implementation and SQLite integration tests.

## Not yet implemented

- EF Core migration files and migration lifecycle.
- Production SQLCipher provider, native libraries, key storage, and encrypted CRUD validation.
- Default Persian category/system-account seeding.
- MAUI application project and RTL UI.
- People/debt, cheque, loan, SMS, reminders, reports, export, and backup features.
- Android/iOS platform validation.

The current SQLite provider is an unencrypted development/test configuration. This branch does not yet represent a runnable MAUI application or a production-ready encrypted database.
