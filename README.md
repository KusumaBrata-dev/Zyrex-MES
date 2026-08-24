# Zyrex MES Production System

Sistem Manufacturing Execution System (MES) untuk PT Zyrexindo Mandiri Buana Tbk.
Fase 1: fondasi API .NET (minimal API + PostgreSQL + pgvector).

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

- Health check: `GET http://localhost:<port>/health` → `200 {"status":"ok"}`
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
