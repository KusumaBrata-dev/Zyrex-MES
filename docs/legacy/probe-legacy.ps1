<#
.SYNOPSIS
    Read-only probe ke legacy MES vendor system. TIDAK PERNAH memanggil UpdateInfo.

.DESCRIPTION
    ZERO-DISTURBANCE CONTRACT (hard rules — jangan dilanggar):
      * Legacy MES = PRODUCTION. Probe ini read-only dan dibatasi desain.
      * MAKSIMAL 2 HTTP request per invokasi: 1x GetToken + 1x data service.
        (Anggaran total sesi probe: 3 request — lihat .superpowers/sdd progress.md)
      * LOOP DILARANG KERAS: jangan loop daftar SN, jangan panggil berulang/batch.
      * Hanya service READ yang diizinkan: CheckFlow / GetMesData.
        UpdateInfo (WRITE) tidak pernah dikirim script ini maupun sistem baru.
      * Kredensial via process env (MES_LEGACY__URL / MES_LEGACY__USERID /
        MES_LEGACY__PASSWORD). Jangan pernah hardcode atau log kredensial/token.

    Envelope = versi TERBUKTI live probe 2026-08-24:
      request bersarang { APIReqHeader{RequestId,ServiceName,Language,
      ClientData="MESTools"}, APIReqData{...} }; kredensial GetToken di BODY;
      token balasan dipakai mentah sebagai header "Authorization" (tanpa Bearer);
      Code respons adalah STRING ("000000" sukses).

.PARAMETER SN
    Serial number SATU unit fisik. WAJIB sebelum service data dipanggil.

.PARAMETER Station
    Kode station (contoh: PT). WAJIB sebelum service data dipanggil.

.PARAMETER Service
    Service data yang diprobe: CheckFlow (default) atau GetMesData.

.PARAMETER TokenOnly
    Probe GetToken saja (total 1 request). SN/Station tidak diperlukan.

.EXAMPLE
    ./probe-legacy.ps1 -TokenOnly
    ./probe-legacy.ps1 -SN SN1234 -Station PT
#>
[CmdletBinding()]
param(
    [string]$SN,
    [string]$Station,
    [ValidateSet('CheckFlow', 'GetMesData')]
    [string]$Service = 'CheckFlow',
    [switch]$TokenOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Gerbang ZERO-DISTURBANCE ----------------------------------------------
if (-not $TokenOnly) {
    if ([string]::IsNullOrWhiteSpace($SN) -or [string]::IsNullOrWhiteSpace($Station)) {
        throw '-SN dan -Station WAJIB diisi sebelum service data dipanggil. Untuk probe token saja pakai -TokenOnly.'
    }
}

$url      = $env:MES_LEGACY__URL
$userId   = $env:MES_LEGACY__USERID
$password = $env:MES_LEGACY__PASSWORD   # sudah hash-hex32 dari sumbernya; JANGAN log
if ([string]::IsNullOrWhiteSpace($url) -or [string]::IsNullOrWhiteSpace($userId) -or [string]::IsNullOrWhiteSpace($password)) {
    throw 'Env wajib belum lengkap: MES_LEGACY__URL / MES_LEGACY__USERID / MES_LEGACY__PASSWORD.'
}

# Header HTTP meniru fingerprint klien legacy (MESTools.dis.txt __init__).
# Content-Type lewat -ContentType (bukan -Headers) agar kompatibel PS 5.1 & 7+.
$httpHeaders = @{
    'Accept'           = 'application/json'
    'User-Agent'       = 'python-requests'
    'X-Requested-With' = 'XMLHttpRequest'
}

function New-LegacyEnvelope {
    param(
        [Parameter(Mandatory)][string]$ServiceName,
        [Parameter(Mandatory)][hashtable]$ReqData
    )
    @{
        APIReqHeader = @{
            RequestId   = [guid]::NewGuid().ToString('N')
            ServiceName = $ServiceName
            Language    = ''
            ClientData  = 'MESTools'
        }
        APIReqData   = $ReqData
    }
}

function Invoke-LegacyProbe {
    # SATU HTTP request per pemanggilan; anggaran dijaga oleh caller.
    param([Parameter(Mandatory)][hashtable]$Envelope)
    $json = $Envelope | ConvertTo-Json -Depth 6
    return Invoke-RestMethod -Method Post -Uri $url -ContentType 'application/json' `
        -Headers $httpHeaders -Body $json
}

$MAX_REQUESTS = 2   # batas keras per invokasi; DILARANG menaikkan nilai ini
$requestCount = 0

# --- Langkah 1/2: GetToken ---------------------------------------------------
$tokenRes = Invoke-LegacyProbe -Envelope (New-LegacyEnvelope -ServiceName 'GetToken' `
    -ReqData @{ UserID = $userId; Password = $password })
$requestCount++
Write-Host ("[{0}/{1}] GetToken -> Code={2} Desc={3}" -f $requestCount, $MAX_REQUESTS, `
    $tokenRes.APIResHeader.Code, $tokenRes.APIResHeader.Desc)

# Code adalah STRING — bandingkan string, jangan parse integer.
if ($tokenRes.APIResHeader.Code -ne '000000') {
    Write-Warning 'GetToken gagal. Stop SEBELUM service data (hemat anggaran request).'
    exit 1
}

$token = $tokenRes.APIResData.token
Write-Host ("token length={0} (harapan ~88 char, opaque)" -f $token.Length)
$httpHeaders['Authorization'] = $token   # mentah, tanpa prefix Bearer

if ($TokenOnly) {
    Write-Host 'Probe token-only selesai. Total request: 1.'
    exit 0
}

# --- Langkah 2/2: SATU panggilan service data --------------------------------
$dataRes = Invoke-LegacyProbe -Envelope (New-LegacyEnvelope -ServiceName $Service `
    -ReqData @{ SN = $SN; SNType = 'SN'; Station = $Station })
$requestCount++
Write-Host ("[{0}/{1}] {2} -> Code={3} Desc={4}" -f $requestCount, $MAX_REQUESTS, $Service, `
    $dataRes.APIResHeader.Code, $dataRes.APIResHeader.Desc)

if ($dataRes.APIResHeader.Code -eq '000000') {
    Write-Host 'APIResData:'
    $dataRes.APIResData | ConvertTo-Json -Depth 10
} else {
    Write-Host 'APIResData = null saat error (terkonfirmasi).'
}

Write-Host ("Selesai. Total request: {0}/{1}. JANGAN jalankan ulang dalam loop." -f $requestCount, $MAX_REQUESTS)
