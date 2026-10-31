# Adopting Digitals.Analyzers

Guide for adding the `Digitals.Analyzers` meta-package to a repo. Written for an agent doing the migration.

Production code only. Test projects move separately onto Digitals.Testing ([its guide](testing.md)), which carries the test severity policy, the test-package bans and the coverage gate.

## What the package does

One NuGet package that brings SonarAnalyzer.CSharp, Roslynator (Analyzers + Formatting), VisualStudio.Threading.Analyzers, BannedApiAnalyzers, and the custom `Digitals.Analyzers.CSharp` rules. At build it injects:

- `Digitals.Analyzers.globalconfig` — org style + severity policy (global_level 100)
- `BannedSymbols.txt` — RS0030 clock ban: `DateTime.Now/UtcNow/Today`, `DateTimeOffset.Now/UtcNow` → use injected `TimeProvider`
- `BannedSymbols.Production.txt` — RS0030 in production code only: `FromSqlRaw`/`ExecuteSqlRaw`/`SqlQueryRaw` → `FromSql($"...")`/`ExecuteSql`/`SqlQuery`; `Html.Raw`/`HtmlString`/`MarkupString` → `@value`; `ProcessStartInfo.Arguments` and the one-string `Process.Start` overloads → `ArgumentList`. Digitals.Testing drops it from test projects

Custom rules: **DT00001** any `System.DateTime` use incl. bare declarations, **DT00002** `DateTimeOffset.DateTime`/`.LocalDateTime`, **DT00003** `new HttpClient` (use `IHttpClientFactory`), **DT00004** `dynamic`, **DT00006** money as `double`/`float` (suggestion only), **DT00007** any class deriving from `System.TimeProvider` (use Microsoft's `FakeTimeProvider`). Also sets `SYSLIB1045` (use `[GeneratedRegex]`) to warning.

The package also ships `Digitals.Analyzers.targets` — **DIGB001** build error on direct references to banned packages (Newtonsoft.Json, AutoMapper, EF6, System.Data.SqlClient, EF Proxies). Full list and replacements on [the rules page](../rules/analyzers.md). Test frameworks, assertion and mocking libraries are banned by Digitals.Testing under `DIGT002`. Escape hatch, next to a comment explaining why:

```xml
<DigitalsAllowBannedPackages>Newtonsoft.Json</DigitalsAllowBannedPackages>
```

It also gates compiler strictness: **DIGB002** build error when `<Nullable>` is not `enable`, **DIGB003** when `<TreatWarningsAsErrors>` is not `true`. Per-project escape hatches for staged migrations, each next to a comment saying which campaign removes it:

```xml
<DigitalsAllowNullableDisabled>true</DigitalsAllowNullableDisabled>
<DigitalsAllowWarningsNotAsErrors>true</DigitalsAllowWarningsNotAsErrors>
```

A repo-local `.editorconfig` always **overrides** the packaged globalconfig — that is the per-repo opt-out mechanism.

## Install

1. **nuget.config** — nothing to add. The package is published to nuget.org, so the
   default source already resolves it. If the repo uses package source mapping,
   `Digitals.*` has to be mapped to `nuget.org`, otherwise restore will not look there:

   ```xml
   <packageSourceMapping>
     <packageSource key="nuget.org">
       <package pattern="Digitals.*" />
     </packageSource>
   </packageSourceMapping>
   ```

2. **Directory.Build.props** — one reference for the whole solution; **remove** any existing references to SonarAnalyzer.CSharp, Roslynator.*, Microsoft.VisualStudio.Threading.Analyzers, Microsoft.CodeAnalysis.BannedApiAnalyzers (the package brings them; duplicates cause version conflicts):

   ```xml
   <ItemGroup>
     <PackageReference Include="Digitals.Analyzers" PrivateAssets="all" />
   </ItemGroup>
   ```

   With Central Package Management, the version goes in Directory.Packages.props:

   ```xml
   <PackageVersion Include="Digitals.Analyzers" Version="0.4.0" />
   ```

3. **Restore gotcha (NU1100):** if restore says the package doesn't exist while nuget.org clearly has it, check the *user-level* `~/.nuget/NuGet/NuGet.Config` for `nuget.org` under `<disabledPackageSources>` — `dotnet nuget enable source nuget.org`. Also clear a stale http-cache: `dotnet nuget locals http-cache --clear`. Repos still pointing at the old Azure Artifacts feed will pin at the last version pushed there; move them to nuget.org.

4. `TreatWarningsAsErrors=true` means every warning-severity rule fails the build. That is intended; the graces below are how you stage adoption.

## Trim the repo .editorconfig

The package now owns style + severity policy. The repo `.editorconfig` should keep only what a global config cannot express:

- editor-core keys (`[*]` indent/charset/eol — editors read these from disk, not from the package)
- `max_line_length` (editor ruler)
- path-scoped overrides — **global configs have no path sections**, so anything per-folder lives here

Delete everything that duplicates the packaged globalconfig.

## First build — expect these, fix in this order

Do a **full clean rebuild** before trusting any count (`dotnet clean` + wipe `bin`/`obj`); stale incremental builds under-report analyzer errors.

| Diagnostic | Fix |
| --- | --- |
| DIGB001 | Swap the banned package for its replacement, or `<DigitalsAllowBannedPackages>` + reason comment if migration is a separate campaign. |
| DIGB002 / DIGB003 | Set `<Nullable>enable</Nullable>` / `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in Directory.Build.props. If the repo cannot absorb that yet, the `DigitalsAllow*` opt-outs above, per project, + reason comment. |
| RS0030 (clock) | Inject `TimeProvider`, call `GetUtcNow()`. Bootstrap logging before DI exists: scoped pragma with reason. |
| DT00001 | Rewrite to `DateTimeOffset`/`DateOnly` where cheap. Entity/DTO properties mirroring `datetime2` columns or wire contracts: downgrade repo-wide to `suggestion` until a column migration retypes them (see template). |
| DT00002 | `.DateTime` → `.UtcDateTime`. Same ticks at offset 0, but Kind flips Unspecified→Utc, so `"O"` round-trip strings gain a `Z` — check wire consumers. |
| DT00003 | `IHttpClientFactory` via `AddHttpClient`. |
| CA1851 | Materialise once with `ToList()`/`ToArray()`, or take `IReadOnlyCollection<T>` instead of `IEnumerable<T>`. On a deferred LINQ query this is a real duplicated round trip, not style. |
| CA1310 | Add `StringComparison.Ordinal` (or `OrdinalIgnoreCase`) to the `StartsWith`/`EndsWith`/`IndexOf`/`Compare` call. |
| S3776 | Extract methods until each is under 15 cognitive complexity. The threshold is not settable from the packaged globalconfig — Sonar reads rule parameters from a `SonarLint.xml` AdditionalFile, so a repo that needs a different limit ships one, or downgrades S3776 in its `.editorconfig` with a comment naming the campaign that restores it. |
| CA2248 | `HasFlag` is being passed a member of a different enum, so the call always returns false. Fix the argument, or the enum it came from. |
| RCS1157 | A `[Flags]` composite covers a bit no member defines (`All = 7` over `Read = 1, Write = 2`). Define the missing member or narrow the composite. |
| S6513 | Add `Justification = "..."` to the `[ExcludeFromCodeCoverage]`, saying what makes the code untestable. No reason to give means the exclusion should go, not the warning. |
| SYSLIB1045 | Convert `new Regex(...)` to `[GeneratedRegex]` partial methods (class becomes `partial`). |

Rules of thumb:

- **Genuine rewrite** when cheap and behavior-preserving (DateOnly, GeneratedRegex, TimeProvider injection).
- **File-level `#pragma warning disable DTxxxxx // reason`** where a DB/wire contract forces the type (EF expression trees, raw SqlDataReader, sanctioned adapter extensions). The pragmas are grep-able — they *are* the future column-migration worklist.
- **`.editorconfig` scoped grace** for generated code and whole-tier campaigns. Never blanket-`none` a rule repo-wide without a comment saying what campaign removes it.

## .editorconfig template (scoped overrides)

```ini
# ADOPTION GRACE: remove each block when its campaign lands.
# EF Core migrations are generated — but only *.Designer.cs carries the
# <auto-generated/> header, so hand-written migration .cs files are NOT
# auto-skipped. Exempt the folder.
[**/Migrations/**.cs]
dotnet_diagnostic.RS0030.severity = none
dotnet_diagnostic.DT00001.severity = none

# EF model mapping = SQL interop; property lambdas must reference DateTime columns
[**/EntityFramework/Configurations/**.cs]
dotnet_diagnostic.DT00001.severity = none
```

## Verify

1. Full clean rebuild — zero warnings/errors.
2. `dotnet format --verify-no-changes` (note: `dotnet format --diagnostics <id>` also applies unrelated fixers — review the diff).
3. Full test suite + whatever coverage gate the repo enforces.

## Known limitations

- `using DT = System.DateTime;` reports the alias line itself, but not the places that use `DT`.
- RS0030 fires on creation/invocation/member access, **not** bare declarations — DT00001 exists to close that gap.
- Naming-convention rules only fail the build via `IDE1006` severity (already set in the packaged globalconfig); per-`dotnet_naming_rule` severity is IDE-only.
