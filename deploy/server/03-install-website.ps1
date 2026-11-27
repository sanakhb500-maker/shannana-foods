<#
.SYNOPSIS
  Installs or upgrades the public website: Node/Express runs as a start-up task on 127.0.0.1, IIS (with Application Request Routing) serves it on ports 80/443.
  First install:  .\03-install-website.ps1 -AdminEmail owner@company.com -AdminName "Owner"      (asks for the passwords)
  Upgrade:        extract the new release and run the same command again (uploads, secrets and logs are kept).
.PARAMETER PgSuperPassword
  Password of the PostgreSQL "postgres" superuser, needed once to create the website's database and role.
#>
param(
    [string]$AdminEmail,
    [string]$AdminName = 'Administrator',
    [SecureString]$AdminPassword,
    [SecureString]$PgSuperPassword,
    [string]$ReleaseDir = (Join-Path $PSScriptRoot '..\website')
)
. "$PSScriptRoot\common.ps1"
Assert-Admin
Import-Module WebAdministration
$cfg = Get-DeployConfig

function ToPlain([SecureString]$s) { [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) }

$siteName = 'ShannanaWebsite'
$taskName = 'ShannanaWebsite'
$root = Join-Path $cfg.SitesRoot 'website'
$backend = Join-Path $root 'backend'
$iisRoot = Join-Path $root 'iis'
$data = Join-Path $cfg.DataRoot 'website'
$uploads = Join-Path $data 'products'
$logs = Join-Path $data 'logs'
$canonical = $cfg.WebsiteHosts[0]
$service = 'NT AUTHORITY\LOCAL SERVICE'

if (-not (Test-Path (Join-Path $ReleaseDir 'backend\package.json'))) { throw "Website release not found at $ReleaseDir." }

Write-Step 'Tools'
$node = Get-Command node -ErrorAction SilentlyContinue
if (-not $node) { throw 'Node.js is not installed (run 01-prerequisites.ps1).' }
$npm = Join-Path (Split-Path $node.Source) 'npm.cmd'
$psql = Find-Psql
if (-not $psql) { throw 'PostgreSQL client (psql) not found (run 01-prerequisites.ps1).' }
Write-Ok "node $(& node --version)   $psql"

Write-Step 'Database (PostgreSQL)'
$pgPassword = Get-OrCreateSecret $cfg.DataRoot 'website-db-password' 32
$jwtSecret = Get-OrCreateSecret $cfg.DataRoot 'website-jwt-secret' 64
if (-not $PgSuperPassword) { $PgSuperPassword = Read-Host -AsSecureString 'PostgreSQL "postgres" superuser password' }
$env:PGPASSWORD = ToPlain $PgSuperPassword
try {
    $h = @('-h', $cfg.PgHost, '-p', "$($cfg.PgPort)", '-U', 'postgres', '-v', 'ON_ERROR_STOP=1', '-tA')
    $role = (& $psql @h -d postgres -c "SELECT 1 FROM pg_roles WHERE rolname='$($cfg.PgUser)'") -join ''
    if ($LASTEXITCODE -ne 0) { throw 'cannot connect to PostgreSQL as postgres (wrong password or server not running).' }
    if ($role.Trim() -ne '1') { & $psql @h -d postgres -c "CREATE ROLE $($cfg.PgUser) LOGIN PASSWORD '$pgPassword'" | Out-Null; Write-Ok "role $($cfg.PgUser) created" }
    else { & $psql @h -d postgres -c "ALTER ROLE $($cfg.PgUser) PASSWORD '$pgPassword'" | Out-Null }
    $dbExists = (& $psql @h -d postgres -c "SELECT 1 FROM pg_database WHERE datname='$($cfg.PgDbName)'") -join ''
    if ($dbExists.Trim() -ne '1') {
        & $psql @h -d postgres -c "CREATE DATABASE $($cfg.PgDbName) OWNER $($cfg.PgUser) ENCODING 'UTF8' TEMPLATE template0" | Out-Null
        Write-Ok "database $($cfg.PgDbName) created"
    } else { Write-Ok "database $($cfg.PgDbName) exists" }
} finally { Remove-Item Env:\PGPASSWORD -ErrorAction SilentlyContinue }

Write-Step 'Stop the running website (upgrade)'
Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
Get-CimInstance Win32_Process -Filter "Name='node.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like "*$backend*" -or $_.CommandLine -like '*src\server.js*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

Write-Step 'Files'
New-DataFolder $uploads; New-DataFolder $logs; New-DataFolder $iisRoot
# uploaded product images live under DataRoot and are linked into the site, so an upgrade never touches them
$frontend = Join-Path $root 'frontend'
$link = Join-Path $frontend 'img\products'
Sync-Folder -From (Join-Path $ReleaseDir 'backend') -To $backend -KeepDirs @('node_modules', 'generated') -KeepFiles @('.env')
Sync-Folder -From (Join-Path $ReleaseDir 'frontend') -To $frontend -KeepDirs @('products')
if (-not (Test-Path $link)) {
    New-Item -ItemType Directory -Path (Split-Path $link) -Force | Out-Null
    New-Item -ItemType Junction -Path $link -Target $uploads | Out-Null
} elseif (-not ((Get-Item $link).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    # first run after a copy that contained a real folder: move what is in it, then link
    Get-ChildItem $link -Force | Move-Item -Destination $uploads -Force
    Remove-Item $link -Force -Recurse
    New-Item -ItemType Junction -Path $link -Target $uploads | Out-Null
}
Write-Ok "program: $root   uploads: $uploads"

Write-Step 'Settings (.env)'
$envFile = Join-Path $backend '.env'
$lines = @(
    'NODE_ENV=production',
    "PORT=$($cfg.NodePort)",
    "DATABASE_URL=postgresql://$($cfg.PgUser):$pgPassword@$($cfg.PgHost):$($cfg.PgPort)/$($cfg.PgDbName)",
    "JWT_SECRET=$jwtSecret",
    "WHATSAPP_NUMBER=$($cfg.WhatsappNumber)",
    "ORDER_WHATSAPP_NUMBER=$($cfg.OrderWhatsappNumber)",
    'CORS_ORIGINS=',
    'TRUST_PROXY=1',                                  # one proxy (IIS) in front: real client IPs and https detection
    "SITE_URL=https://$canonical"
)
Set-Content -Path $envFile -Value $lines -Encoding ASCII
Protect-Path $envFile -ReadAccounts @($service)
Write-Ok '.env written (readable only by Administrators and the website account)'

Write-Step 'Packages and database schema'
Push-Location $backend
try {
    & $npm ci --no-audit --no-fund
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed (is the server online?)' }
    & $npm run db:generate
    if ($LASTEXITCODE -ne 0) { throw 'prisma generate failed' }
    & $npm run db:migrate:deploy
    if ($LASTEXITCODE -ne 0) { throw 'prisma migrate deploy failed' }
    Write-Ok 'schema is up to date'
} finally { Pop-Location }

# the very first install also loads the categories/products and the first admin
$seedMarker = Join-Path $data 'seeded.txt'
if (-not (Test-Path $seedMarker)) {
    if (-not $AdminEmail) { throw 'First install needs -AdminEmail (the website admin).' }
    if (-not $AdminPassword) { $AdminPassword = Read-Host -AsSecureString "Website admin password for $AdminEmail (12+ characters)" }
    Push-Location $backend
    try {
        $env:ADMIN_NAME = $AdminName; $env:ADMIN_EMAIL = $AdminEmail; $env:ADMIN_PASSWORD = ToPlain $AdminPassword
        & $npm run db:seed
        if ($LASTEXITCODE -ne 0) { throw 'seed failed' }
        Set-Content -Path $seedMarker -Value (Get-Date -Format s)
        Write-Ok 'starter data and the first admin loaded'
    } finally { Remove-Item Env:\ADMIN_NAME, Env:\ADMIN_EMAIL, Env:\ADMIN_PASSWORD -ErrorAction SilentlyContinue; Pop-Location }
}

Write-Step 'Permissions'
Grant-Path $root $service 'ReadAndExecute'
Grant-Path $data $service 'Modify'
Write-Ok 'the website runs as LOCAL SERVICE (no admin rights); it can write only the uploads and logs'

Write-Step 'Start-up task (keeps Node running, restarts it if it stops)'
$runner = Join-Path $root 'run-website.ps1'
$runnerText = @"
`$ErrorActionPreference = 'Continue'
Set-Location '$backend'
`$log = '$logs\website.log'
if ((Test-Path `$log) -and ((Get-Item `$log).Length -gt 20MB)) { Move-Item `$log ('$logs\website-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date)) -Force }
& '$($node.Source)' --disable-warning=ExperimentalWarning src\server.js 2>&1 | ForEach-Object { `$_.ToString() } | Out-File -FilePath `$log -Append -Encoding utf8
"@
Set-Content -Path $runner -Value $runnerText -Encoding ASCII
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$runner`""
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId $service -LogonType ServiceAccount -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -StartWhenAvailable
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Ok "task $taskName registered and started"

Write-Step 'IIS (reverse proxy to Node)'
$proxy = 'MACHINE/WEBROOT/APPHOST'
Set-WebConfigurationProperty -PSPath $proxy -Filter 'system.webServer/proxy' -Name enabled -Value $true
Set-WebConfigurationProperty -PSPath $proxy -Filter 'system.webServer/proxy' -Name preserveHostHeader -Value $true   # the website refuses writes whose Origin differs from Host
Set-WebConfigurationProperty -PSPath $proxy -Filter 'system.webServer/proxy' -Name reverseRewriteHostInResponseHeaders -Value $false
Set-WebConfigurationProperty -PSPath $proxy -Filter 'system.webServer/proxy' -Name timeout -Value ([TimeSpan]::FromMinutes(2))

$altHosts = @($cfg.WebsiteHosts | Select-Object -Skip 1)
$altRule = ''
if ($altHosts.Count -gt 0) {
    $alts = ($altHosts | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $altRule = @"

          <rule name="CanonicalHost" stopProcessing="true">
            <match url="(.*)" />
            <conditions><add input="{HTTP_HOST}" pattern="^($alts)$" /></conditions>
            <action type="Redirect" url="https://$canonical/{R:1}" redirectType="Permanent" />
          </rule>
"@
}
$webConfig = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <security><requestFiltering removeServerHeader="true"><requestLimits maxAllowedContentLength="10485760" /></requestFiltering></security>
    <httpProtocol><customHeaders><remove name="X-Powered-By" /></customHeaders></httpProtocol>
    <staticContent><remove fileExtension="." /><mimeMap fileExtension="." mimeType="text/plain" /></staticContent>
    <rewrite>
      <rules>
          <rule name="AcmeChallenge" stopProcessing="true">
            <match url="^\.well-known/acme-challenge/.*" />
            <action type="None" />
          </rule>
          <rule name="HttpsRedirect" stopProcessing="true">
            <match url="(.*)" />
            <conditions logicalGrouping="MatchAll">
              <add input="{HTTPS}" pattern="off" />
              <add input="{HTTP_HOST}" pattern="^localhost(:\d+)?$" negate="true" />
              <add input="{REQUEST_URI}" pattern="^/\.well-known/" negate="true" />
            </conditions>
            <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
          </rule>$altRule
          <rule name="ToNode" stopProcessing="true">
            <match url="(.*)" />
            <serverVariables><set name="HTTP_X_FORWARDED_PROTO" value="https" /></serverVariables>
            <action type="Rewrite" url="http://127.0.0.1:$($cfg.NodePort)/{R:1}" />
          </rule>
      </rules>
    </rewrite>
  </system.webServer>
</configuration>
"@
Set-Content -Path (Join-Path $iisRoot 'web.config') -Value $webConfig -Encoding UTF8

if (-not (Get-Website -Name $siteName -ErrorAction SilentlyContinue)) {
    New-Website -Name $siteName -PhysicalPath $iisRoot -Port 80 -HostHeader $canonical | Out-Null
} else { Set-ItemProperty "IIS:\Sites\$siteName" -Name physicalPath -Value $iisRoot }
$httpsAny = $false
foreach ($h in $cfg.WebsiteHosts) {
    if (-not (Get-WebBinding -Name $siteName -Protocol http | Where-Object { $_.bindingInformation -like "*:80:$h" })) { New-WebBinding -Name $siteName -Protocol http -Port 80 -HostHeader $h | Out-Null }
    if (Add-HttpsBinding $siteName $h $cfg.CertThumbprint) { $httpsAny = $true }
}
if (-not (Get-WebBinding -Name $siteName -Protocol http | Where-Object { $_.bindingInformation -eq '127.0.0.1:8082:' })) {
    New-WebBinding -Name $siteName -Protocol http -IPAddress 127.0.0.1 -Port 8082 -HostHeader '' | Out-Null
}
# the rewrite rule sets a server variable: it must be allowed for this site
$existingVar = Get-WebConfiguration -PSPath $proxy -Location $siteName -Filter "system.webServer/rewrite/allowedServerVariables/add[@name='HTTP_X_FORWARDED_PROTO']"
if (-not $existingVar) { Add-WebConfigurationProperty -PSPath $proxy -Location $siteName -Filter 'system.webServer/rewrite/allowedServerVariables' -Name '.' -Value @{ name = 'HTTP_X_FORWARDED_PROTO' } }
Start-Website -Name $siteName -ErrorAction SilentlyContinue
Write-Ok 'site and bindings ready'

Write-Step 'Start-up check'
$nodeOk = Wait-Http "http://127.0.0.1:$($cfg.NodePort)/robots.txt" 90
$iisOk = Wait-Http 'http://127.0.0.1:8082/robots.txt' 30
if ($nodeOk) { Write-Ok 'Node answers' } else { Write-Warn2 "Node does not answer: read $logs\website.log" }
if ($iisOk) { Write-Ok 'IIS forwards to Node' } else { Write-Warn2 'IIS does not forward: check that URL Rewrite and ARR are installed.' }

Write-Step 'Done'
Write-Info "Website: http://$canonical/     admin: /admin/login.html"
if (-not $httpsAny) { Write-Warn2 'No https binding yet. Get certificates (deploy/README.md, step "HTTPS"), put the thumbprint in config.psd1 and run this script again.' }
if (-not ($nodeOk -and $iisOk)) { exit 1 }
