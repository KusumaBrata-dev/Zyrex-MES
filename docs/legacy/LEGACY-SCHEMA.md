# Legacy MES Response Schema (hasil live probe 2026-08-24)

> Sumber kebenaran: sample live di [`samples/`](samples/) + envelope terkonfirmasi
> di [`LEGACY-API.md`](LEGACY-API.md) (commit a0ecc0a). Fixture test:
> `server/tests/ZyrexMES.Api.Tests/Fixtures/`.
>
> **Status legenda:**
> - **TERKONFIRMASI** — field terlihat langsung pada respons live.
> - **UNVERIFIED-PENDING-SAMPLE** — keberadaan/bentuk diperkirakan dari
>   disassembly (`MESTools.dis.txt`), belum ada sample sukses nyata.
>   Task 2+ WAJIB merujuk file ini sebelum menulis mapper.

## Konvensi umum (TERKONFIRMASI via live probe)

| Aturan | Detail |
|---|---|
| `Code` adalah STRING | 6 digit, `"000000"` = sukses; contoh error terverifikasi `"000500"`. **Jangan parse sebagai integer.** |
| `Desc` bisa mengandung CJK | Contoh live: `报文APIReqData内容不能为空` (APIReqData kosong), pesan SN tidak ditemukan. Aman-kan encoding UTF-8 saat log/parse. |
| Token GetToken | String opaque ±88 char. Dipakai mentah sebagai HTTP header `Authorization: <token>` — **tanpa prefix `Bearer`.** |
| `APIResData` saat error | `null` (terverifikasi pada CheckFlow SN-not-found). |
| RequestId respons | Echo kosong/terisi server-side; jangan jadikan kunci logika. |

## GetToken

Request: `APIReqData = { "UserID": "<user>", "Password": "<hash-hex32>" }` (kredensial di body).

| Field | Tipe | Status |
|---|---|---|
| `APIResHeader.Code` | string 6 digit | TERKONFIRMASI (`"000000"` sukses; `"000500"` bila body kosong/salah) |
| `APIResHeader.Desc` | string (bisa CJK) | TERKONFIRMASI (`"OK"` sukses; pesan CJK untuk error) |
| `APIResHeader.ServiceName` | string | TERKONFIRMASI (`"GetToken"`) |
| `APIResHeader.RequestId` | string | TERKONFIRMASI (echo) |
| `APIResData.token` | string opaque ±88 char | TERKONFIRMASI |

## CheckFlow

Request: `APIReqData = { "SN", "SNType", "Station" }`.

| Field | Tipe | Status |
|---|---|---|
| `APIResHeader.Code` | string 6 digit | TERKONFIRMASI (`"000500"` untuk SN tidak ditemukan) |
| `APIResHeader.Desc` | string (bisa CJK) | TERKONFIRMASI (pesan SN-not-exist mengandung CJK) |
| `APIResData` (error path) | `null` | TERKONFIRMASI |
| `APIResData` (success payload) | object — struktur tak diketahui | **UNVERIFIED-PENDING-SAMPLE** |

Fixture: `Fixtures/checkflow-snnotfound.json` (hanya error path yang autentik).

## GetMesData

Request: `APIReqData = { "SN", "SNType", "Station" }`.

| Field | Tipe | Status |
|---|---|---|
| `APIResHeader.*` | sama dengan konvensi umum | TERKONFIRMASI (envelope) |
| `APIResData` (success payload) | object — struktur tak diketahui | **UNVERIFIED-PENDING-SAMPLE** |

Fixture: `Fixtures/getmesdata-placeholder.json` — bentuk envelope sama dengan
`APIResData` object kosong `{}`. **Placeholder saja**: sukses-payload menunggu
sample dari SN nyata (probe berikutnya butuh approval pemilik + akses LAN pabrik).
Jangan perlakukan `{}` sebagai kontrak mapper.

## UpdateInfo

**DILARANG** dipanggil dari sistem baru (WRITE service). Tidak ada fixture/schema.

## Kewajiban Task 2+

1. Mapper hanya boleh mengandalkan field berlabel TERKONFIRMASI.
2. Setiap field UNVERIFIED-PENDING-SAMPLE harus tetap punya jalur fallback +
   audit log sampai sample nyata memvalidasinya.
3. Saat sample sukses didapat: perbarui fixture + tabel di file ini, lalu hapus
   label pending.
