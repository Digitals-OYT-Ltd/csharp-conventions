# Digitals.Testing

`Digitals.Testing` is the whole test stack for a .NET test project in one reference: [xunit.v3](https://xunit.net) on Microsoft.Testing.Platform, [NSubstitute](https://nsubstitute.github.io), `FakeTimeProvider`, Microsoft's coverage engine and the TRX reporter. It adds the Digitals test policy, our own `DT1####` rules and a coverage gate that fails the run below the floor. It is the test-project companion to [Digitals.Analyzers](https://www.nuget.org/packages/Digitals.Analyzers).

Reference it from each test project, and remove the test packages it replaces (`xunit.v3`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `NSubstitute`, `Moq`, `coverlet.collector`, `coverlet.msbuild`, `FluentAssertions`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Testing.Extensions.TrxReport`). The reference has no `Version` because it assumes central package management; without it, add `Version` with the latest from this page:

```xml
<ItemGroup>
  <PackageReference Include="Digitals.Testing" />
</ItemGroup>
```

Select the Microsoft.Testing.Platform runner in `global.json` at the repo root. Without it, `dotnet build` fails with `DIGT001` and .NET 10's `dotnet test` refuses to run, since the VSTest route would collect no coverage and never run the gate:

```json
{
  "test": { "runner": "Microsoft.Testing.Platform" }
}
```

| What | Does |
| --- | --- |
| Project defaults | `OutputType=Exe`, `IsTestProject`, `IsPackable=false` and `global using Xunit`. |
| Test policy | A global AnalyzerConfig at `global_level 300`, which relaxes the production rules that fight the way tests are written and raises a skipped test (`xUnit1004`) to a warning. |
| `DT1####` rules | Test names made of PascalCase segments, a `Category` trait on every test, no wall-clock waits, and an assertion every test reaches. |
| `DIGT002` | Fails the build on a direct reference to the VSTest stack, coverlet, Moq, FluentAssertions, xUnit v2, NUnit, MSTest, or the xUnit v2 builds of ArchUnitNET, FsCheck, Verify and Playwright. |
| Coverage | Collected on every `dotnet test`, with no flags. Test assemblies, generated code, migrations and `[ExcludeFromCodeCoverage]` are left out. |
| Coverage gate | Fails the run below 100% line or branch coverage, above a CRAP score of 30 or above a cyclomatic complexity of 20 in any method. |
| TRX report | Written to `TestResults/` for a CI step to publish. `<DigitalsTrxReport>false</DigitalsTrxReport>` turns it off. |

Below a floor, `dotnet test` fails with the numbers:

```txt
Line coverage 82.4% is below the 100.0% floor (416 of 505 covered). Add tests; do not lower the floor.
```

Each limit is a property (`DigitalsMinLineCoverage`, `DigitalsMinBranchCoverage`, `DigitalsMaxCrapScore`, `DigitalsMaxCyclomaticComplexity`) and a ratchet: an existing repo sets it once to where it is, beside a comment naming what moves it back, and tightens it from there. `DigitalsSkipCoverageGate` turns the gate off and keeps the report.

The [Testing rules page](https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/testing.md) explains every rule, check and limit, with a failing and a passing example and the message it prints. Moving an existing repo's tests onto the package: [the adoption guide](https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/adoption/testing.md).

No `PrivateAssets` on the reference: the package carries runtime assemblies, and a shared test-utilities library that packs itself needs them to flow on. The package targets `net10.0` and later. The bundled packages keep their own licences and reach you as ordinary NuGet dependencies. Release notes are on [GitHub Releases](https://github.com/Digitals-OYT-Ltd/csharp-conventions/releases?q=testing).
