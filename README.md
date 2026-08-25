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
