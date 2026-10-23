# Downloads the offline voices for the AI viva (sherpa-onnx Piper, the fallback when VieNeu-TTS is not used).
#   .\scripts\download-speech-models.ps1                       # into AIVES.WebRazor\App_Data\speech-models
#   .\scripts\download-speech-models.ps1 -Target D:\models
# About 150 MB. Existing files are kept.
param(
    [string]$Target = (Join-Path $PSScriptRoot '..\AIVES.WebRazor\App_Data\speech-models')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$ttsDir = Join-Path $Target 'tts'
New-Item -ItemType Directory -Force $ttsDir | Out-Null

function Get-Voice([string]$name) {
    $folder = Join-Path $ttsDir $name
    if (Test-Path (Join-Path $folder 'tokens.txt')) { Write-Host "ok   $folder"; return }
    Write-Host "get  $name"
    # Each archive is unpacked in its own empty folder before it is moved into place.
    $work = Join-Path ([IO.Path]::GetTempPath()) ("aives-voice-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $work | Out-Null
    try {
        $archive = Join-Path $work "$name.tar.bz2"
        Invoke-WebRequest "https://github.com/k2-fsa/sherpa-onnx/releases/download/tts-models/$name.tar.bz2" -OutFile $archive
        & "$env:SystemRoot\System32\tar.exe" -xjf $archive -C $work
        if ($LASTEXITCODE -ne 0) { throw "tar failed for $name" }
        Move-Item (Join-Path $work $name) $folder
    }
    finally {
        Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
    }
}

Get-Voice 'vits-piper-vi_VN-vais1000-medium'
Get-Voice 'vits-piper-en_US-amy-low'

Write-Host ''
Write-Host "Models are in $((Resolve-Path $Target).Path)"
