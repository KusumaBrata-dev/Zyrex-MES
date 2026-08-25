# Phase 2 — Legacy Migration & Station Transactions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrasi baca-saja ke MES legacy via HTTP API (GetToken/CheckFlow/GetMesData), migrasi hybrid (master data + transaksi 12 bulan) dengan rekonsiliasi, dan API transaksi stasiun (scan + QC) dengan validasi routing serta broadcast realtime.

**Architecture:** `LegacyMesClient` typed HttpClient (tanpa metode tulis — dilarang by design) di Infrastructure; tabel staging `legacy_*` untuk snapshot mentah; service import transformasi ke tabel inti ber-flag `Source`; endpoint produksi append-only memakai rantai Unit/UnitTransaction yang sudah ada; broadcast lewat ProductionHub group `line:{code}`.

**Tech Stack:** .NET 10 · EF Core/Npgsql · xUnit + WebApplicationFactory + fake HttpMessageHandler · SignalR client test · System.Text.Json.

## Global Constraints

- Prasyarat sesi tes DB: `powershell -File scripts/wsl-db-restart.ps1` → harus `DB_READY`.
- Connection string dev tetap: `Host=localhost;Port=5433;Database=zyrex_mes;Username=postgres;Password=mes_dev_pwd` (di appsettings.Development.json saja).
- **LEGACY READ-ONLY**: kelas `LegacyMesClient` TIDAK BOLEH memiliki metode yang menghasilkan `UpdateInfo` atau payload tulis apa pun. Reviewer akan menolak bila ada.
- Kredensial legacy via konfigurasi env: `MES_LEGACY__URL`, `MES_LEGACY__USERID`, `MES_LEGACY__PASSWORD`, `MES_LEGACY__SNTYPE`. Dilarang hardcode/log nilai password.
- Port API dev 8080; semua timestamp UTC timestamptz; kode Inggris; commit conventional; coverage ≥80%.
- Nama file/konsep dikunci: `LegacyMesClient.cs`, `LegacyOptions.cs`, entitas staging `Legacy*`, endpoint `/api/production/scan`, `/api/quality/results`, `/api/migration/*`.
- Migrasi EF: `dotnet ef migrations add <Name> --project server/src/ZyrexMES.Infrastructure --startup-project server/src/ZyrexMES.Api` (dari folder `server\`).
- Semua tes baru masuk `server/tests/ZyrexMES.Api.Tests`.

---

### Task 1: Probe legacy endpoint + skema respon nyata + fixture

**Files:**
- Create: `docs/legacy/probe-legacy.ps1`
- Create: `server/tests/ZyrexMES.Api.Tests/Fixtures/legacy-getmesdata-sample.json`, `Fixtures/legacy-checkflow-sample.json`, `Fixtures/legacy-gettoken-sample.json`
- Create: `docs/legacy/LEGACY-SCHEMA.md`

**Interfaces:**
- Produces: fixture JSON autentik (bentuk nyata respons `APIResData`) yang menjadi kontrak mapper Task 2–6; dokumentasi skema; keputusan reachable/tidak.

- [ ] **Step 1: Tulis probe script**

`docs/legacy/probe-legacy.ps1`:
```powershell
# Read-only probe ke legacy MES API. Tidak pernah memanggil UpdateInfo.
param(
  [string]$Url    = $env:MES_LEGACY__URL,
  [string]$User   = $env:MES_LEGACY__USERID,
  [string]$Pass   = $env:MES_LEGACY__PASSWORD,
  [string]$SN     = "",
  [string]$Station = ""
)
if (-not $Url -or -not $User) { Write-Error "Set MES_LEGACY__URL / MES_LEGACY__USERID first."; exit 1 }
$hdr = @{ "Content-Type"="application/json"; "Accept"="application/json"; "X-Requested-With"="XMLHttpRequest"; "UserID"=$User; "Password"=$Pass }
function Post([string]$svc, $data) {
  $body = @{ RequestId=[guid]::NewGuid().ToString("N"); ServiceName=$svc; Language=""; ClientData=$data } | ConvertTo-Json -Compress
  Invoke-RestMethod -Uri $Url -Method Post -ContentType "application/json" -Headers $hdr -Body $body
}
$login = Post "GetToken" @{}
$login | ConvertTo-Json -Depth 6
$code = $login.APIResHeader.Code
if ($code -ne 0) { Write-Error "GetToken failed Code=$code"; exit 2 }
$token = $login.APIResData.token
$hdr.Authorization = $token
if ($SN -and $Station) {
  Post "CheckFlow" @{ SN=$SN; SNType="SN"; Station=$Station } | ConvertTo-Json -Depth 8
  Post "GetMesData" @{ SN=$SN; SNType="SN"; Station=$Station } | ConvertTo-Json -Depth 8
}
```

- [ ] **Step 2: Jalankan probe dari jaringan pabrik**

Run (butuh koneksi LAN pabrik): isi env lalu `probe-legacy.ps1 -SN <contoh-sn-valid> -Station <contoh-station>`.
Expected: dua blok JSON tersimpan. **Jika laptop tidak reach `192.168.1.245:8090`**: minta pengguna menjalankan probe/MESTools di PC pabrik dan tempelkan hasilnya; simpan sebagai fixture pada Step 3 tanpa mengubah struktur langkah lain.

- [ ] **Step 3: Simpan fixture**

Simpan output sebagai 3 file fixture di folder Fixtures (gettoken/checkflow/getmesdata). Redaksi nilai yang dianggap sensitif tanpa mengubah bentuk struktur (ganti nilai string dengan placeholder bertanda sama panjangnya bila perlu).

- [ ] **Step 4: Tulis LEGACY-SCHEMA.md**

Dokumentasikan field `APIResData` tiap service: nama field, tipe, contoh. Sumber kebenaran = fixture. Kosongkan bagian yang belum terkonfirmasi dengan label `UNVERIFIED` (Task 2+ wajib merujuk file ini).

- [ ] **Step 5: Commit**

```bash
git add docs/legacy server/tests
git commit -m "docs(legacy): live api probes, response fixtures, schema notes"
```

---

### Task 2: LegacyOptions + LegacyMesClient (read-only) + unit tests

**Files:**
- Create: `server/src/ZyrexMES.Infrastructure/Legacy/LegacyOptions.cs`, `LegacyMesClient.cs`
- Modify: `server/src/ZyrexMES.Api/appsettings.json` (section `Legacy` kosong), `appsettings.Development.json` (nilai dev), `Program.cs` (register client)
- Test: `server/tests/ZyrexMES.Api.Tests/LegacyMesClientTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed class LegacyOptions { public string Url {get;set;}=""; public string UserId{get;set;}=""; public string Password{get;set;}=""; public string SnType{get;set;}="SN"; public int TimeoutSeconds{get;set;}=20; }
  public record LegacyEnvelope(int Code, JsonElement Data, string Raw);
  public class LegacyMesClient {
     Task<LegacyEnvelope> GetTokenAsync(CancellationToken ct);
     Task<LegacyEnvelope> CheckFlowAsync(string sn, string station, CancellationToken ct);
     Task<LegacyEnvelope> GetMesDataAsync(string sn, string station, CancellationToken ct);
  }
  ```
  Register: `builder.Services.Configure<LegacyOptions>(builder.Configuration.GetSection("Legacy"))` + typed client base address dari options.
- Kontrak envelope: `Code` dari `APIResHeader.Code`; `Data` = elemen `APIResData`; `Raw` = body mentah.

- [ ] **Step 1: Failing tests (fake handler)**

`LegacyMesClientTests.cs`: fake `HttpMessageHandler` menyuntik JSON fixture (dari Task 1); verifikasi: (a) GetToken mengekstrak token & Code==0 path; (b) CheckFlow mengirim ServiceName benar + payload `{SN,SNType,Station}`; (c) timeout melempar `LegacyException` (wrapper) bukan raw HttpRequestException; (d) **tidak ada metode publik selain 3 di atas** (reflection test anti-regresi read-only).

- [ ] **Step 2: Run → FAIL** (kelas belum ada)

- [ ] **Step 3: Implementasi**

`LegacyException(string Message, int LegacyCode)` di file sama. Envelope parse: `JsonDocument` → header/data. Retry manual 3× backoff 500ms hanya untuk connect-failure/timeouts (bukan untuk Code!=0). Log tanpa body sensitif.

- [ ] **Step 4: Run → PASS**, lalu commit `feat(legacy): readonly mes client (token/flow/data) with envelope parsing`

---

### Task 3: Tabel staging legacy_* + entitas + migrasi

**Files:**
- Create: `server/src/ZyrexMES.Domain/Entities/Legacy/LegacyLineSnapshot.cs`, `LegacyStationSnapshot.cs`, `LegacyProductSnapshot.cs`, `LegacyRoutingSnapshot.cs`, `LegacyTransactionSnapshot.cs`
- Modify: `AppDbContext.cs` (+5 DbSet), `EntityConfigurations.cs`
- Migrasi: `AddLegacyStaging`
- Test: `server/tests/ZyrexMES.Api.Tests/LegacyStagingPersistenceTests.cs`

**Interfaces:**
- Produces (semua kolom teks fleksibel + jsonb mentah):
  ```
  LegacyLineSnapshot   { Id, LegacyCode, LegacyName, RawJson, ImportedAtUtc }
  LegacyStationSnapshot{ Id, LegacyLineCode, LegacyCode, LegacyName, ProcessType?, RawJson, ... }
  LegacyProductSnapshot{ Id, LegacySku, LegacyName, RawJson, ... }
  LegacyRoutingSnapshot{ Id, LegacySku, Sequence, LegacyStationCode, RequireLabel, RawJson, ... }
  LegacyTransactionSnapshot { Id, SN, StationCode, ResultChar(P/F), ScannedAtUtc, OperatorCode?, RawJson, ... }
  ```
  Unik: `(LegacyCode)` per tabel master; `(SN, StationCode, ScannedAtUtc)` pada transaksi.

- [ ] **Step 1:** Failing persistence test pola TraceabilityPersistenceTests (ctor cleanup sendiri: hapus snapshot milik prefix `LGCY-`). Run FAIL.
- [ ] **Step 2:** Entitas + config (RawJson → `jsonb`; maxlength longgar 128/64; unique index seperti atas) → migrasi `AddLegacyStaging` → run PASS ×2.
- [ ] **Step 3:** Commit `feat(staging): legacy snapshot tables for hybrid migration`

---

### Task 4: Import master data (staging → core)

**Files:**
- Create: `server/src/ZyrexMES.Infrastructure/Legacy/LegacyImportService.cs`
- Modify: `EntityConfigurations.cs` (tambah `Source` string default `"Manual"` pada Line/Station/Product/Routing — migrasi `AddSourceColumns`)
- Endpoint: `POST /api/migration/import-master` (Admin only, RequireRoles) → jalankan import asinkron-style sinkron (skala kecil) → return ringkasan jumlah baris
- Test: `server/tests/ZyrexMES.Api.Tests/LegacyMasterImportTests.cs`

**Interfaces:**
- Produces: `ImportSummary { LinesCreated, StationsCreated, ProductsCreated, RoutingsCreated, SkippedDuplicates }`. Mapping: staging→core memakai kode `LGCY-` prefix guard agar tak menimpa data manual. Routing dipetakan via station code lookup; station hilang → skip + counter `SkippedMissingStation`.

- [ ] **Step 1:** Failing test: seed staging snapshots in-memory → panggil service → assert core rows + idempotency (jalankan dua kali, kedua kali summary sama & row count tak bertambah).
- [ ] **Step 2:** Implementasi service + endpoint + migrasi kolom Source. Run PASS.
- [ ] **Step 3:** Commit `feat(migration): master data import staging->core with source flag`

---

### Task 5: Import transaksi 12 bulan (window)

**Files:**
- Create: `server/src/ZyrexMES.Infrastructure/Legacy/LegacyTransactionImporter.cs`
- Endpoint: `POST /api/migration/import-transactions` body `{ FromUtc, ToUtc }` (validasi: ToUtc≤now, window ≤366 hari) Admin only
- Test: `server/tests/ZyrexMES.Api.Tests/LegacyTransactionImportTests.cs`

**Interfaces:**
- Produces: `TransactionImportSummary { Fetched, UnitsCreated, TransactionsInserted, DuplicatesSkipped, Errors[] (maks 50 pertama) }`. Alur: enumerate SN unik dari `GetMesData` paging (loop sampai kosong / batas aman 10k call), upsert Unit bila belum ada, insert UnitTransaction `Result=Pass/Fail` mapping `ResultChar P/F`, sumber ditandai Notes prefix `LEGACY:`. Idempoten via unique (SN, StationCode, ScannedAtUtc).

- [ ] **Step 1:** Failing test dengan fake LegacyMesClient (interface `ILegacyMesClient` diekstrak agar mockable) — window kecil 3 SN.
- [ ] **Step 2:** Implementasi + endpoint; run PASS ×2 (idempoten).
- [ ] **Step 3:** Commit `feat(migration): 12-month transaction import with dedupe`

---

### Task 6: Rekonsiliasi (AC-07, AC-08)

**Files:**
- Create: `server/src/ZyrexMES.Api/Modules/Migration/ReconciliationEndpoints.cs`
- Test: `server/tests/ZyrexMES.Api.Tests/ReconciliationTests.cs`

**Interfaces:**
- Produces:
  - `GET /api/migration/reconciliation/report` → per tabel: `{ table, legacyCount(stage), coreCount, match }`
  - `POST /api/migration/reconciliation/sample` body `{ Count:int (default 100, maks 500) }` → ambil sampel acak SN staging, bandingkan field-by-field (SN/station/result/time) vs core; return `{ Sampled, Matched, Mismatches[≤20] }`
  - Keduanya role Supervisor/Admin.

- [ ] **Step 1:** Failing test: seed staging + core identik → report match=true; corekoreksi 1 field → mismatch terdeteksi di sample.
- [ ] **Step 2:** Implementasi (SQL set-based via EF LINQ; sampling `ORDER BY random() LIMIT n`). Run PASS.
- [ ] **Step 3:** Commit `feat(migration): reconciliation report + random sample verification`

---

### Task 7: Endpoint scan produksi + broadcast realtime

**Files:**
- Create: `server/src/ZyrexMES.Api/Modules/Production/ProductionEndpoints.cs`, `ScanRequest.cs`, `ScanResultBroadcaster.cs`
- Modify: `Program.cs`
- Test: `server/tests/ZyrexMES.Api.Tests/ScanEndpointTests.cs` (+ hub broadcast assertion via SignalR client test)

**Interfaces:**
- Produces:
  - `POST /api/production/scan` body `{ SerialNumber, StationId }` (Operator+) →
    - 200 `{ result:"PASS", unitId, transactionId, nextStationCode? }` + suara di sisi klien (Plan 3)
    - 422 `{ result:"REJECTED", reason }` untuk: SN tak dikenal / urutan routing salah / duplikat step yang sama
    - Insert `UnitTransaction(Result=Pass, ScannedAtUtc=UtcNow)` append-only + update `Unit.Status` Created→InProgress→Completed sesuai posisi step terakhir
    - Broadcast `ScanAccepted` / `ScanRejected` ke group line via ProductionHub ≤2 detik (ukur dalam tes)
  - `IScanResultBroadcaster.BroadcastAccepted/Rejected(...)` (interface untuk testability).

- [ ] **Step 1:** Failing tests: happy path (unit baru → first step OK), wrong-order rejected dengan pesan, duplicate rejected, unknown SN 422, broadcast event diterima subscriber test ≤2s.
- [ ] **Step 2:** Implementasi: validasi via query routing steps product unit; transaction save + broadcast after save; all in one SaveChanges scope untuk atomicity (broadcast setelah commit sukses).
- [ ] **Step 3:** Run PASS ×2; commit `feat(production): scan endpoint with routing validation + realtime broadcast`

---

### Task 8: QC submit (AC-04) + antrean repair

**Files:**
- Create: `server/src/ZyrexMES.Api/Modules/Quality/QualityEndpoints.cs`, `QcSubmitRequest.cs`
- Test: `server/tests/ZyrexMES.Api.Tests/QualityEndpointTests.cs`

**Interfaces:**
- Produces: `POST /api/quality/results` body `{ SerialNumber, StationId, Verdict:"Pass"|"Fail", NgCodeId?, Notes? }` (role Qa, Leader, Supervisor, Admin) →
  - Verdict Fail tanpa NgCodeId → **400** `{ error:"ng_code required for fail verdict" }` (AC-04)
  - Simpan QcResult + bila Fail buat Repair Status=Open (ProblemDescription=Notes)
  - Broadcast `ScanRejected`(fail) / `ScanAccepted` konsisten event dashboard.

- [ ] **Step 1:** Failing tests: pass OK; fail tanpa ng_code 400; fail dengan ng_code → QcResult + Repair Open tercipta; role operator 403.
- [ ] **Step 2:** Implementasi. Run PASS.
- [ ] **Step 3:** Commit `feat(quality): qc submit enforcing ng-code + repair queue`

---

### Task 9: Latency harness (AC-01) + indeks

**Files:**
- Test: `server/tests/ZyrexMES.Api.Tests/ScanLatencyTests.cs`
- Modify (bila perlu): migrasi `AddScanIndexes` (index tambahan bila EXPLAIN menunjukkan seq scan)

**Interfaces:**
- Produces: tes performa lokal: 60 scan berurutan pada dataset seeded; hitung p95 dari durasi HTTP; assert `p95 <= 500ms` (threshold AC-01). Jika gagal → tambah indeks (`unit_transactions(unit_id, scanned_at_utc)` sudah ada; kemungkinan tambah `stations(id)` trivial / `units(serial_number)` sudah unik) lalu ulangi.

- [ ] **Step 1:** Tulis harness (Stopwatch per call, percentile helper inline).
- [ ] **Step 2:** Jalankan; jika >500ms lakukan analisa EXPLAIN + tambah indeks + migrasi; ulangi hingga lulus 2× berturut-turut.
- [ ] **Step 3:** Commit `test(production): scan latency p95 harness (AC-01)`

---

### Task 10: Dokumentasi + tag fase

**Files:**
- Modify: `README.md` (bagian Migration & Production API singkat + contoh curl scan/QC)
- Create: `docs/plans/2026-08-24-phase2-summary.md` (ringkasan hasil + cara jalankan ETL berurutan: import-master → import-transactions → reconciliation)

**Interfaces:** —

- [ ] **Step 1:** Smoke ETL end-to-end di DB dev: seed staging via fixture → import-master → import-transactions (window 1 hari fiktif) → reconciliation match=true. Catat output ke summary doc.
- [ ] **Step 2:** Update README + summary; commit `docs(phase2): migration runbook and phase summary`; tag `phase2-migration-complete`.

---

## Peta Lanjutan

- **Plan 3 — Print Agent & Station Kiosk**: Windows service .NET + BarTender (AC-05), Next.js kiosk PWA + overlay SERVER OFFLINE (AC-13), suara OK/NG, logo Zyrex branding, OnMessageReceived JWT untuk WebSocket browser.
- **Plan 4 — Dashboard Realtime & Reports**: monitoring 9 line, yield/WIP/NG Pareto, Andon TV, export Excel/PDF, alert anomali threshold (AC-12).
- **Plan 5 — AI Assistant lokal**: Ollama + Qwen2.5-14B, RAG SOP pgvector (AC-10), NL→SQL guarded (AC-11), isolasi outbound (AC-09), analisa akar masalah repair.
