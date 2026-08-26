# Plan 2 — Migration & Station Transactions: Ringkasan Fase

Tanggal selesai: 2026-08-25 · Branch: `feature/phase2-migration` · Tag: `phase2-migration-complete`

## Hasil Fase

| # | Deliverable | Commit | Bukti |
|---|---|---|---|
| 1 | Probe legacy + fixture + LEGACY-SCHEMA | 0d07e96 | samples/, Fixtures/ copy-to-output |
| 2 | LegacyOptions + LegacyMesClient (READ-ONLY) | fcc30c5 | 6 unit test (fake handler) |
| 3 | 5 tabel staging legacy_* (jsonb) + migrasi | d83bcda | persistence test ×2 idempoten |
| 4 | LegacyImportService + import-master + kolom Source | a6d581c | 5 test (idempoten, RBAC) |
| 5 | ILegacyMesClient + LegacyTransactionImporter | 002eb26 | 4 test (window/dedupe/409) |
| 6 | ReconciliationEndpoints (report + sample) | 10602aa | 7 test |
| 7 | POST /api/production/scan + SignalR broadcast | f226cb5 | 8 test (termasuk hub ≤2s) |
| 8 | POST /api/quality/results (AC-04) + repair queue | 22d7753 | 9 test |
| 9 | Latency harness p95 (AC-01) + duplicate guard | 80f9154 | p95 58.8/63.2 ms; guard 23505→422 |
| 10 | Smoke E2E + runbook + tag | (commit ini) | Phase2SmokeTests |

Full suite akhir: **68/68 PASS** (dua run beruntun).

## Smoke ETL End-to-End (Phase2SmokeTests)

Alur satu tes: seed staging (3 line / 6 station / 4 product+routing / 10 tx) →
`POST /api/migration/import-master` (3/6/4 dibuat, 8 routing steps) →
`POST /api/migration/import-transactions` window 1 hari (fetched=10, units=5,
inserted=10) → `GET /api/migration/reconciliation/report` (5/5 match=true) →
`POST .../sample {count:10}` (matched=10) → scan happy path PASS → QC fail
dengan ng_code → Repair Open tercipta. **ZERO-DISTURBANCE**: tidak ada panggilan
ke server legacy; staging di-seed lokal.

## Runbook ETL Produksi (urutan wajib)

> Prasyarat: env `MES_LEGACY__URL`, `MES_LEGACY__USERID`, `MES_LEGACY__PASSWORD`
> terisi (kredensial dari mescfg.ini; jangan pernah hardcode/log). Semua endpoint
> di bawah butuh JWT (login `/api/auth/login`).

1. **Import master data** — `POST /api/migration/import-master` (Admin).
   Sumber: tabel staging. Idempoten; kode inti diberi prefix `LGCY-`, baris
   ditandai `Source='Legacy'`. Ulangi sampai `skippedDuplicates` stabil.
2. **Import transaksi 12 bulan** — `POST /api/migration/import-transactions`
   body `{ "fromUtc": "...", "toUtc": "...", "source": "Staging" }` (Admin).
   Window maks 366 hari per panggilan; ulangi per bulan. Idempoten via
   unique (SN, StationCode, ScannedAtUtc); error per-baris masuk `errors[]`.
3. **Verifikasi rekonsiliasi** — `GET /api/migration/reconciliation/report`
   dan `POST /api/migration/reconciliation/sample {count:100}` (Supervisor/Admin).
   Wajib semua `match=true` dan sample `matched=sampled` sebelum lanjut.
4. **(Opsional, terkendali ganda) Live pull** — `source:"LegacyApi"` hanya jalan
   bila body `confirmLive:true` DAN env `MES_LEGACY_LIVE_APPROVED=true`;
   selain itu 409. Hanya READ service (GetMesData); jalankan di jendela sepi
   dengan persetujuan pemilik legacy. UpdateInfo tidak pernah dipanggil.
5. **Validasi operasional** — scan produksi (`POST /api/production/scan`) dan
   QC (`POST /api/quality/results`) aktif; dashboard menerima event
   ScanAccepted/ScanRejected via `/hubs/production`.

## Status Acceptance Criteria

| AC | Deskripsi | Status |
|---|---|---|
| AC-01 | Latensi scan p95 ≤ 500 ms | ✅ Task 9 (58.8/63.2 ms terukur, harness permanen) |
| AC-04 | QC Fail wajib ng_code | ✅ Task 8 (400 pesan persis, test khusus) |
| AC-07 | Rekonsiliasi row-count | ✅ Task 6 (report 5 pasangan) |
| AC-08 | Sampling 100 SN verifikasi field | ✅ Task 6 (sample acak ≤500, mismatch ≤20) |
| AC-05/06/13 | Print agent, kiosk PWA, overlay offline | ⏳ Plan 3 |
| AC-09..11 | Isolasi outbound, RAG SOP, NL→SQL guarded | ⏳ Plan 5 |
| AC-12 | Dashboard realtime & reports | ⏳ Plan 4 |

## Batasan yang Disengaja

- Import live (LegacyApi) tidak pernah diuji terhadap server nyata sesuai
  kontrak ZERO-DISTURBANCE; gerbang ganda (body + env) sudah terpasang.
- Duplicate guard DB memakai triple (unit, station, scanned_at_utc), bukan
  (unit, station), agar multi-transaksi legacy per pasangan tetap sah;
  rescan same-station tetap ditahan app-level check.
