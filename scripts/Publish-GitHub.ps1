param(
    [string]$Repository = "FF14Yoshiko/FF14musicplayer",
    [string]$Branch = "main"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & "$PSScriptRoot\Build-Release.ps1"

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI gh 未安装。请先安装 gh 并运行 gh auth login。"
    }

    gh auth status

    if (-not (Test-Path .git)) {
        git init
        git branch -M $Branch
        git remote add origin "https://github.com/$Repository.git"
    }

    git add README.md .gitignore AllTimeSoundTrigger.csproj AllTimeSoundTrigger.json plugin.json pluginmaster.json config.example.json images src tests scripts dist
    git commit -m "Prepare public Dalamud plugin release"
    git push -u origin $Branch
}
finally {
    Pop-Location
}
