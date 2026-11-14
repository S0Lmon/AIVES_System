$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is required: https://dotnet.microsoft.com/download'
}

Write-Host 'Starting AIVES at http://localhost:5201 (Ctrl+C to stop).'
Write-Host 'Default database: SQL Server LocalDB. Question audio uses installed Windows voices.'
Write-Host 'Live speech recognition uses local Vosk models; install them with scripts/download-vosk-models.ps1.'
dotnet run --project AIVES.WebRazor --launch-profile http
if ($LASTEXITCODE -ne 0) { throw "AIVES exited with code $LASTEXITCODE." }
