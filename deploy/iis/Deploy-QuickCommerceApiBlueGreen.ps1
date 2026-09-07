[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath
)

$ErrorActionPreference = 'Stop'

$requiredVariables = @(
    'BLUE_GREEN_ACTIVE_SLOT',
    'BLUE_DEPLOY_PATH',
    'GREEN_DEPLOY_PATH',
    'BLUE_APP_POOL_NAME',
    'GREEN_APP_POOL_NAME',
    'BLUE_SITE_NAME',
    'GREEN_SITE_NAME',
    'BLUE_HEALTH_LIVE_URL',
    'BLUE_HEALTH_READY_URL',
    'GREEN_HEALTH_LIVE_URL',
    'GREEN_HEALTH_READY_URL',
    'TRAFFIC_HEALTH_URL',
    'TRAFFIC_SWITCH_SCRIPT'
)
foreach ($variable in $requiredVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($variable))) {
        throw "Required Blue-Green deployment variable '$variable' is missing."
    }
}

if ($env:BLUE_GREEN_ACTIVE_SLOT -notin @('Blue', 'Green')) {
    throw "BLUE_GREEN_ACTIVE_SLOT must be 'Blue' or 'Green'."
}

if (-not (Test-Path -LiteralPath $ArtifactPath -PathType Leaf)) {
    throw "Deployment artifact was not found: $ArtifactPath"
}

$activeSlot = $env:BLUE_GREEN_ACTIVE_SLOT
$inactiveSlot = if ($activeSlot -eq 'Blue') { 'Green' } else { 'Blue' }
$activeAppPool = if ($activeSlot -eq 'Blue') { $env:BLUE_APP_POOL_NAME } else { $env:GREEN_APP_POOL_NAME }
$inactiveAppPool = if ($inactiveSlot -eq 'Blue') { $env:BLUE_APP_POOL_NAME } else { $env:GREEN_APP_POOL_NAME }
$inactiveSite = if ($inactiveSlot -eq 'Blue') { $env:BLUE_SITE_NAME } else { $env:GREEN_SITE_NAME }
$inactivePath = if ($inactiveSlot -eq 'Blue') { $env:BLUE_DEPLOY_PATH } else { $env:GREEN_DEPLOY_PATH }
$inactiveLiveUrl = if ($inactiveSlot -eq 'Blue') { $env:BLUE_HEALTH_LIVE_URL } else { $env:GREEN_HEALTH_LIVE_URL }
$inactiveReadyUrl = if ($inactiveSlot -eq 'Blue') { $env:BLUE_HEALTH_READY_URL } else { $env:GREEN_HEALTH_READY_URL }
$activeReadyUrl = if ($activeSlot -eq 'Blue') { $env:BLUE_HEALTH_READY_URL } else { $env:GREEN_HEALTH_READY_URL }
$trafficSwitchScript = [Environment]::ExpandEnvironmentVariables($env:TRAFFIC_SWITCH_SCRIPT)

if (-not (Test-Path -LiteralPath $trafficSwitchScript -PathType Leaf)) {
    throw "Traffic switch hook was not found: $trafficSwitchScript"
}

Import-Module WebAdministration
foreach ($site in @($env:BLUE_SITE_NAME, $env:GREEN_SITE_NAME)) {
    if (-not (Get-Website -Name $site -ErrorAction SilentlyContinue)) {
        throw "IIS site '$site' was not found on this instance."
    }
}

$timestamp = Get-Date -Format 'yyyyMMddHHmmss'
$backupRoot = Join-Path (Split-Path -Parent $inactivePath) "QuickCommerce.Api.$inactiveSlot.backups"
$backupPath = Join-Path $backupRoot $timestamp
$stagingPath = Join-Path $env:TEMP "QuickCommerce.Api.$inactiveSlot.$timestamp"
$switchAttempted = $false
$deploymentSucceeded = $false

function Invoke-HealthCheck([string]$url, [string]$name) {
    $lastError = $null
    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) {
                Write-Host "$name health check passed: $url"
                return
            }
            $lastError = "HTTP $($response.StatusCode)"
        }
        catch {
            $lastError = $_.Exception.Message
        }
        Start-Sleep -Seconds 2
    }
    throw "$name health check failed for ${url}: $lastError"
}

function Invoke-TrafficSwitch([string]$fromSlot, [string]$toSlot) {
    Write-Host "Switching traffic from $fromSlot to $toSlot using $trafficSwitchScript"
    $global:LASTEXITCODE = 0
    & $trafficSwitchScript -FromSlot $fromSlot -ToSlot $toSlot
    if ($LASTEXITCODE -is [int] -and $LASTEXITCODE -ne 0) {
        throw "Traffic switch hook failed with exit code $LASTEXITCODE."
    }
}

try {
    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
    Expand-Archive -LiteralPath $ArtifactPath -DestinationPath $stagingPath -Force

    if (Test-Path -LiteralPath $inactivePath) {
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        Copy-Item -Path (Join-Path $inactivePath '*') -Destination $backupPath -Recurse -Force
    }

    Stop-WebAppPool -Name $inactiveAppPool
    New-Item -ItemType Directory -Path $inactivePath -Force | Out-Null
    Remove-Item -Path (Join-Path $inactivePath '*') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $stagingPath '*') -Destination $inactivePath -Recurse -Force
    Start-WebAppPool -Name $inactiveAppPool

    Invoke-HealthCheck $inactiveLiveUrl "$inactiveSlot live"
    Invoke-HealthCheck $inactiveReadyUrl "$inactiveSlot ready"
    Invoke-HealthCheck $activeReadyUrl "$activeSlot pre-switch ready"

    $switchAttempted = $true
    Invoke-TrafficSwitch $activeSlot $inactiveSlot
    Invoke-HealthCheck $env:TRAFFIC_HEALTH_URL 'Traffic target'

    $deploymentSucceeded = $true
    Write-Host "Blue-Green deployment succeeded. Active slot is now $inactiveSlot."
    Write-Host "Retained previous slot $activeSlot for rollback. Update BLUE_GREEN_ACTIVE_SLOT to $inactiveSlot for the next deployment."
}
catch {
    Write-Error "Blue-Green deployment failed: $($_.Exception.Message)"
    if ($switchAttempted) {
        try {
            Invoke-TrafficSwitch $inactiveSlot $activeSlot
            Invoke-HealthCheck $env:TRAFFIC_HEALTH_URL 'Rollback traffic target'
            Write-Warning "Traffic rolled back to $activeSlot."
        }
        catch {
            Write-Error "Automatic traffic rollback failed: $($_.Exception.Message)"
        }
    }

    if (Test-Path -LiteralPath $backupPath) {
        Write-Warning "Restoring previous $inactiveSlot files from $backupPath"
        Stop-WebAppPool -Name $inactiveAppPool -ErrorAction SilentlyContinue
        Remove-Item -Path (Join-Path $inactivePath '*') -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item -Path (Join-Path $backupPath '*') -Destination $inactivePath -Recurse -Force
        Start-WebAppPool -Name $inactiveAppPool -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
    if ($deploymentSucceeded) {
        Write-Host "Previous slot remains available. Inactive-slot backup retained at $backupPath."
    }
}
