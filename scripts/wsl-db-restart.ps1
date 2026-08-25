# WSL DB restart helper — pakai ini SETIAP sebelum sesi tes yang butuh PostgreSQL.
# Root cause terkonfirmasi: relay localhostForwarding NAT (wslrelay) kehilangan binding
# setelah VM idle/suspend; siklus bersih memulihkannya.
# Usage: powershell -File .superpowers\sdd\wsl-db-restart.ps1
$ErrorActionPreference = 'Continue'
wsl --shutdown
Start-Sleep -Seconds 12
@'
systemctl start docker >/dev/null 2>&1
docker start zyrex-pg >/dev/null 2>&1
nohup sleep 3600 >/dev/null 2>&1 &
sleep 5
'@ | wsl -d Ubuntu -u root -- bash
Start-Sleep -Seconds 4
$l = netstat -ano | Select-String '127\.0\.0\.1:5433.*LISTENING'
if ($l) { "DB_READY (relay bound): $l" } else { "DB_NOT_READY - ulangi sekali lagi" }
