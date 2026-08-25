# Legacy MES API Contract (hasil reverse-engineering MESTools.exe)

> Sumber: `C:\Users\Assaemon\Documents\MES-API.rar` → `MESTools.exe` (PyInstaller,
> Python 3.8) + `mescfg.ini`. Diekstrak read-only ke folder temp; tidak ada file
> MES lama yang disentuh. Disassembly referensi: `MESTools.dis.txt` (folder ini).

## Aturan Keras Integrasi (dari pemilik proyek)

**LEGACY MES = READ-ONLY.** Sistem baru dilarang memanggil service yang menulis
(`UpdateInfo`) ke produksi legacy. Hanya boleh: `GetToken`, `CheckFlow`,
`GetMesData`.

## Endpoint & Kredensial

- Base URL: dari `mescfg.ini [MES] URL` (contoh terpantau: `http://192.168.1.245:8090/API/TE/PostData`)
- UserID / Password: dari `[MES]` juga — **jangan pernah hardcode/log; simpan di env**
  (`MES_LEGACY__URL`, `MES_LEGACY__USERID`, `MES_LEGACY__PASSWORD`).
- Password dikirim dalam bentuk sudah ter-hash (hex 32 char) oleh sumbernya.

## Envelope

Request JSON:
```json
{
  "RequestId": "<guid/urut>",
  "ServiceName": "GetToken | CheckFlow | GetMesData | UpdateInfo",
  "Language": "",
  "ClientData": ""
}
```
Response JSON:
```json
{
  "APIResHeader": { "Code": 0 },
  "APIResData": { "...": "..." }
}
```
`Code != 0` = gagal.

## Alur Autentikasi

1. POST dengan `ServiceName="GetToken"`; kredensial `UserID`/`Password`
   dikirim pada bagian header-request internal tool.
2. Ambil `APIResData.token`.
3. Request berikutnya menyertakan header `Authorization` berisi token.

## Service Data (payload `APIReqData`)

| Service | Payload | Arah | Fungsi |
|---|---|---|---|
| `CheckFlow` | `{ SN, SNType, Station }` | READ | Validasi apakah SN boleh diproses di station (routing check) |
| `GetMesData` | `{ SN, SNType, Station }` | READ | Tarik data unit dari MES |
| `UpdateInfo` | `{ SN, SNType, Station, LinkedSN? }` | **WRITE** | Dorong hasil proses — DILARANG dari sistem baru |

`SNType` default `"SN"` (dari config).

## Pemakaian di Sistem Baru (Plan 2)

- **ETL migrasi**: loop `GetMesData` per SN/rentang → transform → insert ke
  PostgreSQL baru; rekonsiliasi row-count + sampling 100 SN (AC-07, AC-08).
- **Validasi silang**: `CheckFlow` bisa dipakai untuk membuktikan routing baru
  setara legacy pada sampel unit.
- Klien HTTP .NET (`LegacyMesClient`) dengan retry/timeouts; semua panggilan
  tercatat audit; tidak ada jalur tulis yang diekspos.

## Catatan Teknis Tool Asal

- CLI: `MESTools.exe <CheckFlow|UpdateInfo|GetMesData> <SN> <station> [data]`
  — output `SET Code=`/`SET msg=` untuk dipakai script batch pabrik.
- Kelas `MES`: `login()` (GetToken), `post(fun, sn, station, link_data)`,
  `request()` = `requests.post(url, json=post_data, headers=headers)`.
