# Starts the API, runs the mobile smoke test against it, then stops the API.
#   powershell -File mobile/tool/smoke.ps1            (from the repository root or anywhere)
# Needs the SQL Server database from src/QuickCommerce.Api/appsettings.Development.json (or the
# ConnectionStrings__QuickCommerceDb environment variable) to be reachable.
$ErrorActionPreference = 'Stop'
$mobile = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $mobile '..')
$url = 'http://localhost:5067'

$alreadyUp = $false
try { $alreadyUp = (Invoke-WebRequest "$url/health/live" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch {}

if ($alreadyUp) { Write-Host "Using the API already running at $url. If you changed backend code, restart it first so the tests see the new routes." -ForegroundColor Yellow }
$api = $null
if (-not $alreadyUp) {
  Write-Host 'Starting the API...'
  $api = Start-Process dotnet -ArgumentList 'run', '--project', 'src/QuickCommerce.Api', '--launch-profile', 'http' -WorkingDirectory $root -PassThru -WindowStyle Hidden
  $deadline = (Get-Date).AddSeconds(90)
  do {
    Start-Sleep 2
    try { $up = (Invoke-WebRequest "$url/health/live" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { $up = $false }
  } until ($up -or (Get-Date) -gt $deadline)
  if (-not $up) { if ($api) { taskkill /PID $api.Id /T /F | Out-Null }; throw "The API did not become healthy at $url within 90 seconds." }
}

try {
  Set-Location $mobile
  flutter test test/integration/api_smoke_test.dart "--dart-define=SMOKE_API_URL=$url"
  $code = $LASTEXITCODE
} finally {
  if ($api) { taskkill /PID $api.Id /T /F | Out-Null; Write-Host 'API stopped.' }
}
exit $code
