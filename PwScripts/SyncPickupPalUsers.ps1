# Fetches Pickup Pal users and signs each phone number into the production
# SouthBaySoccer Functions endpoint. This syncs Pickup Pal users into the
# production SouthBaySoccer database through the existing phone sign-in flow.
#
# Examples:
# .\PwScripts\SyncPickupPalUsers.ps1 -DryRun
# .\PwScripts\SyncPickupPalUsers.ps1 -AllowProduction
# .\PwScripts\SyncPickupPalUsers.ps1 -SignInUrl "http://localhost:7071/api/auth/pickuppal/phone/sign-in"
# .\PwScripts\SyncPickupPalUsers.ps1 -ContinueOnError -DelayMilliseconds 250

param(
    [string]$PickupPalUsersUrl = "https://pickuppal-bot-dev.up.railway.app/api/users",

    [string]$SignInUrl = "https://southbaysoccerfunc-cndha8gtc4bxdtfe.westus2-01.azurewebsites.net/api/auth/pickuppal/phone/sign-in",

    [int]$DelayMilliseconds = 0,

    [switch]$DryRun,

    [switch]$ContinueOnError,

    [switch]$AllowProduction
)

$ErrorActionPreference = "Stop"

$productionHostName = "southbaysoccerfunc-cndha8gtc4bxdtfe.westus2-01.azurewebsites.net"
$signInUri = [Uri]$SignInUrl
if (-not $DryRun -and $signInUri.Host -ieq $productionHostName -and -not $AllowProduction) {
    throw "The sign-in URL targets production. Re-run with -AllowProduction to confirm production writes."
}

function Get-ResponseItems {
    param([Parameter(Mandatory = $true)]$Response)

    if ($null -eq $Response) {
        return @()
    }

    if ($Response -is [System.Array]) {
        return @($Response)
    }

    foreach ($propertyName in @("users", "data", "items", "results")) {
        $property = $Response.PSObject.Properties[$propertyName]
        if ($null -ne $property -and $null -ne $property.Value) {
            return @(Get-ResponseItems -Response $property.Value)
        }
    }

    return @($Response)
}

function Normalize-PhoneNumber {
    param([AllowNull()][string]$PhoneNumber)

    if ([string]::IsNullOrWhiteSpace($PhoneNumber)) {
        return $null
    }

    $digits = -join ($PhoneNumber.ToCharArray() | Where-Object { [char]::IsDigit($_) })
    if ([string]::IsNullOrWhiteSpace($digits)) {
        return $null
    }

    return $digits
}

function Mask-PhoneNumber {
    param([Parameter(Mandatory = $true)][string]$PhoneNumberDigits)

    if ($PhoneNumberDigits.Length -le 4) {
        return "****"
    }

    return ("*" * ($PhoneNumberDigits.Length - 4)) + $PhoneNumberDigits.Substring($PhoneNumberDigits.Length - 4)
}

Write-Host "Fetching Pickup Pal users from $PickupPalUsersUrl."
$pickupPalResponse = Invoke-RestMethod -Method Get -Uri $PickupPalUsersUrl
$users = @(Get-ResponseItems -Response $pickupPalResponse)

Write-Host "Found $($users.Count) Pickup Pal user item(s)."
if ($users.Count -eq 0) {
    return
}

$successCount = 0
$skippedCount = 0
$failedCount = 0

foreach ($user in $users) {
    $phoneNumberDigits = Normalize-PhoneNumber -PhoneNumber $user.phoneNumber
    if ($null -eq $phoneNumberDigits) {
        $skippedCount++
        Write-Warning "Skipping user without a usable phone number. PickupPalUserId=$($user.id)"
        continue
    }

    $maskedPhone = Mask-PhoneNumber -PhoneNumberDigits $phoneNumberDigits
    if ($DryRun) {
        Write-Host "Dry run: would sign in PickupPalUserId=$($user.id), Phone=$maskedPhone."
        continue
    }

    $body = @{
        phoneNumber = $phoneNumberDigits
    } | ConvertTo-Json -Depth 3

    try {
        Write-Host "Signing in PickupPalUserId=$($user.id), Phone=$maskedPhone."
        $null = Invoke-RestMethod `
            -Method Post `
            -Uri $SignInUrl `
            -ContentType "application/json" `
            -Body $body

        $successCount++
    }
    catch {
        $failedCount++
        Write-Warning "Failed to sync PickupPalUserId=$($user.id), Phone=$maskedPhone. $($_.Exception.Message)"

        if (-not $ContinueOnError) {
            throw
        }
    }

    if ($DelayMilliseconds -gt 0) {
        Start-Sleep -Milliseconds $DelayMilliseconds
    }
}

Write-Host "Pickup Pal sync complete. Success=$successCount Skipped=$skippedCount Failed=$failedCount DryRun=$DryRun."
