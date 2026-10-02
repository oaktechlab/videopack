param(
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$data = Join-Path $root 'data'
$ffmpegDirectory = Join-Path $data 'ffmpeg'
$licenseDirectory = Join-Path $data 'licenses'
$assetDirectory = Join-Path $data 'assets'
$downloadUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

function Get-ProductVersion {
    param([string]$ProjectPath)

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath
    $versionNode = $projectXml.SelectSingleNode('//Version')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw 'No se encuentra <Version> en src\VideoPack\VideoPack.csproj.'
    }
    $version = $versionNode.InnerText.Trim()
    if ($version -notmatch '^[0-9A-Za-z][0-9A-Za-z\.\-_]*$') {
        throw "La versión '$version' no es válida para el nombre del zip."
    }
    return $version
}

function Copy-RequiredAsset {
    param([string]$RelativeSource, [string]$RelativeDestination)

    $source = Join-Path $root $RelativeSource
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "No se encuentra el asset requerido: $RelativeSource"
    }
    $destination = Join-Path $data $RelativeDestination
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

New-Item -ItemType Directory -Force -Path $ffmpegDirectory, $licenseDirectory | Out-Null
Copy-RequiredAsset 'resources\bumper\bumper_tecnalia_in_landscape.mp4' 'assets\intros\bumper_tecnalia_in_landscape.mp4'
Copy-RequiredAsset 'resources\bumper\bumper_tecnalia_out_landscape.mp4' 'assets\outros\bumper_tecnalia_out_landscape.mp4'
Copy-RequiredAsset 'resources\bumper\bumper_tecnalia_out_portrait.mp4' 'assets\outros\bumper_tecnalia_out_portrait.mp4'

$watermarkDirectory = Join-Path $assetDirectory 'watermarks'
New-Item -ItemType Directory -Force -Path $watermarkDirectory | Out-Null
Get-ChildItem -LiteralPath (Join-Path $root 'resources\logos') -Filter '*.png' -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $watermarkDirectory $_.Name) -Force
}

if (-not (Test-Path -LiteralPath (Join-Path $ffmpegDirectory 'ffmpeg.exe')) -or
    -not (Test-Path -LiteralPath (Join-Path $ffmpegDirectory 'ffprobe.exe'))) {
    $archive = Join-Path $env:TEMP "videopack-ffmpeg-$PID.zip"
    $extractDirectory = Join-Path $env:TEMP "videopack-ffmpeg-$PID"
    try {
        Write-Host 'Descargando FFmpeg Essentials (aprox. 115 MB)...'
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath $extractDirectory -Force
        $ffmpeg = Get-ChildItem -LiteralPath $extractDirectory -Filter 'ffmpeg.exe' -File -Recurse | Select-Object -First 1
        if ($null -eq $ffmpeg) { throw 'El paquete descargado no contiene ffmpeg.exe.' }
        $binDirectory = $ffmpeg.Directory.FullName
        foreach ($name in @('ffmpeg.exe', 'ffprobe.exe')) {
            $source = Join-Path $binDirectory $name
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "El paquete FFmpeg no contiene $name." }
        }
        Get-ChildItem -LiteralPath $binDirectory -File | Copy-Item -Destination $ffmpegDirectory -Force
        Get-ChildItem -LiteralPath $extractDirectory -Include 'LICENSE*', 'COPYING*', 'README*' -File -Recurse |
            Copy-Item -Destination $licenseDirectory -Force
    }
    finally {
        Remove-Item -LiteralPath $archive -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$ffmpegPath = Join-Path $ffmpegDirectory 'ffmpeg.exe'
$versionOutput = & $ffmpegPath -version 2>&1
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg no se ha podido iniciar desde data/ffmpeg.' }
Write-Host ($versionOutput | Select-Object -First 1)

if ($PrepareOnly) {
    Write-Host 'Assets y FFmpeg preparados para desarrollo.'
    return
}

$project = Join-Path $root 'src\VideoPack\VideoPack.csproj'
$publishDirectory = Join-Path $root "dist\.publish-$PID"
$distribution = Join-Path $root 'dist\VideoPack'
try {
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'La publicación de VideoPack ha fallado.' }

    New-Item -ItemType Directory -Force -Path $distribution | Out-Null
    Copy-Item -LiteralPath (Join-Path $publishDirectory 'VideoPack.exe') -Destination (Join-Path $distribution 'VideoPack.exe') -Force
    $outputData = Join-Path $distribution 'data'
    New-Item -ItemType Directory -Force -Path $outputData | Out-Null
    Copy-Item -Path (Join-Path $data '*') -Destination $outputData -Recurse -Force
    $outputTemp = Join-Path $outputData 'temp'
    New-Item -ItemType Directory -Force -Path $outputTemp, (Join-Path $outputData 'logs') | Out-Null
    Get-ChildItem -LiteralPath $outputTemp -Force | Remove-Item -Recurse -Force
    Write-Host "Portable listo: $distribution"

    $productVersion = Get-ProductVersion $project
    $archivePath = Join-Path $root "dist\VideoPack-$productVersion.zip"
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    Compress-Archive -Path (Join-Path $distribution '*') -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Host "Zip listo: $archivePath"
}
finally {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force -ErrorAction SilentlyContinue
}