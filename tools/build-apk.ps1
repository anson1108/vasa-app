param(
    [string]$SdkPath = $env:ANDROID_HOME,
    [string]$JavaHome = $env:JAVA_HOME,
    [switch]$SkipFrontend
)
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
if (-not $SdkPath) { $SdkPath = Join-Path $env:LOCALAPPDATA 'Android\Sdk' }
if (-not (Test-Path (Join-Path $SdkPath 'platforms\android-35\android.jar'))) { throw 'Android Platform 35 not found. Pass -SdkPath.' }
if (-not $JavaHome -or -not (Test-Path (Join-Path $JavaHome 'bin\java.exe'))) { throw 'JDK 21 not found. Pass -JavaHome.' }
$env:JAVA_HOME = (Resolve-Path $JavaHome).Path
$env:ANDROID_HOME = (Resolve-Path $SdkPath).Path
$android = Join-Path $project 'frontend\android'
Set-Content -LiteralPath (Join-Path $android 'local.properties') -Value ('sdk.dir=' + $env:ANDROID_HOME.Replace('\','/')) -Encoding ascii
Push-Location (Join-Path $project 'frontend')
try {
    if (-not $SkipFrontend) {
        & npm.cmd ci
        if ($LASTEXITCODE) { throw 'npm ci failed' }
        & npm.cmd run android:sync
        if ($LASTEXITCODE) { throw 'Frontend build / sync failed' }
    }
    Set-Location $android
    & .\gradlew.bat assembleDebug --no-daemon --console=plain
    if ($LASTEXITCODE) { throw 'Android build failed' }
    $apk = Join-Path $android 'app\build\outputs\apk\debug\app-debug.apk'
    if (-not (Test-Path $apk)) { throw 'APK output missing' }
    $destination = Join-Path $project 'artifacts'
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    $outputApk = Join-Path $destination 'ec2-switch-1.2-debug.apk'
    Copy-Item -LiteralPath $apk -Destination $outputApk -Force
    Get-FileHash -LiteralPath $outputApk -Algorithm SHA256
    Write-Host "Test APK ready: $outputApk"
} finally { Pop-Location }
