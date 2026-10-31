#!/usr/bin/env pwsh
# Build and pack every package into artifacts/, or one with -Package Analyzers or Testing. MinVer
# versions each package from its own tag: analyzers/v* or testing/v*.
# Run from the repo root: pwsh scripts/pack.ps1 [-Package Analyzers]

param(
    [string]$Package = '',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = if ($Package) { "src/Digitals.$Package" } else { '.' }
if (-not (Test-Path $target)) {
    Write-Host "No package at $target. Use Analyzers or Testing." -ForegroundColor Red
    exit 1
}

# No --no-build: a file added under build/ would otherwise ship from a stale nuspec.
Write-Host "`n==> 🗜️ Pack $target" -ForegroundColor Cyan
dotnet pack $target --configuration $Configuration --output $Output
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: dotnet pack (exit $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`n✅ Packed to $Output/." -ForegroundColor Green
