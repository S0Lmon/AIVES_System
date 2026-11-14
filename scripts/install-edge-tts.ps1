$ErrorActionPreference = "Stop"

$python = Get-Command python -ErrorAction Stop
$requirements = Join-Path $PSScriptRoot "requirements-edge-tts.txt"

& $python.Source -m pip install -r $requirements
if ($LASTEXITCODE -ne 0) {
    throw "Edge TTS installation failed with exit code $LASTEXITCODE."
}

Write-Host "Edge TTS installed for Python at $($python.Source). Configure EdgeTts:PythonPath to this executable if it is not on the AIVES service PATH."
