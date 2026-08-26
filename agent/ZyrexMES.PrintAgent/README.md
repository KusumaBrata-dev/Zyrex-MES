# ZyrexMES.PrintAgent

Station-side Windows Service that polls the MES API for pending label print
jobs (`POST /api/print/jobs/claim`), prints them through the BarTender CLI,
and acknowledges each job (`POST /api/print/jobs/{id}/ack`).

## Configuration (`Agent` section of appsettings.json; env override `Agent__*`)

| Key | Default | Notes |
|---|---|---|
| `ApiBaseUrl` | `http://localhost:5000` | MES API base URL |
| `StationId` | `1` | Station served by this agent (>0, required) |
| `PollIntervalSeconds` | `2` | Claim poll interval (min 1) |
| `PrinterExePath` | `""` | Full path to the BarTender executable (required for printing) |
| `PrinterArgsTemplate` | `/F="{TemplatePath}" /P /D="{DataFile}"` | `{TemplatePath}` and `{DataFile}` are replaced at runtime |
| `DataDir` | `data` | Where job payload files are written (deleted after each run) |
| `PrintTimeoutSeconds` | `60` | Hard limit per printer run; on timeout the process tree is killed |
| `Username` / `Password` | — | Agent account (role `Agent`, created by an admin at deployment). Set via environment in production. |
| `Templates` | — | Map of template code → template file, e.g. `"SN_LABEL": "C:\\bt\\SN_LABEL.btw"` |

All four of ApiBaseUrl/StationId/Username/Password are validated at startup;
the service fails fast when they are missing.

## Behavior

- Up to **3 print attempts** per job before it is acked as failed.
- A failed final ack triggers a `print_failed` alert **on the server side**;
  the agent itself never sends alerts.
- If `ack(ok=true)` fails after a successful print, no `ack(false)` is sent
  (prevents duplicate printing); the job stays `Sent` for manual follow-up.

## Install as Windows Service

```
sc create "ZyrexMES PrintAgent" binPath= "<publish dir>\ZyrexMES.PrintAgent.exe"
```
