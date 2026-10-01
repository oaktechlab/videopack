$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

& (Join-Path $root 'build-portable.ps1') -PrepareOnly
if ($LASTEXITCODE -ne 0) { throw 'No se pudieron preparar FFmpeg y los assets.' }

dotnet run --project (Join-Path $root 'src\VideoPack\VideoPack.csproj') `
    --configuration Debug --no-launch-profile -p:SelfContained=false -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'VideoPack no pudo iniciarse en modo desarrollo.' }