# Applies EF Core schema migrations as an explicit deployment step.
# Local uses SQL Server LocalDB by default. SqlServer can use either a full
# connection string or Azure SQL server/database/admin parameters.

# LocalDB only
# .\PwScripts\UpdateEfDatabase.ps1 -Target Local

# # Azure SQL / SQL Server using default server/db/admin values, prompts for password
# .\PwScripts\UpdateEfDatabase.ps1 -Target SqlServer

# # Both local and SQL Server
# .\PwScripts\UpdateEfDatabase.ps1 -Target All

# # Or pass a full SQL Server connection string
# .\PwScripts\UpdateEfDatabase.ps1 -Target SqlServer -SqlServerConnectionString "<connection-string>"

# # Optionally list tables after migration
# .\PwScripts\UpdateEfDatabase.ps1 -Target All -ListTables

param(
    [ValidateSet("Local", "SqlServer", "All")]
    [string]$Target = "Local",

    [string]$LocalConnectionString = "Server=(localdb)\MSSQLLocalDB;Database=SouthBaySoccer_Local;Trusted_Connection=True;MultipleActiveResultSets=True;TrustServerCertificate=True",

    [string]$SqlServerConnectionString,
    [string]$SqlServerFullyQualifiedDomainName = "southbaysoccer-prod-sql.database.windows.net",
    [string]$SqlDatabase = "SouthBaySoccerProdDb",
    [string]$SqlAdmin = "sqladminuser",
    [securestring]$SqlPassword,

    [string]$InfrastructureProject = "src/SouthBaySoccer.Infrastructure/SouthBaySoccer.Infrastructure.csproj",
    [string]$StartupProject = "src/SouthBaySoccer.Infrastructure/SouthBaySoccer.Infrastructure.csproj",
    [string]$ConnectionStringSettingName = "ConnectionStrings__SouthBaySoccerDb",

    [switch]$ListTables
)

$ErrorActionPreference = "Stop"
$repoRoot = (Get-Location).ProviderPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function ConvertTo-PlainText {
    param([Parameter(Mandatory = $true)][securestring]$SecureString)

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureString)
    try {
        [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

function New-AzureSqlConnectionString {
    if ([string]::IsNullOrWhiteSpace($SqlServerFullyQualifiedDomainName)) {
        throw "SqlServerFullyQualifiedDomainName is required when SqlServerConnectionString is not supplied."
    }

    if ([string]::IsNullOrWhiteSpace($SqlDatabase)) {
        throw "SqlDatabase is required when SqlServerConnectionString is not supplied."
    }

    if ([string]::IsNullOrWhiteSpace($SqlAdmin)) {
        throw "SqlAdmin is required when SqlServerConnectionString is not supplied."
    }

    if ($null -eq $SqlPassword) {
        $script:SqlPassword = Read-Host "Enter the SQL password for $SqlAdmin@$SqlServerFullyQualifiedDomainName" -AsSecureString
    }

    $password = ConvertTo-PlainText -SecureString $SqlPassword
    return "Server=tcp:$SqlServerFullyQualifiedDomainName,1433;Initial Catalog=$SqlDatabase;Persist Security Info=False;User ID=$SqlAdmin;Password=$password;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
}

function Invoke-EfDatabaseUpdate {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ConnectionString
    )

    Write-Host "Applying EF Core migrations to $Name."

    $previousConnectionString = [Environment]::GetEnvironmentVariable($ConnectionStringSettingName, "Process")
    try {
        [Environment]::SetEnvironmentVariable($ConnectionStringSettingName, $ConnectionString, "Process")

        dotnet ef database update `
            --project $infrastructureProjectFullPath `
            --startup-project $startupProjectFullPath

        if ($LASTEXITCODE -ne 0) {
            throw "EF Core database update failed for $Name with exit code $LASTEXITCODE."
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable($ConnectionStringSettingName, $previousConnectionString, "Process")
    }

    if ($ListTables) {
        Show-DatabaseTables -Name $Name -ConnectionString $ConnectionString
    }
}

function Show-DatabaseTables {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$ConnectionString
    )

    Add-Type -AssemblyName System.Data

    $query = @"
SELECT TABLE_SCHEMA, TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_SCHEMA, TABLE_NAME;
"@

    $connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
    $command = $connection.CreateCommand()
    $command.CommandText = $query

    $table = [System.Data.DataTable]::new()
    try {
        $connection.Open()
        $reader = $command.ExecuteReader()
        $table.Load($reader)
    }
    finally {
        $connection.Dispose()
    }

    Write-Host "Database tables in ${Name}:"
    if ($table.Rows.Count -eq 0) {
        Write-Warning "No user tables were found."
        return
    }

    $table | Format-Table -AutoSize
}

$infrastructureProjectFullPath = Resolve-RepoPath -Path $InfrastructureProject
$startupProjectFullPath = Resolve-RepoPath -Path $StartupProject

if (-not (Test-Path $infrastructureProjectFullPath)) {
    throw "Infrastructure project was not found: $infrastructureProjectFullPath"
}

if (-not (Test-Path $startupProjectFullPath)) {
    throw "Startup project was not found: $startupProjectFullPath"
}

if ($Target -eq "Local" -or $Target -eq "All") {
    Invoke-EfDatabaseUpdate `
        -Name "local database" `
        -ConnectionString $LocalConnectionString
}

if ($Target -eq "SqlServer" -or $Target -eq "All") {
    $connectionString = $SqlServerConnectionString
    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        $connectionString = [Environment]::GetEnvironmentVariable($ConnectionStringSettingName, "Process")
    }

    if ([string]::IsNullOrWhiteSpace($connectionString)) {
        $connectionString = New-AzureSqlConnectionString
    }

    Invoke-EfDatabaseUpdate `
        -Name $SqlDatabase `
        -ConnectionString $connectionString
}

Write-Host "EF Core database update complete for target: $Target."
