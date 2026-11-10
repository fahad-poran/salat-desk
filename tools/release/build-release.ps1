# Builds a signed Salat Desk release APK.
#
#   powershell -ExecutionPolicy Bypass -File tools\release\build-release.ps1
#
# First run: creates the signing key at %USERPROFILE%\.salatdesk\salatdesk.keystore (keytool asks
# for a password and your name). Keep that file and password safe: every future update, including
# Google Play, must be signed with the same key. The password is only typed into this window; it is
# not stored anywhere.

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$keyDir = Join-Path $env:USERPROFILE '.salatdesk'
$keystore = Join-Path $keyDir 'salatdesk.keystore'
$keytool = Join-Path $env:LOCALAPPDATA 'Microsoft\jdk\bin\keytool.exe'
$project = Join-Path $root 'src\DeskDashboard.App\DeskDashboard.App.csproj'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.ApplicationDisplayVersion } | Select-Object -First 1).ApplicationDisplayVersion

if (-not (Test-Path $keystore)) {
    Write-Host "No signing key yet. Creating $keystore" -ForegroundColor Cyan
    Write-Host 'Choose a password of at least 6 characters and remember it.' -ForegroundColor Cyan
    New-Item -ItemType Directory -Force $keyDir | Out-Null
    & $keytool -genkeypair -v -keystore $keystore -alias salatdesk -keyalg RSA -keysize 2048 -validity 10000
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $keystore)) { throw 'Creating the signing key failed.' }
    Write-Host 'Signing key created. Back up this file somewhere safe.' -ForegroundColor Green
}

$secure = Read-Host 'Signing key password' -AsSecureString
$env:SALATDESK_KEY_PASS = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

try {
    Write-Host "Building Salat Desk $version (Release)..." -ForegroundColor Cyan
    dotnet publish $project -f net10.0-android -c Release `
        -p:AndroidPackageFormats=apk -p:AndroidKeyStore=true `
        "-p:AndroidSigningKeyStore=$keystore" -p:AndroidSigningKeyAlias=salatdesk `
        -p:AndroidSigningKeyPass=env:SALATDESK_KEY_PASS -p:AndroidSigningStorePass=env:SALATDESK_KEY_PASS
    if ($LASTEXITCODE -ne 0) { throw 'The build failed. Check the password and the messages above.' }
}
finally {
    Remove-Item Env:SALATDESK_KEY_PASS -ErrorAction SilentlyContinue
}

$apk = Get-ChildItem (Join-Path $root 'src\DeskDashboard.App\bin\Release') -Recurse -Filter '*-Signed.apk' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null
$out = Join-Path $dist "SalatDesk-$version.apk"
Copy-Item $apk.FullName $out -Force
Write-Host "Done: $out" -ForegroundColor Green
