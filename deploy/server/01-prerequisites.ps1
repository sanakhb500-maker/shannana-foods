<#
.SYNOPSIS
  Prepares Windows Server (IIS roles, firewall) and reports which required software is still missing.
  Run first, from an elevated PowerShell. Safe to run again.
#>
. "$PSScriptRoot\common.ps1"
Assert-Admin

Write-Step 'IIS roles'
$features = @('Web-Server', 'Web-Common-Http', 'Web-Static-Content', 'Web-Default-Doc', 'Web-Http-Errors', 'Web-Http-Redirect',
              'Web-Http-Logging', 'Web-Request-Monitor', 'Web-Filtering', 'Web-Stat-Compression', 'Web-Dyn-Compression', 'Web-Mgmt-Console')
if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
    $r = Install-WindowsFeature -Name $features
    if ($r.RestartNeeded -eq 'Yes') { Write-Warn2 'A restart is required to finish installing IIS. Restart, then run this script again.' } else { Write-Ok 'IIS roles installed' }
} else {
    Write-Warn2 'Install-WindowsFeature is not available (not Windows Server?). Enable IIS from "Turn Windows features on or off".'
}

Write-Step 'Firewall (only web ports are opened)'
foreach ($p in @(@{ n = 'Shannana HTTP'; port = 80 }, @{ n = 'Shannana HTTPS'; port = 443 })) {
    if (-not (Get-NetFirewallRule -DisplayName $p.n -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $p.n -Direction Inbound -Protocol TCP -LocalPort $p.port -Action Allow | Out-Null
    }
}
Write-Ok 'ports 80 and 443 allowed. Do NOT open 1433 (SQL Server), 5432 (PostgreSQL) or 3000 (Node) to the internet.'

Write-Step 'Required software'
$missing = New-Object System.Collections.ArrayList
function Check($name, $ok, $detail, $url) {
    if ($ok) { Write-Ok "$name  $detail" }
    else { Write-Host "   --  $name  MISSING" -ForegroundColor Red; Write-Info "    get it: $url"; [void]$missing.Add($name) }
}

$aspnet = $null
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { $aspnet = (& dotnet --list-runtimes) | Where-Object { $_ -like 'Microsoft.AspNetCore.App 10.*' } | Select-Object -First 1 }
Check '.NET 10 ASP.NET Core runtime' ([bool]$aspnet) "$aspnet" 'https://dotnet.microsoft.com/download/dotnet/10.0  ("Hosting Bundle" under ASP.NET Core Runtime; restart IIS after: iisreset)'
Check 'ASP.NET Core IIS module' (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll") '' 'the same Hosting Bundle installer'
Check 'IIS URL Rewrite' (Test-Path "$env:windir\System32\inetsrv\rewrite.dll") '' 'https://www.iis.net/downloads/microsoft/url-rewrite'
Check 'IIS Application Request Routing (website proxy)' (Test-Path "$env:ProgramFiles\IIS\Application Request Routing\requestRouter.dll") '' 'https://www.iis.net/downloads/microsoft/application-request-routing'

$sqlService = Get-Service -Name 'MSSQL$*', 'MSSQLSERVER' -ErrorAction SilentlyContinue | Select-Object -First 1
Check 'SQL Server (ERP database)' ([bool]$sqlService) ($(if ($sqlService) { $sqlService.Name } else { '' })) 'https://www.microsoft.com/sql-server/sql-server-downloads  (Express is enough to start)'
Check 'sqlcmd' ([bool](Find-Sqlcmd)) '' 'https://learn.microsoft.com/sql/tools/sqlcmd/sqlcmd-utility  (installed with SQL Server)'

$node = Get-Command node -ErrorAction SilentlyContinue
$nodeOk = $false
$nodeVer = ''
if ($node) { $nodeVer = (& node --version).TrimStart('v'); $nodeOk = ([version]($nodeVer -replace '^(\d+\.\d+\.\d+).*', '$1')) -ge [version]'22.18.0' }
Check 'Node.js 22.18 or newer (website)' $nodeOk $nodeVer 'https://nodejs.org  (LTS, "install for all users")'
Check 'PostgreSQL (website database)' ([bool](Find-Psql)) '' 'https://www.postgresql.org/download/windows/'

Write-Step 'Result'
if ($missing.Count -eq 0) {
    Write-Ok 'Everything is in place. Next: 02-install-erp.ps1 and 03-install-website.ps1.'
    $acme = Get-ChildItem 'C:\Program Files*\win-acme*\wacs.exe', 'C:\tools\win-acme*\wacs.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $acme) { Write-Info 'For free HTTPS certificates install win-acme (https://www.win-acme.com) after the sites exist; see deploy/README.md.' }
} else {
    Write-Warn2 ("Install these first, then run this script again: " + ($missing -join ', '))
    exit 1
}
