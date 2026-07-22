param(
    [ValidateSet("Prompt", "All", "iPhone", "iPad", "Android")]
    [string]$Target = "Prompt",

    [string]$Configuration = "Release",

    [string]$Project = "SouthBaySoccer/SouthBaySoccer.csproj",

    [string]$ClientDataSource = "Api",

    [string]$PrdApiBaseUrl = "https://southbaysoccerfunc-cndha8gtc4bxdtfe.westus2-01.azurewebsites.net/api/",

    [string]$IPhoneUdid = "51AB9FE3-E057-4E66-B637-84F7259CBC07",

    [string]$IPadUdid = "1DDF1401-3D22-43FD-8D4B-73D35CD6159E",

    [string]$AndroidSerial = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-Choice {
    param(
        [string]$Message,
        [string]$Default,
        [string[]]$Allowed
    )

    $allowedText = $Allowed -join "/"
    $value = Read-Host "$Message [$allowedText] (default: $Default)"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    $match = $Allowed | Where-Object { $_.Equals($value, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($null -eq $match) {
        throw "Unsupported choice '$value'. Expected one of: $allowedText."
    }

    return $match
}

function Read-Defaulted {
    param(
        [string]$Message,
        [string]$Default
    )

    $value = Read-Host "$Message (default: $Default)"
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }

    return $value
}

function Invoke-DotNet {
    param([string[]]$Arguments)

    Write-Host ""
    Write-Host "dotnet $($Arguments -join ' ')" -ForegroundColor Cyan
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

function Get-AndroidSerial {
    param([string]$Current)

    if (-not [string]::IsNullOrWhiteSpace($Current)) {
        return $Current
    }

    $adb = Get-Command adb -ErrorAction SilentlyContinue
    if ($null -eq $adb) {
        return Read-Defaulted "Android device serial" "emulator-5554"
    }

    $devices = & adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\tdevice$" }
    if ($devices.Count -eq 0) {
        return Read-Defaulted "Android device serial" "emulator-5554"
    }

    $defaultSerial = ($devices[0] -split "\s+")[0]
    return Read-Defaulted "Android device serial" $defaultSerial
}

function Run-IosSimulator {
    param(
        [string]$Name,
        [string]$Udid
    )

    $selectedUdid = Read-Defaulted "$Name simulator UDID" $Udid

    $simctl = Get-Command xcrun -ErrorAction SilentlyContinue
    if ($null -ne $simctl) {
        Write-Host "Booting $Name simulator $selectedUdid if needed..." -ForegroundColor DarkCyan
        & xcrun simctl boot $selectedUdid 2>$null
    }

    Invoke-DotNet @(
        "build",
        $Project,
        "-f", "net10.0-ios",
        "-c", $Configuration,
        "-t:Run",
        "-p:RuntimeIdentifier=iossimulator-arm64",
        "-p:_DeviceName=:v2:udid=$selectedUdid"
    )
}

function Run-Android {
    $selectedSerial = Get-AndroidSerial $AndroidSerial

    Invoke-DotNet @(
        "build",
        $Project,
        "-f", "net10.0-android",
        "-c", $Configuration,
        "-t:Run",
        "-p:AndroidDeviceSerial=$selectedSerial"
    )
}

if (-not (Test-Path $Project)) {
    throw "Project file '$Project' was not found. Run this script from the repo root."
}

if ($Target -eq "Prompt") {
    $Target = Read-Choice "Run target" "All" @("All", "iPhone", "iPad", "Android")
}

$env:ClientDataSource = $ClientDataSource
$env:PrdApiBaseUrl = $PrdApiBaseUrl

Write-Host "Configuration: $Configuration" -ForegroundColor Green
Write-Host "ClientDataSource: $env:ClientDataSource" -ForegroundColor Green
Write-Host "PrdApiBaseUrl: $env:PrdApiBaseUrl" -ForegroundColor Green

switch ($Target) {
    "All" {
        Run-IosSimulator "iPhone" $IPhoneUdid
        Run-IosSimulator "iPad Air" $IPadUdid
        Run-Android
    }
    "iPhone" {
        Run-IosSimulator "iPhone" $IPhoneUdid
    }
    "iPad" {
        Run-IosSimulator "iPad Air" $IPadUdid
    }
    "Android" {
        Run-Android
    }
}
