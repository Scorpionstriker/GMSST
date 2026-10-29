<#
.SYNOPSIS
    Creates the GSST databases and sets the environment variables for the current Windows user (WP-1).

.DESCRIPTION
    1. Asks for the password of the PostgreSQL superuser (postgres) and runs create_database.sql with psql, which
       creates the role gmss_app and the databases gmss, inventory_demo and maintenance_demo.
    2. Sets the user environment variables GMSS_DB_PASSWORD (the gmss_app password you choose) and GSST_AGENT_KEY
       (a new random key shared by the Admin application and the Maintenance Agent).
    Nothing is written to a file. Run it again at any time: it only creates what is missing. Use -NewAgentKey to
    replace an existing agent key. Close and reopen Visual Studio, terminals and the applications afterwards so they
    see the new variables.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File database\setup_windows.ps1
#>
[CmdletBinding()]
param(
    # Folder that holds psql.exe. Empty: PATH, then the usual PostgreSQL install folders.
    [string] $PostgresBinFolder = "",
    [string] $SuperUser = "postgres",
    [string] $DatabaseHost = "localhost",
    [int] $Port = 5432,
    [switch] $NewAgentKey
)

$ErrorActionPreference = "Stop"

function Find-Psql {
    if ($PostgresBinFolder) {
        $candidate = Join-Path $PostgresBinFolder "psql.exe"
        if (Test-Path $candidate) { return $candidate }
        throw "psql.exe was not found in $PostgresBinFolder."
    }

    $onPath = Get-Command psql.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $roots = @("C:\PostgreSQL 18", "C:\Program Files\PostgreSQL\18", "C:\Program Files\PostgreSQL\17", "C:\Program Files\PostgreSQL\16")
    foreach ($root in $roots) {
        $candidate = Join-Path $root "bin\psql.exe"
        if (Test-Path $candidate) { return $candidate }
    }

    throw "psql.exe was not found. Install PostgreSQL or pass -PostgresBinFolder 'C:\path\to\PostgreSQL\bin'."
}

function Read-Secret([string] $prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$psql = Find-Psql
$script = Join-Path $PSScriptRoot "create_database.sql"
Write-Host "Using $psql"

$appPassword = [Environment]::GetEnvironmentVariable("GMSS_DB_PASSWORD", "User")
if ($appPassword) {
    Write-Host "GMSS_DB_PASSWORD is already set for this user; it is used as the gmss_app password."
} else {
    $appPassword = Read-Secret "Choose a password for the gmss_app database role"
    $again = Read-Secret "Enter the same password again"
    if ($appPassword -ne $again) { throw "The two passwords are different." }
    if ($appPassword.Length -lt 12) { throw "Use a password of at least 12 characters." }
}

$superPassword = Read-Secret "Password of the PostgreSQL user '$SuperUser'"
$env:PGPASSWORD = $superPassword
try {
    # The password goes to psql on standard input, never on the command line, where other programs could see it.
    $quoted = $appPassword.Replace("\", "\\").Replace("'", "''")
    $scriptPath = $script.Replace("\", "/")
    "\set app_password '$quoted'`n\i '$scriptPath'`n" | & $psql -h $DatabaseHost -p $Port -U $SuperUser -d postgres -X -q -f -
    if ($LASTEXITCODE -ne 0) { throw "psql failed with exit code $LASTEXITCODE." }
}
finally {
    Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue
}

[Environment]::SetEnvironmentVariable("GMSS_DB_PASSWORD", $appPassword, "User")
Write-Host "GMSS_DB_PASSWORD is set for $env:USERNAME."

$agentKey = [Environment]::GetEnvironmentVariable("GSST_AGENT_KEY", "User")
if (-not $agentKey -or $NewAgentKey) {
    $bytes = New-Object byte[] 32
    [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $agentKey = [Convert]::ToBase64String($bytes)
    [Environment]::SetEnvironmentVariable("GSST_AGENT_KEY", $agentKey, "User")
    Write-Host "GSST_AGENT_KEY is set to a new random key."
} else {
    Write-Host "GSST_AGENT_KEY is already set; kept."
}

Write-Host ""
Write-Host "Done. Close and reopen Visual Studio and any terminals, then start the Admin application and the Maintenance Agent."
