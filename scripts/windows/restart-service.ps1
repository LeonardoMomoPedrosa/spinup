param(
    [string]$ServiceName = "SpinUp.Api",
    [string]$Url = "http://localhost:5042"
)

$ErrorActionPreference = "Stop"

Write-Host "Restarting service $ServiceName..."
Restart-Service -Name "$ServiceName" -Force

$healthUrl = "$Url/health/live"
Write-Host "Checking live health endpoint immediately (no grace period)..."
for ($i = 1; $i -le 10; $i++) {
    try {
        Invoke-WebRequest -Uri "$healthUrl" -UseBasicParsing -TimeoutSec 3 | Out-Null
        Write-Host "Service restarted and healthy at $Url"
        exit 0
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

throw "Service restart completed but health check failed at $healthUrl."
