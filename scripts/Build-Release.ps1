param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build .\AllTimeSoundTrigger.csproj -c $Configuration
    New-Item -ItemType Directory -Force -Path .\dist | Out-Null
    Copy-Item -LiteralPath .\bin\$Configuration\AllTimeSoundTrigger\latest.zip -Destination .\dist\latest.zip -Force
    Write-Host "Release package written to dist\latest.zip"
}
finally {
    Pop-Location
}
