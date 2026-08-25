# WSL DB restart helper — pakai ini SETIAP sebelum sesi tes yang butuh PostgreSQL.
# Root cause terkonfirmasi: relay localhostForwarding NAT (wslrelay) kehilangan binding
# setelah VM idle/suspend; siklus bersih memulihkannya.
# Usage: powershell -File .superpowers\sdd\wsl-db-restart.ps1
$ErrorActionPreference = 'Continue'
wsl --shutdown
Start-Sleep -Seconds 12
$cmds = @'
systemctl start docker >/dev/null 2>&1
docker start zyrex-pg >/dev/null 2>&1
nohup sleep 3600 >/dev/null 2>&1 &
sleep 5
'@
# PowerShell pipe ke native exe selalu menambah CRLF -> kirim via base64 agar bash menerima LF murni.
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($cmds))
wsl -d Ubuntu -u root -- bash -c "echo $b64 | base64 -d | bash"
Start-Sleep -Seconds 4
$l = netstat -ano | Select-String '127\.0\.0\.1:5433.*LISTENING'
if ($l) { "DB_READY (relay bound): $l" } else { "DB_NOT_READY - ulangi sekali lagi" }
