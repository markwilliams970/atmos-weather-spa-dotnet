# D17B (CREATE) - deploys Atmos Weather to the win2025app lab VM: IIS
# prerequisites, the SQL Server persistence layer (dedicated login + AtmosDb
# database + current EF Core migration), the published app itself, web.config
# wiring, the log directory, and the firewall rule. The reverse of
# vm-teardown.ps1, formalizing what was previously only a sequence of
# commands run by hand during Phase C (docs/phase-c-build-environment.md) /
# described in docs/manual-deployment-walkthrough.md.
#
# Run this ON the VM (console/RDP) or remotely via scripts/lab/deploy-to-vm.sh,
# which builds+publishes the app, generates the migration script, bridges
# both to the VM, and invokes this over WinRM — the same pattern
# run-vm-teardown.sh already uses.
#
# Idempotent: safe to re-run against a VM that already has some or all of
# this. An already-existing atmos_app login has its password reset to
# whatever this run is using (so this script never needs to "recover" an
# unknown password — every run makes the login's password match the
# connection string it's about to write) rather than being left alone.
#
# Explicitly NOT automated here: installing the SQL Server *engine* itself.
# Phase C's own automation never did this either (see
# docs/manual-deployment-walkthrough.md Part 1.3) - a silent SQL Server
# install is a much bigger, riskier undertaking than the rest of this
# script, and this project has never actually exercised one. This script
# verifies SQL Server is reachable and in Mixed Mode auth and fails with a
# clear message if not, rather than pretending to install it.
#
# Defaults to a DRY RUN: prints current state and what would change/be
# created. Pass -Force (and the two parameters below) to actually deploy.
#
#   powershell -File vm-create.ps1
#   powershell -File vm-create.ps1 -Force -BundleUrl 'http://192.168.122.1:8899/atmos-deploy-bundle.zip' -SqlPassword 'Xxxxxxxx1!'

param(
    [switch]$Force,
    [string]$BundleUrl,
    [string]$SqlPassword
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$SiteName        = 'AtmosWeb'
$AppPoolName     = 'AtmosWebPool'
$PhysicalPath    = 'C:\inetpub\AtmosWeb'
$SitePort        = 8080
$FirewallRule    = 'AtmosWeb HTTP 8080'
$LogDir          = 'C:\ProgramData\atmos\logs'
$SqlDatabase     = 'AtmosDb'
$SqlLogin        = 'atmos_app'
$BundleZipPath   = 'C:\Windows\Temp\atmos-deploy-bundle.zip'
$BundleExtractPath = 'C:\Windows\Temp\atmos-deploy-bundle'
$SqlSetupPath    = 'C:\Windows\Temp\atmos-sql-setup.sql'

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Artifact, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Artifact = $Artifact; Ok = $Ok; Detail = $Detail })
}

# Get-IISAppPool (the IISAdministration module) and New-WebAppPool (the
# classic WebAdministration module used everywhere else in this script)
# each keep their own independent, in-process ServerManager cache — a
# New-WebAppPool earlier in *this same* script would not be visible to a
# later Get-IISAppPool call in it, which could report a freshly-created pool
# as still absent (confirmed as a real bug against vm-teardown.ps1's mirror
# image of this same mixing, where a just-removed pool falsely reported
# "STILL PRESENT"). Test-Path/Get-WebAppPoolState against the IIS:\ PSDrive
# stay within WebAdministration throughout, so every read in this script
# sees the same mutations New-WebAppPool makes.
function Get-AppPoolInfo([string]$Name) {
    if (-not (Test-Path "IIS:\AppPools\$Name")) { return $null }
    return [pscustomobject]@{ State = (Get-WebAppPoolState -Name $Name -ErrorAction SilentlyContinue).Value }
}

# Stop-WebAppPool/Stop-Website returning does not guarantee the w3wp.exe
# worker process has actually exited and released its file locks yet, and
# IIS's Windows Process Activation Service can transiently refuse a
# Start-WebAppPool/Start-Website call immediately after a Stop with "The
# service cannot accept control messages at this time" - both confirmed as
# real failures during a live redeploy, not theoretical. Retrying with a
# short backoff is the practical fix; there's no clean "wait until truly
# ready" signal IIS exposes for either case.
function Invoke-WithRetry([scriptblock]$Action, [string]$Description, [int]$MaxAttempts = 5, [int]$DelaySeconds = 3) {
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            & $Action
            return
        } catch {
            if ($attempt -eq $MaxAttempts) { throw }
            Write-Output "  $Description failed (attempt $attempt/$MaxAttempts): $($_.Exception.Message) - retrying in ${DelaySeconds}s..."
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

Write-Output "================================================================"
Write-Output " Atmos Weather VM deploy - win2025app"
Write-Output " Mode: $(if ($Force) { 'FORCE (will deploy/create things)' } else { 'DRY RUN (no changes)' })"
Write-Output "================================================================"
Write-Output ""

Import-Module WebAdministration -ErrorAction SilentlyContinue

# ---------------------------------------------------------------------------
# 1. Pre-flight snapshot (read-only, always runs).
# ---------------------------------------------------------------------------
Write-Output "--- Pre-flight snapshot ---"

$webServerFeature = Get-WindowsFeature -Name Web-Server -ErrorAction SilentlyContinue
Write-Output "IIS (Web-Server) feature: $(if ($webServerFeature -and $webServerFeature.InstallState -eq 'Installed') { 'installed' } else { 'NOT installed' })"

$ancmRegistered = $false
try {
    $modules = & "$env:windir\System32\inetsrv\appcmd.exe" list config -section:system.webServer/globalModules 2>$null
    if ($modules -match 'AspNetCoreModuleV2') { $ancmRegistered = $true }
} catch { }
Write-Output "ASP.NET Core Hosting Bundle (AspNetCoreModuleV2): $(if ($ancmRegistered) { 'installed' } else { 'NOT installed' })"

$sqlReachable = $false
try {
    sqlcmd -S localhost -E -Q "SELECT 1" -h -1 *> $null
    if ($LASTEXITCODE -eq 0) { $sqlReachable = $true }
} catch { }
Write-Output "SQL Server (localhost): $(if ($sqlReachable) { 'reachable' } else { 'NOT reachable' })"

$sqlMixedMode = $false
if ($sqlReachable) {
    $authMode = sqlcmd -S localhost -E -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE SERVERPROPERTY('IsIntegratedSecurityOnly') WHEN 1 THEN 'WindowsOnly' ELSE 'Mixed' END"
    $sqlMixedMode = ($authMode.Trim() -eq 'Mixed')
    Write-Output "SQL Server auth mode: $($authMode.Trim())"
}

$siteBefore = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
Write-Output "IIS site '$SiteName': $(if ($siteBefore) { "present (state: $($siteBefore.State))" } else { 'absent' })"

$poolBefore = Get-AppPoolInfo $AppPoolName
Write-Output "App pool '$AppPoolName': $(if ($poolBefore) { "present (state: $($poolBefore.State))" } else { 'absent' })"

if ($sqlReachable) {
    $dbExists = sqlcmd -S localhost -E -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$SqlDatabase'"
    Write-Output "SQL database '$SqlDatabase': $(if ($dbExists.Trim() -eq '1') { 'present' } else { 'absent' })"

    $loginExists = sqlcmd -S localhost -E -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.server_principals WHERE name = N'$SqlLogin'"
    Write-Output "SQL login '$SqlLogin': $(if ($loginExists.Trim() -eq '1') { 'present (password will be reset by this script)' } else { 'absent' })"
}

Write-Output ""

if (-not $Force) {
    Write-Output "--- Dry run: nothing was changed. Re-run with -Force -BundleUrl <url> -SqlPassword <pwd> to actually deploy. ---"
    return
}

if (-not $sqlReachable) {
    Write-Output "ERROR: SQL Server is not reachable on localhost. This script does not install the SQL Server engine (see docs/manual-deployment-walkthrough.md Part 1.3) - install/start it first."
    exit 1
}
if (-not $sqlMixedMode) {
    Write-Output "ERROR: SQL Server is in Windows-only authentication mode. The '$SqlLogin' SQL login needs Mixed Mode auth (SSMS: Server Properties > Security > SQL Server and Windows Authentication mode), then restart the SQL Server service."
    exit 1
}
if ([string]::IsNullOrWhiteSpace($BundleUrl)) {
    Write-Output "ERROR: -BundleUrl is required with -Force (URL to the zip containing publish/ and migrate.sql)."
    exit 1
}
if ([string]::IsNullOrWhiteSpace($SqlPassword)) {
    Write-Output "ERROR: -SqlPassword is required with -Force."
    exit 1
}

# ---------------------------------------------------------------------------
# 2. IIS prerequisites - idempotent; genuinely a no-op on this VM today,
#    since both are already installed, but this is the "formally codify all
#    aspects" piece: a truly blank VM would need these actually run.
# ---------------------------------------------------------------------------
Write-Output "--- IIS prerequisites ---"
try {
    if (-not ($webServerFeature -and $webServerFeature.InstallState -eq 'Installed')) {
        Write-Output "Installing IIS (Web-Server)..."
        Install-WindowsFeature -Name Web-Server -IncludeManagementTools | Out-Null
        Write-Output "IIS installed."
    } else {
        Write-Output "IIS already installed."
    }

    if (-not $ancmRegistered) {
        Write-Output "Installing .NET Hosting Bundle..."
        $hostingUrl = "https://builds.dotnet.microsoft.com/dotnet/aspnetcore/Runtime/10.0.0/dotnet-hosting-10.0.0-win.exe"
        $hostingInstaller = "C:\Windows\Temp\dotnet-hosting-10.exe"
        Invoke-WebRequest -Uri $hostingUrl -OutFile $hostingInstaller -UseBasicParsing
        Start-Process -FilePath $hostingInstaller -ArgumentList "/install /quiet /norestart /log C:\Windows\Temp\hosting-install.log" -Wait
        Remove-Item -Path $hostingInstaller -ErrorAction SilentlyContinue
        iisreset /noforce | Out-Null
        Write-Output "Hosting Bundle installed."
    } else {
        Write-Output "Hosting Bundle already installed."
    }
} catch {
    Write-Output "ERROR during IIS prerequisites: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 3. SQL Server persistence layer: the AtmosDb database, the dedicated
#    atmos_app login/user/grants, and the current EF Core migration. This is
#    the piece to look at in isolation if only the persistence layer needs
#    validating - it's fully idempotent on its own, independent of the IIS
#    steps around it.
# ---------------------------------------------------------------------------
Write-Output "--- SQL Server persistence layer ---"
try {
    $sqlSetup = @"
SET NOCOUNT ON;

IF DB_ID(N'$SqlDatabase') IS NULL
BEGIN
    PRINT N'Creating database $SqlDatabase...';
    CREATE DATABASE [$SqlDatabase];
END
ELSE
BEGIN
    PRINT N'Database $SqlDatabase already exists.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$SqlLogin')
BEGIN
    PRINT N'Creating login $SqlLogin...';
    CREATE LOGIN [$SqlLogin] WITH PASSWORD = N'$SqlPassword', CHECK_POLICY = ON;
END
ELSE
BEGIN
    PRINT N'Login $SqlLogin already exists - resetting its password to match this deployment.';
    ALTER LOGIN [$SqlLogin] WITH PASSWORD = N'$SqlPassword';
END
GO

USE [$SqlDatabase];
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$SqlLogin')
BEGIN
    PRINT N'Creating database user $SqlLogin...';
    CREATE USER [$SqlLogin] FOR LOGIN [$SqlLogin];
END
ELSE
BEGIN
    PRINT N'Database user $SqlLogin already exists.';
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals p ON drm.member_principal_id = p.principal_id
    JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
    WHERE p.name = N'$SqlLogin' AND r.name = N'db_datareader'
)
BEGIN
    ALTER ROLE db_datareader ADD MEMBER [$SqlLogin];
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members drm
    JOIN sys.database_principals p ON drm.member_principal_id = p.principal_id
    JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id
    WHERE p.name = N'$SqlLogin' AND r.name = N'db_datawriter'
)
BEGIN
    ALTER ROLE db_datawriter ADD MEMBER [$SqlLogin];
END
GO

GRANT CREATE TABLE TO [$SqlLogin];
GRANT ALTER ON SCHEMA::dbo TO [$SqlLogin];
GO
"@
    Set-Content -Path $SqlSetupPath -Value $sqlSetup -Encoding UTF8
    sqlcmd -S localhost -E -i $SqlSetupPath
    $sqlSetupExitCode = $LASTEXITCODE
    Remove-Item -Path $SqlSetupPath -ErrorAction SilentlyContinue
    if ($sqlSetupExitCode -ne 0) {
        Write-Output "WARNING: SQL persistence-layer setup exited with code $sqlSetupExitCode - see output above."
    }
} catch {
    Write-Output "ERROR during SQL Server persistence-layer setup: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 4. Download the deploy bundle (publish/ + migrate.sql) and apply the
#    migration. Applying it BEFORE swapping the site's files means a failed
#    migration leaves the previous deployment still serving, not a site
#    pointed at a schema its code doesn't match.
# ---------------------------------------------------------------------------
Write-Output "--- Downloading deploy bundle and applying migration ---"
try {
    Invoke-WebRequest -Uri $BundleUrl -OutFile $BundleZipPath -UseBasicParsing
    if (Test-Path $BundleExtractPath) { Remove-Item -Path $BundleExtractPath -Recurse -Force }
    Expand-Archive -Path $BundleZipPath -DestinationPath $BundleExtractPath -Force
    Remove-Item -Path $BundleZipPath -ErrorAction SilentlyContinue

    $migratePath = Join-Path $BundleExtractPath "migrate.sql"
    if (-not (Test-Path $migratePath)) {
        throw "migrate.sql not found in the downloaded bundle."
    }
    # -d $SqlDatabase is required here: sqlcmd -E with no -d connects using the
    # admin login's default database (master on this VM), and the idempotent
    # migration script has no USE statement of its own — without -d, this
    # silently creates the schema in master instead of AtmosDb. sqlcmd still
    # exits 0 and prints success either way, so this was only caught by the
    # schema-verification query later in this script actually checking
    # INFORMATION_SCHEMA.TABLES *inside AtmosDb specifically* (Verification
    # section, below) rather than trusting a clean exit code.
    sqlcmd -S localhost -E -d $SqlDatabase -i $migratePath
    $migrateExitCode = $LASTEXITCODE
    if ($migrateExitCode -ne 0) {
        Write-Output "WARNING: migration script exited with code $migrateExitCode - see output above."
    } else {
        Write-Output "Migration applied."
    }
} catch {
    Write-Output "ERROR downloading/applying the bundle: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 5. Stop the site/pool (if already running) before swapping files under
#    them - the in-process hosting model loads Atmos.Web.dll into w3wp.exe,
#    which locks the file while running.
# ---------------------------------------------------------------------------
if ($siteBefore -and $siteBefore.State -eq 'Started') {
    Write-Output "Stopping site '$SiteName' before file swap..."
    Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
}
if ($poolBefore -and $poolBefore.State -eq 'Started') {
    Write-Output "Stopping app pool '$AppPoolName' before file swap..."
    Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    # Stop-WebAppPool returning does not mean w3wp.exe has actually exited
    # yet - give it a moment before the file operations below, which retry
    # on their own anyway if this isn't enough.
    Start-Sleep -Seconds 3
}
Write-Output ""

# ---------------------------------------------------------------------------
# 6. Deploy the published files.
# ---------------------------------------------------------------------------
Write-Output "--- Deploying published files to $PhysicalPath ---"
try {
    $publishSource = Join-Path $BundleExtractPath "publish"
    if (-not (Test-Path $publishSource)) {
        throw "publish/ not found in the downloaded bundle."
    }
    Invoke-WithRetry -Description "removing the previous deployment" -Action {
        if (Test-Path $PhysicalPath) { Remove-Item -Path $PhysicalPath -Recurse -Force }
    }
    New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
    Invoke-WithRetry -Description "copying published files" -Action {
        Copy-Item -Path (Join-Path $publishSource "*") -Destination $PhysicalPath -Recurse -Force
    }
    Remove-Item -Path $BundleExtractPath -Recurse -Force -ErrorAction SilentlyContinue
    Write-Output "Files deployed."
} catch {
    Write-Output "ERROR deploying files: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 7. App pool and site - create if this is a genuinely fresh VM, otherwise
#    leave the existing objects (and their bindings) alone.
# ---------------------------------------------------------------------------
Write-Output "--- App pool and site ---"
try {
    if (-not (Get-AppPoolInfo $AppPoolName)) {
        Write-Output "Creating app pool '$AppPoolName'..."
        New-WebAppPool -Name $AppPoolName | Out-Null
        Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value ""
    } else {
        Write-Output "App pool '$AppPoolName' already exists."
    }

    if (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) {
        Write-Output "Creating site '$SiteName'..."
        New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $SitePort | Out-Null
    } else {
        Write-Output "Site '$SiteName' already exists."
    }
} catch {
    Write-Output "ERROR creating app pool/site: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 8. Filesystem permissions for the app pool identity.
# ---------------------------------------------------------------------------
Write-Output "--- Filesystem permissions ---"
try {
    icacls $PhysicalPath /grant "IIS AppPool\${AppPoolName}:(OI)(CI)RX" /T | Out-Null
    $siteLogsPath = Join-Path $PhysicalPath "logs"
    New-Item -ItemType Directory -Force -Path $siteLogsPath | Out-Null
    icacls $siteLogsPath /grant "IIS AppPool\${AppPoolName}:(OI)(CI)M" /T | Out-Null
    Write-Output "Granted IIS AppPool\$AppPoolName read/execute on $PhysicalPath, modify on $siteLogsPath."
} catch {
    Write-Output "ERROR setting filesystem permissions: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 9. web.config: ASPNETCORE_ENVIRONMENT + the connection string secret.
#    dotnet publish regenerates web.config from scratch every time (Step 6
#    just overwrote it), so this always needs to be re-applied - see
#    docs/logging.md's "Deployment note".
# ---------------------------------------------------------------------------
Write-Output "--- web.config environment variables ---"
try {
    $webConfigPath = Join-Path $PhysicalPath "web.config"
    [xml]$webConfig = Get-Content -Path $webConfigPath
    $aspNetCoreNode = $webConfig.SelectSingleNode("//aspNetCore")
    if (-not $aspNetCoreNode) { throw "Could not find <aspNetCore> element in $webConfigPath" }

    $existingEnvVars = $aspNetCoreNode.SelectSingleNode("environmentVariables")
    if ($existingEnvVars) { $aspNetCoreNode.RemoveChild($existingEnvVars) | Out-Null }

    $envVarsNode = $webConfig.CreateElement("environmentVariables")
    function Add-EnvVarNode([string]$Name, [string]$Value) {
        $node = $webConfig.CreateElement("environmentVariable")
        $node.SetAttribute("name", $Name)
        $node.SetAttribute("value", $Value)
        $envVarsNode.AppendChild($node) | Out-Null
    }
    $connectionString = "Server=localhost;Database=$SqlDatabase;User Id=$SqlLogin;Password=$SqlPassword;TrustServerCertificate=True"
    Add-EnvVarNode "ASPNETCORE_ENVIRONMENT" "Production"
    Add-EnvVarNode "ConnectionStrings__AtmosDb" $connectionString
    $aspNetCoreNode.AppendChild($envVarsNode) | Out-Null
    $webConfig.Save($webConfigPath)
    Write-Output "web.config updated with ASPNETCORE_ENVIRONMENT and ConnectionStrings__AtmosDb."
} catch {
    Write-Output "ERROR updating web.config: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 10. Structured log directory.
# ---------------------------------------------------------------------------
Write-Output "--- Log directory ---"
try {
    New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
    icacls "C:\ProgramData\atmos" /grant "IIS AppPool\${AppPoolName}:(OI)(CI)M" /T | Out-Null
    Write-Output "$LogDir ready, IIS AppPool\$AppPoolName granted modify."
} catch {
    Write-Output "ERROR creating/permissioning log directory: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 11. Firewall rule.
# ---------------------------------------------------------------------------
Write-Output "--- Firewall ---"
try {
    if (-not (Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $FirewallRule -Direction Inbound -Protocol TCP -LocalPort $SitePort -Action Allow | Out-Null
        Write-Output "Created firewall rule '$FirewallRule'."
    } else {
        Write-Output "Firewall rule '$FirewallRule' already exists."
    }
} catch {
    Write-Output "ERROR creating firewall rule: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 12. Start everything back up.
# ---------------------------------------------------------------------------
Write-Output "--- Starting app pool and site ---"
try {
    # WAS can transiently refuse this immediately after the Stop earlier in
    # this run ("The service cannot accept control messages at this time") -
    # confirmed as a real failure, not theoretical; -ErrorAction
    # SilentlyContinue does not suppress it since it's a thrown exception,
    # not a non-terminating error, so the retry below actually sees it.
    Invoke-WithRetry -Description "starting app pool" -Action { Start-WebAppPool -Name $AppPoolName }
    Invoke-WithRetry -Description "starting site" -Action { Start-Website -Name $SiteName }
    Write-Output "Started."
} catch {
    Write-Output "ERROR starting app pool/site: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 13. Verification - query-based for the persistence layer (per the same
#     "validate, don't assume" discipline as vm-teardown.ps1), plus a real
#     end-to-end HTTP request through IIS -> Kestrel -> SQL Server.
# ---------------------------------------------------------------------------
Write-Output "--- Verification ---"

$dbCount = sqlcmd -S localhost -E -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$SqlDatabase'"
Add-Result "SQL database '$SqlDatabase'" ($dbCount.Trim() -eq '1') "sys.databases row count: $($dbCount.Trim())"

$loginCount = sqlcmd -S localhost -E -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.server_principals WHERE name = N'$SqlLogin'"
Add-Result "SQL login '$SqlLogin'" ($loginCount.Trim() -eq '1') "sys.server_principals row count: $($loginCount.Trim())"

$tableCount = sqlcmd -S localhost -E -h -1 -W -d $SqlDatabase -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('RecentSearch', '__EFMigrationsHistory')"
Add-Result "AtmosDb schema (RecentSearch + __EFMigrationsHistory)" ($tableCount.Trim() -eq '2') "matching table count: $($tableCount.Trim())"

$dbUserCount = sqlcmd -S localhost -E -h -1 -W -d $SqlDatabase -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.database_principals WHERE name = N'$SqlLogin'"
Add-Result "'$SqlLogin' database user (in $SqlDatabase)" ($dbUserCount.Trim() -eq '1') "sys.database_principals row count: $($dbUserCount.Trim())"

$roleCount = sqlcmd -S localhost -E -h -1 -W -d $SqlDatabase -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.database_role_members drm JOIN sys.database_principals p ON drm.member_principal_id = p.principal_id JOIN sys.database_principals r ON drm.role_principal_id = r.principal_id WHERE p.name = N'$SqlLogin' AND r.name IN (N'db_datareader', N'db_datawriter')"
Add-Result "'$SqlLogin' role memberships (db_datareader + db_datawriter)" ($roleCount.Trim() -eq '2') "matching role-membership count: $($roleCount.Trim())"

Add-Result "IIS site '$SiteName' started" ((Get-Website -Name $SiteName -ErrorAction SilentlyContinue).State -eq 'Started') ""
Add-Result "App pool '$AppPoolName' started" ((Get-AppPoolInfo $AppPoolName).State -eq 'Started') ""
Add-Result "Physical path '$PhysicalPath'" (Test-Path $PhysicalPath) ""
Add-Result "web.config has ConnectionStrings__AtmosDb" ((Select-String -Path (Join-Path $PhysicalPath "web.config") -Pattern "ConnectionStrings__AtmosDb" -Quiet)) ""
Add-Result "Firewall rule '$FirewallRule'" ([bool](Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue)) ""

$healthOk = $false
$healthDetail = ""
try {
    Start-Sleep -Seconds 3
    $resp = Invoke-WebRequest -Uri "http://localhost:$SitePort/healthz" -UseBasicParsing -TimeoutSec 15
    $healthOk = ($resp.StatusCode -eq 200 -and $resp.Content -eq 'Healthy')
    $healthDetail = "HTTP $($resp.StatusCode): $($resp.Content)"
} catch {
    $healthDetail = $_.Exception.Message
}
Add-Result "End-to-end /healthz (IIS -> Kestrel -> SQL Server)" $healthOk $healthDetail

Write-Output ""
$results | Format-Table -AutoSize @{L='Artifact';E={$_.Artifact}}, @{L='OK?';E={if ($_.Ok) {'OK'} else {'FAILED'}}}, @{L='Detail';E={$_.Detail}}

if ($results | Where-Object { -not $_.Ok }) {
    Write-Output "One or more checks failed - see table above."
    exit 1
} else {
    Write-Output "VM deploy complete and verified."
}
