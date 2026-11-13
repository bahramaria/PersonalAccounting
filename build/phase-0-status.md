# Phase 0 — Technical validation status

This repository currently contains the first Core foundation only. The following roadmap spikes remain release gates and must be completed before committing to the full mobile implementation:

- [ ] S1 — EF Core + SQLite/SQLCipher on Android and iOS; encrypted CRUD and migrations.
- [ ] S2 — MAUI RTL spike covering Shell, CollectionView, amount Entry, and charts.
- [ ] S3 — Persian calendar reference corpus (years 1200–1500) and round-trip property tests.
- [ ] S4 — Android SMS permission, delivery behavior, and anonymized bank fixtures.
- [ ] S5 — Local notifications on Android/iOS, including restrictive Android vendors.

The initial Domain and Localization projects are intentionally platform-independent so their tests can run on Linux without the MAUI workload. The current PersianCalendar adapter is a candidate implementation, not yet accepted as the final calendar engine; S3 must validate it against the project's reference corpus.

No production database provider, SQLCipher key storage, SMS reader, reminder scheduler, or MAUI UI is claimed complete in this commit.
