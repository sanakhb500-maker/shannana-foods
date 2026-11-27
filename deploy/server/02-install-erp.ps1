<#
.SYNOPSIS
  Installs or upgrades the ERP (API + web UI + driver API) in IIS with its SQL Server database.
  First install:  .\02-install-erp.ps1 -AdminEmail admin@company.com
  Upgrade:        extract the new release and run the same command again (data, secrets and logs are kept; the database is backed up first).
.PARAMETER ConnectionString
  Only for a SQL Server on ANOTHER machine: the full connection string (SQL login). The database and its permissions must already exist.
#>
param(
    [string]$AdminEmail,
    [SecureString]$AdminPassword,
    [string]$AdminName = 'Administrator',
    [string]$ReleaseDir = (Join-Path $PSScriptRoot '..\erp'),
    [string]$ConnectionString
)
. "$PSScriptRoot\common.ps1"
Assert-Admin
Import-Module WebAdministration
$cfg = Get-DeployConfig

$siteName = 'ShannanaErp'
$poolName = 'ShannanaErp'
$app = Join-Path $cfg.SitesRoot 'erp'
$data = Join-Path $cfg.DataRoot 'erp'
$proofs = Join-Path $data 'proofs'
$identity = "IIS APPPOOL\$poolName"

if (-not (Test-Path (Join-Path $ReleaseDir 'Erp.Api.dll'))) { throw "Release not found at $ReleaseDir (Erp.Api.dll missing). Run from the extracted release's server folder." }

Write-Step 'Folders'
New-DataFolder $app; New-DataFolder $proofs; New-DataFolder (Join-Path $cfg.DataRoot 'backups'); New-DataFolder (Join-Path $app 'logs')
Write-Ok "$app  (program)    $data  (data)"

Write-Step 'Application pool'
if (-not (Test-Path "IIS:\AppPools\$poolName")) { New-WebAppPool -Name $poolName | Out-Null }
Set-ItemProperty "IIS:\AppPools\$poolName" -Name managedRuntimeVersion -Value ''          # no managed code: ASP.NET Core runs in-process
Set-ItemProperty "IIS:\AppPools\$poolName" -Name startMode -Value 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$poolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$poolName" -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
Write-Ok "pool $poolName runs as $identity"

Write-Step 'Database'
$settingsFile = Join-Path $app 'appsettings.Production.json'
$firstInstall = -not (Test-Path $settingsFile)   # no settings yet = nothing installed before
if ($ConnectionString) {
    Write-Warn2 'Remote SQL Server: using the connection string you gave; database and permissions are your responsibility.'
    $conn = $ConnectionString
} else {
    if (-not (Test-LocalSql $cfg.SqlServer)) { throw "SqlServer '$($cfg.SqlServer)' is not this machine. Pass -ConnectionString for a remote server." }
    $db = $cfg.ErpDbName
    $exists = (Invoke-Sql $cfg.SqlServer "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$db') IS NULL THEN 0 ELSE 1 END" | Where-Object { $_ -match '^\s*[01]\s*$' } | Select-Object -First 1).Trim() -eq '1'
    if ($exists) {
        $bak = Join-Path $cfg.DataRoot ("backups\$db-before-upgrade-{0:yyyyMMdd-HHmmss}.bak" -f (Get-Date))
        Write-Info "database exists; backing it up first: $bak"
        Invoke-Sql $cfg.SqlServer "BACKUP DATABASE [$db] TO DISK = N'$bak' WITH CHECKSUM, INIT" | Out-Null
        Write-Ok 'backup done'
    } else {
        Invoke-Sql $cfg.SqlServer "CREATE DATABASE [$db]" | Out-Null
        Write-Ok "database $db created"
    }
    # the application pool identity is the database user (Windows authentication, no password stored anywhere)
    $login = "IF SUSER_ID(N'$identity') IS NULL CREATE LOGIN [$identity] FROM WINDOWS WITH DEFAULT_DATABASE=[$db];"
    Invoke-Sql $cfg.SqlServer $login | Out-Null
    $user = "IF USER_ID(N'$identity') IS NULL CREATE USER [$identity] FOR LOGIN [$identity]; ALTER ROLE db_owner ADD MEMBER [$identity];"
    Invoke-Sql $cfg.SqlServer $user $db | Out-Null
    Write-Ok "login and db_owner for $identity (the app creates and migrates its own tables)"
    $conn = "Server=$($cfg.SqlServer);Database=$db;Integrated Security=true;TrustServerCertificate=True;MultipleActiveResultSets=False"
}

Write-Step 'Files'
$offline = Join-Path $app 'app_offline.htm'
if (Test-Path (Join-Path $app 'Erp.Api.dll')) {
    Set-Content -Path $offline -Value '<html><body style="font-family:sans-serif;text-align:center;padding:4rem"><h2>System update in progress, back in a minute.</h2><h2 dir="rtl">جاري تحديث النظام، نعود خلال دقيقة.</h2></body></html>' -Encoding UTF8
    Start-Sleep -Seconds 4
}
Sync-Folder -From (Resolve-Path $ReleaseDir).Path -To $app -KeepDirs @('logs') -KeepFiles @('appsettings.Production.json', 'app_offline.htm')
Write-Ok 'application files copied'

Write-Step 'Settings (appsettings.Production.json)'
$jwtKey = Get-OrCreateSecret $cfg.DataRoot 'erp-jwt-key' 64
$seed = [ordered]@{ LocalCurrencyCode = $cfg.LocalCurrencyCode }
if ($firstInstall) {
    if (-not $AdminEmail) { throw 'First install needs -AdminEmail (the first administrator).' }
    if (-not $AdminPassword) { $AdminPassword = Read-Host -AsSecureString "Password for $AdminEmail (min 8 chars, upper+lower+digit)" }
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminPassword))
    $seed['AdminEmail'] = $AdminEmail; $seed['AdminName'] = $AdminName; $seed['AdminPassword'] = $plain
}
$settings = [ordered]@{
    AllowedHosts      = "$($cfg.ErpHost);localhost"
    ConnectionStrings = [ordered]@{ Default = $conn }
    Jwt               = [ordered]@{ Key = $jwtKey; Issuer = 'shannana-erp'; Audience = 'shannana-erp-web'; ExpiryMinutes = 480; MobileExpiryMinutes = 720 }
    Storage           = [ordered]@{ ProofsPath = $proofs }
    Seed              = $seed
}
if ((Test-Path $settingsFile) -and -not $firstInstall) {
    Write-Info 'keeping the existing settings file (delete it to regenerate)'
} else {
    ($settings | ConvertTo-Json -Depth 5) | Set-Content -Path $settingsFile -Encoding UTF8
}
Protect-Path $settingsFile -ReadAccounts @($identity)
Grant-Path $proofs $identity 'Modify'
Grant-Path (Join-Path $app 'logs') $identity 'Modify'
Grant-Path $app $identity 'ReadAndExecute'
Write-Ok 'settings written; only Administrators and the app pool can read them'

Write-Step 'IIS site'
if (-not (Get-Website -Name $siteName -ErrorAction SilentlyContinue)) {
    New-Website -Name $siteName -PhysicalPath $app -ApplicationPool $poolName -Port 80 -HostHeader $cfg.ErpHost | Out-Null
} else {
    Set-ItemProperty "IIS:\Sites\$siteName" -Name physicalPath -Value $app
    Set-ItemProperty "IIS:\Sites\$siteName" -Name applicationPool -Value $poolName
}
# a local-only door for health checks (the https redirect ignores localhost)
if (-not (Get-WebBinding -Name $siteName -Protocol http | Where-Object { $_.bindingInformation -eq '127.0.0.1:8081:' })) {
    New-WebBinding -Name $siteName -Protocol http -IPAddress 127.0.0.1 -Port 8081 -HostHeader '' | Out-Null
}
$https = Add-HttpsBinding $siteName $cfg.ErpHost $cfg.CertThumbprint
if (Test-Path $offline) { Remove-Item $offline -Force }
Start-WebAppPool -Name $poolName -ErrorAction SilentlyContinue
Start-Website -Name $siteName -ErrorAction SilentlyContinue

Write-Step 'Start-up check'
if (Wait-Http 'http://127.0.0.1:8081/api/v1/health' 150) {
    Write-Ok 'the ERP answers (database created/migrated and starter data seeded)'
    if ($firstInstall -and (Test-Path $settingsFile)) {
        # the admin password is only needed for the first start: remove it from the file
        $j = Get-Content $settingsFile -Raw | ConvertFrom-Json
        $j.Seed.PSObject.Properties.Remove('AdminPassword')
        ($j | ConvertTo-Json -Depth 5) | Set-Content -Path $settingsFile -Encoding UTF8
        Write-Ok 'first administrator created; the password is no longer stored on disk'
    }
} else {
    Write-Warn2 "no answer. Look at $app\logs and the Event Viewer (Windows Logs > Application). Common causes: Hosting Bundle / URL Rewrite not installed, SQL permission."
    exit 1
}

Write-Step 'Done'
Write-Info "ERP: http://$($cfg.ErpHost)/   (admin: $AdminEmail)"
if ($https) { Write-Ok "https binding on $($cfg.ErpHost) is active" }
else { Write-Warn2 "No https binding yet. Get a certificate for $($cfg.ErpHost) (deploy/README.md, step 'HTTPS'), put its thumbprint in config.psd1 and run this script again." }
Write-Info ('Driver app: build it with  --dart-define=API_BASE=https://' + $cfg.ErpHost)
