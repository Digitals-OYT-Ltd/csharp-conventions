using Microsoft.CodeAnalysis.Testing;
using Xunit;

// Fully qualified: a using alias never sees the other using directives in its own file.
using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Testing.CSharp.TestAssertionAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Testing.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class TestAssertionAnalyzerTests
{
    // Stand-ins for the real assertion APIs, declared under their real namespaces and type names,
    // which is all the rule matches on.
    private const string Stubs = """

        namespace Xunit
        {
            public class FactAttribute : System.Attribute { }
            public sealed class TheoryAttribute : FactAttribute { }
            public static class Assert
            {
                public static void Equal(int expected, int actual) { }
                public static void Null(object value) { }
            }
            public static class Record
            {
                public static System.Exception Exception(System.Action action) { return null; }
            }
        }

        namespace VerifyXunit
        {
            public static class Verifier
            {
                public static System.Threading.Tasks.Task Verify(object target) { return System.Threading.Tasks.Task.CompletedTask; }
                public static void DerivePathInfo(object derive) { }
            }
        }

        namespace NSubstitute
        {
            public static class SubstituteExtensions
            {
                public static T Received<T>(this T substitute) { return substitute; }
                public static object ReceivedCalls<T>(this T substitute) { return null; }
            }
            public static class Received
            {
                public static void InOrder(System.Action calls) { }
            }
        }

        namespace Microsoft.CodeAnalysis.Testing
        {
            public abstract class AnalyzerTest
            {
                public virtual System.Threading.Tasks.Task RunAsync() { return System.Threading.Tasks.Task.CompletedTask; }
            }
        }

        public interface IService { void Run(); }

        public static class Calculator
        {
            public static int Add(int a, int b) { return a + b; }
        }
        """;

    private static Verify Test(string source) => new()
    {
        TestCode = source + Stubs,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static Verify Test(string source, params string[] settings)
    {
        var test = Test(source);
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"""
            is_global = true
            {string.Join("\n", settings)}
            """));
        return test;
    }

    private static Verify Reports(string source, string method, params string[] settings)
    {
        var test = Test(source, settings);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(TestAssertionAnalyzer.Rule).WithLocation(0).WithArguments(method));
        return test;
    }

    [Fact]
    public async Task Analyze_AssertCall_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { Xunit.Assert.Equal(3, Calculator.Add(1, 2)); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_EmptyTest_Reports()
    {
        var test = Reports("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { }
            }
            """, "Add_TwoNumbers_ReturnsSum");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // The case the rule exists for: every line of Add is covered, and nothing is checked.
    [Fact]
    public async Task Analyze_TestOnlyRunsCode_Reports()
    {
        var test = Reports("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { Calculator.Add(1, 2); }
            }
            """, "Add_TwoNumbers_ReturnsSum");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_TheoryWithoutAssertion_Reports()
    {
        var test = Reports("""
            public sealed class CalculatorTests
            {
                [Xunit.Theory]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}(int a) { Calculator.Add(a, 2); }
            }
            """, "Add_TwoNumbers_ReturnsSum");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_UsingStaticAssert_ReportsNothing()
    {
        var test = Test("""
            using static Xunit.Assert;

            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { Equal(3, Calculator.Add(1, 2)); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_DoesNotThrowPattern_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_DoesNotThrow()
                {
                    Xunit.Assert.Null(Xunit.Record.Exception(() => Calculator.Add(1, 2)));
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AssertionInSourceHelper_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { AssertSum(3, 1, 2); }

                private static void AssertSum(int expected, int a, int b) { Xunit.Assert.Equal(expected, Calculator.Add(a, b)); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AssertionTwoHelpersDeepInAnotherClass_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { Checks.Sum(); }
            }

            internal static class Checks
            {
                public static void Sum() { Equal(3); }
                private static void Equal(int expected) { Xunit.Assert.Equal(expected, Calculator.Add(1, 2)); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AssertionInGenericExtensionHelper_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { Calculator.Add(1, 2).ShouldBe(3); }
            }

            internal static class Should
            {
                public static void ShouldBe<T>(this T actual, T expected) { Xunit.Assert.Null(null); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_RecursiveHelperThatNeverAsserts_Reports()
    {
        var test = Reports("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { Ping(3); }

                private static void Ping(int n) { if (n > 0) { Pong(n - 1); } }
                private static void Pong(int n) { Ping(n); }
            }
            """, "Add_TwoNumbers_ReturnsSum");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AssertionInLambda_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum()
                {
                    System.Action check = () => Xunit.Assert.Equal(3, Calculator.Add(1, 2));
                    check();
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AssertionInLocalFunction_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum()
                {
                    Check();

                    void Check() { Xunit.Assert.Equal(3, Calculator.Add(1, 2)); }
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AssertingHelperPassedAsMethodGroup_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { System.Array.ForEach(new[] { 1, 2 }, Check); }

                private static void Check(int value) { Xunit.Assert.Equal(value, value); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_VerifySnapshot_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public System.Threading.Tasks.Task Add_TwoNumbers_ReturnsSum() { return VerifyXunit.Verifier.Verify(Calculator.Add(1, 2)); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // Only the Verify* methods compare anything. The rest of the class is configuration.
    [Fact]
    public async Task Analyze_VerifierConfigurationOnly_Reports()
    {
        var test = Reports("""
            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void {|#0:Add_TwoNumbers_ReturnsSum|}() { VerifyXunit.Verifier.DerivePathInfo(null); }
            }
            """, "Add_TwoNumbers_ReturnsSum");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_SubstituteReceived_ReportsNothing()
    {
        var test = Test("""
            using NSubstitute;

            public sealed class ServiceTests
            {
                [Xunit.Fact]
                public void Run_Called_IsReceived(IService service) { service.Received().Run(); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // ReceivedCalls() lists calls and checks nothing, however close its name is to Received().
    [Fact]
    public async Task Analyze_SubstituteReceivedCallsOnly_Reports()
    {
        var test = Reports("""
            using NSubstitute;

            public sealed class ServiceTests
            {
                [Xunit.Fact]
                public void {|#0:Run_Called_IsListed|}(IService service) { service.ReceivedCalls(); }
            }
            """, "Run_Called_IsListed");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_ReceivedInOrder_ReportsNothing()
    {
        var test = Test("""
            public sealed class ServiceTests
            {
                [Xunit.Fact]
                public void Run_CalledTwice_InOrder(IService service) { NSubstitute.Received.InOrder(() => service.Run()); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AnalyzerHarnessThroughDerivedType_ReportsNothing()
    {
        var test = Test("""
            public sealed class Harness : Microsoft.CodeAnalysis.Testing.AnalyzerTest { }

            public sealed class RuleTests
            {
                [Xunit.Fact]
                public System.Threading.Tasks.Task Analyze_Code_Reports() { return new Harness().RunAsync(); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_AnalyzerHarnessOverride_ReportsNothing()
    {
        var test = Test("""
            public sealed class Harness : Microsoft.CodeAnalysis.Testing.AnalyzerTest
            {
                public override System.Threading.Tasks.Task RunAsync() { return System.Threading.Tasks.Task.CompletedTask; }
            }

            public sealed class RuleTests
            {
                [Xunit.Fact]
                public System.Threading.Tasks.Task Analyze_Code_Reports() { return new Harness().RunAsync(); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // A repo's own Assert class counts without configuration.
    [Fact]
    public async Task Analyze_OwnAssertClass_ReportsNothing()
    {
        var test = Test("""
            namespace Company.Testing
            {
                public static class Assert { public static void Valid(object value) { } }
            }

            public sealed class CalculatorTests
            {
                [Xunit.Fact]
                public void Add_TwoNumbers_ReturnsSum() { Company.Testing.Assert.Valid(Calculator.Add(1, 2)); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    private const string ExternalHarnessTest = """
        namespace Company.Testing
        {
            public static class Harness { public static void Check(object value) { } }
        }

        public sealed class CalculatorTests
        {
            [Xunit.Fact]
            public void {|#0:Add_TwoNumbers_ReturnsSum|}() { Company.Testing.Harness.Check(Calculator.Add(1, 2)); }
        }
        """;

    // Harness has a body here, so it would be followed anyway. Configuration matters for harnesses
    // in another assembly; a source one is simply the easiest way to prove the name matching.
    [Theory]
    [InlineData("Company.Testing.Harness.Check")]
    [InlineData("Harness.Check")]
    [InlineData("Check")]
    [InlineData("Harness.Ch*")]
    [InlineData("Other.Thing, Harness.Check")]
    public async Task Analyze_ConfiguredAssertionMethod_IsHonoured(string configured)
    {
        // Harness.Check has an empty body, so without the setting the test would be reported.
        var test = Test(
            ExternalHarnessTest.Replace("{|#0:", string.Empty, StringComparison.Ordinal)
                .Replace("|}(", "(", StringComparison.Ordinal),
            $"{TestAssertionAnalyzer.AssertionMethodsOption} = {configured}");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Other.Testing.Harness.Check")]
    [InlineData("Checker.Check")]
    [InlineData("Harness.Checks")]
    public async Task Analyze_UnmatchedConfiguration_Reports(string configured)
    {
        var test = Reports(
            ExternalHarnessTest,
            "Add_TwoNumbers_ReturnsSum",
            $"{TestAssertionAnalyzer.AssertionMethodsOption} = {configured}");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_NonTestMethod_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
                public void Setup() { Calculator.Add(1, 2); }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Analyze_PragmaSuppressed_ReportsNothing()
    {
        var test = Test("""
            public sealed class CalculatorTests
            {
            #pragma warning disable DT10005 // smoke test: construction must not throw
                [Xunit.Fact]
                public void Add_TwoNumbers_Runs() { Calculator.Add(1, 2); }
            #pragma warning restore DT10005
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
