# Phase 4 — Web Dashboard Monitoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dashboard web monitoring: grid realtime seluruh stasiun 9 line (AC-19), mode Andon TV per line, tren yield 7 hari, Pareto NG, alert anomali ambang terkonfigurasi (AC-12), ekspor Excel + print-to-PDF — sekaligus menutup backlog pra-pilot.

**Architecture:** Backend menambah modul Insights (trends/pareto), tabel `alerts` + `AnomalyDetectorService` (BackgroundService evaluasi aturan terkonfigurasi → persist + broadcast `AlertRaised` ke semua klien hub), rate-limiter login, dan endpoint ekspor ClosedXML. Frontend menambah area `/dashboard` (grid + charts + alerts) di aplikasi web existing; chart = komponen SVG hand-rolled tanpa dependensi baru.

**Tech Stack:** Existing stack · ClosedXML (Excel) · ASP.NET Core RateLimiter · SVG charts custom · Vitest · Playwright.

## Global Constraints

- Prasyarat sesi tes DB: `powershell -File scripts/wsl-db-restart.ps1` → `DB_READY`. Port API 8080 / web 3000 / DB 5433.
- Legacy READ-ONLY & zero-disturbance tetap berlaku.
- Warna brand: zred #C71C2E, zbright #FA1A3F. Status tile: active=green, idle=gray, down=red.
- Kode Inggris; commit conventional; coverage ≥80%; migrasi EF dari folder `server\`; solusi .sln klasik.
- Nama dikunci: tabel `Alerts`; endpoint `/api/insights/*`, `/api/alerts*`, `/api/export/*`; halaman web `/dashboard`, `/dashboard/tv/[lineCode]`.
- Anomaly thresholds dari config `Insights:{MinYieldPercent:85, YieldDropPercent:20, NgSpikePerHour:10, EvaluationIntervalMinutes:5}` — dipakai detector & diekspos via endpoint config-read untuk dashboard badge.
- Semua tes backend pola CustomWebAppFactory; tes web vitest mock-fetch; E2E Playwright reuse global-setup fase 3.

---

### Task 1: Backlog pra-pilot batch (hygiene)

**Files:**
- Modify: `Program.cs` (rate limiter + ValidateLifetime eksplisit), `Infrastructure/Legacy/Seeder.cs` (Distinct), `Infrastructure/Security/PasswordHasher.cs` (FormatException guard), `Api/Modules/Auth/AuthService.cs` (dummy verify timing), `Modules/MasterData/LinesEndpoints.cs` + `StationsEndpoints.cs` (PUT dup-check 409), `Common/AuditMiddleware.cs` (ILogger inject)
- Test: tambah kasus di `AuthEndpointTests.cs`, `MasterDataEndpointsTests.cs`, `AuditLogTests.cs`

**Interfaces:**
- Login rate-limit: FixedWindow 5 req/menit per-IP pada `/api/auth/login` → 429 saat lebih; tes: 6 panggilan cepat → yang ke-6 HTTP 429.
- Verify hash korup → return false (bukan throw); tes hash `"argon2id$$$"` → false.
- Timing: user tak dikenal tetap menjalankan Argon2 verify terhadap hash dummy statis (field private static readonly) sebelum return null.
- PUT lines/stations mengubah Code ke nilai yang sudah dipakai → 409 (bukan 500); tes masing-masing.
- AuditMiddleware: inject `ILogger<AuditMiddleware>`; catch audit-failure kini `_log.LogWarning(ex,...)`; tes tetap hijau (perilaku tak berubah).

- [ ] **Step 1:** Tulis semua failing tests dahulu (daftar di atas). Run FAIL.
- [ ] **Step 2:** Implementasi enam perbaikan. Run PASS ×2 penuh.
- [ ] **Step 3:** Commit `feat(api): pre-pilot hardening batch (rate limit, guards, honest comments)`

---

### Task 2: Tabel Alerts + detector anomali + endpoint (AC-12)

**Files:**
- Create: `Domain/Entities/Alert.cs`; `Api/Modules/Insights/InsightsEndpoints.cs`; `Api/Modules/Insights/AnomalyDetectorService.cs`
- Modify: AppDbContext (+DbSet+config), EntityConfigurations, Program.cs (register hosted service), migrasi `AddAlerts`
- Test: `server/tests/ZyrexMES.Api.Tests/AnomalyDetectionTests.cs`

**Interfaces:**
- `Alert { long Id; string Type; string Severity; string Message; string? LineCode; DateTime CreatedAtUtc; DateTime? AcknowledgedAtUtc; }`
- Detector (BackgroundService tiap EvaluationIntervalMinutes): per line hitung yield jam berjalan vs rata-rata jam sama 7 hari terakhir (fallback 60%) → drop ≥ YieldDropPercent → alert type `yield_drop`; yield absolut < MinYieldPercent → `low_yield` (dedupe: jangan duplikat type+line dalam 30 menit); NG count jam ini ≥ NgSpikePerHour → `ng_spike`. Insert Alert + broadcast hub `Clients.All.SendAsync("AlertRaised", dto)` via IHubContext.
- Endpoint (Supervisor/Admin untuk ack; semua role utk GET):
  - `GET /api/alerts?unackedOnly=true&take=50`
  - `POST /api/alerts/{id}/ack`
  - `GET /api/insights/thresholds` → config aktif (untuk badge UI)

- [ ] **Step 1:** Failing tests: seed transaksi historis menyebabkan yield_drop → Alert row tercipta oleh detector yang dipicu manual (`ExecuteOnceAsync()` public untuk testability — hosted wrapper memanggilnya); dedupe 30 menit; GET list + ack idempotent; broadcast terverifikasi via fake broadcaster pattern seperti Task 7 P2.
- [ ] **Step 2:** Implementasi + migrasi. Run PASS ×2.
- [ ] **Step 3:** Commit `feat(insights): alerts table, anomaly detector service, alert endpoints`

---

### Task 3: Trends + Pareto endpoints

**Files:**
- Modify: `Modules/Reports/ReportsEndpoints.cs` (tambah 2 route) atau file baru `Modules/Insights/InsightsChartEndpoints.cs`
- Test: tambah kasus `ReportsEndpointsTests.cs`

**Interfaces:**
- `GET /api/insights/yield-trend?days=7&lineCode?` → `{ points:[{ date, output, ng, yieldPercent|null }] }` (hari WIB, oldest→newest; hari tanpa data tetap muncul dengan 0/null)
- `GET /api/insights/ng-pareto?from=&to=&lineCode?` → `{ items:[{ ngCode, count }] desc }` (join QcResult Fail ↔ NgCodes)

- [ ] **Step 1:** Failing tests: seed 7 hari bervariasi (termasuk hari kosong) → bentuk points benar; pareto urutan desc + filter tanggal.
- [ ] **Step 2:** Implementasi set-based. Run PASS ×2.
- [ ] **Step 3:** Commit `feat(insights): yield trend and ng pareto endpoints`

---

### Task 4: Ekspor Excel (ClosedXML) + print-to-PDF

**Files:**
- Create: `Api/Modules/Reports/ExportEndpoints.cs`
- Modify: `Api.csproj` (+ClosedXML), README note
- Test: `server/tests/ZyrexMES.Api.Tests/ExportEndpointsTests.cs`

**Interfaces:**
- `GET /api/export/ng-list.xlsx?from=&to=&stationId?` (Leader+) → file .xlsx (content-type application/vnd.openxmlformats-officedocument.spreadsheetml.sheet) berisi kolom SN/Station/NGCode/Notes/Time; dibaca balik dengan ClosedXML dalam test untuk assert header+≥1 row.
- `GET /api/export/station-summary.xlsx?date=` → satu sheet ringkas per station.
- PDF = tombol Print browser (UI Task 8, print CSS); backend tidak membuat PDF (keputusan hemat-dependensi, dicatat di summary).

- [ ] **Step 1:** Failing tests (seed data → panggil → buka byte via ClosedXL → assert sheet/cell). Run FAIL.
- [ ] **Step 2:** Implementasi. Run PASS ×2.
- [ ] **Step 3:** Commit `feat(reports): excel exports for ng list and station summary`

---

### Task 5: Dashboard grid realtime (AC-19 UI)

**Files:**
- Create: `web/app/dashboard/page.tsx`, `components/dashboard/StationTile.tsx`, `components/dashboard/GridBoard.tsx`, `lib/useLiveEvents.ts`
- Test: vitest komponen + hook

**Interfaces:**
- useLiveEvents(lineCodes[]): satu HubConnection ke `/hubs/production` (JWT auth, LongPolling fallback pola SeedTests) → JoinLine tiap kode; expose counter event stream; re-connect otomatis.
- GridBoard: fetch `/api/reports/line-grid` awal + interval 30s fallback; event ScanAccepted/Rejected/QC-fail → increment tile terkait secara optimis (match by serialNumber→station tidak ada di payload; cukup increment tile station dari payload.stationCode) + lastEventAt update → status active.
- StationTile: kode stasiun besar, output/ng angka, strip warna status, klik → link detail ng-report?stationId.
- Halaman: header + selector line (All | per line) + link TV mode + badge thresholds dari `/api/insights/thresholds`.

- [ ] **Step 1:** Failing tests: render tile sesuai payload; event ScanAccepted meningkatkan output tile; offline gate tidak menghalangi dashboard (gate global sudah ada); selector memfilter line.
- [ ] **Step 2:** Implementasi. PASS ×2 + build sukses.
- [ ] **Step 3:** Commit `feat(web): realtime all-stations grid board (AC-19)`

---

### Task 6: Andon TV mode per line

**Files:**
- Create: `web/app/dashboard/tv/[lineCode]/page.tsx`, `components/dashboard/TvStats.tsx`
- Test: vitest render + interval refresh mock

**Interfaces:**
- Fullscreen dark theme: nama line raksasa, OUTPUT hari ini angka super besar, NG + YIELD%, daftar 5 event terakhir scrolling; refresh data tiap 10s (summary endpoint per line via station-summary agregat = gunakan line-grid filtered client-side); tombol exit TV mode; auto-hide cursor CSS.
- URL dipakai TV: `/dashboard/tv/E99`.

- [ ] **Step 1:** Failing tests render + refresh.
- [ ] **Step 2:** Implementasi. PASS ×2 + build.
- [ ] **Step 3:** Commit `feat(web): andon tv fullscreen mode per line`

---

### Task 7: Charts — trend & pareto (SVG hand-rolled)

**Files:**
- Create: `components/charts/YieldTrendChart.tsx`, `components/charts/NgParetoChart.tsx`, `app/dashboard/insights/page.tsx`
- Test: vitest proporsi/path sederhana (jumlah bar = items length; path line punya N titik)

**Interfaces:**
- YieldTrendChart: SVG polyline + area fill; sumbu label tanggal ringkas; titik null → gap.
- NgParetoChart: bar horizontal sorted desc + persentase kumulatif teks.
- Halaman insights: dua chart + export buttons (link `/api/export/*.xlsx`) + tombol Print (window.print) + print CSS `@media print` sembunyikan nav.

- [ ] **Step 1:** Failing tests → implement → PASS ×2 + build.
- [ ] **Step 2:** Commit `feat(web): svg yield trend, ng pareto charts, insights page with exports`

---

### Task 8: Panel alerts + ack (AC-12 UI)

**Files:**
- Create: `components/dashboard/AlertsPanel.tsx`, `lib/useAlerts.ts`
- Modify: dashboard layout (panel kanan collapsible), useLiveEvents → tangkap event `AlertRaised` → prepend list + toast merah singkat
- Test: vitest list render, ack tombol memanggil api + item hilang, event realtime prepend.

**Interfaces:**
- useAlerts: fetch `/api/alerts?unackedOnly=true` awal + interval 60s + expose ack(id).
- Badge jumlah unack di header dashboard.

- [ ] **Step 1:** Failing tests → implement → PASS ×2 + build.
- [ ] **Step 2:** Commit `feat(web): alerts panel with realtime feed and ack (AC-12 ui)`

---

### Task 9: E2E dashboard + verifikasi AC-12/19

**Files:**
- Create: `web/e2e/dashboard.spec.ts`
- Modify: `e2e/global-setup.ts` (seed tambahan opsional param)

**Interfaces:**
- Skenario: login supervisor-seeded (EnsureSeedUsers tak punya supervisor — buat via SQL seed helper sama pola e2e_op) → /dashboard grid tile muncul (≥1 line, station counter) → scan seeded SN via API langsung (request context POST scan pakai token operator) → ≤2s tile output naik (AC-19) → trigger alert: manipulasi? Gunakan detektor ExecuteOnceAsync lewat… tidak ada endpoint. SOLUSI: seed QcResult fail melimpah hari ini via SQL sehingga evaluasi manual? Detector hanya jalan interval background — untuk E2E cukup verifikasi endpoint alerts kosong→isi setelah POST manual insert alert via SQL lalu UI menampilkan setelah refresh 60s? Terlalu lambat. KEPUTUSAN: E2E membatasi ke AC-19 realtime + halaman insights render + export 200; AC-12 UI sudah tertutup vitest; AC-12 end-to-end ditandai CLOSED-PARTIAL (detector unit-tested + broadcast tested; full-loop manual saat pilot). Dokumentasikan di summary.

- [ ] **Step 1:** Tulis spec; jalankan dengan stack hidup → hijau; artifacts screenshot.
- [ ] **Step 2:** Commit `test(web): dashboard e2e realtime grid verification`

---

### Task 10: Docs + tag

**Files:**
- Modify: README.md (section Dashboard), 
- Create: `docs/plans/2026-08-24-phase4-summary.md` (status AC penuh, cara pakai dashboard, konfigurasi thresholds, catatan pilot)

- [ ] **Step 1:** Summary + README; commit `docs(phase4): dashboard guide and phase summary`; tag `phase4-dashboard-complete`.

---

## Peta Lanjutan

- **Plan 5 — AI Assistant lokal (AC-09/10/11)**: Ollama+Qwen2.5 server GPU, RAG SOP pgvector sitasi versi, NL→SQL guarded, analisa akar masalah repair; ganti placeholder kiosk.
