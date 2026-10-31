#!/usr/bin/env pwsh
# Run every test project, or one package's with -Package Analyzers or Testing.
# Run from the repo root: pwsh scripts/test-unit.ps1 [-Package Analyzers]

param(
    [string]$Package = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$filter = if ($Package) { "Digitals.$Package.*Tests" } else { '*' }
$projects = @(Get-ChildItem tests -Directory -Filter $filter)
if ($projects.Count -eq 0) {
    Write-Host "No test project matches '$Package'. Use Analyzers or Testing." -ForegroundColor Red
    exit 1
}

# Align DOTNET_ROOT with the 'dotnet' actually on PATH. xunit.v3 runs on Microsoft Testing Platform,
# which launches the test project's native apphost, and an apphost resolves its runtime from
# DOTNET_ROOT rather than from PATH. Some pwsh launchers (Homebrew's is a shell script) hardcode
# DOTNET_ROOT to their own bundled .NET, which need not have the test project's runtime ->
# "To install missing framework ...".
$dotnetOnPath = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($dotnetOnPath) {
    $dotnetRoot = Split-Path -Parent $dotnetOnPath.Source
    if (Test-Path (Join-Path $dotnetRoot 'shared/Microsoft.NETCore.App')) {
        $env:DOTNET_ROOT = $dotnetRoot
    }
}

foreach ($project in $projects) {
    Write-Host "`n==> 🔬 $($project.Name)" -ForegroundColor Cyan
    dotnet test --project $project.FullName --configuration Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $($project.Name) (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
