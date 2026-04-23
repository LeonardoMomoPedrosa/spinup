param(
    [string]$ServiceName = "SpinUp.Api"
)

$ErrorActionPreference = "Stop"

$existing = Get-Service -Name "$ServiceName" -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service $ServiceName not found."
    exit 0
}

if ($existing.Status -ne "Stopped") {
    Write-Host "Stopping service..."
    Stop-Service -Name "$ServiceName" -Force
}

Write-Host "Deleting service..."
sc.exe delete "$ServiceName" | Out-Null
Write-Host "Service $ServiceName removed."
