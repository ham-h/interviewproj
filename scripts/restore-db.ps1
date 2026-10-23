<#
.SYNOPSIS
    Restores the VoiceHire database from a .bak file into SQL Server LocalDB.

.EXAMPLE
    .\scripts\restore-db.ps1 -BackupFile "D:\VoiceHire.bak"
    .\scripts\restore-db.ps1 -BackupFile "D:\VoiceHire.bak" -Force   # replace an existing VoiceHire database

.NOTES
    Needs only SQL Server LocalDB (installed with Visual Studio's ASP.NET workload). sqlcmd is not required.
    Stop the app before restoring.
#>
param(
    [Parameter(Mandatory = $true)][string]$BackupFile,
    [string]$Instance = "MSSQLLocalDB",
    [string]$Database = "VoiceHire",
    [string]$DataFolder = $env:USERPROFILE,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$BackupFile = (Resolve-Path $BackupFile).Path
Write-Host "Starting LocalDB instance '$Instance'..."
sqllocaldb start $Instance | Out-Null

$connection = New-Object System.Data.SqlClient.SqlConnection "Server=(localdb)\$Instance;Database=master;Integrated Security=true;Connect Timeout=60"
$connection.Open()

function Invoke-Sql([string]$sql) {
    $cmd = $connection.CreateCommand()
    $cmd.CommandText = $sql
    $cmd.CommandTimeout = 600
    return $cmd
}

try {
    $exists = (Invoke-Sql "SELECT COUNT(*) FROM sys.databases WHERE name = N'$Database'").ExecuteScalar()
    if ($exists -gt 0 -and -not $Force) {
        throw "Database '$Database' already exists. Re-run with -Force to replace it (its current data will be lost)."
    }

    # Read the logical file names stored in the backup so they can be moved to this computer's folders.
    $reader = (Invoke-Sql "RESTORE FILELISTONLY FROM DISK = N'$BackupFile'").ExecuteReader()
    $moves = @()
    while ($reader.Read()) {
        $logical = $reader["LogicalName"]
        $extension = if ($reader["Type"] -eq "L") { "_log.ldf" } else { ".mdf" }
        $target = Join-Path $DataFolder ($Database + $extension)
        $moves += "MOVE N'$logical' TO N'$target'"
    }
    $reader.Close()

    if ($exists -gt 0) {
        Write-Host "Closing connections to the existing '$Database' database..."
        (Invoke-Sql "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE").ExecuteNonQuery() | Out-Null
    }

    Write-Host "Restoring '$Database' from $BackupFile ..."
    (Invoke-Sql "RESTORE DATABASE [$Database] FROM DISK = N'$BackupFile' WITH $($moves -join ', '), REPLACE, RECOVERY").ExecuteNonQuery() | Out-Null
    (Invoke-Sql "ALTER DATABASE [$Database] SET MULTI_USER").ExecuteNonQuery() | Out-Null

    $count = (Invoke-Sql "SELECT COUNT(*) FROM [$Database].dbo.Questions").ExecuteScalar()
    Write-Host "Done. '$Database' restored to $DataFolder ($count questions in the bank)." -ForegroundColor Green
}
finally {
    $connection.Close()
}
