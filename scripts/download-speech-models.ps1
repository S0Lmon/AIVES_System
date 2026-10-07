# Downloads the offline speech models for the AI viva (Whisper.net + sherpa-onnx Piper voices).
#   .\scripts\download-speech-models.ps1                       # into AIVES.WebRazor\App_Data\speech-models
#   .\scripts\download-speech-models.ps1 -Target D:\models -WhisperModel medium
# About 600 MB with the default "small" Whisper model. Existing files are kept.
param(
    [string]$Target = (Join-Path $PSScriptRoot '..\AIVES.WebRazor\App_Data\speech-models'),
    [ValidateSet('base', 'small', 'medium')]
    [string]$WhisperModel = 'small',
    [switch]$WithPreviewModel
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$whisperDir = Join-Path $Target 'whisper'
$ttsDir = Join-Path $Target 'tts'
New-Item -ItemType Directory -Force $whisperDir, $ttsDir | Out-Null

function Get-Whisper([string]$name) {
    $file = Join-Path $whisperDir "ggml-$name.bin"
    if (Test-Path $file) { Write-Host "ok   $file"; return }
    Write-Host "get  ggml-$name.bin"
    Invoke-WebRequest "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-$name.bin" -OutFile "$file.part"
    Move-Item "$file.part" $file
}

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

Get-Whisper $WhisperModel
if ($WithPreviewModel) { Get-Whisper 'base' }
Get-Voice 'vits-piper-vi_VN-vais1000-medium'
Get-Voice 'vits-piper-en_US-amy-low'

Write-Host ''
Write-Host "Models are in $((Resolve-Path $Target).Path)"
if ($WhisperModel -ne 'small') { Write-Host "Set Speech:WhisperModel to whisper/ggml-$WhisperModel.bin" }
if ($WithPreviewModel) { Write-Host 'Set Speech:WhisperPreviewModel to whisper/ggml-base.bin for a faster live preview' }
