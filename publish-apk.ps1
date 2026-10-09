#!/usr/bin/env pwsh
# Builds the sideload APK. See GuildWarsCompanion.Avalonia.Android/Properties/PublishProfiles/SideloadApk.md
#
#   .\publish-apk.ps1              -> arm64, for a phone
#   .\publish-apk.ps1 -Rid android-x64  -> for the x86_64 emulator

param(
    [string]$Rid = "android-arm64"
)

$ErrorActionPreference = "Stop"
$head = Join-Path $PSScriptRoot "GuildWarsCompanion.Avalonia.Android"
$project = Join-Path $head "GuildWarsCompanion.Avalonia.Android.csproj"

dotnet publish $project -p:PublishProfile=SideloadApk -p:RuntimeIdentifier=$Rid
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$apk = Join-Path $head "bin\Release\net10.0-android\$Rid\publish\com.mpsoft.gw2companion-Signed.apk"

if (-not (Test-Path $apk)) {
    throw "Expected the signed APK at $apk but it is not there."
}

# Checked, not assumed. After a failed signing step the next publish can count itself up to date
# and leave an unsigned file behind under the -Signed name, which the phone then refuses.
# The fingerprint is the app's own certificate; see SideloadApk.md.
$expected = "d3859e06843f4f79bfca2a85f01fae447a39150953b3529feda8943b86ef0340"
$sdk = "C:\Program Files (x86)\Android"
$apksigner = Get-ChildItem "$sdk\android-sdk\build-tools" -Recurse -Filter apksigner.bat -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
$jdk = Get-ChildItem "$sdk\openjdk" -Directory -ErrorAction SilentlyContinue | Select-Object -First 1

if ($apksigner -and $jdk) {
    $env:JAVA_HOME = $jdk.FullName
    $env:PATH = "$($jdk.FullName)\bin;$env:PATH"
    $certs = & $apksigner.FullName verify --print-certs $apk 2>&1 | Out-String

    if ($LASTEXITCODE -ne 0 -or $certs -notmatch $expected) {
        throw "The APK is not signed with the app's key. Delete bin\Release and obj\Release and publish again.`n$certs"
    }
} else {
    Write-Warning "apksigner or a JDK not found under $sdk; the signature was not checked."
}

$size = [math]::Round((Get-Item $apk).Length / 1MB, 1)
Write-Host ""
Write-Host "APK ready ($size MB):" -ForegroundColor Green
Write-Host "  $apk"
Write-Host ""
Write-Host "Install over USB with:"
Write-Host "  adb install -r `"$apk`""
