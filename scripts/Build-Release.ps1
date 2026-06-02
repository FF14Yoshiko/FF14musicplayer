param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build .\AllTimeSoundTrigger.csproj -c $Configuration

    function Add-FolderToZip {
        param(
            [Parameter(Mandatory = $true)]
            [string]$ZipPath,

            [Parameter(Mandatory = $true)]
            [string]$SourcePath,

            [Parameter(Mandatory = $true)]
            [string]$ZipFolder
        )

        if (-not (Test-Path -LiteralPath $SourcePath)) {
            return
        }

        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem

        $resolvedSource = (Resolve-Path -LiteralPath $SourcePath).Path.TrimEnd('\', '/')
        $archive = [System.IO.Compression.ZipFile]::Open($ZipPath, [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            $prefix = $ZipFolder.Trim('/').TrimEnd('/')
            $existingEntries = @($archive.Entries | Where-Object {
                ($_.FullName -replace '\\', '/').StartsWith("$prefix/", [System.StringComparison]::OrdinalIgnoreCase)
            })
            foreach ($entry in $existingEntries) {
                $entry.Delete()
            }

            Get-ChildItem -LiteralPath $resolvedSource -Recurse -File | ForEach-Object {
                $relative = $_.FullName.Substring($resolvedSource.Length).TrimStart('\', '/')
                $entryName = ($prefix + '/' + ($relative -replace '\\', '/')).TrimStart('/')
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive,
                    $_.FullName,
                    $entryName,
                    [System.IO.Compression.CompressionLevel]::Optimal
                ) | Out-Null
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    $packageDirectory = Join-Path (Join-Path .\bin $Configuration) 'AllTimeSoundTrigger'
    if (Test-Path -LiteralPath .\docs) {
        New-Item -ItemType Directory -Force -Path (Join-Path $packageDirectory 'docs') | Out-Null
        Copy-Item -Path .\docs\* -Destination (Join-Path $packageDirectory 'docs') -Recurse -Force
    }

    if (Test-Path -LiteralPath .\example-sounds) {
        New-Item -ItemType Directory -Force -Path (Join-Path $packageDirectory 'example-sounds') | Out-Null
        Copy-Item -Path .\example-sounds\* -Destination (Join-Path $packageDirectory 'example-sounds') -Recurse -Force
    }

    Add-FolderToZip -ZipPath (Join-Path $packageDirectory 'latest.zip') -SourcePath .\docs -ZipFolder docs
    Add-FolderToZip -ZipPath (Join-Path $packageDirectory 'latest.zip') -SourcePath .\example-sounds -ZipFolder example-sounds

    New-Item -ItemType Directory -Force -Path .\dist | Out-Null
    Copy-Item -LiteralPath (Join-Path $packageDirectory 'latest.zip') -Destination .\dist\latest.zip -Force
    Write-Host "Release package written to dist\latest.zip"
}
finally {
    Pop-Location
}
