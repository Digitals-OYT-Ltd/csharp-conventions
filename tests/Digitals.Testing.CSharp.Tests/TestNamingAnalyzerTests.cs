using Microsoft.CodeAnalysis.Testing;
using Xunit;

// Fully qualified: a using alias never sees the other using directives in its own file.
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Testing.CSharp.TestNamingAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Testing.CSharp.Tests;

public sealed class TestNamingAnalyzerTests
{
    private const string XunitStub = """

        namespace Xunit
        {
            public class FactAttribute : System.Attribute { }
            public sealed class TheoryAttribute : FactAttribute { }
        }
        """;

    private static Verify Test(string source) => new()
    {
        TestCode = source + XunitStub,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    // The segment count is deliberately not enforced. Method_Scenario_ExpectedResult is the shape
    // to aim for, but a name that already reads correctly should not have to be padded to three.
    [Theory]
    [InlineData("Add")]
    [InlineData("Add_Works")]
    [InlineData("Add_TwoNumbers_ReturnsSum")]
    [InlineData("Add_TwoNumbers_WhenOverflowing_Throws")]
    public async Task Analyze_AnyNumberOfPascalSegments_ReportsNothing(string name)
    {
        var test = Test($$"""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void {{name}}() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_TheoryMethodWithLowercaseSegment_ReportsDT10001()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Theory]
                public void {|#0:Add_twoNumbers_ReturnsSum|}() { }
            }
            """);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DT10001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments("Add_twoNumbers_ReturnsSum"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // An empty segment is what a leading, trailing or doubled underscore produces.
    [Theory]
    [InlineData("add")]
    [InlineData("addTwoNumbers")]
    [InlineData("_Add")]
    [InlineData("Add_")]
    [InlineData("Add__TwoNumbers")]
    public async Task Analyze_MalformedSegment_ReportsDT10001(string name)
    {
        var test = Test($$"""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void {|#0:{{name}}|}() { }
            }
            """);
        test.ExpectedDiagnostics.Add(new DiagnosticResult("DT10001", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(name));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_NonTestMethodWithAnyName_ReportsNothing()
    {
        var test = Test("""
            public sealed class Calculator
            {
                public void add() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
