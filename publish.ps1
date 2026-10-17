# Gera dist\LiteDeck.exe: Release, win-x64, arquivo único, dependente do .NET 10 Desktop Runtime (sem self-contained, sem trimming).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$dist = Join-Path $root 'dist'

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }

dotnet publish (Join-Path $root 'LiteDeck\LiteDeck.csproj') `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=none `
    -o $dist
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item (Join-Path $root 'install.ps1'), (Join-Path $root 'uninstall.ps1') $dist
Get-ChildItem $dist | Format-Table Name, Length
