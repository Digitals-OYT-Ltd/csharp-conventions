# AGENTS.md: Digitals.Testing

The test half: the test stack (xunit.v3, NSubstitute, FakeTimeProvider, Microsoft's coverage engine, the TRX reporter), the test severity policy, the banned test packages, the coverage settings, the coverage gate and the `DT1####` rules. Only test projects reference it, so everything in `build/` is scoped correctly by construction. That is why the `.Tests` name matching Digitals.Analyzers once needed has no equivalent here; don't reintroduce it.

## Layout

- `build/` is the whole consumer-facing contract. `Digitals.Testing.props` holds the project defaults, the globalconfig, the `using Xunit` and the gate's registration. `Digitals.Testing.targets` holds `DIGT001`, `DIGT002`, the coverage arguments and `DigitalsDropProductionBannedSymbols`, which removes Digitals.Analyzers' `BannedSymbols.Production.txt` by its `DigitalsProductionOnly` metadata. `build/config/` holds the severity policy (`global_level 300`) and the coverage settings.
- The coverage gate is `../Digitals.Testing.Coverage`, a Microsoft.Testing.Platform extension packed into `lib/net10.0/`. It's tested in `../../tests/Digitals.Testing.Coverage.Tests`, which deliberately doesn't reference `Digitals.Testing`: that would run the gate over the gate's own tests in the build that produces it.
- The `DT1####` rules are `../Digitals.Testing.CSharp`, packed into `analyzers/dotnet/cs/` and tested in `../../tests/Digitals.Testing.CSharp.Tests`.

## How the gate connects

The gate is an MTP extension because under .NET 10 `dotnet test` with the MTP runner, no post-test MSBuild target runs at all. `AfterTargets` on `InvokeTestingPlatform`, `Test` and `VSTest` were each probed and none fire, and Microsoft's coverage extension has no threshold option of its own.

1. `Digitals.Testing.props` declares a `TestingPlatformBuilderHook` item naming `Digitals.Testing.Coverage.TestingPlatformBuilderHook`.
2. The MTP source generator turns it into a call in the consumer's generated `SelfRegisteredExtensions.cs`. The type name is a literal string contract: renaming the namespace or type breaks every consumer's build inside generated code, with nothing pointing back here.
3. `Digitals.Testing.targets` injects `--coverage --digitals-coverage-gate ...` through `TestingPlatformCommandLineArguments`.
4. `CoverageGateCommandLineProvider` registers those option names. MTP rejects the whole run with "unknown option" if one is missing, so the targets and the provider move together.
5. `CoverageGate` collects the `.cobertura.xml` session artifacts during the run and judges them in `AfterRunAsync`. `CoverageSummary` reads the two run-wide floors off the root element; `MethodSummary` is parsed once and feeds both the CRAP limit and the complexity cap.

The gate returns without checking when the run's exit code is non-zero: a failing run has already said why, and coverage from a partial run isn't worth judging. It throws when it is enabled but found no report, because a gate that goes quiet without data leaves the build green while nothing is enforced. Reports are aggregated by summing `lines-covered`, `lines-valid`, `branches-covered` and `branches-valid` and dividing once; averaging per-report rates would let a tiny uncovered assembly outweigh a large well-covered one.

## Adding a rule

1. Take the next `DT1####` ID. Read both `AnalyzerReleases.*.md` ledgers first.
2. Write the analyzer in `../Digitals.Testing.CSharp`. Scope it by attribute where you can, as `TestNamingAnalyzer` keys off `[Fact]` and `[Theory]`, so it stays inert in a shared library that happens to reference this package.
3. Add the `AnalyzerReleases.Unshipped.md` row, and the severity to the globalconfig (`warning`).
4. Test the reporting cases, the clean cases and the `#pragma` escape. Add its section to `docs/rules/testing.md` with the message captured from a real build.

## Bumping a bundled package

The version graph is the fragile part. xunit.v3 3.x is the `mtp-v1` lineage and pins Microsoft.Testing.Platform 1.9.1. `Microsoft.Testing.Extensions.CodeCoverage` 18.x lifts `Microsoft.Testing.Platform` to 2.x while leaving `Microsoft.Testing.Platform.MSBuild` at 1.9.1, and the run dies with `TypeLoadException: Could not load type 'Microsoft.Testing.Platform.Extensions.TestHost.IDataConsumer'`. The working set is xunit.v3 4.0.2 (`mtp-v2`), CodeCoverage 18.12.0 and TrxReport 2.5.1, all on platform 2.5.1. xunit.v3 4.0.2 resolves platform 2.5.1 and CodeCoverage 18.12.0 needs at least 2.5.0, so neither 2.4.x nor 2.5.2 fits.

1. Run `dotnet list package --include-transitive` on a scratch consumer and check that `Microsoft.Testing.Platform` and `Microsoft.Testing.Platform.MSBuild` land on the same major.
2. Keep the `Microsoft.Testing.Platform` version at or below what xunit.v3 resolves. It is a floor on the consumer: generated code there compiles the gate against whatever xunit.v3 drags in.
3. Check that every ID the globalconfig names still exists in its vendor's descriptor set.

A bundled package bump is at least a minor release, even when the bundled bump is a patch: consumers can't override it without removing this package, so it reaches them as a change in their test stack.

## Verifying a change

Unit tests don't cover `build/`, the packaging or the gate wiring. Pack, `rm -rf ~/.nuget/packages/digitals.testing`, and build a scratch consumer: a `global.json` with the MTP runner, one library, one test project with a single `PackageReference` to `Digitals.Testing`. Check that:

- a test fully covering the library passes;
- deleting one covered branch from the tests fails with `Line coverage ... is below the ... floor`;
- `<DigitalsSkipCoverageGate>true</DigitalsSkipCoverageGate>` passes and still writes `TestResults/*.cobertura.xml`;
- a fully covered method above `DigitalsMaxCyclomaticComplexity` fails with `above the cyclomatic complexity limit`, and `<DigitalsSkipComplexityGate>true</DigitalsSkipComplexityGate>` leaves the floors and the CRAP limit in place;
- removing the `test` block from `global.json` fails with `DIGT001`;
- adding `Microsoft.NET.Test.Sdk` fails with `DIGT002`;
- a test named `add_Works` warns `DT10001`, proving the analyzer and the globalconfig both arrived.

## Gotchas

- The package project impersonates a test project and fights back in four ways. Referencing xunit.v3 sets `IsTestProject=true`, which makes the SDK set `IsPackable=false`, and `dotnet pack` then reports success and produces nothing. xunit.v3's targets also hard-error unless the project is an executable. `IsPackable=true`, `IsTestProject=false`, `OutputType=Exe` and `TestingPlatformInjectEntryPoint=false` are needed together; removing any one breaks the pack or the build.
- The package targets `net10.0`, not `netstandard2.0`: it restores the bundled test packages itself, and xunit.v3 has no netstandard2.0 target. The two compiled assemblies are netstandard2.0; the gate is packed under `lib/net10.0/` to match the nuspec's only dependency group, since a lib folder with no matching group is NU5128.
- The package isn't marked `DevelopmentDependency`, unlike Digitals.Analyzers. It delivers runtime assemblies, and a shared test-utilities library that packs itself needs them to flow on.
- Anything derived from a `Digitals*` property belongs in the `.targets`, never the `.props`. NuGet imports the `.props` before the consumer's project body and the `.targets` after it, so a string built in the `.props` would bake in the package defaults, and a later `<DigitalsSkipCoverageGate>true</DigitalsSkipCoverageGate>` in the csproj would change the property without changing the string. Defaults with `Condition="'$(X)' == ''"` stay in the `.props`.
- Forward slashes only in `TestingPlatformCommandLineArguments`. MSBuild leaves a literal backslash there on macOS and Linux, and the run fails as `Zero tests ran` with no file-not-found anywhere.
- No `--` inside an XML comment in `Digitals.Testing.coverage.xml`. XML forbids it, the engine rejects the whole document, and the run reports `Zero tests ran` with nothing said about settings or XML. Some SDKs are as strict about a comment before the root element, so keep comments inside `<RunSettings>` and spell option names out in words.
- The coverage settings exclude `Digitals.Testing.Coverage.dll`. The gate ships in `lib/`, lands in every consumer's output directory and would be instrumented like anything else there; left in, it added 43 uncovered lines to a sample project that had two.
- The gate is only heard when it fails. `dotnet test` relays nothing from the test host on a passing run: not `IOutputDevice.DisplayAsync`, not stdout or stderr, at any `--output` level. Only an unhandled exception surfaces, so every number a user needs has to be in the failure message.
- Running a test project's executable by hand doesn't exercise this package. xunit.v3 4.x generates a `Main` that takes the MTP branch only with `--server` or `--internal-msbuild-node`, which `dotnet test` passes; otherwise it runs xunit's plain console runner, where no extension is registered and `--coverage` is an unknown option. Verify through `dotnet test`. Setting `UseMicrosoftTestingPlatformRunner` would force the MTP branch but take away xunit's console runner, so the package leaves it alone.
- Nothing this package ships reaches a Stryker run. `dotnet-stryker` 4.16.0 on the `mtp` runner launches the test host itself with `<project>.dll --server --client-port <port>` and nothing else, so the gate's `IsEnabledAsync` is false and the coverage collector never starts. Confirmed on a 667-mutant repo. When a mutation run misbehaves, this isn't the cause.
- `global_level 300` is what lets the test policy relax rules Digitals.Analyzers sets, and a tie is not a win: when two global configs set the same key at the same level, Roslyn drops the key and the value falls back to the next level down, even when both set the identical value. At 200 it tied with the old `Digitals.Analyzers.Tests.globalconfig` and every shared relaxation reverted. Verified on `VSTHRD200`.
- `DT10005` is a compilation-end rule, and its call graph stops at the assembly boundary. It collects per method and reports in `RegisterCompilationEndAction` (hence the `CompilationEnd` tag RS1037 demands, and no IDE squiggle without full solution analysis). Methods from a package are matched by name against the defaults plus `digitals_testing.assertion_methods`, and configured entries extend the defaults, never replace them.
- `digitals_testing.*` options are read per syntax tree, so a plain `.editorconfig` section sets them. Keys above the first section header are dropped by EditorConfig itself, and the rule silently falls back to its default.
- The Cobertura `complexity` attribute isn't McCabe's number. Microsoft's engine counts roughly two per decision point: one `if` measures 2, five sequential ones 10, `a && b && c` 6, a `foreach` with an inner `if` 4, and `try`/`catch` adds nothing. A contiguous `switch` counts one per arm (a 14-arm mapper measures 15) while a sparse one costs two per arm (eight arms measure 18). `DigitalsMaxCyclomaticComplexity` defaults to 20 because that is McCabe's 10 on this scale; "fixing" it to 10 halves the real threshold. ReportGenerator's Risk Hotspots echo the same attribute.
- The engine loses a contiguous `switch` behind an early-return guard: a nine-arm dispatcher after `if (!verbose) { return "status"; }` reports 2, and so does a twelve-arm one, while the same switch as the whole body reports 9 and 12. The complexity cap is a floor on what gets caught, never a census.

## Domain notes

- `DT#####` rules ship as `warning`. `DIGT001` (MTP runner not selected in `global.json`) and `DIGT002` (banned test package) are build errors, each with a per-project `DigitalsAllow*` opt-out.
- The CRAP limit (`DigitalsMaxCrapScore`, default 30) is `complexity² × (1 − line coverage)³ + complexity` per method. Lines are counted rather than read off `line-rate`, so two reports covering one method merge by addition. `DigitalsSkipCrapGate` makes the targets stop passing the option; the gate reads absent as off, which is deliberately a different answer from a limit of 0.
- The complexity cap (`DigitalsMaxCyclomaticComplexity`, default 20, whole numbers) is separate because coverage moves the CRAP score and not complexity: a fully covered method scores exactly its complexity, so the CRAP limit alone permits a 30-branch method. It must stay below the CRAP default or it is dead weight. Each per-method limit has its own `PropertyGroup` in the `.targets`, so turning one off leaves the other and the floors alone. Only instrumented code is judged, so the coverage exclusions apply to it too.
- Coverage floors default to 100/100 and are a ratchet: an adopting repo lowers them once, with a comment naming what raises them back, then moves them up.
