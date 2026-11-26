$ErrorActionPreference = 'Stop'

$dashboardPath = Join-Path (Get-Location) 'frontend/admin/dashboard.html'
if (-not (Test-Path -LiteralPath $dashboardPath)) {
    throw 'dashboard.html was not found. Run this script from the shannana-foods project root.'
}

$content = [System.IO.File]::ReadAllText($dashboardPath, [System.Text.Encoding]::UTF8)
$inquiriesAnchor = '<a class="nav-item" href="/admin/inquiries">'
if (-not $content.Contains($inquiriesAnchor)) {
    throw 'Could not find the expected Inquiries link in dashboard.html. No changes were made.'
}

# Keep an existing Purchase Orders link, but move it to the required position.
$ordersPattern = '(?s)<a\b(?=[^>]*\bhref="/admin/purchase-orders")[^>]*>.*?</a>'
$ordersMatches = [regex]::Matches($content, $ordersPattern)
if ($ordersMatches.Count -gt 0) {
    $ordersLink = $ordersMatches[0].Value
    $content = [regex]::Replace($content, $ordersPattern, '')
} else {
    $ordersLink = '<a class="nav-item" href="/admin/purchase-orders"><span class="nav-symbol" aria-hidden="true">&#128722;</span> &#1591;&#1604;&#1576;&#1575;&#1578; &#1575;&#1604;&#1588;&#1585;&#1575;&#1569;</a>'
}

$replacement = $ordersLink + "`r`n      " + $inquiriesAnchor
$content = $content.Replace($inquiriesAnchor, $replacement)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($dashboardPath, $content, $utf8NoBom)
Write-Host 'Dashboard navigation updated: Purchase Orders is now before Inquiries.' -ForegroundColor Green
