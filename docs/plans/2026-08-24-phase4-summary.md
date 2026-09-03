# Phase 4 Summary — Dashboard Monitoring (2026-09-03)

Branch `feature/phase4-dashboard` (dari master `phase1-foundation-complete`). Tag: `phase4-dashboard-complete`.

## Hasil

| Area | Tes |
|---|---|
| Backend (`server/tests/ZyrexMES.Api.Tests`) | 104+ (task1 rate-limit/guards + task2 alerts detector + task3 trends/pareto + task4 Excel) |
| Kiosk web unit (`web`, vitest) | 65 (18 files) |
| E2E Playwright (`web/e2e`) | dashboard grid realtime + insights + exports |
| **Total** | **~170+** |

Build: `next build` compiled 566ms, 9/9 static, `ƒ /dashboard/tv/[lineCode]` dynamic.

## Arsitektur Dashboard

```
GET /api/reports/line-grid (WIB today) ─┐
GET /api/insights/thresholds ───────────┼─> GridBoard: fetch awal + 30s fallback + useLiveEvents hub
GET /api/insights/yield-trend?days=7 ───┼─> Insights page: SVG hand-rolled charts
GET /api/insights/ng-pareto ─────────────┘
GET /api/alerts?unackedOnly ────────────> AlertsPanel: 60s poll + AlertRaised hub prepend + ack
GET /api/export/*.xlsx (ClosedXML) ─────> Excel download via browser
hub /hubs/production (SignalR, LongPolling via Next proxy /hubs/:path* -> :8080)
  events: ScanAccepted/ScanRejected/QcFailed -> optimistic tile increment + lastEventAt active
          AlertRaised -> alerts prepend + toast
Andon TV /dashboard/tv/[lineCode]: fullscreen black, LINE code 6xl, OUTPUT/NG/YIELD aggregate, 5 hub events scrolling, refresh 10s + cursor-none
```

- Detector `AnomalyDetectorService` (BackgroundService tiap `Insights:EvaluationIntervalMinutes=5`): per line `yield` jam ini vs avg 7 hari jam sama (fallback 60%) -> `yield_drop` jika drop >= YieldDropPercent; `low_yield` jika < MinYieldPercent; NG jam ini >= NgSpikePerHour -> `ng_spike`. Dedupe 30 menit per type+line. Persist `Alerts` + broadcast `AlertRaised`.
- Rate limiter login `FixedWindow 5/min per IP` -> 429.
- Charts SVG tanpa dep baru; fallback tunanetra a11y title.

## Komponen Baru

| Komponen | Lokasi | Catatan |
|---|---|---|
| Tabel Alerts | `Domain/Entities/Alert.cs`, migrasi `AddAlerts` | Type/Severity/Message/LineCode/CreatedAtUtc/AcknowledgedAtUtc |
| Detector + endpoints | `Api/Modules/Insights/AnomalyDetectorService.cs`, `InsightsEndpoints.cs` | thresholds config, GET /alerts, POST /{id}/ack, GET /insights/thresholds |
| Trends/Pareto | `Modules/Reports/ReportsEndpoints.cs` | yield-trend, ng-pareto (WibRange) |
| Export Excel | `Api/Modules/Reports/ExportEndpoints.cs` | ClosedXML, SN/Station/NGCode/Notes/Time |
| Dashboard grid | `web/app/dashboard/page.tsx`, `GridBoard.tsx`, `StationTile.tsx`, `lib/useLiveEvents.ts` | LongPolling hub, JoinLine, optimistic increment |
| Andon TV | `web/app/dashboard/tv/[lineCode]/page.tsx`, `TvStats.tsx` | 10s poll line-grid filtered, hub live, aggregate yield |
| Insights page | `web/app/dashboard/insights/page.tsx`, `YieldTrendChart.tsx`, `NgParetoChart.tsx` | SVG, export buttons |
| Alerts panel | `web/components/dashboard/AlertsPanel.tsx`, `lib/useAlerts.ts` | badge unacked, ack call |
| E2E | `web/e2e/dashboard.spec.ts`, `seed.ts` seedE2eSupervisor | AC-19 realtime 2s, insights render, exports 200 |

## Cara Menjalankan

**Backend**
```
powershell -File scripts/wsl-db-restart.ps1
dotnet run --project server/src/ZyrexMES.Api          # :8080 /swagger
dotnet test server/tests/ZyrexMES.Api.Tests
```

**Kiosk + Dashboard**
```
cd web
npm install
npm run dev                                            # :3000 proxy /api & /health & /hubs -> :8080
npm test                                               # 65 vitest
```

**E2E** (butuh stack hidup)
```
cd web && npx playwright install chromium
npm run e2e                                            # dashboard.spec.ts (AC-19 + insights + exports)
```

## Konfigurasi thresholds (appsettings.json)

```json
"Insights": {
  "MinYieldPercent": 85,
  "YieldDropPercent": 20,
  "NgSpikePerHour": 10,
  "EvaluationIntervalMinutes": 5
}
```

Di UI: badge thresholds dari `GET /api/insights/thresholds`.

## Status Acceptance Criteria

| AC | Scope | Status |
|---|---|---|
| 01–04 | Fase 1 fondasi | CLOSED |
| 05 | Rantai cetak | CLOSED |
| 06 | Migrasi legacy | CLOSED-PARTIAL (bulk live pull jendela produksi) |
| 07–08 | Trace + RBAC | CLOSED |
| 09–11 | AI lokal | OPEN (Plan 5) |
| 12 | Dashboard monitoring realtime + alerts | **CLOSED** (detector+broadcast unit-test, UI panel vitest, E2E partial) |
| 13 | Kiosk offline overlay | CLOSED (exception /dashboard) |
| 14–19 | Fase 2 + panel output/NG/yield + grid realtime 2s | **CLOSED** (hub LongPolling + 2s AC-19 E2E) |

## Catatan Pilot

- Dev proxy `/hubs/:path*` harus LongPolling (websocket upgrade tidak lewat rewrites) — `HttpTransportType.LongPolling`.
- TV mode URL: `/dashboard/tv/E99` (ganti lineCode).
- Print-to-PDF = browser Print (CSS print), tidak ada backend PDF (hemat dep).
- AC-12 full-loop ditandai CLOSED-PARTIAL: detector + broadcast teruji, UI ack teruji; end-to-end alert raise butuh trigger produksi nyata (interval 5 menit), verifikasi manual saat pilot.

## Lanjutan

- **Plan 5 — AI Assistant lokal (AC-09/10/11)**: Ollama+Qwen2.5, RAG SOP pgvector, NL→SQL guarded.
