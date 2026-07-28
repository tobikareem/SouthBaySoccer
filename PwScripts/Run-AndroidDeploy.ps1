Set-Location "D:\source\SouthBaySoccer"

$keystore = "$env:USERPROFILE\.android\n9jabay-upload.keystore"
$passwordFile = [System.IO.Path]::GetTempFileName()
$securePassword = Read-Host "Enter the N9ja Bay upload-key password" -AsSecureString
$credential = New-Object System.Management.Automation.PSCredential("unused", $securePassword)
$plainPassword = $credential.GetNetworkCredential().Password

try {
    [System.IO.File]::WriteAllText($passwordFile, $plainPassword)

    dotnet publish ".\SouthBaySoccer\SouthBaySoccer.csproj" `
        -f net10.0-android `
        -c Release `
        -p:AndroidPackageFormats=aab `
        -p:AndroidKeyStore=true `
        "-p:AndroidSigningKeyStore=$keystore" `
        -p:AndroidSigningKeyAlias=n9jabay-upload `
        "-p:AndroidSigningKeyPass=file:$passwordFile" `
        "-p:AndroidSigningStorePass=file:$passwordFile"

    if ($LASTEXITCODE -ne 0) {
        throw "Android release build failed with exit code $LASTEXITCODE."
    }
}
finally {
    $plainPassword = $null
    Remove-Item -LiteralPath $passwordFile -Force -ErrorAction SilentlyContinue
}