<#
.SYNOPSIS
    Stamps the iOS build number (ApplicationVersion) into the MAUI client csproj.

.DESCRIPTION
    Used two ways:
      * CI (release-ios.yml): passes -BuildNumber derived from the workflow run number so every
        TestFlight upload gets a unique, monotonically increasing build number without committing
        anything back to the repo.
      * Locally: run with no arguments to increment the current value by one and keep the change,
        e.g. before a manual `dotnet publish` upload.

    Only <ApplicationVersion> (Apple's CFBundleVersion / "build number") is touched.
    <ApplicationDisplayVersion> (the marketing version, e.g. 1.0) stays a deliberate human decision.

.EXAMPLE
    pwsh Scripts/Set-IosBuildNumber.ps1                 # 3 -> 4 (local bump)
    pwsh Scripts/Set-IosBuildNumber.ps1 -BuildNumber 42 # set exactly 42 (CI)
#>
param(
    [long]$BuildNumber = 0,

    [string]$Project = "SouthBaySoccer/SouthBaySoccer.csproj"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path $Project)) {
    throw "Project file '$Project' was not found. Run this script from the repo root."
}

$content = Get-Content -Path $Project -Raw
$pattern = '<ApplicationVersion>(\d+)</ApplicationVersion>'
$match = [regex]::Match($content, $pattern)
if (-not $match.Success) {
    throw "Could not find <ApplicationVersion> in '$Project'."
}

$current = [long]$match.Groups[1].Value
$next = if ($BuildNumber -gt 0) { $BuildNumber } else { $current + 1 }

if ($next -le $current -and $BuildNumber -gt 0) {
    # App Store Connect rejects a build number it has already seen for this version. Fail loudly in
    # CI instead of uploading a doomed artifact.
    throw "Requested build number $next is not greater than the current value $current. " +
        "Raise the run-number offset in the workflow (or the csproj value has drifted ahead)."
}

$updated = [regex]::Replace($content, $pattern, "<ApplicationVersion>$next</ApplicationVersion>", 1)
Set-Content -Path $Project -Value $updated -NoNewline

Write-Host "ApplicationVersion: $current -> $next ($Project)"

# Expose the value to later workflow steps (harmless no-op locally).
if ($env:GITHUB_OUTPUT) {
    "build-number=$next" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}
