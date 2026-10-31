#!/usr/bin/env pwsh
# Fail while AnalyzerReleases.Unshipped.md still lists a rule, for every package or one with
# -Package Analyzers or Testing. Rows move to Shipped.md in the release commit, and RS2008 cannot tell
# when that step was skipped. Each package's workflow runs this before packing a tag.
# Run from the repo root: pwsh scripts/check-release-ledger.ps1 [-Package Analyzers]

param(
    [string]$Package = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$filter = if ($Package) { "Digitals.$Package.CSharp" } else { 'Digitals.*.CSharp' }
$ledgers = @(Get-ChildItem src -Directory -Filter $filter | ForEach-Object { Join-Path $_.FullName 'AnalyzerReleases.Unshipped.md' })
if ($ledgers.Count -eq 0) {
    Write-Host "No analyzer project matches '$Package'. Use Analyzers or Testing." -ForegroundColor Red
    exit 1
}

$failed = $false
foreach ($ledger in $ledgers) {
    $name = Resolve-Path -Relative $ledger
    Write-Host "`n==> 🧾 $name" -ForegroundColor Cyan
    $rows = @(Get-Content $ledger | Where-Object { $_ -match '^DT\d{5}\s*\|' })
    if ($rows.Count -gt 0) {
        Write-Host "FAILED: still lists $($rows.Count) rule(s); move them to AnalyzerReleases.Shipped.md under this release's heading:" -ForegroundColor Red
        $rows | ForEach-Object { Write-Host "  $_" }
        $failed = $true
    }
}

if ($failed) { exit 1 }
Write-Host "`n✅ Release ledger clean." -ForegroundColor Green
