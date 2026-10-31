set shell := ["pwsh", "-NoProfile", "-Command"]
set windows-shell := ["powershell.exe", "-NoProfile", "-Command"]

# List all available recipes
default:
    @just --list

# Restore and build the solution
build:
    dotnet build

# Run every test project, or one package's: just test Analyzers
test package="":
    & ./scripts/test-unit.ps1 -Package '{{package}}'

# Pack every package into artifacts/, or one: just pack Testing (version from its testing/v* tag via MinVer)
pack package="":
    & ./scripts/pack.ps1 -Package '{{package}}'

# Fix .cs file formatting
format:
    dotnet format

# Fail if any .cs file is not formatted (what CI runs)
format-check:
    dotnet format --verify-no-changes

# Fail while a package's AnalyzerReleases.Unshipped.md still lists a rule (each workflow runs it on a tag)
check-ledger package="":
    & ./scripts/check-release-ledger.ps1 -Package '{{package}}'

# Remove every bin/, obj/, artifacts/ and TestResults/ folder
clean:
    & ./scripts/clean.ps1
