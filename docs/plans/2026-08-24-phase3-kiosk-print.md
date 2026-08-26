# Phase 3 — Print Agent & Station Kiosk Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rantai cetak label end-to-end (scan PASS ber-label → antrian job → Print Agent di PC stasiun → BarTender lokal → status balik + alert gagal) DAN aplikasi stasiun web kiosk lengkap (login operator, layar scan besar bersuara, ringkasan harian Output/NG/Yield, report NG per tanggal, placeholder chat AI, overlay merah SERVER OFFLINE) dengan branding Zyrex.

**Architecture:** Backend menambah modul Reports (agregasi SQL harian) dan PrintJobs (tabel antrian + poll/ack). Print Agent = .NET Worker Service yang jalan sebagai Windows Service di tiap PC stasiun: poll job baru → tulis data-file → panggil BarTender via CLI template-configurable (abstraksi `ILabelPrinter`, tanpa dependensi SDK) → ack status → retry 3× → broadcast `AlertRaised` saat gagal permanen. Kiosk = Next.js PWA (`web/`), Edge kiosk mode, heartbeat `/health`.

**Tech Stack:** Backend existing · Worker Service .NET 10 · Next.js 15 + React 19 + TailwindCSS 4 · Vitest + Testing Library · Playwright (E2E AC-13) · BarTender CLI (sudah terlisensi di PC stasiun).

## Global Constraints

- Prasyarat sesi tes DB: `powershell -File scripts/wsl-db-restart.ps1` → `DB_READY`. Port API 8080; DB 5433.
- **Prasyarat web**: Node.js ≥ 20 (`node --version` wajib diverifikasi di awal Task 6; BLOCKED bila tidak ada).
- **READ-ONLY legacy & zero-disturbance tetap berlaku** (tidak relevan di fase ini tapi tak boleh dilanggar).
- Warna brand Zyrex dari logo (`docs/brand/zyrex-logo.svg`): primary red `#C71C2E`, bright `#FA1A3F`; dark text `#111111`. Dilarang palet lain untuk elemen inti.
- Suara = **WebAudio synthesized** (tanpa file audio): OK = dua nada naik pendek; NG = buzz rendah. Fungsi util murni agar bisa dites.
- Print Agent TIDAK memuat SDK BarTender; hanya `Process.Start` dengan argumen template dari config (`Bartender:ExePath`, `ArgsTemplate`). Argumen default dicatat di README dan **divalidasi manual saat deployment** (Task 5) karena varian versi BarTender.
- Print jobs append-only log ke `audit_logs` via middleware existing; status job diubah lewat endpoint resmi saja.
- Coverage backend ≥80%; kode Inggris; commit conventional; solusi .sln klasik; migrasi EF dari folder `server\`.
- Nama dikunci: tabel `print_jobs`; endpoint `/api/reports/*`, `/api/print/*`; proyek `ZyrexMES.PrintAgent`; web di `web/`.
- Tes backend pola CustomWebAppFactory + EnsureSeedUsers + cleanup ctor idempotent.

---

### Task 1: Modul Reports backend (AC-18, fondasi AC-19)

**Files:**
- Create: `server/src/ZyrexMES.Api/Modules/Reports/ReportsEndpoints.cs`, `ReportDtos.cs`
- Test: `server/tests/ZyrexMES.Api.Tests/ReportsEndpointsTests.cs`

**Interfaces:**
- Produces (semua role login):
  - `GET /api/reports/station-summary?stationId=&date=yyyy-MM-dd` → `{ stationId, stationCode, output, ng, yieldPercent }`
    - output = COUNT UnitTransaction hari tsb (timezone lokal pabrik = WIB: konversi tanggal → rentang UTC [date 00:00 +7h, +24h))
    - ng = COUNT QcResult Verdict=Fail hari tsb; yieldPercent = round(100*(output-ng)/output,1), null bila output==0
  - `GET /api/reports/ng-list?date=&stationId?&page=1&pageSize=25` → `{ total, page, items:[{ sn, stationCode, ngCode, notes, checkedAtUtc }] }`
  - `GET /api/reports/line-grid` → `{ lines:[{ lineCode, stations:[{ stationId, stationCode, name, outputToday, ngToday, lastEventAtUtc? , status:"active"|"idle" }] }] }` (status idle bila lastEvent >30 menit)

- [ ] **Step 1: Failing tests** — seed hari ini: 3 tx Pass + 1 QC Fail di satu station; assert summary {output:3,ng:1,yield:66.7}; ng-list pagination + filter; line-grid mencakup semua line seeded + counter benar. Run FAIL (404).
- [ ] **Step 2:** Implementasi set-based LINQ (group by), timezone helper statis `WibRange(date)` di ReportDtos.cs. Run PASS ×2.
- [ ] **Step 3:** Commit `feat(reports): station summary, ng list, line grid endpoints`

---

### Task 2: Antrian print_jobs + pembuatan otomatis dari scan berlabel

**Files:**
- Create: `server/src/ZyrexMES.Domain/Entities/PrintJob.cs`
- Modify: `AppDbContext.cs` (+DbSet), `EntityConfigurations.cs`, `Modules/Production/ProductionEndpoints.cs` (buat job setelah commit bila step.RequireLabel)
- Migrasi: `AddPrintJobs`
- Create: `server/src/ZyrexMES.Api/Modules/Printing/PrintingEndpoints.cs`
- Test: `server/tests/ZyrexMES.Api.Tests/PrintJobsTests.cs`

**Interfaces:**
- Entitas: `PrintJob { long Id; int UnitId; int StationId; string TemplateCode; string PayloadJson; string Status; // Pending|Sent|Printed|Failed
    int Attempts; DateTime CreatedAtUtc; DateTime? CompletedAtUtc; }` unique `(UnitId, StationId)` partial? — gunakan unique `(UnitId, TemplateCode)` untuk cegah dobel.
- Endpoint agent (role khusus `Agent`: tambah role konstanta `Roles.Agent="Agent"` + policy):
  - `POST /api/print/jobs/claim?stationId=` → atomically ambil hingga 10 job Pending (UPDATE ... RETURNING via raw SQL atau transaction) → set Status=Sent, Attempts++ → return daftar
  - `POST /api/print/jobs/{id}/ack` body `{ ok:bool, error? }` → Printed | (Attempts<3 ? Pending : Failed + broadcast `AlertRaised{type:"print_failed", jobId, error}` via broadcaster existing)
- Scan PASS pada step `RequireLabel=true` → insert PrintJob(PayloadJson = {sn, productSku, stationCode, scannedAtUtc}, TemplateCode dari config `Printing:DefaultTemplate` default `"SN_LABEL"`) dalam SaveChanges yang sama.

- [ ] **Step 1:** Failing tests: scan berlabel → job tercipta 1× (scan ulang ditolak duplicate, tak nambah job); claim mengembalikan + set Sent; ack ok=true → Printed; ack fail ×3 → Failed + broadcast alert; RBAC role Agent.
- [ ] **Step 2:** Implementasi + migrasi. Run PASS ×2.
- [ ] **Step 3:** Commit `feat(printing): print job queue with claim/ack and auto-create on labeled scans`

---

### Task 3: Print Agent — scaffold worker + poll/ack loop

**Files:**
- Create: `agent/ZyrexMES.PrintAgent/ZyrexMES.PrintAgent.csproj`, `Program.cs`, `AgentOptions.cs`, `PollingService.cs`, `ApiClient.cs`
- Create: solution reference — tambah proyek ke `server/ZyrexMES.sln` folder solution virtual `agent`
- Test: `agent/ZyrexMES.PrintAgent.Tests/PollingServiceTests.cs` (xUnit + fake HttpMessageHandler)

**Interfaces:**
- `AgentOptions { ApiBaseUrl, StationId(int), PollIntervalSeconds=2, PrinterExePath, PrinterArgsTemplate, DataDir }` dari appsettings.json + env override.
- `ApiClient`: login operator-agent (username/password dari config; seed user `print-agent` dibuat manual via register flow admin di deployment — dokumentasikan) menyimpan JWT memori; `ClaimJobs()`/`AckJob(id, ok, err)`.
- `PollingService` (BackgroundService): tiap interval → claim → untuk tiap job: `ILabelPrinter.Print(payload,template)` → ack; exception → ack(false,error).
- Register Windows Service: `builder.Services.AddWindowsService()` + `UseWindowsService()` (package Microsoft.Extensions.Hosting.WindowsServices).

- [ ] **Step 1:** Scaffold: `dotnet new worker -n ZyrexMES.PrintAgent -o agent/ZyrexMES.PrintAgent` + sln add + tests project. Failing test PollingService: fake ApiClient + fake printer → job diproses & ack benar; printer throw → ack(false) dengan pesan.
- [ ] **Step 2:** Implementasi hingga PASS; `dotnet publish -c Release -r win-x64` sukses (verifikasi artefak exe).
- [ ] **Step 3:** Commit `feat(agent): print agent worker skeleton with poll/ack loop`

---

### Task 4: Print Agent — BarTender adapter + retry + alert

**Files:**
- Modify: `agent/ZyrexMES.PrintAgent/` — tambah `ILabelPrinter.cs`, `BartenderCliPrinter.cs`
- Test: `agent/ZyrexMES.PrintAgent.Tests/BartenderCliPrinterTests.cs`

**Interfaces:**
- `ILabelPrinter { void Print(string payloadJson, string templateCode); }`
- `BartenderCliPrinter(IOptions<AgentOptions>)`: tulis `DataDir\job-{guid}.json`; args = ArgsTemplate.Replace("{TemplatePath}", map templateCode→file btw dari config `Templates:{code}`) `.Replace("{DataFile}",...)`; `Process.Start(exe,args)` wait exit ≤60s; exitcode≠0 → throw `PrintException` (memicu retry di PollingService — implementasi retry: PollingService mencoba hingga 3 attempt sebelum ack(false)).
- Default ArgsTemplate (dokumentasi README, divalidasi saat deployment): `"/F=\"{TemplatePath}\" /P /D=\"{DataFile}\""`.

- [ ] **Step 1:** Failing tests dengan printer fiktif `EchoPrinter` (process stub): file data dibuat & terhapus setelahnya; argumen mengandung path; non-zero exit → PrintException; retry logic di service: 2 gagal → attempt ke-3 sukses → ack(true); 3 gagal → ack(false)+alert event tersedia via callback.
- [ ] **Step 2:** Implementasi. Run PASS. Publish ulang sukses.
- [ ] **Step 3:** Commit `feat(agent): bartender cli printer with 3-attempt retry and alert callback`

---

### Task 5: Deployment Print Agent + validasi BarTender nyata (manual runbook)

**Files:**
- Create: `docs/deploy/print-agent-deployment.md`

**Interfaces:** — (deliverable runbook + checklist)

- [ ] **Step 1:** Tulis runbook: sc.exe create command (binPath publish output), config appsettings.json per stasiun (StationId unik!), prasyarat BarTender (exe path umum `C:\Program Files\Seagull\BarTender\bartend.exe`), langkah uji cetak 1 label dengan template asli + cara mengganti ArgsTemplate bila sintaks versi BarTender beda, troubleshooting exit code.
- [ ] **Step 2:** Commit `docs(deploy): print agent installation and bartender validation runbook`. *(Eksekusi nyata di PC stasiun = pekerjaan deployment user, bukan sesi coding.)*

---

### Task 6: Kiosk — scaffold Next.js + tema Zyrex + auth shell

**Files:**
- Create: seluruh struktur `web/` (Next.js App Router, TS, Tailwind 4): `package.json`, `app/layout.tsx`, `app/page.tsx` (redirect ke /login atau /scan), `app/login/page.tsx`, `lib/api.ts` (fetch wrapper + token sessionStorage), `lib/sound.ts` (WebAudio ok/ng), `components/*` dasar, `.env.local.example` (NEXT_PUBLIC_API_BASE=http://localhost:8080)
- Test: vitest setup + `web/lib/__tests__/sound.test.ts`, `api.test.ts`

**Interfaces:**
- `sound.playOk()`: dua nada 880→1320Hz 90ms; `sound.playNg()`: square 180Hz 350ms. `api.login(u,p)` POST /api/auth/login → simpan token; `api.scan(...)` dsb membawa Authorization.
- Tema Tailwind: `--color-zred:#C71C2E; --color-zbright:#FA1A3F;` font system; komponen LogoHeader pakai inline SVG dari `docs/brand/zyrex-logo-white.svg` (copy ke `public/`).

- [ ] **Step 1:** Verifikasi `node --version` ≥20 (BLOCKED bila tidak). Scaffold `create-next-app@latest web --ts --tailwind --app --src-dir=false`. Install vitest+RTL. Konfigurasi proxy dev rewrite `/api/*` → API 8080.
- [ ] **Step 2:** Login page (form username/password → api.login → redirect /scan; error merah). Sound utils + tests. Layout dengan header logo + tombol logout. Run `npm run build` sukses + tests hijau.
- [ ] **Step 3:** Commit `feat(web): kiosk scaffold, zyrex theme, auth shell, webaudio sounds`

---

### Task 7: Kiosk — layar scan besar (inti operator)

**Files:**
- Create: `web/app/scan/page.tsx`, `components/ScanInput.tsx`, `components/ResultOverlay.tsx`, `components/DailySummary.tsx` (stub data dulu)
- Test: vitest komponen: Enter di input memanggil onSubmit; overlay PASS tampil hijau + playOk dipanggil; REJECTED merah + reason + playNg; input re-focus otomatis.

**Interfaces:**
- ScanInput: satu `<input>` fullscreen-width auto-focus, `onKeyDown Enter` → submit sekali (guard double-submit 500ms). Hasil 200 → overlay hijau 1.5s (SN besar, next station) + playOk + refresh DailySummary; 422 → overlay merah sampai dismiss-tap (reason besar) + playNg; error lain → toast kecil.

- [ ] **Step 1:** Failing component tests → implement → PASS; `npm run build` OK.
- [ ] **Step 2:** Commit `feat(web): big scan screen with pass/reject overlays and sounds`

---

### Task 8: Kiosk — DailySummary (AC-18) + halaman NG Report

**Files:**
- Modify: `components/DailySummary.tsx` (real data), Create: `web/app/ng-report/page.tsx`
- Test: vitest mock fetch: summary render angka; yield null → "—"; ng-report filter tanggal + pagination tombol.

**Interfaces:**
- DailySummary: 3 kartu besar (OUTPUT / NG / YIELD%) dari `GET /api/reports/station-summary?stationId=<dari login response? gunakan query config>` refresh tiap scan sukses + interval 60s.
- NG Report page: date input (default hari ini) + table SN/station/ngCode/notes/waktu + paging.

- [ ] **Step 1:** Tests fail → implement → PASS; build OK.
- [ ] **Step 2:** Commit `feat(web): daily summary cards and ng report page`

---

### Task 9: Kiosk — AI chat placeholder + SERVER OFFLINE overlay (AC-13)

**Files:**
- Create: `components/AiChatPlaceholder.tsx`, `hooks/useServerHeartbeat.ts`, `components/OfflineOverlay.tsx`
- Test: heartbeat mock fetch timeout → offline true; pulih → false; overlay render & z-block saat offline; chat placeholder disabled note.

**Interfaces:**
- useServerHeartbeat: ping `${API}/health` tiap 5s (AbortController 3s); expose `offline:boolean`.
- OfflineOverlay: fixed inset-0 z-[100] bg `#C71C2E` teks putih besar "SERVER OFFLINE — HUBUNGI LEADER"; pointer-events block global (render di root layout conditional); hilang otomatis saat online.
- AiChatPlaceholder: panel samping collapsible, input disabled + badge "AI — PLAN 5".

- [ ] **Step 1:** Tests fail → implement → PASS; build OK.
- [ ] **Step 2:** Commit `feat(web): server-offline blocking overlay and ai chat placeholder`

---

### Task 10: E2E Playwright bukti AC-13 + alur utuh

**Files:**
- Create: `web/e2e/kiosk.spec.ts`, `playwright.config.ts`
- Modify: `web/package.json` scripts e2e

**Interfaces:**
- Skenario: (a) login → scan SN seeded → overlay hijau + summary bertambah; (b) matikan backend (hanya bisa di env dev: stop docker? NO — cukup intercept route abort via playwright `context.route('**/health', abort)`) → overlay SERVER OFFLINE muncul ≤10s; pulihkan → hilang. (c) screenshot disimpan `e2e/artifacts/`.

- [ ] **Step 1:** Install playwright + chromium; tulis spec; jalankan `npx playwright test` hijau (backend harus running: dokumen langkah start di README web section).
- [ ] **Step 2:** Commit `test(web): e2e kiosk flows incl. ac13 offline overlay proof`

---

### Task 11: Dokumentasi fase + tag

**Files:**
- Modify: `README.md` (section Kiosk & Print Agent quick start)
- Create: `docs/plans/2026-08-24-phase3-summary.md` (arsitektur rantai cetak, runbook deployment kiosk Edge kiosk-mode: `msedge --kiosk url --edge-kiosk-type=fullscreen`, status AC)

**Interfaces:** —

- [ ] **Step 1:** Summary + README; commit `docs(phase3): kiosk deployment guide and phase summary`; tag `phase3-kiosk-print-complete`.

---

## Peta Lanjutan

- **Plan 4 — Dashboard Monitoring (AC-12, AC-19 UI)**: grid tile ±100 stasiun realtime (konsumsi `/api/reports/line-grid` + subscribe SignalR group per line), trend yield 7 hari, Pareto NG, Andon TV fullscreen per line, export Excel/PDF, alert anomali threshold.
- **Plan 5 — AI Assistant lokal (AC-09/10/11)**: Ollama+Qwen2.5 di server, RAG SOP pgvector dengan sitasi, NL→SQL guarded read-only, analisa akar masalah repair; ganti placeholder kiosk dengan chat hidup.
