# AGENTS.md: agent instructions

> Canonical agent/project instructions for this repo. Tool-agnostic.

csharp-conventions, the two NuGet packages that carry the Digitals C# rules into every .NET repo. `Digitals.Analyzers` covers production code: the vendor analyzers, the severity policy, banned APIs and packages, the `DT0####` rules. `Digitals.Testing` covers test projects: the test stack, the test policy, the `DT1####` rules and the coverage gate. A consuming repo references the first from `Directory.Build.props` and the second from each test project, so a test project gets both.

## Tooling: `just`

- `just test`: every test project; `just test Analyzers` (or `Testing`) runs one package's.
- `just format` fixes formatting; `just format-check` checks it, and CI runs the same check scoped to the package.
- `just pack`: every `.nupkg` into `artifacts/`; `just pack Testing` packs one.
- `just check-ledger`: fails while an `AnalyzerReleases.Unshipped.md` still lists a rule. Each workflow runs it on a release tag.
- `just clean` removes every `bin/`, `obj/`, `artifacts/` and `TestResults/`. Run it before believing a `Zero tests ran`, and before repacking after adding a file under a package's `build/`.
- Release one package by tagging `analyzers/vX.Y.Z` or `testing/vX.Y.Z`; its workflow in `.github/workflows/` publishes it. MinVer reads the version from that package's tags only (`MinVerTagPrefix` in its csproj). There is no `<Version>` anywhere; never add one.
- A root file both packages build from (the props files, `global.json`, `nuget.config`, `.editorconfig`, `scripts/`) is in both workflows' `paths`, so add a new shared file to both.
- `.github/dependabot.yml` updates NuGet packages and Actions. It ignores the `Microsoft.CodeAnalysis.CSharp` floor and leaves `global.json` alone; keep both that way. `.github/workflows/security.yml` scans every package weekly and opens an issue on a known vulnerability. `SECURITY.md` is the runbook for withdrawing a vulnerable release.

Verification gate: before claiming done, run `just format-check`, `just test` and `just pack`. A change under a package's `build/`, its globalconfig or its packaging also needs a scratch consumer built against the packed nupkg, as that package's AGENTS.md describes.

## Standards

Digitals maintainers have the org standards through the `dt` CLI: `dt rules csharp` before C# work, `dt rules testing` before tests, citing rule IDs such as `csharp/pin-analyzer-ruleset`. Before changing policy (severities, banned symbols or packages, coverage floors), `dt read` the analyzer or testing standards pages. Without `dt`, `docs/rules/` and the `AGENTS.md` beside each package are the reference.

## Layout

- `src/Digitals.Analyzers/` is the package: dependencies plus `build/`, no compiled output of its own. Its rules are `src/Digitals.Analyzers.CSharp/`, tested in `tests/Digitals.Analyzers.CSharp.Tests/`. `src/Digitals.Analyzers/AGENTS.md` covers the severity policy, the vendor analyzers and the `DIGB` checks.
- `src/Digitals.Testing/` is the package, with its rules in `src/Digitals.Testing.CSharp/` and the coverage gate in `src/Digitals.Testing.Coverage/`, tested in `tests/Digitals.Testing.CSharp.Tests/` and `tests/Digitals.Testing.Coverage.Tests/`. `src/Digitals.Testing/AGENTS.md` covers the test stack, the policy, the gate and the `DIGT` checks.
- Each package's `README.md` is its nuget.org page. `docs/rules/<package>.md` explains every rule with a failing and a passing example; `docs/adoption/<package>.md` is the guide for moving a consuming repo onto it.
- `Directory.Packages.props` holds every version (central package management): what each package ships and what this repo builds with. A `PackageReference` in a csproj names the package only.
- `tests/Directory.Build.props` imports `Digitals.Testing.globalconfig` and the `DT1####` rules from source into every test project, so a relaxation these tests need is added where every consumer gets it, never in `.editorconfig`. Each test class carries `[Trait("Category", "Unit")]`, or `"Integration"` when it touches the file system.
- `.editorconfig` is the shared Digitals file, identical in every org .NET repo; change the org template first. Silence a rule for one file there, with a comment giving the reason.

## Gotchas

- This repo's own code is analyzed by the released `Digitals.Analyzers` from nuget.org (the version in `Directory.Packages.props`), not by the source in `src/`. A policy change reaches this repo only after it is released and that version is bumped.
- One ID space, two allocators. `Digitals.Analyzers` owns `DT0####` and `Digitals.Testing` owns `DT1####`; both reach every test project, so a shared number would make one `#pragma warning disable` silence two unrelated rules. Never reuse or renumber a shipped ID, since IDs live in suppressions and configs across org repos. A rule that moves out keeps its ID and gets a `### Removed Rules` row in `AnalyzerReleases.Unshipped.md`; the ID is retired, never recycled.
- A new rule is a row in its project's `AnalyzerReleases.Unshipped.md` (RS2008 fails the build without it), moved to `Shipped.md` under the version heading in the release commit. Don't list either file as `AdditionalFiles`: `Microsoft.CodeAnalysis.Analyzers` adds both itself, and a second item makes `dotnet format` report `Duplicate source file`.
- Never put a section header in a globalconfig: not `[*]`, not a path glob. A global AnalyzerConfig only accepts absolute paths as section names; Roslyn rejects anything else with `warning InvalidGlobalSectionName` and drops the section and every entry after it, which is the whole policy. Whitespace settings (`indent_size`, `charset`, ...) can't live there for the same reason; they belong in each consuming repo's `.editorconfig`.
- Unknown diagnostic IDs in a globalconfig are ignored with no warning, so a vendor that deletes a rule leaves a line that looks live and enforces nothing. Grep or `strings` on a vendor DLL gives a false pass: .NET literals are UTF-16LE and deleted IDs linger in deprecation maps. Enumerate `DiagnosticAnalyzer.SupportedDiagnostics` by reflection instead; `VendorDiagnosticIdsTests` does this for the production policy.
- `PrivateAssets="none"` on every shipped `PackageReference` in a package csproj is load-bearing. NuGet's default for a dependency is `exclude="build,analyzers"`; without it the package silently brings nothing.
- `build/<PackageId>.props` and `.targets` are imported by NuGet only because the filename matches the `PackageId` exactly. Renaming either breaks injection with no error.
- Verifying a package change needs a scratch consumer against the packed nupkg and `rm -rf ~/.nuget/packages/digitals.<package>` first. NuGet keys its extract by id and version, so a repacked same-version package is silently ignored in favour of the stale copy. This has produced a false "the policy still works" once already.
- `Microsoft.CodeAnalysis.CSharp` in `Directory.Packages.props` is a floor on every consumer's compiler, shared by both analyzer projects and their tests, never a "latest is better" knob. A consumer on an older compiler gets `warning CS9057`, the analyzer is dropped and every `DT#####` silently stops firing; under `TreatWarningsAsErrors` the build fails outright. The pin is `5.0.0`, what SDK 10.0.100 ships; the org runs nothing older than .NET 10. `5.9.0` is a `release/insiders` build that killed every DT rule everywhere.
- Analyzer test snippets don't inherit `LangVersion` 14. `CSharpAnalyzerTest` compiles `TestCode` at the harness default, so a collection expression in a snippet fails as `CS1525` and reports as an analyzer mismatch ("expected 0 diagnostics, actual 7"). Write snippets in older syntax (`Array.Empty<T>()`) or set `LanguageVersion` on the test.
- `GenerateDocumentationFile` plus `TreatWarningsAsErrors` makes every public member need a doc comment: `/// <inheritdoc />` on overrides, a one-line summary on descriptors. A generic written inside a comment (`<Main>$`) is XML there and fails as CS1570; write `&lt;Main&gt;`.
- `just test` needs `DOTNET_ROOT` to match the `dotnet` on PATH, and `scripts/test-unit.ps1` realigns it. xunit.v3 on Microsoft.Testing.Platform launches the test project's native apphost, which resolves its runtime from `DOTNET_ROOT`. Homebrew's `pwsh` hardcodes `DOTNET_ROOT` to its own bundled .NET, so a `just` recipe fails with "To install missing framework" while a bare `dotnet test` from zsh passes. Don't "fix" it by retargeting a test project.
- `dotnet test --nologo` reports `Zero tests ran`: the flag reaches the test host, which doesn't know it, and the failure reads as zero tests rather than a bad option. So does stale build state after changing a test project's TFM or MTP properties: `just clean` before believing it.
- A package's `README.md` is its nuget.org page (`PackageReadmeFile`). nuget.org doesn't resolve relative links, so link with absolute `https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/...` URLs.
- `<Description>` is deliberately generic and names no packages or rule IDs. nuget.org shows the current description for every past version, so anything version-specific there goes stale on the next release.
- Each workflow's nuget.org Trusted Publishing policy is bound to its filename (`analyzers.yml`, `testing.yml`). Renaming one still runs it, but `NuGet/login` fails to get a key. Update the policy on nuget.org first.

## Domain notes

- Which package a rule goes in: a rule that holds in production code goes in `Digitals.Analyzers`, which reaches every project. Only a rule that means nothing outside a test project goes in `Digitals.Testing`, which is scoped by its package reference. The `TimeProvider` subclass ban is `DT00007` in Analyzers for that reason; a copy that briefly shipped in Testing was deleted.
- Precedence: `Digitals.Analyzers.globalconfig` is `global_level 100`, `Digitals.Testing.globalconfig` is `300` and relaxes it in test projects. A repo-local `.editorconfig` beats both, and per-repo opt-outs there are expected.
- `DT#####` rules ship as `warning` (`DT00006` and `DT00011` as `suggestion`); repos escalate with `TreatWarningsAsErrors`. The `DIGB` and `DIGT` build checks are errors, each with a `DigitalsAllow*` opt-out.

## Recorded deviations

- From the org rule `csharp/use-directory-build-props`: `TargetFramework` stays per csproj and `LangVersion` is set (14). The analyzers and the coverage gate are netstandard2.0 by necessity, Digitals.Testing and the tests are net10.0. Reasons are in `Directory.Build.props`.
