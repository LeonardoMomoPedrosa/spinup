param(
    [string]$ServiceName = "SpinUp.Api",
    [string]$Url = "http://localhost:5042",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$installScript = Join-Path $PSScriptRoot "install-service.ps1"

if (-not (Test-Path $installScript)) {
    throw "Install script not found at $installScript"
}

powershell -ExecutionPolicy Bypass -File "$installScript" -ServiceName "$ServiceName" -Url "$Url" -Configuration "$Configuration"
