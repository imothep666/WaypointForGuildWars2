#!/usr/bin/env pwsh
# Builds the desktop app for Linux as one folder that installs itself: the app, install.sh, the
# menu entry and the icon. Copy the folder to the Linux machine and run install.sh there.
#
#   .\packaging\linux\build.ps1                 -> artifacts\linux-arm64  (a Switch under L4T, a Pi)
#   .\packaging\linux\build.ps1 -Rid linux-x64  -> artifacts\linux-x64    (an ordinary PC)
#
# Self-contained: the machine needs no .NET of its own. It does need ICU and fontconfig, which
# install.sh checks for. Runs under PowerShell 7 on Linux too, so the build can also happen on the
# Switch itself if the .NET SDK is installed there.

param(
    [ValidateSet("linux-arm64", "linux-x64")]
    [string]$Rid = "linux-arm64"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$project = Join-Path $root "GuildWarsCompanion.Avalonia.Desktop\GuildWarsCompanion.Avalonia.Desktop.csproj"
$icon = Join-Path $root "GuildWarsCompanion.Avalonia.Android\Resources\mipmap-xxxhdpi\appicon.png"
$out = Join-Path $root "artifacts\$Rid"
$app = Join-Path $out "app"

# A fresh folder each time: a file left over from an earlier build would be installed as if it
# belonged to this one.
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish $project -r $Rid --self-contained true -c Release -o $app -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# install.sh with LF line endings whatever the checkout did: bash reads a CRLF script as commands
# ending in a carriage return, and fails with "$'\r': command not found". .gitattributes keeps *.sh
# at LF in the repository; this keeps it at LF here even if that ever slips.
$script = (Get-Content (Join-Path $PSScriptRoot "install.sh") -Raw) -replace "`r`n", "`n"
[System.IO.File]::WriteAllText((Join-Path $out "install.sh"), $script, [System.Text.UTF8Encoding]::new($false))

Copy-Item $icon (Join-Path $out "gw2companion.png")

# What install.sh checks against the machine it runs on, and what it reports as installed.
$version = (git -C $root describe --always --dirty 2>$null)
if (-not $version) { $version = "unknown" }
[System.IO.File]::WriteAllText((Join-Path $out "BUILD"), "rid=$Rid`nversion=$version`n", [System.Text.UTF8Encoding]::new($false))

$size = [math]::Round(((Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)

Write-Host ""
Write-Host "Built $Rid ($version), $size MB: $out"
Write-Host ""
Write-Host "Next, copy the folder over, for example:"
Write-Host "  scp -r `"$out`" user@switch:~/gw2companion-build"
Write-Host "and on that machine:"
Write-Host "  bash ~/gw2companion-build/install.sh            (add --software if the window stays black)"
