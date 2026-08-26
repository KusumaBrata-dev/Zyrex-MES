# Phase 3 Summary — Kiosk & Print Agent (2026-08-24)

Branch `feature/phase3-kiosk-print` (dari master cdd830f). Tag: `phase3-kiosk-print-complete`.

## Hasil

| Area | Tes |
|---|---|
| Backend (`server/tests/ZyrexMES.Api.Tests`) | 85 |
| Print Agent (`agent/ZyrexMES.PrintAgent.Tests`) | 16 |
| Kiosk web unit (`web`, vitest) | 28 |
| E2E Playwright (`web/e2e`) | 2/2 |
| **Total** | **131** |

## Arsitektur Rantai Cetak

```
scan PASS (RoutingStep.RequireLabel=true)
  └─> PrintJobs row dibuat DALAM SaveChanges yang sama (atomic; unique UnitId+TemplateCode)
        └─> agent POST /api/print/jobs/claim?stationId=   (SELECT..FOR UPDATE SKIP LOCKED, ≤10 job, Status→Sent, Attempts++)
              └─> BartenderCliPrinter: tulis job-{guid}.json → bartend.exe
                    (timeout 60s → Kill entireProcessTree; retry ≤3 attempt di PollingService)
                    ├─ sukses            → ack(ok=true)  → Status=Printed
                    └─ gagal final (3×)  → ack(ok=false) → Status=Failed + server broadcast AlertRaised{print_failed}
```

- Ack bersifat idempoten untuk status terminal (Printed/Failed) — re-ack tidak memutasi dan tidak menggandakan alert.
- Jika ack(true) gagal setelah label tercetak, agent TIDAK mengirim ack(false) (mencegah cetak duplikat); job tetap `Sent` untuk follow-up manual.
- Alert selalu dari sisi server saat menerima ack(false) final — agent tidak punya channel alert.

## Komponen Baru

| Komponen | Lokasi | Catatan |
|---|---|---|
| Tabel + entitas PrintJobs | `server/src/ZyrexMES.Domain/Entities/PrintJob.cs`, migrasi `AddPrintJobs` | jsonb payload, unique (UnitId, TemplateCode) |
| Printing endpoints | `Modules/Printing/PrintingEndpoints.cs` | claim/ack, role baru `Agent` |
| Auto-create job | `Modules/Production/ProductionEndpoints.cs` | RequireLabel → job dalam commit yang sama |
| Alert broadcast | `IScanResultBroadcaster.BroadcastAlertAsync` | SignalR event `AlertRaised` |
| Reports API | `Modules/Reports/ReportsEndpoints.cs` | station-summary (WIB), ng-list, line-grid (fondasi AC-19) |
| Print Agent worker | `agent/ZyrexMES.PrintAgent/` | PollingService (PeriodicTimer), ApiClient (JWT in-memory, re-login 401 sekali), BartenderCliPrinter + IProcessRunner, Windows Service via AddWindowsService |
| Kiosk web | `web/` | Next.js App Router + React 19 + Tailwind 4; tema zred #C71C2E / zbright #FA1A3F; login shell; layar scan besar (auto-refocus, guard double-scan 500 ms); overlay PASS/REJECTED + WebAudio; DailySummary live; NG Report page; heartbeat 5s/abort 3s anti-flap 2 fail; OfflineGate global di root layout; AI chat placeholder (Plan 5) |
| E2E | `web/e2e/` | Playwright: happy path + bukti AC-13 (route abort /health) |

## Cara Menjalankan

**Backend**
```
powershell -File scripts/wsl-db-restart.ps1
dotnet run --project server/src/ZyrexMES.Api          # :8080
dotnet test server/tests/ZyrexMES.Api.Tests           # 85 tes
```

**Print Agent** (di PC stasiun; runbook lengkap: `docs/deploy/print-agent-deployment.md`)
```
dotnet publish agent/ZyrexMES.PrintAgent -c Release -r win-x64 --self-contained false
sc.exe create "ZyrexMES.PrintAgent" binPath= "<publish>\ZyrexMES.PrintAgent.exe" start= auto
npx playwright test   # bukan bagian agent — lihat web/
dotnet test agent/ZyrexMES.PrintAgent.Tests            # 16 tes
```

**Kiosk dev**
```
cd web
npm install
npm run dev                                            # :3000, proxy /api & /health → :8080
npm test                                               # 28 tes unit
```

**E2E** (butuh WSL zyrex-pg + .NET SDK; setup menyalakan stack sendiri)
```
cd web && npx playwright install chromium
npm run e2e                                            # 2 skenario
```

## Checklist Deployment Stasiun

- [ ] Instal .NET 10 runtime x64 + salin publish agent ke `C:\Apps\ZyrexMES.PrintAgent`
- [ ] Buat user `print-agent-*` role Agent (SQL + hash Argon2id — lihat runbook §2), smoke-test login
- [ ] Edit appsettings.json per stasiun: StationId unik, PrinterExePath BarTender, Templates, kredensial via env
- [ ] `sc.exe create` + failure recovery + start → RUNNING
- [ ] Validasi BarTender nyata: uji CLI manual 1 label, lalu scan uji → label keluar & job `Printed`
- [ ] Shortcut Edge kiosk mode di desktop/autostart:
      `msedge --kiosk http://localhost:3000/scan?stationId=<ID> --edge-kiosk-type=fullscreen`
- [ ] (Opsional) auto-login Windows + restart agent service on failure sudah dikonfigurasi

Runbook lengkap: **docs/deploy/print-agent-deployment.md**. Runbook ETL tidak berubah dari fase 2.

## Status Acceptance Criteria

| AC | Scope | Status |
|---|---|---|
| 01–04 | Fase 1 fondasi | CLOSED |
| 05 | Rantai cetak end-to-end (job queue + agent + BarTender) | **CLOSED** |
| 06 | Migrasi data legacy | CLOSED-PARTIAL (bulk live pull menunggu jendela produksi) |
| 07 | Traceability & audit | CLOSED |
| 08 | RBAC + audit log | CLOSED |
| 09 | AI assistant lokal (Ollama+RAG) | OPEN (Plan 5) |
| 10 | NL→SQL guarded | OPEN (Plan 5) |
| 11 | Analisa akar masalah repair | OPEN (Plan 5) |
| 12 | Dashboard monitoring realtime | OPEN (Plan 4) |
| 13 | Kiosk offline blocking overlay | **CLOSED** |
| 14–17 | Fase 2 (scan flow, quality, reconciliation, smoke) | CLOSED |
| 18 | Panel output/day, NG/day, yield% + report NG per tanggal | **CLOSED** |
| 19 | Grid semua stasiun realtime ≤2s | CLOSED-PARTIAL (endpoint line-grid siap; UI grid penuh di Plan 4) |

## Lanjutan

- **Plan 4 — Dashboard Monitoring (AC-12, AC-19 UI)**: grid tile ±100 stasiun realtime (konsumsi `/api/reports/line-grid` + SignalR per line), trend yield 7 hari, Pareto NG, Andon TV fullscreen, export Excel/PDF, alert anomali.
- **Plan 5 — AI Assistant lokal (AC-09/10/11)**: Ollama+Qwen2.5, RAG SOP pgvector ber sitasi, NL→SQL guarded read-only, ganti placeholder kiosk dengan chat hidup.
