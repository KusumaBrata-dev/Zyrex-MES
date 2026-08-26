# Print Agent — Deployment Runbook & BarTender Validation

Target: one Windows PC per labeled station. The agent polls the MES API for
pending print jobs and prints them through the BarTender CLI.

---

## 1. Prasyarat

| Item | Requirement |
|---|---|
| OS | Windows 10/11 (or WinPE-capable kiosk image with .NET runtime) |
| .NET Runtime | **ASP.NET Core / .NET Desktop Runtime 10.0 x64** (publish is framework-dependent) |
| BarTender | Installed locally; typical exe: `C:\Program Files\Seagull\BarTender\bartend.exe` |
| Label template | `.btw` file accessible from the station PC, e.g. `C:\Labels\sn_label.btw` |
| Network | Outbound HTTP to the MES API host (default port 5000) |
| Printer | Windows default printer configured for the label stock |

Verify BarTender manually once before installing the agent:

```
"C:\Program Files\Seagull\BarTender\bartend.exe" /F="C:\Labels\sn_label.btw" /P /D="C:\Labels\sample-data.json"
```

`sample-data.json` must contain at least the named data fields your template
uses. The agent payload looks like:

```json
{ "sn": "SN-2026-000123", "productSku": "SKU-001", "stationCode": "ST-10", "scannedAtUtc": "2026-08-26T03:15:42.476Z" }
```

If nothing prints, fix BarTender first — the agent only launches it.

---

## 2. Buat User Agent di MES (sekali)

The claim/ack endpoints require role `Agent`. Create a dedicated account
(NOT a human's Operator account):

1. Generate an Argon2id hash for the agent password using the same algorithm
   as the server (`argon2id$<salt-b64>$<hash-b64>`, m=19456 t=2 p=4, 32-byte key).
   One-off C# script (run anywhere with .NET 10 SDK):

   ```
   mkdir hashgen && cd hashgen
   dotnet new console --force -o .
   dotnet add package Konscious.Security.Cryptography.Argon2 --version 1.3.1
   ```

   Replace `Program.cs` with:

   ```csharp
   using System.Security.Cryptography;
   using System.Text;
   using Konscious.Security.Cryptography;

   var password = args.Length > 0 ? args[0] : throw new ArgumentException("usage: hashgen <password>");
   var salt = RandomNumberGenerator.GetBytes(16);
   var hash = new Argon2id(Encoding.UTF8.GetBytes(password))
   {
       Salt = salt, MemorySize = 19456, Iterations = 2, DegreeOfParallelism = 4,
   }.GetBytes(32);
   Console.WriteLine($"argon2id${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
   ```

   ```
   dotnet run -- "<agent-password>"
   ```

2. Insert the user (psql against the MES database):

   ```sql
   INSERT INTO "users" ("Username", "PasswordHash", "FullName", "Role", "IsActive")
   VALUES ('print-agent-st10', '<paste-hash-here>', 'Print Agent ST-10', 'Agent', true);
   ```

3. Smoke-test login:

   ```
   curl http://<mes-host>:5000/api/auth/login -H "Content-Type: application/json" ^
        -d "{\"username\":\"print-agent-st10\",\"password\":\"<agent-password>\"}"
   ```

   Expect `{"token":"...","expiresAt":"..."}`.

> Interim alternative (before the Agent role is provisioned in production):
> use a dedicated Operator account. Claim/ack require the Agent role, so this
> only works if you temporarily grant that role — prefer the SQL insert above.

---

## 3. Instalasi Agent

1. Build & publish (from the repo root, on any machine with the SDK):

   ```
   dotnet publish agent/ZyrexMES.PrintAgent -c Release -r win-x64 --self-contained false
   ```

2. Copy `agent/ZyrexMES.PrintAgent/bin/Release/net10.0/win-x64/publish/` to the
   station PC, e.g. `C:\Apps\ZyrexMES.PrintAgent\`.

3. Edit `appsettings.json` **per station** (see section 4).

4. Install as an auto-start Windows service:

   ```
   sc.exe create "ZyrexMES.PrintAgent" binPath= "C:\Apps\ZyrexMES.PrintAgent\ZyrexMES.PrintAgent.exe" start= auto obj= LocalSystem
   sc.exe failure "ZyrexMES.PrintAgent" reset= 86400 actions= restart/10000/restart/30000/restart/60000
   sc.exe start "ZyrexMES.PrintAgent"
   ```

   (`actions=` restarts after 10s / 30s / 60s on consecutive failures; `reset=`
   resets the failure counter daily.)

5. Verify it runs:

   ```
   sc.exe query "ZyrexMES.PrintAgent"     → STATE: RUNNING
   type C:\Apps\ZyrexMES.PrintAgent\data  → created on first print attempt
   ```

Uninstall: `sc.exe stop` → `sc.exe delete "ZyrexMES.PrintAgent"`.

---

## 4. Konfigurasi Per Stasiun (`appsettings.json` → section `"Agent"`)

```json
{
  "Agent": {
    "ApiBaseUrl": "http://mes-prod.internal:5000",
    "StationId": 12,
    "PollIntervalSeconds": 2,
    "PrinterExePath": "C:\\Program Files\\Seagull\\BarTender\\bartend.exe",
    "PrinterArgsTemplate": "/F=\"{TemplatePath}\" /P /D=\"{DataFile}\"",
    "DataDir": "data",
    "PrintTimeoutSeconds": 60,
    "Username": "print-agent-st10",
    "Password": "<agent-password>",
    "Templates": {
      "SN_LABEL": "C:\\Labels\\sn_label.btw"
    }
  }
}
```

Rules:

- **`StationId` must be unique per station** — two agents sharing a StationId
  will steal each other's jobs.
- `Username`/`Password`: required; startup fails fast when empty. Prefer env
  vars over editing the file: set `Agent__Username` / `Agent__Password`
  (system-wide environment variables of the service account).
- `Templates` keys are template codes chosen by the server
  (`Printing:DefaultTemplate`, default `SN_LABEL`). A job whose code has no
  entry here fails with `unknown template '...'`.
- Restart the service after every config change:
  `sc.exe stop "ZyrexMES.PrintAgent" && sc.exe start "ZyrexMES.PrintAgent"`.

---

## 5. Validasi BarTender Nyata (uji cetak 1 label)

1. Pick a real pending job or create one by scanning a unit at a routing step
   flagged *RequireLabel* (job appears in `print_jobs` with status `Pending`).
2. Watch the queue while the agent claims it:

   ```sql
   SELECT "Id", "Status", "Attempts", "TemplateCode", "CompletedAtUtc"
   FROM "PrintJobs" ORDER BY "Id" DESC LIMIT 5;
   ```

   Expected lifecycle: `Pending → Sent → Printed`. One physical label comes
   out of the printer.
3. If the label prints but data fields are blank: the `.btw` template's named
   data fields don't match the payload keys (`sn`, `productSku`,
   `stationCode`, `scannedAtUtc`). Fix the template's data bindings.
4. If nothing prints at all, run the manual BarTender command from section 1
   with the exact data file the agent wrote (see Troubleshooting #4 to keep it).

### Menyesuaikan ArgsTemplate antar versi BarTender

`PrinterArgsTemplate` is a plain string with two placeholders:

- `{TemplatePath}` → absolute path of the `.btw` file (from `Templates`)
- `{DataFile}` → absolute path of the JSON payload file written per job

Examples:

| Situation | ArgsTemplate |
|---|---|
| BarTender 2016+ default (print & exit) | `/F="{TemplatePath}" /P /D="{DataFile}"` |
| Older builds needing quotes stripped | `/F={TemplatePath} /P /D={DataFile}` |
| Print via BarTender `/InProcess` switch | `/InProcess /F="{TemplatePath}" /P /D="{DataFile}"` |
| Third-party CLI wrapper | anything accepting both paths |

Validate any variant manually (section 1 command) before putting it into
config. The agent treats exit code 0 as success; anything else is retried up
to 3 times then acked failed.

---

## 6. Operasional

| Task | Command |
|---|---|
| Status | `sc.exe query "ZyrexMES.PrintAgent"` |
| Restart (after config change) | `sc.exe stop "ZyrexMES.PrintAgent"` then `sc.exe start ...` |
| Queue snapshot | SQL in section 5 step 2 |
| Jobs stuck in `Sent` (> a few minutes) | see Troubleshooting #5 |
| Logs | Windows Event Viewer → Windows Logs → Application (source `.NET Runtime` / `ZyrexMES PrintAgent`) |

Retry semantics recap: each claimed job gets up to **3 print attempts** inside
the agent before it is acked failed; the server raises the `print_failed`
alert — the agent itself never sends alerts.

---

## 7. Troubleshooting

1. **Service exits immediately at start** — config validation failed.
   Check Event Viewer for `Agent:ApiBaseUrl is required` /
   `Agent:StationId must be a positive integer` / Username/Password messages.
   Fix appsettings.json or env vars, restart.

2. **Login 401 in logs** — wrong agent credentials, or the user row is missing
   `Role = 'Agent'` / `IsActive = true`. Re-run the smoke-test curl from
   section 2.

3. **Claim returns nothing but jobs are Pending** — StationId mismatch between
   appsettings.json and the jobs' station, or the agent user lacks the Agent
   role (403). Compare `SELECT "StationId", count(*) FROM "PrintJobs" WHERE
   "Status"='Pending' GROUP BY 1;` with the config.

4. **BarTender exits non-zero** — agent retries 3×, acks failed, server alerts.
   Common BarTender CLI causes:
   - `exit 1` generic failure: bad template path, missing license, printer offline.
   - Template locked/open in BarTender Designer on the same PC.
   - Data field mismatch (template expects a named field absent from the JSON).
   Debug tip: temporarily comment out the delete step by pointing `DataDir` at
   a scratch folder and grabbing the `job-*.json` file right after a failure,
   then replay the manual command from section 1.

5. **Job stuck in `Sent`** — the agent claimed it but died (power loss, crash)
   before acking, or ack(ok=true) failed after printing (by design no false
   fallback is sent, so the label may already exist). Resolution:
   - If the label printed: re-ack manually as success:
     `curl -X POST http://<mes-host>:5000/api/print/jobs/<id>/ack -H "Authorization: Bearer <agent-token>" -H "Content-Type: application/json" -d "{\"ok\":true}"`
   - If not printed: re-ack with `{\"ok\":false,\"error\":\"manual retry\"}` —
     attempts < 3 puts it back to `Pending`; at ≥ 3 it becomes `Failed`.

6. **`bartender timeout` errors** — a run exceeded `PrintTimeoutSeconds` (60 s
   default); the agent kills the whole process tree. Causes: license dialog
   waiting for input, printer spooler hung. Run BarTender interactively once
   to dismiss dialogs; check the Windows print spooler.

7. **Duplicate labels** — should not happen (unique `(UnitId, TemplateCode)`
   index + duplicate-scan rejection). If observed, check for two agents with
   the same `StationId` and audit `print_jobs.Attempts` history.

---

## Checklist ringkas per stasiun

- [ ] .NET 10 runtime x64 terpasang
- [ ] BarTender terpasang, uji manual section 1 sukses
- [ ] User `print-agent-*` role Agent dibuat + login curl OK
- [ ] Publish output disalin, appsettings.json diedit (StationId unik!)
- [ ] `sc.exe create` + failure recovery + start → RUNNING
- [ ] Scan uji 1 unit berlabel → label keluar, job `Printed` di DB
