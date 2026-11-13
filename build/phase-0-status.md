# Phase 0 — Technical validation status

The repository has a platform-independent Core foundation, an EF Core SQLite relational model, an initial migration, and a migration startup service. The migration integration tests use SQLite. These changes do not satisfy the SQLCipher or cross-platform validation gates.

## Roadmap spikes

- [ ] S1 — EF Core + SQLite/SQLCipher on Android/iOS/Windows; encrypted CRUD, key handling, and migrations. The SQLite migration and startup lifecycle are implemented and tested, but SQLCipher, native binaries, secure key handling, and device-level validation remain open.
- [ ] S2 — MAUI RTL spike covering Shell, CollectionView, amount Entry, and charts. Shell and a basic RTL dashboard are scaffolded; device screenshots and the remaining controls are not validated.
- [ ] S3 — Persian calendar reference corpus (years 1200–1500) and round-trip property tests.
- [ ] S4 — Android SMS permission, delivery behavior, and anonymized bank fixtures.
- [ ] S5 — Local notifications on Android/iOS, including restrictive Android vendors.

## SQLCipher package decision

The previously common SQLitePCLRaw.bundle_e_sqlcipher package is deprecated. The maintained SQLitePCLRaw.provider.sqlcipher package targets official SQLCipher builds from Zetetic but does not include those native binaries. Provider selection and native-library licensing/distribution must be resolved and tested on Android and iOS before production encryption is claimed.

The current AppDbContext and migrations use the unencrypted Microsoft EF Core SQLite provider for development and migration verification only. They are not a production encrypted database configuration. No database key is generated or persisted by this project yet.

The initial PersianCalendar adapter remains a candidate implementation, not the final calendar engine; S3 must validate it against the project's reference corpus.
