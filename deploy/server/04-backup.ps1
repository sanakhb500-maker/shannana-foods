<#
.SYNOPSIS
  Nightly backup of everything that cannot be rebuilt: the ERP database, the website database, proof photos and product images.
  Run by the scheduled task created with  .\04-backup.ps1 -Register ; run it by hand any time with no switch.
  Also trims old backups and old log files.
#>
param([switch]$Register, [string]$At = '02:15')
. "$PSScriptRoot\common.ps1"
Assert-Admin
$cfg = Get-DeployConfig

if ($Register) {
    $script = (Resolve-Path $MyInvocation.MyCommand.Path).Path
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`""
    $trigger = New-ScheduledTaskTrigger -Daily -At $At
    $principal = New-ScheduledTaskPrincipal -UserId 'NT AUTHORITY\SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 3)
    Register-ScheduledTask -TaskName 'ShannanaBackup' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Ok "daily backup scheduled at $At (task ShannanaBackup, runs as SYSTEM)"
    return
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$root = Join-Path $cfg.DataRoot 'backups'
$sqlDir = Join-Path $root 'erp-sql'; $pgDir = Join-Path $root 'website-pg'; $filesDir = Join-Path $root 'files'
foreach ($d in @($sqlDir, $pgDir, $filesDir)) { New-DataFolder $d }
$logFile = Join-Path $root 'backup.log'
function Log($m) { ("{0:s}  {1}" -f (Get-Date), $m) | Add-Content -Path $logFile -Encoding UTF8; Write-Host $m }
$failed = $false

Log "---- backup $stamp"

# 1) ERP database (SQL Server): compressed when the edition allows it
try {
    $bak = Join-Path $sqlDir "$($cfg.ErpDbName)-$stamp.bak"
    try { Invoke-Sql $cfg.SqlServer "BACKUP DATABASE [$($cfg.ErpDbName)] TO DISK = N'$bak' WITH COMPRESSION, CHECKSUM, INIT" | Out-Null }
    catch { Invoke-Sql $cfg.SqlServer "BACKUP DATABASE [$($cfg.ErpDbName)] TO DISK = N'$bak' WITH CHECKSUM, INIT" | Out-Null }
    Invoke-Sql $cfg.SqlServer "RESTORE VERIFYONLY FROM DISK = N'$bak' WITH CHECKSUM" | Out-Null
    Log "ERP database OK: $bak ($([math]::Round((Get-Item $bak).Length / 1MB, 1)) MB)"
} catch { Log "ERP database FAILED: $_"; $failed = $true }

# 2) website database (PostgreSQL custom format; restore with pg_restore)
try {
    $psql = Find-Psql
    if ($psql) {
        $pgDump = Join-Path (Split-Path $psql) 'pg_dump.exe'
        $dump = Join-Path $pgDir "$($cfg.PgDbName)-$stamp.dump"
        $env:PGPASSWORD = Get-OrCreateSecret $cfg.DataRoot 'website-db-password' 32
        try { & $pgDump -h $cfg.PgHost -p "$($cfg.PgPort)" -U $cfg.PgUser -Fc -f $dump $cfg.PgDbName } finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }
        if ($LASTEXITCODE -ne 0) { throw "pg_dump exit code $LASTEXITCODE" }
        Log "website database OK: $dump ($([math]::Round((Get-Item $dump).Length / 1MB, 1)) MB)"
    } else { Log 'website database skipped (PostgreSQL not found)' }
} catch { Log "website database FAILED: $_"; $failed = $true }

# 3) files: proof photos and product images (incremental mirror)
foreach ($pair in @(@{ from = (Join-Path $cfg.DataRoot 'erp\proofs'); to = (Join-Path $filesDir 'erp-proofs') },
                    @{ from = (Join-Path $cfg.DataRoot 'website\products'); to = (Join-Path $filesDir 'website-products') })) {
    if (Test-Path $pair.from) {
        & robocopy $pair.from $pair.to /MIR /NFL /NDL /NJH /NJS /NP /R:2 /W:2 | Out-Null
        if ($LASTEXITCODE -ge 8) { Log "files FAILED: $($pair.from)"; $failed = $true } else { Log "files OK: $($pair.from)" }
        $global:LASTEXITCODE = 0
    }
}

# 4) keep only the last N days; trim the logs too
$cut = (Get-Date).AddDays(-[int]$cfg.BackupKeepDays)
Get-ChildItem $sqlDir, $pgDir -File -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -lt $cut } | Remove-Item -Force
Get-ChildItem (Join-Path $cfg.DataRoot 'website\logs'), (Join-Path $cfg.SitesRoot 'erp\logs') -File -ErrorAction SilentlyContinue |
    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-14) -and $_.Name -ne 'website.log' } | Remove-Item -Force -ErrorAction SilentlyContinue

# 5) optional copy to another machine (a backup on the same disk does not survive a dead disk)
if ($cfg.OffsiteBackupPath) {
    try {
        New-DataFolder $cfg.OffsiteBackupPath
        & robocopy $root $cfg.OffsiteBackupPath /MIR /XF backup.log /NFL /NDL /NJH /NJS /NP /R:2 /W:5 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy exit code $LASTEXITCODE" }
        $global:LASTEXITCODE = 0
        Log "copied to $($cfg.OffsiteBackupPath)"
    } catch { Log "off-site copy FAILED: $_"; $failed = $true }
} else { Log 'no OffsiteBackupPath set: backups stay on this server only' }

if ($failed) { Log 'finished WITH ERRORS'; exit 1 }
Log 'finished OK'
