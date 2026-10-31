using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.FileNameTypeNameAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class FileNameTypeNameAnalyzerTests
{
    private static Verify Test(string fileName, string source)
    {
        var test = new Verify { ReferenceAssemblies = ReferenceAssemblies.Net.Net80 };
        test.TestState.Sources.Add(($"/src/{fileName}", source));
        return test;
    }

    private static DiagnosticResult Warning(int marker, string fileName, string typeName) =>
        new DiagnosticResult("DT00012", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(marker)
            .WithArguments(fileName, typeName);

    [Fact]
    public async Task FileNameDiffersFromType_ReportsDT00012()
    {
        var test = Test("Bookings.cs", """
            namespace Rooms;

            public sealed class {|#0:Booking|}
            {
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "Bookings.cs", "Booking"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NoTypeMatches_ReportsOnFirstType()
    {
        var test = Test("Helpers.cs", """
            namespace Rooms
            {
                public interface {|#0:IClock|}
                {
                }

                public enum Status
                {
                    Active,
                }
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "Helpers.cs", "IClock"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Booking.cs")]
    [InlineData("Booking.Validation.cs")]
    public async Task MatchingOrPartialFileName_Clean(string fileName)
    {
        var test = Test(fileName, """
            namespace Rooms;

            public sealed partial class Booking
            {
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("Result{T}.cs")]
    [InlineData("Result`1.cs")]
    public async Task GenericFileName_Clean(string fileName)
    {
        var test = Test(fileName, """
            public sealed class Result<T>
            {
                public T? Value { get; init; }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecordWithValidator_MatchesEitherType_Clean()
    {
        var test = Test("RenewalRequest.cs", """
            public sealed class RenewalRequestValidator
            {
            }

            public sealed class RenewalRequest
            {
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DelegateAndStruct_MatchedByName_Clean()
    {
        var test = Test("Handler.cs", """
            public delegate void Handler(int value);

            public struct Unrelated
            {
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NoTypeDeclaration_Clean()
    {
        var test = Test("GlobalUsings.cs", """
            global using System;
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TopLevelStatementsWithHelperType_Clean()
    {
        var test = Test("Program.cs", """
            System.Console.WriteLine(new Options().Name);

            internal sealed class Options
            {
                public string Name { get; init; } = "x";
            }
            """);
        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FileLocalTypesOnly_Clean()
    {
        var test = Test("Extensions.cs", """
            file static class Helper
            {
            }
            """);
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var project = solution.GetProject(projectId)!;
            var options = (Microsoft.CodeAnalysis.CSharp.CSharpParseOptions)project.ParseOptions!;
            return solution.WithProjectParseOptions(
                projectId, options.WithLanguageVersion(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp11));
        });
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
