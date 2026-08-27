# D17B (DELETE) — tears down the Atmos Weather deployment on the win2025app
# lab VM, as documented in docs/phase-c-build-environment.md and
# docs/manual-deployment-walkthrough.md: the IIS site, its app pool, the
# published files, the firewall rule, the structured-log directory, and —
# per explicit instruction — the SQL Server database *and* its dedicated
# login, dropped completely (schema and data, not just emptied), with
# before/after query-based validation printed so the cleanup can be checked
# by eye, not just trusted.
#
# Run this ON the VM (console/RDP) or remotely via
# scripts/lab/run-vm-teardown.sh, which pipes it over WinRM exactly the way
# every other piece of this project's VM automation has worked.
#
# Defaults to a DRY RUN: it only prints what currently exists and what would
# be removed/dropped. Pass -Force to actually perform the teardown.
#
#   powershell -File vm-teardown.ps1              # dry run (default)
#   powershell -File vm-teardown.ps1 -Force        # actually tear down
#
# Idempotent: every step checks whether its target exists before acting, so
# re-running after a partial cleanup (or against a VM that was never fully
# set up) doesn't error out.

param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$SiteName       = 'AtmosWeb'
$AppPoolName    = 'AtmosWebPool'
$PhysicalPath   = 'C:\inetpub\AtmosWeb'
$FirewallRule   = 'AtmosWeb HTTP 8080'
$LogDir         = 'C:\ProgramData\atmos\logs'
$LogDirParent   = 'C:\ProgramData\atmos'
$SqlDatabase    = 'AtmosDb'
$SqlLogin       = 'atmos_app'
$TeardownSqlPath = 'C:\Windows\Temp\atmos-teardown.sql'

$results = New-Object System.Collections.Generic.List[object]
function Add-Result([string]$Artifact, [bool]$Ok, [string]$Detail) {
    $results.Add([pscustomobject]@{ Artifact = $Artifact; Ok = $Ok; Detail = $Detail })
}

# Get-IISAppPool (the IISAdministration module) and Remove-WebAppPool/
# New-WebAppPool (the classic WebAdministration module used everywhere else
# in this script) each keep their own independent, in-process ServerManager
# cache — a Remove-WebAppPool earlier in *this same* script does not
# invalidate what Get-IISAppPool reports later in it, producing a false
# "STILL PRESENT" in the post-cleanup report even though the pool really is
# gone (confirmed: a fresh WinRM call, i.e. a fresh process, reported it
# correctly absent immediately afterward). Test-Path/Get-WebAppPoolState
# against the IIS:\ PSDrive stay within WebAdministration throughout, so
# every read in this script sees the same mutations the removals make.
function Get-AppPoolInfo([string]$Name) {
    if (-not (Test-Path "IIS:\AppPools\$Name")) { return $null }
    return [pscustomobject]@{ State = (Get-WebAppPoolState -Name $Name -ErrorAction SilentlyContinue).Value }
}

Write-Output "================================================================"
Write-Output " Atmos Weather VM teardown — win2025app"
Write-Output " Mode: $(if ($Force) { 'FORCE (will delete/drop things)' } else { 'DRY RUN (no changes)' })"
Write-Output "================================================================"
Write-Output ""

# ---------------------------------------------------------------------------
# 1. Pre-cleanup snapshot (read-only, always runs, even in dry-run mode) —
#    this is the "before" half of the validation the cleanup is checked
#    against below.
# ---------------------------------------------------------------------------
Write-Output "--- Pre-cleanup snapshot ---"

Import-Module WebAdministration -ErrorAction SilentlyContinue

$siteBefore = Get-Website -Name $SiteName -ErrorAction SilentlyContinue
Write-Output "IIS site '$SiteName': $(if ($siteBefore) { "present (state: $($siteBefore.State))" } else { 'absent' })"

$poolBefore = Get-AppPoolInfo $AppPoolName
Write-Output "App pool '$AppPoolName': $(if ($poolBefore) { "present (state: $($poolBefore.State))" } else { 'absent' })"

$pathBefore = Test-Path $PhysicalPath
Write-Output "Physical path '$PhysicalPath': $(if ($pathBefore) { 'present' } else { 'absent' })"

$fwBefore = Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue
Write-Output "Firewall rule '$FirewallRule': $(if ($fwBefore) { 'present' } else { 'absent' })"

$logDirBefore = Test-Path $LogDir
Write-Output "Log directory '$LogDir': $(if ($logDirBefore) { 'present' } else { 'absent' })"

Write-Output ""
Write-Output "SQL Server — databases:"
sqlcmd -S localhost -E -Q "SET NOCOUNT ON; SELECT name FROM sys.databases ORDER BY name" -W

Write-Output ""
Write-Output "SQL Server — '$SqlDatabase' tables and row counts (if the database exists):"
sqlcmd -S localhost -E -Q "SET NOCOUNT ON; IF DB_ID(N'$SqlDatabase') IS NOT NULL EXEC('USE [$SqlDatabase]; SELECT t.name AS TableName, SUM(p.rows) AS ApproxRows FROM sys.tables t JOIN sys.partitions p ON p.object_id = t.object_id AND p.index_id IN (0,1) GROUP BY t.name') ELSE PRINT N'(database does not exist)'" -W

Write-Output ""
Write-Output "SQL Server — login '$SqlLogin':"
sqlcmd -S localhost -E -Q "SET NOCOUNT ON; SELECT name, type_desc, create_date FROM sys.server_principals WHERE name = N'$SqlLogin'" -W

Write-Output ""

if (-not $Force) {
    Write-Output "--- Dry run: nothing was changed. Re-run with -Force to actually tear this down. ---"
    return
}

# ---------------------------------------------------------------------------
# 2. Stop the site/pool first — releases file locks and any pooled SQL
#    connections before we delete files or drop the database underneath them.
# ---------------------------------------------------------------------------
Write-Output "--- Stopping site and app pool ---"
if ($siteBefore -and $siteBefore.State -eq 'Started') {
    Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
    Write-Output "Stopped site '$SiteName'."
}
if ($poolBefore -and $poolBefore.State -eq 'Started') {
    Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    Write-Output "Stopped app pool '$AppPoolName'."
}
Write-Output ""

# ---------------------------------------------------------------------------
# 3. SQL Server: drop the database (schema + data, not just truncated) and
#    the dedicated login. SINGLE_USER WITH ROLLBACK IMMEDIATE forcibly
#    disconnects anything still attached (belt-and-suspenders alongside
#    having just stopped the app above) so DROP DATABASE can't be blocked.
# ---------------------------------------------------------------------------
Write-Output "--- SQL Server cleanup ---"

try {

$teardownSql = @"
SET NOCOUNT ON;

IF DB_ID(N'$SqlDatabase') IS NOT NULL
BEGIN
    PRINT N'Dropping database $SqlDatabase...';
    ALTER DATABASE [$SqlDatabase] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [$SqlDatabase];
    PRINT N'Database $SqlDatabase dropped.';
END
ELSE
BEGIN
    PRINT N'Database $SqlDatabase does not exist — nothing to drop.';
END

IF EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'$SqlLogin')
BEGIN
    PRINT N'Dropping login $SqlLogin...';
    DROP LOGIN [$SqlLogin];
    PRINT N'Login $SqlLogin dropped.';
END
ELSE
BEGIN
    PRINT N'Login $SqlLogin does not exist — nothing to drop.';
END
"@

Set-Content -Path $TeardownSqlPath -Value $teardownSql -Encoding UTF8
sqlcmd -S localhost -E -i $TeardownSqlPath
$sqlExitCode = $LASTEXITCODE
Remove-Item -Path $TeardownSqlPath -ErrorAction SilentlyContinue

if ($sqlExitCode -ne 0) {
    Write-Output "WARNING: sqlcmd exited with code $sqlExitCode — see output above for the actual error."
}

} catch {
    # Caught, not re-thrown: the post-cleanup validation below is the whole
    # point of this script per the "validate via queries" requirement — it
    # needs to run and show what's still present even if this step failed
    # partway through, rather than the script dying here with a bare
    # exception and no final status report.
    Write-Output "ERROR during SQL Server cleanup: $($_.Exception.Message)"
}
Write-Output ""

# ---------------------------------------------------------------------------
# 4. IIS site, app pool, physical files, firewall rule, log directory.
#    Site must go before its app pool — IIS won't let you delete a pool
#    that's still assigned to an existing site.
# ---------------------------------------------------------------------------
Write-Output "--- IIS / filesystem / firewall cleanup ---"

try {

if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
    Remove-Website -Name $SiteName -Confirm:$false
    Write-Output "Removed IIS site '$SiteName'."
} else {
    Write-Output "IIS site '$SiteName' already absent."
}

if (Get-AppPoolInfo $AppPoolName) {
    Remove-WebAppPool -Name $AppPoolName -Confirm:$false
    Write-Output "Removed app pool '$AppPoolName'."
} else {
    Write-Output "App pool '$AppPoolName' already absent."
}

if (Test-Path $PhysicalPath) {
    Remove-Item -Path $PhysicalPath -Recurse -Force
    Write-Output "Removed '$PhysicalPath'."
} else {
    Write-Output "'$PhysicalPath' already absent."
}

if (Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue) {
    Remove-NetFirewallRule -DisplayName $FirewallRule
    Write-Output "Removed firewall rule '$FirewallRule'."
} else {
    Write-Output "Firewall rule '$FirewallRule' already absent."
}

if (Test-Path $LogDir) {
    Remove-Item -Path $LogDir -Recurse -Force
    Write-Output "Removed '$LogDir'."
    # Only remove the parent if this app was the only thing using it —
    # never delete C:\ProgramData\atmos if something else left files there.
    if ((Test-Path $LogDirParent) -and -not (Get-ChildItem -Path $LogDirParent -Force -ErrorAction SilentlyContinue)) {
        Remove-Item -Path $LogDirParent -Recurse -Force
        Write-Output "Removed now-empty '$LogDirParent'."
    }
} else {
    Write-Output "'$LogDir' already absent."
}

} catch {
    # Same reasoning as the SQL cleanup's try/catch above: let the
    # validation section below run and report actual state regardless.
    Write-Output "ERROR during IIS/filesystem/firewall cleanup: $($_.Exception.Message)"
}

Write-Output ""

# ---------------------------------------------------------------------------
# 5. Post-cleanup validation — explicit queries/checks confirming every
#    artifact is actually gone, not just assumed gone because the commands
#    above didn't error.
# ---------------------------------------------------------------------------
Write-Output "--- Post-cleanup validation ---"

$dbStillExists = sqlcmd -S localhost -E -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$SqlDatabase'"
Add-Result "SQL database '$SqlDatabase'" ($dbStillExists.Trim() -eq '0') "sys.databases row count: $($dbStillExists.Trim())"

$loginStillExists = sqlcmd -S localhost -E -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.server_principals WHERE name = N'$SqlLogin'"
Add-Result "SQL login '$SqlLogin'" ($loginStillExists.Trim() -eq '0') "sys.server_principals row count: $($loginStillExists.Trim())"

Add-Result "IIS site '$SiteName'" (-not (Get-Website -Name $SiteName -ErrorAction SilentlyContinue)) ""
Add-Result "App pool '$AppPoolName'" (-not (Get-AppPoolInfo $AppPoolName)) ""
Add-Result "Physical path '$PhysicalPath'" (-not (Test-Path $PhysicalPath)) ""
Add-Result "Firewall rule '$FirewallRule'" (-not (Get-NetFirewallRule -DisplayName $FirewallRule -ErrorAction SilentlyContinue)) ""
Add-Result "Log directory '$LogDir'" (-not (Test-Path $LogDir)) ""

Write-Output ""
$results | Format-Table -AutoSize @{L='Artifact';E={$_.Artifact}}, @{L='Gone?';E={if ($_.Ok) {'OK'} else {'STILL PRESENT'}}}, @{L='Detail';E={$_.Detail}}

if ($results | Where-Object { -not $_.Ok }) {
    Write-Output "One or more artifacts are still present — see table above."
    exit 1
} else {
    Write-Output "VM teardown complete and verified."
}
