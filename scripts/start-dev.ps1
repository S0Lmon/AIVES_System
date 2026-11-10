param([switch]$DownloadSpeechModels)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK is required: https://dotnet.microsoft.com/download'
}

if ($DownloadSpeechModels) {
    & (Join-Path $PSScriptRoot 'download-speech-models.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'Speech model download failed.' }
}

Write-Host 'Starting AIVES at http://localhost:5201 (Ctrl+C to stop).'
Write-Host 'Default database: SQL Server LocalDB. Browser STT requires Chrome or Edge.'
dotnet run --project AIVES.WebRazor --launch-profile http
if ($LASTEXITCODE -ne 0) { throw "AIVES exited with code $LASTEXITCODE." }
