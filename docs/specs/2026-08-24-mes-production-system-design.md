# MES Production System — Design Document

| | |
|---|---|
| **Perusahaan** | PT Zyrexindo Mandiri Buana Tbk |
| **Tanggal** | 2026-08-24 |
| **Status** | Approved (desain disetujui user, menunggu review spec) |
| **Pengganti** | MES.Enter.exe + MOM_MES (MES.Winform.exe) legacy client & backend |
| **Dirawat oleh** | Tim IT Zyrexindo + pemilik proyek + AI Agent |

---

## 1. Ringkasan

Sistem MES (Manufacturing Execution System) baru yang **menggantikan penuh** sistem
legacy: aplikasi stasiun berbasis web kiosk untuk pelaporan produksi per stasiun,
web dashboard monitoring realtime seluruh stasiun pada 9 line, traceability SN
selaras ISO 9001, dan AI Assistant 100% lokal. Data historis dari database MES
lama dimigrasi penuh.

## 2. Kebutuhan Terkunci (Hasil Brainstorming)

| # | Aspek | Keputusan |
|---|---|---|
| 1 | Mode | Pengganti penuh MES lama |
| 2 | Skope | Full stack (aplikasi stasiun + backend/database baru) |
| 3 | Data | Migrasi penuh dari DB MES lama; akses penuh ke DB lama tersedia |
| 4 | Skala | 9 line; daftar station diambil dari MES lama (±100); 40 operator; ±2.000 unit/hari; tanpa shift |
| 5 | ISO | ISO 9001 (QMS) + traceability unit SN — level internal best practice (belum audit eksternal) |
| 6 | AI | SOP Q&A, query produksi bahasa natural, deteksi anomali + rekomendasi, bantu input laporan, analisa akar masalah QA/Repair — **wajib lokal, tanpa data keluar pabrik** |
| 7 | Lapangan | PC stasiun i5 Gen-12 / 16 GB / Win 11 Pro / SSD; scanner USB wedge; printer Honeywell/Panda/Zebra via **BarTender (wajib)**; monitor biasa; WiFi+LAN |
| 8 | Server | Spek diusulkan dalam dokumen ini; fail-safe fase 1 = A (stop saat server mati) → roadmap C (queue lokal) |
| 9 | Teknologi | Bebas dengan syarat mudah dirawat tim IT + AI Agent |

---

## 3. Arsitektur (Opsi 1 Disetujui: Unified Web Platform + Print Agent)

```
┌─ STASIUN (±100 PC Win11) ─────┐      ┌─ SERVER PABRIK ─────────────────────┐
│ Edge Kiosk Mode               │ HTTP │ ┌────────────────────────────┐      │
│  └ App Stasiun (Next.js PWA)  ├──────┼─► Backend API ASP.NET Core 8   │      │
│ Print Agent (.NET 8 svc)      │ WSS  │ │  ├ Modul MasterData         │      │
│  └ BarTender lokal ◄──────────┼──────┼─┤  ├ Modul Production         │      │
│ Scanner wedge → input fokus   │      │ │  ├ Modul Quality            │      │
└───────────────────────────────┘      │ │  ├ Modul Traceability       │      │
                                       │ │  ├ Modul Reporting          │      │
┌─ OFFICE / TV ANDON ───────────┐      │ │  ├ Modul AIGateway          │      │
│ Browser: Web Dashboard        ├──────┼─┤  ├ Modul Auth/AuditLog      │      │
│  realtime 9 line              │ WSS  │ │  └ Modul PrintService       │      │
└───────────────────────────────┘      │ ├ PostgreSQL 16 + pgvector ◄─┼──┐   │
                                       │ ├ Ollama + Qwen2.5-14B Q4    │  │RAG│
                                       │ └ SignalR (WebSocket)        │  │   │
                                       └──────────────────────────────┘  │   │
                                              sop_embeddings ◄───────────┘   │
```

Prinsip: **modular monolith** (bukan microservices), semua komponen open-source
kecuali lisensi BarTender yang sudah ada.

### 3.1 Komponen

| Komponen | Teknologi | Tanggung Jawab |
|---|---|---|
| Backend API | ASP.NET Core 8 (C#) | Logika bisnis terpusat, REST + SignalR hub, audit middleware |
| Database | PostgreSQL 16 + pgvector | Transaksi, traceability, embedding SOP, satu database |
| App Stasiun | React/Next.js PWA | UI operator kiosk: scan, OK/NG + suara, form NG, chat AI |
| Web Dashboard | Next.js (satu codebase dgn app stasiun) | Monitoring realtime, Andon TV, laporan, admin |
| Print Agent | .NET 8 Windows Service | Terima job cetak → jalankan BarTender lokal → status balik ke backend |
| AI Lokal | Ollama + Qwen2.5-14B-Instruct Q4 | LLM inferensi lokal, fasih Bahasa Indonesia |
| Realtime | SignalR (WebSocket) | Push event scan/NG/anomali tanpa refresh |
| Deployment | Docker Compose, 1 host | Orkestrasi backend + DB + Ollama |

### 3.2 Alasan Pemilihan

- Update aplikasi stasiun **nol-sentuh**: cukup deploy di server, 100 kiosk otomatis dapat versi baru.
- Satu codebase web utama → permukaan maintenance minimum untuk Tim IT + AI Agent.
- Print Agent dibangun **sekali** sebagai jembatan wajib ke BarTender (printer USB lokal).
- PostgreSQL gratis selamanya; pgvector menghilangkan kebutuhan vector DB terpisah.
- Roadmap fail-safe C tetap terbuka lewat modul queue pada Print Agent + service worker.

---

## 4. Struktur Data Inti

```
lines ──< stations                    # 9 line + station hasil import dari MES lama
products ──< boms                     # produk + komponen (dari migrasi)
routings ──< routing_steps            # urutan proses valid per produk
units (SN)                            # 1 baris per unit fisik — jantung traceability
units ──< unit_transactions           # setiap scan: user, station, timestamp, hasil
qc_results ──< ng_codes               # hasil QC + kode NG terstandar
repairs                              # catatan repair + akar masalah
sop_documents ──< sop_embeddings     # SOP terversi + vector (pgvector)
ai_conversations                      # riwayat chat AI per user (audit)
users, roles                          # RBAC: Operator, Leader, QA, Supervisor, Admin
audit_logs                            # APPEND-ONLY di level role database
label_templates                       # template BarTender per produk/proses
```

Traceability SN = rantai `units → unit_transactions → stations/lines/operators/qc_results/repairs`.

---

## 5. Alur Transaksi Stasiun

1. Operator login di kiosk → app memuat konfigurasi station.
2. Scanner wedge mengisi field auto-focus → **scan SN**.
3. Backend validasi: SN terdaftar? urutan routing benar? duplikat?
   - PASS → transaksi tersimpan + suara OK + push realtime (+ job cetak label bila step mensyaratkan).
   - FAIL → suara NG + alasan eksplisit di layar (mis. *"SN harus melewati ICT dahulu"*).
4. Input hasil QC: PASS, atau NG wajib pilih `ng_code` + catatan → masuk antrean repair.
5. Semua langkah append-only: timestamp + user ID (tidak ada update/hapus record transaksi).

---

## 6. AI Assistant (Lokal)

| Fitur | Mekanisme |
|---|---|
| SOP Q&A | RAG: pertanyaan → retrieval pgvector → jawaban + kutipan dokumen sumber & versi |
| Query Produksi | NL→SQL dengan guardrail ketat: role DB read-only, whitelist tabel, timeout, blokir kata kunci tulis |
| Deteksi Anomali | Job terjadwal statistik (yield drop > ambang, spike NG) → alert dashboard + penjelasan LLM dari data |
| Bantu Laporan | Deskripsi operator → draft entry NG/report → konfirmasi manual sebelum tersimpan |
| Analisa QA/Repair | Korelasi pola histori repair (kode NG berulang, station, lot komponen) → rekomendasi akar masalah |

Model awal: Qwen2.5-14B-Instruct quantized Q4 (±9 GB VRAM). Jalur upgrade ke 32B
hanya konfigurasi Ollama, tanpa perubahan kode.

---

## 7. Server Pabrik (Spek Usulan)

| Komponen | Spek Minimum | Catatan |
|---|---|---|
| CPU | AMD Ryzen 9 / Intel i9 ≥16 core | API + DB sangat ringan utk beban ini |
| RAM | 64 GB ECC | Cache DB + konteks LLM |
| GPU | NVIDIA RTX 4090 24 GB | Inferensi LLM lokal — komponen kritis |
| Storage | 2 × 2 TB NVMe RAID1 | Data + model |
| OS | Ubuntu Server 24.04 LTS + Docker Compose | Alternatif Windows Server didukung Ollama |
| Proteksi | UPS + NAS/HDD backup harian `pg_dump` | Wajib ISO |

Beban aktual ±10–20 ribu transaksi/hari = ringan; GPU dipakai untuk AI.

---

## 8. Fail-safe, Keamanan, ISO 9001

- **Fail-safe A (v1)**: server tak terjangkau → overlay merah blocking "SERVER OFFLINE"; scan diblokir; tidak ada transaksi setengah-jadi (ACID).
- **Jaringan**: internal saja, tanpa dependensi internet runtime (unduh model hanya saat setup).
- **Auth**: Argon2id password hashing; RBAC 5 role; secret via environment, dilarang hardcode/log.
- **ISO 9001 mapping**: SOP terversi ✓ · record otomatis siapa-kapan-apa ✓ · audit log append-only (blokir UPDATE/DELETE di level role DB) ✓ · alur corrective action NG→repair→verifikasi ✓ · data management review dari dashboard ✓ → **audit-ready**.
- **Cetak gagal**: retry 3× → alert leader di dashboard; tidak pernah hilang senyap.

---

## 9. Pengujian & Rollout

1. Unit test backend (xUnit) + integration test API — target coverage ≥ 80%.
2. E2E Playwright: scan→PASS, scan→NG→repair, cetak label, overlay offline.
3. Validasi migrasi: rekonsiliasi jumlah baris per tabel + sampling acak 100 SN field-by-field identik.
4. Pilot 1 line (1–2 minggu evaluasi) → rollout 9 line.

**Roadmap**: ① Infra+skeleton → ② Master data+migrasi ETL → ③ Core stasiun (scan/QC/label) → ④ Dashboard+laporan → ⑤ AI Assistant bertahap → ⑥ Pilot→Rollout → *(masa depan)* ⑦ fail-safe C offline queue.

---

## SDD Specification

### Goals
Sistem MES pengganti penuh: reporting stasiun berbasis kiosk, dashboard monitoring
realtime 9 line, traceability SN selaras ISO 9001, AI Assistant lokal 5 fitur,
migrasi penuh data legacy.

### Non-Goals
- Mode offline queue (fail-safe C) — fase mendatang
- Penggantian WMS gudang
- Integrasi ERP, payroll/HR
- Multi-pabrik, aplikasi mobile
- Artefak sertifikasi ISO eksternal (internal best practice saja)

### Acceptance Criteria
- [ ] AC-01: Login operator kiosk berhasil; scan SN valid tercatat lengkap (user, station, timestamp) dengan latensi p95 ≤ 500 ms
- [ ] AC-02: Scan SN melanggar urutan routing DITOLAK dengan pesan alasan + suara NG
- [ ] AC-03: Scan duplikat pada step yang sama DITOLAK
- [ ] AC-04: Submit hasil QC NG tanpa memilih ng_code DITOLAK oleh validasi
- [ ] AC-05: Job cetak label memicu BarTender lokal mencetak di Honeywell/Panda/Zebra; gagal → retry 3× → muncul alert di dashboard
- [ ] AC-06: Dashboard menampilkan event scan semua 9 line via WebSocket ≤ 2 detik setelah transaksi
- [ ] AC-07: Query traceability SN mana pun mengembalikan rantai lengkap transaksi+QC+repair termasuk data migrasi
- [ ] AC-08: Rekonsiliasi migrasi: jumlah baris identik per tabel antara extract DB lama vs DB baru; 100 SN sampel acak cocok field-by-field
- [ ] AC-09: Seluruh endpoint AI merespons saat server diputus dari internet (uji isolasi jaringan) — nol outbound call
- [ ] AC-10: Jawaban SOP Q&A selalu menyertakan kutipan dokumen sumber + nomor versi SOP
- [ ] AC-11: NL→SQL menolak INSERT/UPDATE/DELETE/DROP dan hanya membaca tabel whitelist
- [ ] AC-12: Yield drop melewati ambang terkonfigurasi → alert anomali tampil di dashboard
- [ ] AC-13: Server unreachable → app stasiun menampilkan overlay blocking "SERVER OFFLINE"; input scan dinonaktifkan
- [ ] AC-14: Role database aplikasi ditolak melakukan UPDATE/DELETE pada audit_logs (error di level DB)
- [ ] AC-15: RBAC: token role Operator memanggil endpoint admin → HTTP 403
- [ ] AC-16: Restore uji coba pg_dump pada instance bersih berhasil + lolos pengecekan jumlah baris
- [ ] AC-17: Coverage unit+integration backend ≥ 80%

### Constraints
- PC stasiun Win 11 Pro + Edge kiosk mode; scanner USB wedge (input keyboard)
- BarTender wajib untuk label; printer Honeywell/Panda/Zebra
- Jaringan WiFi+LAN campur; akses server sebaiknya dialihkan ke LAN
- Data produksi tidak boleh keluar jaringan pabrik (AI strictly local)
- Fail-safe v1 = A (berhenti saat server mati)
- Deployment single-host Docker Compose; GPU RTX 4090-class utk LLM
- PostgreSQL + pgvector sebagai satu-satunya database
- Stack harus mainstream agar dirawat Tim IT + AI Agent

### Out of Scope (perubahan masa depan)
- Fail-safe C: queue lokal Print Agent/service worker
- APS/scheduling lanjutan, SPC chart statistik lanjut
- API integrasi ERP/WMS
- Bahasa ketiga selain ID/EN
- Upgrade model LLM 14B→32B (hanya config, sudah didokumentasikan)

---

## Referensi Legacy (hasil eksplorasi laptop)

- `C:\MES.Enter.exe` — launcher .NET WinForms ©2018; selector Line/Station; sinkronisasi FTP (`gFactoryFtpIP`, `FtpRootPath`)
- `C:\LocalSetting\GW_ZZ_TEST\LocalSetting.ini` — `Line=ZZ-D`, `Station=ACOS_QC`
- `C:\FA\EXE\GW_ZZ_TEST\MOM_MES\` — MES.Winform.exe (Application/ClientBusiness/ClientService/Repository/DBUtility), Interop.BarTender.dll, OK/NG/FQA.wav, zh-CN/en-US
- `C:\FA\EXE\GW_ZZ_TEST\WMS\` — MES.WMS (EntityFramework) — **tidak digantikan**
