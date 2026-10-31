# Digitals.Analyzers

`Digitals.Analyzers` brings the Digitals C# analyzers and severity policy to every project in a repo: [SonarAnalyzer.CSharp](https://www.nuget.org/packages/SonarAnalyzer.CSharp), [Roslynator](https://github.com/dotnet/roslynator), the [VS Threading analyzers](https://github.com/microsoft/vs-threading) and [BannedApiAnalyzers](https://github.com/dotnet/roslyn-analyzers), configured as one policy, plus our own `DT0####` rules and four build checks. It covers production code; test projects add [Digitals.Testing](https://www.nuget.org/packages/Digitals.Testing) on top.

Reference it once, in `Directory.Build.props`, and remove any direct reference to the analyzers it bundles:

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <WarningsAsErrors>nullable</WarningsAsErrors>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Digitals.Analyzers" PrivateAssets="all" />
</ItemGroup>
```

Without `EnforceCodeStyleInBuild` the `IDE` rules stay in the editor, and without `GenerateDocumentationFile` the build never reports an unused `using`. On a target older than `net10.0`, also set `<NuGetAuditMode>all</NuGetAuditMode>`.

| What | Does |
| --- | --- |
| Severity policy | A global AnalyzerConfig at `global_level 100`. Every rule it turns on is a `warning`, so `TreatWarningsAsErrors` decides how hard it bites. |
| Banned APIs (`RS0030`) | The ambient clock (`DateTime.Now`, `DateTimeOffset.UtcNow`, ...): inject `TimeProvider`. The `int` millisecond overloads of `Task.Delay`, `Thread.Sleep` and `CancelAfter`: pass a `TimeSpan`. In production code only, EF Core's `FromSqlRaw` family (use `FromSql($"...")`), `Html.Raw`, `HtmlString` and `MarkupString` (let Razor encode), and process arguments as one string (use `ArgumentList`). |
| Taint analysis | The SDK's `CA3001`-`CA3012`, shipped off, raised to `warning`: a request value that reaches SQL, a file path, a process start or another injection sink unchecked, followed across methods and classes. |
| `DT0####` rules | Time types, `new HttpClient()`, `dynamic`, nullable collections, `#region`, negative boolean names, file names, and six rules that keep each method at one level of abstraction. |
| `DIGB001` | Fails the build on a direct reference to `Newtonsoft.Json`, AutoMapper, EF6, `System.Data.SqlClient` or EF Core lazy-loading proxies. |
| `DIGB002`, `DIGB003` | Fail the build when `Nullable` isn't `enable` or `TreatWarningsAsErrors` isn't `true`, since without them the policy is advisory. |
| `DIGB004` | Warns, and under `TreatWarningsAsErrors` fails, when the NuGet vulnerability audit is weakened. |
| `SonarLint.xml` | Sonar's thresholds, such as cyclomatic complexity 15 rather than 10. Add your own `SonarLint.xml` as `AdditionalFiles` and the packaged one steps aside. |
| SARIF | `dotnet build -p:DigitalsSarifDir=artifacts/sarif` writes every finding, `suggestion` level included, as SARIF per project. |

A repo-local `.editorconfig` beats the packaged policy, so `dotnet_diagnostic.<ID>.severity = none` there turns a rule off. Each build check has its own opt-out (`DigitalsAllowBannedPackages`, `DigitalsAllowNullableDisabled`, `DigitalsAllowWarningsNotAsErrors`, `DigitalsAllowWeakNuGetAudit`), set beside a comment saying why.

The [Analyzers rules page](https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md) explains every rule: why it exists, a failing and a passing example, and the message the build prints. Moving an existing repo onto the package, including how to stage the first wave of findings: [the adoption guide](https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/adoption/analyzers.md).

The analyzers run in the compiler, so the package works for any target framework from a .NET 10 SDK up. It is C# only in practice: Sonar and Roslynator don't analyze VB or F#. The bundled analyzers keep their own licences and reach you as ordinary NuGet dependencies. Release notes are on [GitHub Releases](https://github.com/Digitals-OYT-Ltd/csharp-conventions/releases?q=analyzers).
