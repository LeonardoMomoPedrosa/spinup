param(
    [string]$ServiceName = "SpinUp.Api",
    [string]$Url = "http://localhost:5042",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$installScript = Join-Path $repoRoot "scripts\windows\install-service.ps1"

Write-Host "Updating $ServiceName by republishing and reinstalling..."
powershell -ExecutionPolicy Bypass -File "$installScript" -ServiceName "$ServiceName" -Url "$Url" -Configuration "$Configuration"
