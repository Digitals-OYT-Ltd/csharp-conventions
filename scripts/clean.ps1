#!/usr/bin/env pwsh
# Remove every bin/, obj/, artifacts/ and TestResults/ folder. Run it before repacking after adding a
# file under a package's build/ folder, or when a test run reports "Zero tests ran" for no reason.
# Run from the repo root: pwsh scripts/clean.ps1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Write-Host "`n==> 🧹 Cleaning" -ForegroundColor Cyan

Get-ChildItem -Path src, tests -Include bin, obj, TestResults -Recurse -Directory |
    Remove-Item -Recurse -Force
foreach ($folder in 'artifacts', 'TestResults') {
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
}

Write-Host "`n✅ Clean complete." -ForegroundColor Green
