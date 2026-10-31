# Adopting Digitals.Testing

Guide for moving a repo's test projects onto the `Digitals.Testing` meta-package. Written for an agent doing the migration.

This is a bigger change than adopting `Digitals.Analyzers`, because it replaces the test runner as well as the packages. Budget a session per repo, not ten minutes.

## What the package does

One NuGet package in a test project brings xunit.v3, NSubstitute, `Microsoft.Extensions.TimeProvider.Testing`, Microsoft's code coverage engine, the TRX reporter and the Microsoft.Testing.Platform host. At build it injects:

- **Project defaults** — `OutputType=Exe`, `IsTestProject=true`, `IsPackable=false`, the MTP entry point.
- **`global using Xunit;`** — delete any `<Using Include="Xunit" />` your test projects carry. Leaving it in is harmless; the SDK deduplicates.
- **`Digitals.Testing.globalconfig`** — test severity policy at `global_level 300`, so it beats both of `Digitals.Analyzers`' configs and can relax rules that are right for production code.
- **`Digitals.Testing.coverage.xml`** — what coverage measures. Test assemblies, auto-properties, `[ExcludeFromCodeCoverage]`, generated code and EF migrations are out.
- **Coverage collection and a coverage gate** — `dotnet test` collects Cobertura and fails below the floor.
- **Custom rules** — `DT10001` (test names are PascalCase segments separated by single underscores), `DT10003` (every test carries a category trait), `DT10004` (no `Thread.Sleep`, no `Task.Delay` without a `TimeProvider`) and `DT10005` (every test reaches an assertion). `xUnit1004`, a skipped test, is raised from Info to warning. This package allocates from `DT1####`; `Digitals.Analyzers` keeps `DT0####`, and its `DT00007` is what bans a hand-rolled `TimeProvider`, in test and production code alike.
- **`DIGT001`** — build error when the MTP runner is not selected in `global.json`.
- **`DIGT002`** — build error on a direct reference to a banned test package.

A repo-local `.editorconfig` always **overrides** the packaged globalconfig — that is the per-repo opt-out mechanism.

## Install

1. **`global.json` at the repo root** — the runner selection. Do this first; everything else fails without it.

   ```json
   {
     "sdk": {
       "version": "10.0.100",
       "rollForward": "latestMinor"
     },
     "test": {
       "runner": "Microsoft.Testing.Platform"
     }
   }
   ```

   This needs .NET SDK 10 or later. A repo that cannot move yet sets `<DigitalsAllowVSTestRunner>true</DigitalsAllowVSTestRunner>` per test project with a comment naming what removes it — but note that the coverage gate does nothing under VSTest, so this is a staging step, not a destination.

   `dotnet.config` and the `TestingPlatformDotnetTestSupport` property do **not** select the runner on their own. Only `global.json` does.

2. **Each test project** — one reference, and delete what it replaces:

   ```xml
   <ItemGroup>
     <PackageReference Include="Digitals.Testing" />
   </ItemGroup>
   ```

   Remove `xunit.v3`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `Moq`, `coverlet.collector`, `coverlet.msbuild`, `FluentAssertions`, `Microsoft.Extensions.TimeProvider.Testing` and `Microsoft.Testing.Extensions.TrxReport`. The package brings the survivors; duplicates cause version conflicts and four of them are hard `DIGT002` errors. Keep a test data generator such as `Bogus` if you use one — this package does not ship one.

   With Central Package Management the version goes in `Directory.Packages.props`:

   ```xml
   <PackageVersion Include="Digitals.Testing" Version="1.0.0" />
   ```

   Do **not** put the reference in `Directory.Build.props`. The package configures the project it is referenced from, and that scoping is what keeps the test policy off production code.

3. **No `PrivateAssets`.** Unlike `Digitals.Analyzers`, this package ships runtime assemblies, and a shared test-utilities project that packs itself needs them to flow on.

4. **Package source mapping**, if the repo uses it — map `Digitals.*` to `nuget.org`, otherwise restore never looks there:

   ```xml
   <packageSourceMapping>
     <packageSource key="nuget.org">
       <package pattern="Digitals.*" />
     </packageSource>
   </packageSourceMapping>
   ```

## Migrating from xUnit v2

If the repo is still on `xunit` + `xunit.runner.visualstudio`, that move comes first and is its own campaign:

- `IClassFixture<T>` and `ICollectionFixture<T>` carry over unchanged.
- `Assert.ThrowsAsync` and friends are unchanged.
- Test methods taking a `CancellationToken` get it from `TestContext.Current.CancellationToken`.
- `IAsyncLifetime` moved to `ValueTask` returns.
- `xunit.runner.json` still works, but most of what lived there is now command-line or MSBuild.

Do the v2 to v3 move, get the suite green, then add this package.

## First build — expect these, fix in this order

Do a **full clean rebuild** first (`dotnet clean` plus wiping `bin`/`obj`). Stale incremental state under-reports analyzer errors, and after an MTP switch it also produces a convincing `Zero tests ran` that has nothing to do with your change.

| Diagnostic | Fix |
| --- | --- |
| `DIGT001` | Add the `test` block to `global.json`. If the SDK cannot move yet, `<DigitalsAllowVSTestRunner>true</DigitalsAllowVSTestRunner>` + reason comment. |
| `DIGT002` | Drop the package. For `Moq` and `FluentAssertions` that is a real rewrite — stage it with `<DigitalsAllowBannedPackages>Moq</DigitalsAllowBannedPackages>` + reason comment. |
| `DT10001` | Fix the segment, not the count: every underscore-separated part starts uppercase and is alphanumeric. A two-segment or four-segment name is already legal, so this is usually a casing fix or a stray underscore rather than a rename campaign. |
| `DT10003` | Add `[Trait("Category", "...")]`. On the class, not the method, when a whole fixture shares a category — that is one edit for the file. The values are yours; pin them per repo with `digitals_testing.allowed_trait_values` once you have settled on a set. |
| `DT10004` | Usually `FakeTimeProvider.Advance`. A wait for a condition becomes a poll with a deadline, or the library's own wait strategy. An integration fixture that genuinely needs wall-clock delay is a legitimate suppression, with a comment. |
| `DT10005` | Look at each one before fixing it: some are real gaps, a test that ran the code and checked nothing. A does-not-throw test becomes `Assert.Null(Record.Exception(...))` or `Record.ExceptionAsync`. A test that asserts through a harness from another package is fixed once, by naming the harness in `digitals_testing.assertion_methods`. |
| `xUnit1004` | Unskip it or delete it. A test that only runs on demand is `[Fact(Explicit = true)]`. |
| Coverage gate failure | See below. This is the one that actually takes time. |
| `MTP0001` | A VSTest-only property is still set (`VSTestLogger`, `CollectCoverage`, `RunSettingsFilePath`, …). Delete it. |

Two things that commonly break on the runner switch and report as something else:

- **A `.runsettings` file is ignored under MTP.** Anything real in it — data collectors, test session timeouts, environment variables — has to move to the coverage settings file or to `TestingPlatformCommandLineArguments`.
- **CI invocations with VSTest flags fail.** `--logger trx`, `--collect "XPlat Code Coverage"`, `--settings` all go. The package already collects coverage and already writes a TRX file, so most pipelines end up with a plain `dotnet test`.

## Setting the coverage floor

The package defaults to 100/100. An existing repo will not be there. Measure first, then set the floor at what you have:

```bash
dotnet test
```

Leave the floors at the default 100 for that first run. The gate fails and the failure carries the
counts you need:

```
Line coverage 71.4% is below the 100.0% floor (1042 of 1459 covered). Branch coverage 58.2% is below the 100.0% floor (213 of 366 covered). Add tests; do not lower the floor.
```

A run the gate passes prints nothing at all. `dotnet test` does not relay the test host's output to
the console, neither the platform's output device nor plain stdout, so the gate is only ever heard
when it fails. To see the numbers without failing a build, read
`TestResults/*.cobertura.xml` or run ReportGenerator over it.

Set those, rounded **down**, in the test project, next to a comment naming what raises them:

```xml
<!-- ADOPTION FLOOR: raised to 80/70 by the repository-layer test campaign (TICKET-1234). -->
<DigitalsMinLineCoverage>71</DigitalsMinLineCoverage>
<DigitalsMinBranchCoverage>58</DigitalsMinBranchCoverage>
```

This is a ratchet. Raise it when coverage climbs; never lower it to make a build pass.

If the number is wildly lower than what the repo's old coverlet setup reported, the exclusions differ rather than the code. Compare against `Digitals.Testing.coverage.xml` — it excludes auto-properties, which coverlet counts by default, and that alone moves a DTO-heavy repo by several points.

The TRX report is on for the same reason coverage is, and comes off the same way when a project
does not want the file:

```xml
<DigitalsTrxReport>false</DigitalsTrxReport>
```

To take the gate off entirely while still producing a report:

```xml
<DigitalsSkipCoverageGate>true</DigitalsSkipCoverageGate>
```

Use it for a suite where the number is meaningless — a smoke-test or contract-test project — not to dodge a floor.

## Setting the CRAP limit

CRAP is `complexity² × (1 − line coverage)³ + complexity`, judged per method, and it catches what the
two run-wide floors cannot: an untested method with a dozen branches inside a repo sitting
comfortably at 85%. The package defaults to 30. A fully covered method scores exactly its
complexity, so this limit alone would let a well-tested method reach a complexity of 30; the cap in
the next section is what holds it to 20.

Find your worst methods the same way, by letting the gate fail:

```bash
dotnet test -p:DigitalsMaxCrapScore=1
```

It names the five worst and counts the rest:

```
23 method(s) score above the CRAP limit of 1.0: Acme.Invoicing.Calculator.Apply(Invoice) scores 210.0 (complexity 14, 0 of 11 lines covered); … and 18 more. Cover the branches or split the method; raising the limit is the last resort.
```

Set the limit just above your worst, with a comment, and ratchet it down:

```xml
<!-- ADOPTION LIMIT: back to 30 once Calculator.Apply is covered (TICKET-1236). -->
<DigitalsMaxCrapScore>220</DigitalsMaxCrapScore>
```

Or turn the check off while leaving the coverage floors in place:

```xml
<DigitalsSkipCrapGate>true</DigitalsSkipCrapGate>
```

`DigitalsSkipCoverageGate` turns off all three, since it stops the gate running at all.

## Setting the complexity cap

Cyclomatic complexity is capped per method as well, from the same reports, and coverage does not move
it. A repo at 100% coverage needs this one: there the CRAP score *is* the complexity, so a CRAP limit
of 30 caps complexity at 30 and the CRAP limit does no other work. The package defaults to 20, which
is McCabe's 10 on the scale these reports use, since Microsoft's engine counts roughly two per
decision point. Do not count branches by eye; read the number off the report.

Find your worst methods by failing the gate:

```bash
dotnet test -p:DigitalsMaxCyclomaticComplexity=1
```

```
23 method(s) are above the cyclomatic complexity limit of 1: Acme.Invoicing.Calculator.Apply(Invoice) has complexity 31; … and 18 more. Split the method or flatten the branching; covering it does not help here.
```

ReportGenerator's Risk Hotspots table is the other way to see them, and its Cyclomatic complexity
column is the same `complexity` attribute the gate reads, echoed from the Cobertura file rather than
recomputed. Whatever that table shows is what the gate will judge.

Set the cap just above your worst, with a comment, and ratchet it down. Whole numbers only:

```xml
<!-- ADOPTION LIMIT: back to 20 once Calculator.Apply is broken up (TICKET-1237). -->
<DigitalsMaxCyclomaticComplexity>35</DigitalsMaxCyclomaticComplexity>
```

Or turn the cap off and keep the rest:

```xml
<DigitalsSkipComplexityGate>true</DigitalsSkipComplexityGate>
```

Only instrumented code is judged, so whatever the coverage settings exclude is outside this cap too.
A repo that excludes a legacy namespace from coverage has excluded it from the complexity cap as well.
The engine also misses a contiguous `switch` behind an early-return guard, reporting a complexity of 2
for a nine-arm dispatcher, so the cap is a floor on what gets caught rather than a full census.

## Repo-local coverage settings

To exclude something the org file does not, copy it and point at your own:

```xml
<DigitalsCoverageSettingsFile>$(MSBuildThisFileDirectory)coverage.xml</DigitalsCoverageSettingsFile>
```

This replaces the org file rather than adding to it, so start from a copy.

## .editorconfig template (scoped graces)

```ini
# ADOPTION GRACE: remove each block when its campaign lands.
[**.Tests/**.cs]
dotnet_diagnostic.DT10001.severity = suggestion   # until the test rename campaign (TICKET-1234)
dotnet_diagnostic.DT10003.severity = suggestion   # until every fixture is tagged (TICKET-1235)
```

`DT10003` is the one most likely to arrive in bulk on an existing repo, and the cheap fix is a
class-level `[Trait]` per fixture rather than a per-method edit. Set the vocabulary once you have
one, in the same file:

```ini
[*.cs]
digitals_testing.allowed_trait_values = Unit, Integration, Manual
```

It has to sit under a section header. An `.editorconfig` ignores keys written above the first one,
and the rule then sees no setting and goes back to accepting any value.

Never blanket-`none` a rule without a comment saying what removes it.

## Translating your `dotnet test` command line

Every VSTest-era flag is gone, and none of them fail as a bad option. An unrecognised flag reaches
the test host and the run reports `Zero tests ran` with exit code 5, saying nothing about the
syntax. Reported from a real migration, so budget an hour for this step alone.

| VSTest | Microsoft.Testing.Platform |
|---|---|
| `--filter "Category=Unit"` | `-- --filter-trait "Category=Unit"` |
| `--collect:"XPlat Code Coverage"` | nothing, the package injects `--coverage` |
| `--settings coverlet.runsettings` | nothing, the package injects `--coverage-settings` |
| `--results-directory <dir>` | `-- --results-directory <dir>` |
| `--logger trx` | nothing, the package injects `--report-trx` |

The `--` matters: everything after it goes to the test host rather than to the SDK.

Three more things that bite:

- **A filter that matches nothing fails the whole run**, exit code 8, even when other projects in
  the same invocation ran fine. VSTest tolerated it silently. If you filter by trait across a
  solution, name the exact `.csproj` files instead of globbing, so a project with no matching tests
  is never asked.
- **The TRX file lands in `TestResults/`, named after the project, TFM and architecture** (for
  example `TestResults/LibTests_net10.0_arm64.trx`), not at a path you chose. Point
  `PublishTestResults@2` and its GitHub equivalents at `**/TestResults/*.trx` rather than at a
  fixed filename.
- **`dotnet test` is the only supported way in.** Running a test project's executable by hand takes
  xunit's plain console runner branch, where no coverage is collected and the gate never runs.

## Verify

1. Full clean rebuild — zero warnings and errors.
2. `dotnet test` — green, with a coverage line printed, `TestResults/*.cobertura.xml` and `TestResults/*.trx` on disk.
3. Drop a floor by one point and confirm the run still passes; raise it above actual and confirm it fails. A gate nobody has seen fail is a gate nobody knows is wired up.
4. CI green with whatever `dotnet test` invocation the pipeline uses, after the VSTest flags come out.

## Known limitations

- `DIGT002` catches direct `PackageReference` items only. A banned package arriving transitively is invisible.
- `DIGT001` reads `global.json` as text, looking for the string `Microsoft.Testing.Platform`. A `global.json` that mentions it in a comment would pass — in practice that string appears nowhere else.
- The coverage gate judges the whole run, not per-assembly or per-file. A well-covered module cannot mask a bare one only because they are measured together; split the test project if you need separate floors.
- Coverage is not checked when tests fail. Fix the tests first.
- TRX is the only report format shipped. A pipeline that wants JUnit adds `Microsoft.Testing.Extensions.Reporting` itself.
- Mutation testing tools drive the test host themselves and run a subset of tests per mutant. The
  gate is not enabled on that path, because its options are only ever passed by `dotnet test`.
  Measured with `dotnet-stryker` 4.16.0 on `"test-runner": "mtp"`: every host it spawns receives
  `<project>.dll --server --client-port <port>` and nothing else, so neither the gate nor the
  coverage collector is ever in that process. If a mutation run misbehaves, this package is not
  involved, and `DigitalsSkipCoverageGate` will not change it.
