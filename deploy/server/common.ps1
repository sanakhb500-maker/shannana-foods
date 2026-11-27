# Shared helpers for the install scripts. Dot-source it:  . "$PSScriptRoot\common.ps1"
Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Write-Step($t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "   OK  $t" -ForegroundColor Green }
function Write-Warn2($t){ Write-Host "   !!  $t" -ForegroundColor Yellow }
function Write-Info($t) { Write-Host "       $t" }

function Assert-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    if (-not (New-Object Security.Principal.WindowsPrincipal($id)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from an elevated PowerShell (Run as administrator).'
    }
}

function Get-DeployConfig {
    $candidates = @((Join-Path $PSScriptRoot 'config.psd1'), (Join-Path $PSScriptRoot '..\config.psd1'))
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $cfg = Import-PowerShellDataFile $c
            if ($cfg.ErpHost -like '*example.com' -or ($cfg.WebsiteHosts | Where-Object { $_ -like '*example.com' })) {
                throw "config.psd1 still has the example.com placeholders ($c). Edit it first."
            }
            return $cfg
        }
    }
    throw 'config.psd1 not found. Copy config.example.psd1 to config.psd1 (next to the scripts) and edit it.'
}

function New-RandomSecret([int]$Length = 40) {
    $chars = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'.ToCharArray()
    $bytes = New-Object byte[] ($Length)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    $rng.GetBytes($bytes)
    $rng.Dispose()
    -join ($bytes | ForEach-Object { $chars[$_ % $chars.Length] })
}

# Only Administrators/SYSTEM plus the named accounts; nothing inherited.
function Protect-Path([string]$Path, [string[]]$ReadAccounts = @(), [string[]]$ModifyAccounts = @()) {
    $isDir = Test-Path $Path -PathType Container
    if ($isDir) { $acl = New-Object System.Security.AccessControl.DirectorySecurity } else { $acl = New-Object System.Security.AccessControl.FileSecurity }
    $acl.SetAccessRuleProtection($true, $false)
    $inherit = 'None'
    if ($isDir) { $inherit = 'ContainerInherit,ObjectInherit' }
    foreach ($a in @('BUILTIN\Administrators', 'NT AUTHORITY\SYSTEM')) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($a, 'FullControl', $inherit, 'None', 'Allow')))
    }
    foreach ($a in $ReadAccounts) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($a, 'ReadAndExecute', $inherit, 'None', 'Allow')))
    }
    foreach ($a in $ModifyAccounts) {
        $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($a, 'Modify', $inherit, 'None', 'Allow')))
    }
    Set-Acl -Path $Path -AclObject $acl
}

function Grant-Path([string]$Path, [string]$Account, [string]$Rights = 'Modify') {
    $acl = Get-Acl $Path
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($Account, $Rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    Set-Acl -Path $Path -AclObject $acl
}

# Secrets are generated once and reused, so re-running an installer never changes a password the apps already use.
function Get-OrCreateSecret([string]$DataRoot, [string]$Name, [int]$Length = 40) {
    $dir = Join-Path $DataRoot 'secrets'
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null; Protect-Path $dir }
    $file = Join-Path $dir "$Name.txt"
    if (Test-Path $file) { return (Get-Content $file -Raw).Trim() }
    $v = New-RandomSecret $Length
    Set-Content -Path $file -Value $v -NoNewline -Encoding ASCII
    return $v
}

function Find-Sqlcmd {
    $cmd = Get-Command sqlcmd -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $c = Get-ChildItem 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe', 'C:\Program Files\Microsoft SQL Server\*\Tools\Binn\sqlcmd.exe' -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if ($c) { return $c.FullName }
    return $null
}

function Find-Psql {
    $cmd = Get-Command psql -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $c = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
    if ($c) { return $c.FullName }
    return $null
}

function Test-LocalSql([string]$SqlServer) {
    $h = ($SqlServer -split '\\')[0].Trim()
    return @('.', 'localhost', '(local)', '127.0.0.1', $env:COMPUTERNAME) -contains $h
}

function Invoke-Sql([string]$SqlServer, [string]$Query, [string]$Database = 'master') {
    $sqlcmd = Find-Sqlcmd
    if (-not $sqlcmd) { throw 'sqlcmd not found. Install the SQL Server command line utilities.' }
    $out = & $sqlcmd -S $SqlServer -d $Database -E -b -C -Q $Query 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $out" }
    return $out
}

function Wait-Http([string]$Url, [int]$Seconds = 90) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        try {
            $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10
            if ($r.StatusCode -ge 200 -and $r.StatusCode -lt 400) { return $true }
        } catch { Start-Sleep -Seconds 3 }
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Add-HttpsBinding([string]$SiteName, [string]$HostName, [string]$Thumbprint) {
    if (-not $Thumbprint) { return $false }
    $cert = Get-Item "Cert:\LocalMachine\My\$Thumbprint" -ErrorAction SilentlyContinue
    if (-not $cert) { Write-Warn2 "Certificate $Thumbprint is not in LocalMachine\My; https binding skipped."; return $false }
    $existing = Get-WebBinding -Name $SiteName -Protocol https | Where-Object { $_.bindingInformation -like "*:443:$HostName" }
    if (-not $existing) { New-WebBinding -Name $SiteName -Protocol https -Port 443 -HostHeader $HostName -SslFlags 1 | Out-Null }
    $b = Get-WebBinding -Name $SiteName -Protocol https | Where-Object { $_.bindingInformation -like "*:443:$HostName" }
    $b.AddSslCertificate($Thumbprint, 'My')
    return $true
}

function New-DataFolder([string]$Path) {
    if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

# Copies a release folder over an installed one, leaving the named folders/files of the target alone.
function Sync-Folder([string]$From, [string]$To, [string[]]$KeepDirs = @(), [string[]]$KeepFiles = @()) {
    New-DataFolder $To
    $args2 = @($From, $To, '/MIR', '/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:2', '/W:2')
    if ($KeepDirs.Count -gt 0) { $args2 += '/XD'; $args2 += $KeepDirs }
    if ($KeepFiles.Count -gt 0) { $args2 += '/XF'; $args2 += $KeepFiles }
    & robocopy @args2 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE) copying $From to $To" }
    $global:LASTEXITCODE = 0
}
