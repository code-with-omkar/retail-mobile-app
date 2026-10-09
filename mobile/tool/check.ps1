# Quality gate for the mobile app. Run from the mobile directory:  powershell -File tool/check.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
flutter pub get;  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
flutter analyze;  if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
flutter test;     if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host 'Quality gate passed.'
