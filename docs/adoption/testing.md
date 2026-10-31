# Adopting Digitals.Testing

How to move a repo's test projects onto the `Digitals.Testing` package.

This is a bigger change than adopting `Digitals.Analyzers`, because it replaces the test runner as well as the packages.
Expect the runner switch and the CI command line to take most of the time.

## What the package does

One NuGet package in a test project brings xunit.v3, NSubstitute, `Microsoft.Extensions.TimeProvider.Testing`,
Microsoft's code coverage engine, the TRX reporter and the Microsoft.Testing.Platform (MTP) host. At build it adds:

- Project defaults: `OutputType=Exe`, `IsTestProject=true` and `IsPackable=false`.
- `global using Xunit;`. Delete any `<Using Include="Xunit" />` your test projects carry, though leaving it in does no
  harm.
- `Digitals.Testing.globalconfig`, the test severity policy at `global_level 300`. It beats `Digitals.Analyzers`' config
  and relaxes rules that are right for production code and wrong for tests.
- `Digitals.Testing.coverage.xml`, which says what coverage measures. Test assemblies, auto-properties,
  `[ExcludeFromCodeCoverage]`, generated code and EF Core migrations are left out.
- Coverage collection and a coverage gate: `dotnet test` collects Cobertura and fails below the floor.
- Its own rules: `DT10001` (test names are PascalCase segments separated by single underscores), `DT10003` (every test
  carries a category trait), `DT10004` (no `Thread.Sleep`, no `Task.Delay` without a `TimeProvider`) and `DT10005`
  (every test reaches an assertion). `xUnit1004`, a skipped test, is raised from info to warning. This package
  allocates from `DT1####` and `Digitals.Analyzers` keeps `DT0####`. Its `DT00007` bans a hand-rolled `TimeProvider`,
  in test and production code alike.
- `DIGT001`, a build error when `global.json` doesn't select the MTP runner.
- `DIGT002`, a build error on a direct reference to a banned test package.

A repo-local `.editorconfig` always beats the packaged globalconfig. That is the per-repo opt-out.

Every rule and check, with an example of each, is in [the rules page](../rules/testing.md).

## Install

1. Select the runner in `global.json` at the repo root. Do this first; everything else fails without it.

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

   This needs .NET SDK 10 or later. `dotnet.config` and the `TestingPlatformDotnetTestSupport` property don't select the
   runner on their own; only `global.json` does. A repo that can't change `global.json` yet can set
   `<DigitalsAllowVSTestRunner>true</DigitalsAllowVSTestRunner>` in each test project, with a comment saying what
   removes it. That lets `dotnet build` pass and nothing more: `dotnet test` keeps failing until the runner is
   selected, and no coverage is collected.

2. Add one reference to each test project, and delete what it replaces:

   ```xml
   <ItemGroup>
     <PackageReference Include="Digitals.Testing" />
   </ItemGroup>
   ```

   Remove `xunit.v3`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `Moq`,
   `coverlet.collector`, `coverlet.msbuild`, `FluentAssertions`, `Microsoft.Extensions.TimeProvider.Testing` and
   `Microsoft.Testing.Extensions.TrxReport`. The package brings the ones that stay, a second reference to them causes
   version conflicts, and seven of them are `DIGT002` errors. Keep a test data generator such as Bogus if you use one;
   this package doesn't ship one.

   With central package management the version goes in `Directory.Packages.props`. Without it, put `Version` on the
   reference. Take the latest from [nuget.org](https://www.nuget.org/packages/Digitals.Testing):

   ```xml
   <PackageVersion Include="Digitals.Testing" Version="x.y.z" />
   ```

   Don't put the reference in `Directory.Build.props`. The package configures the project that references it, and that
   scoping is what keeps the test policy off production code.

3. Leave out `PrivateAssets`. Unlike `Digitals.Analyzers`, this package ships runtime assemblies, and a shared
   test-utilities project that packs itself needs them to flow on.

4. If the repo uses package source mapping, map `Digitals.*` to `nuget.org`, or restore never looks there:

   ```xml
   <packageSourceMapping>
     <packageSource key="nuget.org">
       <package pattern="Digitals.*" />
     </packageSource>
   </packageSourceMapping>
   ```

## Migrating from xUnit v2

A repo still on `xunit` and `xunit.runner.visualstudio` moves to v3 first, as a change of its own:

- `IClassFixture<T>` and `ICollectionFixture<T>` carry over unchanged.
- `Assert.ThrowsAsync` and the other assertions are unchanged.
- A test that needs a `CancellationToken` takes it from `TestContext.Current.CancellationToken`.
- `IAsyncLifetime` methods return `ValueTask`.
- `xunit.runner.json` still works, but most of what lived there is now a command-line option or an MSBuild property.

Do the v2 to v3 move, get the suite green, then add this package.

## First build

Start from a clean tree: `dotnet clean`, then delete every `bin` and `obj`. Stale incremental state under-reports
analyzer findings, and after a runner switch it also produces a convincing `Zero tests ran` that has nothing to do with
your change. Expect these, and fix them in this order:

| Diagnostic | Fix |
| --- | --- |
| `DIGT001` | Add the `test` block to `global.json`. |
| `DIGT002` | Drop the package. For `Moq` and `FluentAssertions` that is a real rewrite, so stage it with `<DigitalsAllowBannedPackages>Moq</DigitalsAllowBannedPackages>` and a comment saying why. |
| `DT10001` | Fix the segment, not the count: each underscore-separated part starts uppercase and is alphanumeric. Two or four segments are already legal, so this is usually a casing fix or a stray underscore, not a rename. |
| `DT10003` | Add `[Trait("Category", "...")]`. Put it on the class when the whole fixture shares a category: one edit per file. The values are yours; pin them per repo with `digitals_testing.allowed_trait_values` once the set has settled. |
| `DT10004` | Usually `FakeTimeProvider.Advance`. A wait for a condition becomes a poll with a deadline, or the library's own wait strategy. An integration fixture that really needs a wall-clock delay is a fair suppression, with a comment. |
| `DT10005` | Look at each one before fixing it: some are real gaps, a test that ran the code and checked nothing. A does-not-throw test becomes `Assert.Null(Record.Exception(...))` or `Record.ExceptionAsync`. A test that asserts through a harness from another package is fixed once, by naming the harness in `digitals_testing.assertion_methods`. |
| `xUnit1004` | Unskip the test or delete it. A test that should only run on demand is `[Fact(Explicit = true)]`. |
| Coverage gate failure | See [Setting the coverage floor](#setting-the-coverage-floor). This is the one that takes time. |
| `MTP0001` | A VSTest-only property is still set (`VSTestLogger`, `CollectCoverage`, `RunSettingsFilePath`, ...). Delete it. |

Two things commonly break on the runner switch and report as something else:

- MTP ignores a `.runsettings` file. Move what was real in it: coverage options to the coverage settings file,
  host options such as a session timeout to `TestingPlatformCommandLineArguments`, and environment variables to the
  CI job.
- A CI command line with VSTest flags fails. `--logger trx`, `--collect "XPlat Code Coverage"` and `--settings` all go.
  The package already collects coverage and writes a TRX file, so most pipelines end up with a plain `dotnet test`.
  See [Translating your dotnet test command line](#translating-your-dotnet-test-command-line).

## Setting the coverage floor

The package defaults to 100% line and branch coverage, which an existing repo won't be at. Measure first, then set
the floor at what you have. Leave the floors at the default for the first run:

```bash
dotnet test
```

The gate fails, and the failure carries the counts you need:

```text
Line coverage 71.4% is below the 100.0% floor (1042 of 1459 covered). Branch coverage 58.2% is below the 100.0% floor (213 of 366 covered). Add tests; do not lower the floor.
```

A run the gate passes prints nothing. `dotnet test` doesn't relay the test host's output to the console, so the gate
is only heard when it fails. To see the numbers on a passing run, read `TestResults/*.cobertura.xml` or run
ReportGenerator over it.

Set those numbers, rounded down, in the test project, next to a comment saying what raises them:

```xml
<!-- Adoption floor. The repository-layer tests (TICKET-1234) raise it to 80/70. -->
<DigitalsMinLineCoverage>71</DigitalsMinLineCoverage>
<DigitalsMinBranchCoverage>58</DigitalsMinBranchCoverage>
```

Treat it as a ratchet. Raise it when coverage climbs, and never lower it to make a build pass.

If the number is far below what the repo's old coverlet setup reported, the exclusions differ, not the code. Compare
against `Digitals.Testing.coverage.xml`: it leaves out auto-properties, which coverlet counts by default, and that
alone moves a repo full of DTOs by several points.

The TRX report is on by default, and comes off when a project doesn't want the file:

```xml
<DigitalsTrxReport>false</DigitalsTrxReport>
```

To take the gate off entirely while still writing the report:

```xml
<DigitalsSkipCoverageGate>true</DigitalsSkipCoverageGate>
```

Use that for a suite where the number means nothing, such as a smoke-test or contract-test project, not to get past a
floor.

## Setting the CRAP limit

CRAP is `complexity² × (1 − line coverage)³ + complexity`, judged per method. It catches what the two run-wide floors
can't: an untested method with a dozen branches inside a repo sitting comfortably at 85%. The package defaults to 30.
A fully covered method scores exactly its complexity, so this limit alone would let a well-tested method reach a
complexity of 30; the cap in the next section holds it to 20.

Find your worst methods the same way, by letting the gate fail:

```bash
dotnet test -p:DigitalsMaxCrapScore=1
```

It names the five worst and counts the rest:

```text
23 method(s) score above the CRAP limit of 1.0: Acme.Invoicing.Calculator.Apply(Invoice) scores 210.0 (complexity 14, 0 of 11 lines covered); … and 18 more. Cover the branches or split the method; raising the limit is the last resort.
```

Set the limit just above your worst, with a comment, and ratchet it down:

```xml
<!-- Adoption limit. Back to 30 once Calculator.Apply is covered (TICKET-1236). -->
<DigitalsMaxCrapScore>220</DigitalsMaxCrapScore>
```

Or turn this check off and keep the coverage floors:

```xml
<DigitalsSkipCrapGate>true</DigitalsSkipCrapGate>
```

`DigitalsSkipCoverageGate` turns off every check, since it stops the gate running at all.

## Setting the complexity cap

Cyclomatic complexity is capped per method too, from the same report, and coverage doesn't move it. A repo at 100%
coverage needs this one: there the CRAP score is the complexity, so a CRAP limit of 30 caps complexity at 30 and does
no other work. The package defaults to 20, which is McCabe's 10 on the scale this report uses, since Microsoft's
engine counts about two per decision point. Don't count branches by eye; read the number off the report.

Find your worst methods by failing the gate:

```bash
dotnet test -p:DigitalsMaxCyclomaticComplexity=1
```

```text
23 method(s) are above the cyclomatic complexity limit of 1: Acme.Invoicing.Calculator.Apply(Invoice) has complexity 31; … and 18 more. Split the method or flatten the branching; covering it does not help here.
```

ReportGenerator's Risk Hotspots table is the other way to see them. Its cyclomatic complexity column is the same
`complexity` attribute the gate reads, copied from the Cobertura file rather than recomputed, so what that table shows
is what the gate judges.

Set the cap just above your worst, with a comment, and ratchet it down. Whole numbers only:

```xml
<!-- Adoption limit. Back to 20 once Calculator.Apply is broken up (TICKET-1237). -->
<DigitalsMaxCyclomaticComplexity>35</DigitalsMaxCyclomaticComplexity>
```

Or turn the cap off and keep the rest:

```xml
<DigitalsSkipComplexityGate>true</DigitalsSkipComplexityGate>
```

Only instrumented code is judged, so whatever the coverage settings leave out is outside this cap too. A repo that
excludes a legacy namespace from coverage has excluded it from the complexity cap as well. The engine also misses a
contiguous `switch` behind an early-return guard, reporting a complexity of 2 for a nine-arm dispatcher, so the cap
catches less than it could.

## Repo-local coverage settings

To leave out something the packaged settings file doesn't, copy it and point at your copy:

```xml
<DigitalsCoverageSettingsFile>$(MSBuildThisFileDirectory)coverage.xml</DigitalsCoverageSettingsFile>
```

Your file replaces the packaged one rather than adding to it, so start from a copy.

## Easing rules in while you fix them

```ini
# Adoption: remove each line once its work lands.
[**.Tests/**.cs]
dotnet_diagnostic.DT10001.severity = suggestion   # until the tests are renamed (TICKET-1234)
dotnet_diagnostic.DT10003.severity = suggestion   # until every fixture is tagged (TICKET-1235)
```

`DT10003` is the rule most likely to arrive in bulk on an existing repo, and the cheap fix is one class-level
`[Trait]` per fixture rather than an edit per method. Once you have a set of categories, pin it in the same file:

```ini
[*.cs]
digitals_testing.allowed_trait_values = Unit, Integration, Manual
```

It has to sit under a section header. An `.editorconfig` ignores keys written above the first one, and the rule then
sees no setting and accepts any value.

Don't set a rule to `none` without a comment saying what removes it.

## Translating your dotnet test command line

On .NET 10 with the MTP runner, `dotnet test` takes the platform's options directly. A VSTest flag doesn't fail as a
bad option: it reaches the test host, and the run reports `Zero tests ran` with exit code 5, saying nothing about the
syntax. So does the old habit of passing options after `--`.

| VSTest | Microsoft.Testing.Platform |
| --- | --- |
| `--filter "Category=Unit"` | `--filter-trait "Category=Unit"`. `--filter` still works too |
| `--collect:"XPlat Code Coverage"` | Nothing; the package passes `--coverage` |
| `--settings coverlet.runsettings` | Nothing; the package passes `--coverage-settings` |
| `--results-directory <dir>` | `--results-directory <dir>`, unchanged |
| `--logger trx` | Nothing; the package passes `--report-trx` |
| `-- <option>` | `<option>`, without the `--` |

Three more things that bite:

- A filter that matches nothing fails the whole run with exit code 8, even when other projects in the same invocation
  ran fine. VSTest let it pass. To filter by trait across a solution, name the exact `.csproj` files instead of a glob,
  so a project with no matching tests is never asked.
- The TRX file lands in `TestResults/`, named after the project, TFM and architecture (for example
  `TestResults/LibTests_net10.0_arm64.trx`), not at a path you choose. Point `PublishTestResults@2` and its GitHub
  equivalents at `**/TestResults/*.trx` rather than at a fixed name.
- `dotnet test` is the only supported way in. Running a test project's executable by hand takes xunit's plain console
  runner, which collects no coverage and never runs the gate.

## Verify

1. Clean rebuild, with zero warnings and errors.
2. `dotnet test` passes and leaves `TestResults/*.cobertura.xml` and `TestResults/*.trx` on disk. A passing gate prints
   nothing.
3. Lower a floor by one point and confirm the run still passes; raise it above the actual number and confirm it fails
   with exit code 7. A gate nobody has seen fail is a gate nobody knows is wired up.
4. CI passes with the pipeline's `dotnet test` command, once the VSTest flags are out.

## Known limitations

- `DIGT002` checks direct `PackageReference` items only. A banned package that arrives transitively is not caught.
- `DIGT001` reads `global.json` as text, looking for the string `Microsoft.Testing.Platform`. A `global.json` that
  mentions it only in a comment passes.
- The coverage floors judge the whole run, not each assembly or file, so a well-covered module can hide a bare one.
  The CRAP limit still catches an untested method with branches. Split the test project if you need separate floors.
- Coverage isn't checked when tests fail. Fix the tests first.
- TRX is the only report format the package writes. A pipeline that needs another format converts the TRX file.
- A mutation testing tool drives the test host itself and runs a subset of tests per mutant. The gate isn't on along
  that path, because only `dotnet test` passes its options. Measured with `dotnet-stryker` 4.16.0 on
  `"test-runner": "mtp"`: every host it starts receives `<project>.dll --server --client-port <port>` and nothing else,
  so neither the gate nor the coverage collector is in that process. If a mutation run misbehaves, this package isn't
  involved, and `DigitalsSkipCoverageGate` won't change it.
