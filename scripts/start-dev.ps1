# Starts everything the AI viva needs on a development machine, in one go:
#   1. SQL Server container (aives-sqlserver, port 14330)
#   2. VieNeu-TTS container (vieneu-tts-api-cpu, port 8800)  - reads Vietnamese questions aloud
#   3. AIVES web app (http://localhost:5201)
# Usage:  .\scripts\start-dev.ps1            # start the containers, then run the app in this window
#         .\scripts\start-dev.ps1 -NoApp     # only the containers
# Stop the app with Ctrl+C. The containers keep running (they restart with Docker); stop them with
#   docker stop aives-sqlserver vieneu-tts-api-cpu
param(
    [switch]$NoApp
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')

function Start-Container([string]$name, [string]$hint) {
    $state = docker inspect --format '{{.State.Status}}' $name 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Container '$name' does not exist. $hint"
        return $false
    }
    if ($state -ne 'running') { docker start $name | Out-Null }
    Write-Host "ok   $name"
    return $true
}

function Wait-Until([string]$label, [scriptblock]$test, [int]$seconds = 120) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        try { if (& $test) { Write-Host "up   $label"; return $true } } catch { }
        Start-Sleep -Seconds 2
    }
    Write-Warning "$label did not become ready within $seconds s"
    return $false
}

Start-Container 'aives-sqlserver' 'Create it with: docker compose up -d sqlserver (see README / compose.yaml).' | Out-Null
Wait-Until 'SQL Server (localhost,14330)' { (Test-NetConnection localhost -Port 14330 -WarningAction SilentlyContinue).TcpTestSucceeded } | Out-Null

# VieNeu is optional: without it questions are read by Gemini or the offline Piper voice.
if (Start-Container 'vieneu-tts-api-cpu' 'Set it up once: see the VieNeu-TTS section in README.md. Continuing without it.') {
    Wait-Until 'VieNeu-TTS (http://localhost:8800/health)' { (Invoke-RestMethod http://localhost:8800/health -TimeoutSec 3).status -eq 'ok' } 240 | Out-Null
}

if ($NoApp) { return }

if (Get-NetTCPConnection -LocalPort 5201 -State Listen -ErrorAction SilentlyContinue) {
    Write-Host 'AIVES is already running on http://localhost:5201 (stop it first to restart).'
    return
}

Write-Host 'Starting AIVES on http://localhost:5201 (Ctrl+C to stop)...'
Set-Location $root
dotnet run --project AIVES.WebRazor --launch-profile http
