using Microsoft.CodeAnalysis.Testing;
using Xunit;

// Fully qualified: a using alias never sees the other using directives in its own file.
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Testing.CSharp.TestTraitAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Testing.CSharp.Tests;

public sealed class TestTraitAnalyzerTests
{
    private const string XunitStub = """

        namespace Xunit
        {
            public class FactAttribute : System.Attribute { }
            public sealed class TheoryAttribute : FactAttribute { }
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Method, AllowMultiple = true)]
            public sealed class TraitAttribute : System.Attribute
            {
                public TraitAttribute(string name, string value) { }
            }
        }
        """;

    private static Verify Test(string source) => new()
    {
        TestCode = source + XunitStub,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    // A global config is how the shipped globalconfig and a consuming repo both set these keys.
    private static Verify Test(string source, params string[] settings)
    {
        var test = Test(source);
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"""
            is_global = true
            {string.Join("\n", settings)}
            """));
        return test;
    }

    private static DiagnosticResult Expected(string method, string detail)
        => new DiagnosticResult(TestTraitAnalyzer.Rule).WithLocation(0).WithArguments(method, detail);

    private const string Missing =
        "has no [Trait(\"Category\", ...)], so no CI filter selects it and it runs nowhere";

    [Fact]
    public async Task Analyze_TraitOnMethod_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                [Xunit.Trait("Category", "Unit")]
                public void Add_TwoNumbers_ReturnsSum() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // xUnit resolves a class-level trait onto every test in the class, so the rule has to as well.
    [Fact]
    public async Task Analyze_TraitOnContainingClass_ReportsNothing()
    {
        var test = Test("""
            [Xunit.Trait("Category", "Integration")]
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_TraitOnBaseClass_ReportsNothing()
    {
        var test = Test("""
            [Xunit.Trait("Category", "Unit")]
            public abstract class TestBase { }

            public sealed class CalculatorTests : TestBase
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // The open value set is the point of the rule. A repo that uses Manual, or Smoke, or anything
    // else, passes without configuring a thing.
    [Theory]
    [InlineData("Unit")]
    [InlineData("Integration")]
    [InlineData("Manual")]
    [InlineData("Whatever")]
    public async Task Analyze_AnyTraitValue_ReportsNothing(string value)
    {
        var test = Test($$"""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                [Xunit.Trait("Category", "{{value}}")]
                public void Add_TwoNumbers_ReturnsSum() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_NoTrait_ReportsDT10003()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { }
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Add_TwoNumbers_ReturnsSum", Missing));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_TraitWithAnotherName_ReportsDT10003()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                [Xunit.Trait("Speed", "Fast")]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { }
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Add_TwoNumbers_ReturnsSum", Missing));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // Nothing filters on an empty value, so it counts as absent rather than as a trait.
    [Fact]
    public async Task Analyze_EmptyTraitValue_ReportsDT10003()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                [Xunit.Trait("Category", "")]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { }
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Add_TwoNumbers_ReturnsSum", Missing));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_ConfiguredTraitName_ChecksThatNameInstead()
    {
        var test = Test(
            """
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                [Xunit.Trait("Category", "Unit")]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { }
            }
            """,
            "digitals_testing.required_trait = Speed");
        test.ExpectedDiagnostics.Add(Expected(
            "Add_TwoNumbers_ReturnsSum",
            "has no [Trait(\"Speed\", ...)], so no CI filter selects it and it runs nowhere"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_ValueInConfiguredAllowList_ReportsNothing()
    {
        var test = Test(
            """
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                [Xunit.Trait("Category", "Manual")]
                public void Add_TwoNumbers_ReturnsSum() { }
            }
            """,
            "digitals_testing.allowed_trait_values = Unit, Integration, Manual");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_ValueOutsideConfiguredAllowList_ReportsDT10003()
    {
        var test = Test(
            """
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                [Xunit.Trait("Category", "Smoke")]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { }
            }
            """,
            "digitals_testing.allowed_trait_values = Unit, Integration, Manual");
        test.ExpectedDiagnostics.Add(Expected(
            "Add_TwoNumbers_ReturnsSum",
            "has [Trait(\"Category\", \"Smoke\")], which is not one of the values this repo allows "
                + "(Unit, Integration, Manual)"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_NonTestMethod_ReportsNothing()
    {
        var test = Test("""
            public sealed class Calculator
            {
                public void Add() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
