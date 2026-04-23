param(
    [string]$ServiceName = "SpinUp.Api",
    [string]$Url = "http://localhost:5042",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Invoke-Sc {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $output = & sc.exe @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($output) {
        $output | ForEach-Object { Write-Host $_ }
    }
    if ($exitCode -ne 0) {
        throw "sc.exe failed with exit code $exitCode for arguments: $($Arguments -join ' ')"
    }
}

if (-not (Test-IsAdministrator)) {
    throw "Please run this script from an elevated PowerShell prompt (Run as Administrator)."
}

$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path
$publishDir = Join-Path $repoRoot "artifacts\windows-service"
$projectPath = Join-Path $repoRoot "src\SpinUp.Api\SpinUp.Api.csproj"
$frontendPath = Join-Path $repoRoot "web\spinup-ui"
$exePath = Join-Path $publishDir "SpinUp.Api.exe"
$storageRoot = "C:\ProgramData\SpinUp"
$targetDbPath = Join-Path $storageRoot "spinup.db"
$dbCandidates = @(
    (Join-Path $repoRoot "src\SpinUp.Api\spinup.db"),
    (Join-Path $publishDir "spinup.db"),
    "C:\Windows\System32\spinup.db"
)

Write-Host "Building frontend..."
Push-Location "$frontendPath"
try {
    npm install
    npm run build
}
finally {
    Pop-Location
}

Write-Host "Publishing SpinUp.Api..."
dotnet publish "$projectPath" -c "$Configuration" -o "$publishDir"

if (-not (Test-Path "$exePath")) {
    throw "Published exe not found at $exePath"
}

Write-Host "Ensuring storage root exists at $storageRoot..."
New-Item -Path "$storageRoot" -ItemType Directory -Force | Out-Null

if (-not (Test-Path "$targetDbPath")) {
    foreach ($candidate in $dbCandidates) {
        if (Test-Path "$candidate") {
            Write-Host "Seeding service database from $candidate"
            Copy-Item -Path "$candidate" -Destination "$targetDbPath" -Force
            break
        }
    }
}

if (-not (Test-Path "$targetDbPath")) {
    Write-Host "No existing database found; service will initialize a new database at $targetDbPath"
}

Write-Host "Ensuring service does not already exist..."
$existing = Get-Service -Name "$ServiceName" -ErrorAction SilentlyContinue
if ($existing) {
    if ($existing.Status -ne "Stopped") {
        Stop-Service -Name "$ServiceName" -Force
    }
    Invoke-Sc -Arguments @("delete", "$ServiceName")
    Start-Sleep -Seconds 1
}

$serviceCommand = "`"$exePath`" --urls $Url"
Write-Host "Creating service $ServiceName..."
Write-Host "Service command: $serviceCommand"
New-Service -Name "$ServiceName" -BinaryPathName "$serviceCommand" -DisplayName "$ServiceName" -Description "SpinUp local service manager API." -StartupType Automatic
Invoke-Sc -Arguments @("failure", "$ServiceName", "reset= 86400", "actions= restart/5000/restart/5000/restart/5000")

$created = Get-Service -Name "$ServiceName" -ErrorAction SilentlyContinue
if (-not $created) {
    throw "Service '$ServiceName' was not created. See sc.exe output above."
}

Write-Host "Starting service..."
Start-Service -Name "$ServiceName"

Write-Host "Checking live health endpoint immediately (no grace period)..."
$healthUrl = "$Url/health/live"
$attempts = 15
for ($i = 1; $i -le $attempts; $i++) {
    try {
        Invoke-WebRequest -Uri "$healthUrl" -UseBasicParsing -TimeoutSec 3 | Out-Null
        Write-Host "Service installed and healthy at $Url"
        exit 0
    }
    catch {
        Start-Sleep -Seconds 2
    }
}

throw "Service started but did not pass health check at $healthUrl."
