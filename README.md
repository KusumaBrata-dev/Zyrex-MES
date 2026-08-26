# Zyrex MES Production System

Sistem Manufacturing Execution System (MES) untuk PT Zyrexindo Mandiri Buana Tbk.
Fase 1: fondasi API .NET (minimal API + PostgreSQL + pgvector).

## Quick Start

1. Prasyarat: .NET SDK 10, Docker (dev: engine di WSL2), Git.
2. `docker compose up -d db`  (PostgreSQL 16 + pgvector di localhost:5433)
3. `dotnet ef database update --project server/src/ZyrexMES.Infrastructure --startup-project server/src/ZyrexMES.Api`
4. `dotnet run --project server/src/ZyrexMES.Api`  → http://localhost:8080/swagger
5. Tes: `dotnet test server/tests/ZyrexMES.Api.Tests`

Akun awal dibuat via fixture seeding saat test run; instruksi seed manual menyusul di Plan 2.

Dev DB via WSL: powershell -File scripts/wsl-db-restart.ps1

### Troubleshooting

- **Port 8080 dipakai proses lain** → matikan proses pemilik port atau ubah `applicationUrl` di `server/src/ZyrexMES.Api/Properties/launchSettings.json`.
- **DB tidak reachable (`connection refused` di 5433)** → pastikan kontainer `db` hidup (`docker compose ps`); pada mesin dev ini gunakan skrip WSL di atas untuk siklus DB.
- **Migrasi gagal saat startup** → migrasi dijalankan otomatis oleh seeder/API; jalankan manual langkah 3 bila perlu memaksa.

## Prasyarat

- [.NET SDK 10](https://dotnet.microsoft.com/download)
- Docker engine (dev berjalan di WSL2)

## Menjalankan Database (PostgreSQL + pgvector)

```bash
docker compose up -d db
```

Alternatif tanpa compose file:

```bash
docker run -d --name zyrex-pg \
  -e POSTGRES_DB=zyrex_mes \
  -e POSTGRES_USER=postgres \
  -e POSTGRES_PASSWORD=mes_dev_pwd \
  -p 5433:5432 \
  pgvector/pgvector:pg16
```

Catatan: port host `5433` dipakai karena `5432` sudah digunakan stack lain di mesin dev ini.

## Menjalankan API

```bash
dotnet run --project server/src/ZyrexMES.Api
```

- Health check: `GET http://localhost:8080/health` → `200 {"status":"ok"}`
- Swagger UI tersedia pada route default (`/swagger`) saat aplikasi berjalan.

## Menjalankan Tes

```bash
dotnet test server/tests/ZyrexMES.Api.Tests -v n
```

Tes health endpoint tidak membutuhkan database.

## Migration & Production API

Semua endpoint butuh JWT dari `POST /api/auth/login` (contoh di bawah memakai
curl + jq). Kredensial legacy MES dibaca dari env — jangan pernah hardcode:

```bash
export MES_LEGACY__URL="http://192.168.1.245:8090/API/TE/PostData"
export MES_LEGACY__USERID="..."     # dari mescfg.ini
export MES_LEGACY__PASSWORD="..."   # hash-hex32 dari sumbernya
```

Alur migrasi (detail lengkap: `docs/plans/2026-08-24-phase2-summary.md`,
kontrak API legacy: `docs/legacy/LEGACY-API.md` + `LEGACY-SCHEMA.md`):

```bash
TOKEN=$(curl -s -X POST localhost:8080/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"..."}' | jq -r .token)

# 1) master data dari tabel staging (idempoten, kode LGCY-*, Source=Legacy)
curl -X POST localhost:8080/api/migration/import-master -H "Authorization: Bearer $TOKEN"

# 2) transaksi 12 bulan dari staging (window maks 366 hari per panggilan)
curl -X POST localhost:8080/api/migration/import-transactions \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"fromUtc":"2025-09-01T00:00:00Z","toUtc":"2026-08-25T00:00:00Z","source":"Staging"}'

# 3) verifikasi rekonsiliasi (wajib match=true sebelum live pull)
curl localhost:8080/api/migration/reconciliation/report -H "Authorization: Bearer $TOKEN"
curl -X POST localhost:8080/api/migration/reconciliation/sample \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"count":100}'
```

Live pull dari server legacy (`"source":"LegacyApi"`) hanya berjalan bila body
memakai `"confirmLive":true` DAN env `MES_LEGACY_LIVE_APPROVED=true` berjalan
di proses API; selain itu HTTP 409. ZERO-DISTURBANCE: hanya service read
(GetToken/CheckFlow/GetMesData); `UpdateInfo` tidak pernah dipanggil.

Scan produksi & QC (Operator+/QA+), broadcast realtime ke `/hubs/production`
(group `line:{code}`, event `ScanAccepted`/`ScanRejected`):

```bash
curl -X POST localhost:8080/api/production/scan \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"serialNumber":"SN-0001","stationId":1}'
# → 200 {"result":"PASS","unitId":..,"transactionId":..,"nextStationCode":"ST20"}
# → 422 {"result":"REJECTED","reason":"routing order violation: expected station ST10"}

curl -X POST localhost:8080/api/quality/results \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"serialNumber":"SN-0001","stationId":1,"verdict":"Fail","ngCodeId":3,"notes":"LCD crack"}'
# → 200 {"result":"FAIL","qcResultId":..,"repairId":..}   (AC-04: Fail wajib ngCodeId)
```

## Struktur Solusi

```
server/
├── ZyrexMES.sln
├── src/
│   ├── ZyrexMES.Api/            # ASP.NET Core minimal API (entry point)
│   ├── ZyrexMES.Domain/         # Entitas & aturan domain
│   └── ZyrexMES.Infrastructure/ # EF Core, Npgsql, persistensi
└── tests/
    └── ZyrexMES.Api.Tests/      # Tes integrasi (WebApplicationFactory)
```
