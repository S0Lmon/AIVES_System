$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$modelsRoot = Join-Path $root 'AIVES.WebRazor\App_Data\speech-models'

$models = @(
    @{
        Language = 'vi-VN'
        Name = 'vosk-model-small-vn-0.4'
        Url = 'https://alphacephei.com/vosk/models/vosk-model-small-vn-0.4.zip'
    },
    @{
        Language = 'en-US'
        Name = 'vosk-model-small-en-us-0.15'
        Url = 'https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip'
    }
)

foreach ($model in $models) {
    $destination = Join-Path $modelsRoot $model.Language
    if ((Test-Path (Join-Path $destination 'conf\model.conf')) -and
        (Test-Path (Join-Path $destination 'am\final.mdl'))) {
        Write-Host "$($model.Language) Vosk model is already installed."
        continue
    }
    if (Test-Path $destination) {
        throw "The model destination exists but is incomplete: $destination"
    }

    $work = Join-Path $env:TEMP ("aives-vosk-" + [guid]::NewGuid().ToString('N'))
    $archive = Join-Path $work 'model.zip'
    $extract = Join-Path $work 'extract'
    New-Item -ItemType Directory -Path $extract -Force | Out-Null
    try {
        Write-Host "Downloading $($model.Language) Vosk model..."
        & curl.exe -L --fail --show-error --output $archive $model.Url
        if ($LASTEXITCODE -ne 0) {
            throw "Could not download the $($model.Language) Vosk model (curl exit code $LASTEXITCODE)."
        }

        Expand-Archive -LiteralPath $archive -DestinationPath $extract
        $source = Join-Path $extract $model.Name
        if (-not (Test-Path (Join-Path $source 'conf\model.conf')) -or
            -not (Test-Path (Join-Path $source 'am\final.mdl'))) {
            throw "The downloaded $($model.Language) archive does not contain a valid Vosk model."
        }

        New-Item -ItemType Directory -Path $modelsRoot -Force | Out-Null
        Move-Item -LiteralPath $source -Destination $destination
        Write-Host "$($model.Language) Vosk model installed at $destination"
    }
    finally {
        if (Test-Path $work) {
            Remove-Item -LiteralPath $work -Recurse -Force
        }
    }
}
