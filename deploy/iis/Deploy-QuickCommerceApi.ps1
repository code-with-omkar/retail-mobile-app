[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath
)

$ErrorActionPreference = 'Stop'

$requiredVariables = @('IIS_SITE_NAME', 'IIS_APP_POOL_NAME', 'IIS_DEPLOY_PATH', 'HEALTH_CHECK_URL')
foreach ($variable in $requiredVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
        throw "Required deployment variable '$variable' is missing."
    }
}

if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) {
    throw "Deployment artifact was not found: $ArtifactPath"
}

$siteName = $env:IIS_SITE_NAME
$appPoolName = $env:IIS_APP_POOL_NAME
$deployPath = [Environment]::ExpandEnvironmentVariables($env:IIS_DEPLOY_PATH)
$healthCheckUrl = $env:HEALTH_CHECK_URL
$backupRoot = Join-Path (Split-Path -Parent $deployPath) 'QuickCommerce.Api.backups'
$timestamp = Get-Date -Format 'yyyyMMddHHmmss'
$backupPath = Join-Path $backupRoot $timestamp
$stagingPath = Join-Path $env:TEMP "QuickCommerce.Api.$timestamp"

Import-Module WebAdministration
if (-not (Get-Website -Name $siteName -ErrorAction SilentlyContinue)) {
    throw "IIS site '$siteName' was not found on this instance."
}
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null

$deploymentSucceeded = $false
try {
    Expand-Archive -LiteralPath $ArtifactPath -DestinationPath $stagingPath -Force

    if (Test-Path -LiteralPath $deployPath) {
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        Copy-Item -Path (Join-Path $deployPath '*') -Destination $backupPath -Recurse -Force
    }

    Stop-WebAppPool -Name $appPoolName
    New-Item -ItemType Directory -Path $deployPath -Force | Out-Null
    Remove-Item -Path (Join-Path $deployPath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $stagingPath '*') -Destination $deployPath -Recurse -Force
    Start-WebAppPool -Name $appPoolName

    $ready = $false
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $healthCheckUrl -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            if ($attempt -eq 30) {
                throw "Readiness check failed after deployment: $($_.Exception.Message)"
            }
        }
        Start-Sleep -Seconds 2
    }

    if (-not $ready) {
        throw "Readiness check did not return HTTP 200: $healthCheckUrl"
    }

    $deploymentSucceeded = $true
    Write-Host "Deployment verified successfully at $healthCheckUrl"
}
catch {
    Write-Error "Deployment failed: $($_.Exception.Message)"
    if (Test-Path -LiteralPath $backupPath) {
        Write-Warning "Restoring previous IIS artifact from $backupPath"
        Stop-WebAppPool -Name $appPoolName -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $deployPath '*') -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item -Path (Join-Path $backupPath '*') -Destination $deployPath -Recurse -Force
        Start-WebAppPool -Name $appPoolName
    }
    throw
}
finally {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
    if ($deploymentSucceeded) {
        Write-Host "Previous deployment retained at $backupPath for rollback."
    }
}
