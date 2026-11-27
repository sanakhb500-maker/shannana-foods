<#
.SYNOPSIS
  Builds one release package (zip) with the ERP, the website and the server install scripts. Run on the development PC.
  Output: deploy\release\shannana-<date>-<commit>.zip  - copy it to the server, extract, and follow deploy\README.md.
#>
param([switch]$SkipTests, [switch]$SkipWebBuild)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$release = Join-Path $PSScriptRoot 'release'
$stage = Join-Path $release 'stage'

function Step($t) { Write-Host ""; Write-Host "== $t" -ForegroundColor Cyan }
function Run([scriptblock]$cmd, [string]$what) { & $cmd; if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" } }

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

if (-not $SkipTests) {
    Step 'ERP tests'
    Run { dotnet test (Join-Path $repo 'erp\Erp.sln') --nologo -v q } 'dotnet test'
}

if (-not $SkipWebBuild) {
    Step 'ERP web UI (Angular production build into the API''s wwwroot)'
    Push-Location (Join-Path $repo 'erp\web')
    try {
        if (-not (Test-Path 'node_modules')) { Run { npm ci --no-audit --no-fund } 'npm ci' }
        Run { npx ng build --configuration production } 'ng build'
    } finally { Pop-Location }
}

Step 'ERP API (dotnet publish)'
Run { dotnet publish (Join-Path $repo 'erp\src\Erp.Api\Erp.Api.csproj') -c Release -o (Join-Path $stage 'erp') --nologo -v q } 'dotnet publish'
foreach ($must in @('Erp.Api.dll', 'web.config', 'wwwroot\index.html')) {
    if (-not (Test-Path (Join-Path $stage "erp\$must"))) { throw "published ERP is missing $must" }
}
if (Test-Path (Join-Path $stage 'erp\appsettings.Development.json')) { throw 'appsettings.Development.json must not be in a release' }

Step 'Website (backend + frontend, without secrets, node_modules, uploads)'
$web = Join-Path $stage 'website'
& robocopy (Join-Path $repo 'backend') (Join-Path $web 'backend') /MIR /XD node_modules generated tests /XF .env '*.pdf' '*.phase4-backup' '*.log' /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'robocopy backend failed' }
& robocopy (Join-Path $repo 'frontend') (Join-Path $web 'frontend') /MIR /XD products /XF '*.phase6-backup' /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw 'robocopy frontend failed' }
$global:LASTEXITCODE = 0
if (Get-ChildItem (Join-Path $web 'backend') -Recurse -Filter '.env' -ErrorAction SilentlyContinue) { throw 'a .env file slipped into the release' }

Step 'Server scripts'
Copy-Item (Join-Path $PSScriptRoot 'server') (Join-Path $stage 'server') -Recurse
Copy-Item (Join-Path $PSScriptRoot 'config.example.psd1') (Join-Path $stage 'server\config.example.psd1')
Copy-Item (Join-Path $PSScriptRoot 'README.md') (Join-Path $stage 'README.md')
Copy-Item (Join-Path $PSScriptRoot 'INSTALL-STEPS.md') (Join-Path $stage 'INSTALL-STEPS.md')

$sha = 'nogit'
try { $sha = (git -C $repo rev-parse --short HEAD).Trim(); if (git -C $repo status --porcelain) { $sha += '-dirty' } } catch { }
$name = 'shannana-{0:yyyyMMdd-HHmm}-{1}' -f (Get-Date), $sha
Set-Content -Path (Join-Path $stage 'VERSION.txt') -Value "$name`nbuilt $(Get-Date -Format s) on $env:COMPUTERNAME"

Step 'Zip'
$zip = Join-Path $release "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
Write-Host ""
Write-Host ("Release ready: {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
