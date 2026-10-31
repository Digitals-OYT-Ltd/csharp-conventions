# Adopting Digitals.Analyzers

How to add the `Digitals.Analyzers` package to a repo and work through what its first build reports.

The package covers production code and reaches test projects too. Test projects also move onto Digitals.Testing, which
has [its own guide](testing.md): it carries the test severity policy, the test package bans and the coverage gate.

## What the package does

One NuGet package brings SonarAnalyzer.CSharp, Roslynator (analyzers and formatting), the Visual Studio threading
analyzers, Microsoft's banned API analyzer, and the Digitals rules. At build it adds:

- `Digitals.Analyzers.globalconfig`, the code style and severity policy, at `global_level 100`.
- `BannedSymbols.txt`, reported as `RS0030` in every project. It bans reading the clock directly (`DateTime.Now`,
  `UtcNow` and `Today`, `DateTimeOffset.Now` and `UtcNow`; inject `TimeProvider`), and the millisecond `int` overloads
  of `Task.Delay`, `Thread.Sleep`, `new CancellationTokenSource` and `CancelAfter` (pass a `TimeSpan`).
- `BannedSymbols.Production.txt`, reported as `RS0030` in production code only, since Digitals.Testing drops it from
  test projects. It bans raw SQL strings to EF Core (`FromSqlRaw`, `ExecuteSqlRaw`, `SqlQueryRaw`; use `FromSql($"...")`
  and its siblings), unencoded markup (`Html.Raw`, `HtmlString`, `MarkupString`; write `@value`), and one-string
  process arguments (`ProcessStartInfo.Arguments` and the matching `Process.Start` overloads; use `ArgumentList`).
- `SonarLint.xml`, the Sonar thresholds. It raises `S1541` to 15 and leaves the rest at Sonar's defaults.
- The `DIGB` build checks, described below.

The Digitals rules, each with an example on [the rules page](../rules/analyzers.md):

| Rule | What it reports |
| --- | --- |
| [`DT00001`](../rules/analyzers.md#dt00001) | Any `System.DateTime`, including a bare property declaration |
| [`DT00002`](../rules/analyzers.md#dt00002) | `DateTimeOffset.DateTime` and `DateTimeOffset.LocalDateTime` |
| [`DT00003`](../rules/analyzers.md#dt00003) | `new HttpClient()`; use `IHttpClientFactory`. Production code only |
| [`DT00004`](../rules/analyzers.md#dt00004) | `dynamic` |
| [`DT00006`](../rules/analyzers.md#dt00006) | Money held as `double` or `float`. A suggestion |
| [`DT00007`](../rules/analyzers.md#dt00007) | A class deriving from `TimeProvider`; use `FakeTimeProvider` in tests |
| [`DT00008`](../rules/analyzers.md#dt00008) | A method returning a nullable collection |
| [`DT00009`](../rules/analyzers.md#dt00009) | `#region` |
| [`DT00010`](../rules/analyzers.md#dt00010) | A negatively named boolean such as `IsNotBookable` |
| [`DT00011`](../rules/analyzers.md#dt00011) | A local reused for an unrelated value. A suggestion |
| [`DT00012`](../rules/analyzers.md#dt00012) | A file not named after a type it declares |
| [`DT00013`](../rules/analyzers.md#dt00013) | The same run of steps repeated in one member. Production code only |
| [`DT00014`](../rules/analyzers.md#dt00014) | An orchestration method building a large object inline. Production code only |
| [`DT00015`](../rules/analyzers.md#dt00015) | An orchestration method over 20 statements. Production code only |
| [`DT00016`](../rules/analyzers.md#dt00016) | A run of member assignments on another object between other steps. Production code only |
| [`DT00017`](../rules/analyzers.md#dt00017) | An orchestration method that calls, checks and throws inline |
| [`DT00018`](../rules/analyzers.md#dt00018) | An orchestration method calling infrastructure directly. Production code only |

`DT00005` is retired. Every `DT` rule is a warning except the two marked as suggestions.

The build checks run before compiling and fail the build:

- `DIGB001`, a direct reference to a banned package: `Newtonsoft.Json`, AutoMapper, Entity Framework 6,
  `System.Data.SqlClient` or the EF Core lazy-loading proxies. The replacements are on
  [the rules page](../rules/analyzers.md#digb001). Test frameworks, assertion and mocking libraries are banned by
  Digitals.Testing as `DIGT002`.
- `DIGB002`, when `<Nullable>` is not `enable`.
- `DIGB003`, when `<TreatWarningsAsErrors>` is not `true`.
- `DIGB004`, when the NuGet vulnerability audit is weakened: `NuGetAudit` off, `NuGetAuditMode` not `all`,
  `NuGetAuditLevel` above `low`, or one of `NU1901` to `NU1904` in `NoWarn` or `WarningsNotAsErrors`.

Each check has a per-project opt-out for a staged migration. Set it next to a comment saying why, and what will let
you remove it:

```xml
<!-- Payments still serialises with Newtonsoft attributes; remove with TICKET-1301. -->
<DigitalsAllowBannedPackages>Newtonsoft.Json</DigitalsAllowBannedPackages>
<DigitalsAllowNullableDisabled>true</DigitalsAllowNullableDisabled>
<DigitalsAllowWarningsNotAsErrors>true</DigitalsAllowWarningsNotAsErrors>
<DigitalsAllowWeakNuGetAudit>true</DigitalsAllowWeakNuGetAudit>
```

A repo-local `.editorconfig` always beats the packaged globalconfig. That is the per-repo opt-out for a rule.

## Install

1. Add one reference in `Directory.Build.props`, so it reaches every project. Remove any existing references to
   SonarAnalyzer.CSharp, `Roslynator.*`, Microsoft.VisualStudio.Threading.Analyzers and
   Microsoft.CodeAnalysis.BannedApiAnalyzers: the package brings them, and a second reference causes version conflicts.

   ```xml
   <ItemGroup>
     <PackageReference Include="Digitals.Analyzers" PrivateAssets="all" />
   </ItemGroup>
   ```

   With central package management the version goes in `Directory.Packages.props`. Without it, put `Version` on the
   reference. Take the latest from [nuget.org](https://www.nuget.org/packages/Digitals.Analyzers):

   ```xml
   <PackageVersion Include="Digitals.Analyzers" Version="x.y.z" />
   ```

2. The package is on nuget.org, so `nuget.config` needs nothing new. If the repo uses package source mapping, map
   `Digitals.*` to `nuget.org`, or restore never looks there:

   ```xml
   <packageSourceMapping>
     <packageSource key="nuget.org">
       <package pattern="Digitals.*" />
     </packageSource>
   </packageSourceMapping>
   ```

3. If restore reports `NU1100` while nuget.org has the package, check the user-level `~/.nuget/NuGet/NuGet.Config` for
   `nuget.org` under `<disabledPackageSources>` and run `dotnet nuget enable source nuget.org`. Clear a stale HTTP cache
   with `dotnet nuget locals http-cache --clear`.

4. The SDK must be 10.0.100 or later. The analyzers need the Roslyn compiler that SDK ships; on an older one the build
   prints `CS9057` and drops them.

With `TreatWarningsAsErrors` on, every warning-severity rule fails the build. That is intended. The rest of this guide
is how to stage it.

## Trim the repo .editorconfig

The package carries the style and severity policy, so the repo's `.editorconfig` keeps only what a globalconfig can't
hold:

- Editor settings under `[*]` (indent, charset, line endings). Editors read these from disk, not from the package.
- `max_line_length`, for the editor ruler.
- Anything scoped to a path. A globalconfig has no path sections, so a per-folder setting lives in `.editorconfig`.

Delete everything that repeats the packaged globalconfig.

## First build

Start from a clean tree before trusting any count: `dotnet clean`, then delete every `bin` and `obj`. A stale
incremental build under-reports analyzer findings. Expect these, and fix them in this order:

| Diagnostic | Fix |
| --- | --- |
| `DIGB001` | Swap the package for its replacement, or set `DigitalsAllowBannedPackages` with a reason if the move is separate work. |
| `DIGB002`, `DIGB003` | Set `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in `Directory.Build.props`. If a project can't take that yet, use the `DigitalsAllow*` opt-out in that project, with a reason. |
| `DIGB004` | Remove the setting that weakens the audit. To accept one known advisory, suppress just that one with `<NuGetAuditSuppress Include="https://github.com/advisories/GHSA-…" />`. The SDK defaults `NuGetAuditMode` to `all` only from `net10.0`, so a project on an older target sets it in `Directory.Build.props`. |
| `RS0030`, clock | Inject `TimeProvider` and call `GetUtcNow()`. Logging that runs before the DI container exists keeps a scoped `#pragma` with the reason. |
| `RS0030`, milliseconds | Pass a `TimeSpan`: `Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)`. |
| `RS0030`, raw SQL, HTML and command lines | See [the rules page](../rules/analyzers.md#banned-apis). Each has a safe overload that takes the same input. |
| `DT00001` | Move to `DateTimeOffset` or `DateOnly` where it is cheap. Entity and DTO properties that mirror `datetime2` columns or a wire contract can stay, with the rule lowered to `suggestion` for those files until a column migration retypes them (see the template below). |
| `DT00002` | `.DateTime` becomes `.UtcDateTime`. The ticks are the same at offset 0, but `Kind` changes from `Unspecified` to `Utc`, so an `"O"` round-trip string gains a `Z`. Check what reads it. |
| `DT00003` | `IHttpClientFactory`, through `AddHttpClient`. |
| `DT00013` to `DT00018` | Move the detail into a named helper. Each section on the rules page shows the fix and the `.editorconfig` keys that tune it. |
| `CA1851` | Materialise once with `ToList()` or `ToArray()`, or take `IReadOnlyCollection<T>` instead of `IEnumerable<T>`. On a deferred LINQ query this is a real second round trip, not style. |
| `CA1310` | Pass `StringComparison.Ordinal` or `OrdinalIgnoreCase` to the `StartsWith`, `EndsWith`, `IndexOf` or `Compare` call. |
| `S3776` | Extract methods until each is at 15 cognitive complexity or under. To change the limit, see [Sonar thresholds](#sonar-thresholds). |
| `CA2248` | `HasFlag` is passed a member of a different enum, so the call always returns false. Fix the argument, or the enum it came from. |
| `RCS1157` | A `[Flags]` composite covers a bit no member defines (`All = 7` over `Read = 1, Write = 2`). Define the missing member or narrow the composite. |
| `S6513` | Add `Justification = "..."` to the `[ExcludeFromCodeCoverage]`, saying what makes the code untestable. If there is no reason to give, the exclusion should go, not the warning. |
| `SYSLIB1045` | Turn `new Regex(...)` into a `[GeneratedRegex]` partial method; the class becomes `partial`. |

Three ways to deal with a finding, in order of preference:

- Rewrite the code when that is cheap and keeps behaviour (`DateOnly`, `[GeneratedRegex]`, injecting `TimeProvider`).
- A `#pragma warning disable DT00001 // reason` where a database or wire contract forces the type (EF expression
  trees, a raw `SqlDataReader`). The pragmas are easy to grep, and they become the worklist for the column migration.
- An `.editorconfig` section for generated code and for a whole layer that is being moved over. Don't set a rule to
  `none` for the whole repo without a comment saying what removes it.

## .editorconfig template

```ini
# Adoption: remove each section once its work lands.

# EF Core migrations are generated, but only *.Designer.cs carries the
# <auto-generated/> header, so hand-written migration files are analyzed.
[**/Migrations/**.cs]
dotnet_diagnostic.RS0030.severity = none
dotnet_diagnostic.DT00001.severity = none

# EF model mapping is SQL interop; property lambdas reference DateTime columns.
[**/EntityFramework/Configurations/**.cs]
dotnet_diagnostic.DT00001.severity = none
```

The package already exempts files under `Migrations/` from `S104`, the file length rule.

## Sonar thresholds

Sonar reads rule parameters only from a `SonarLint.xml` additional file, never from a globalconfig. The package ships
one, and steps aside when a project brings its own. Yours replaces the package's file as a whole, so start from a copy
of [the packaged SonarLint.xml](../../src/Digitals.Analyzers/build/config/SonarLint.xml) and keep its `S1541` entry,
or that limit drops back to Sonar's default of 10:

```xml
<ItemGroup>
  <AdditionalFiles Include="$(MSBuildThisFileDirectory)SonarLint.xml" />
</ItemGroup>
```

Lowering a rule in `.editorconfig`, with a comment saying what restores it, is the other way.

## Verify

1. Clean rebuild, with zero warnings and errors.
2. `dotnet format --verify-no-changes`. `dotnet format --diagnostics <id>` also applies fixers for other rules, so
   review its diff.
3. The full test suite, with the repo's coverage gate.

## Known limitations

- `using DT = System.DateTime;` reports the alias line, but not the places that use `DT`.
- `RS0030` reports creating, calling and reading a member, never a bare declaration. `DT00001` covers that gap for
  `DateTime`.
- A naming rule fails the build only through `IDE1006`, which the packaged globalconfig raises. The `severity` on a
  `dotnet_naming_rule` is read by the IDE alone.
