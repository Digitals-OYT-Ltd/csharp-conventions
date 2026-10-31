using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.MoneyTypeAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class MoneyTypeAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Info(int marker, string name, string type) =>
        new DiagnosticResult("DT00006", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(marker)
            .WithArguments(name, type);

    [Fact]
    public async Task DoubleProperty_MoneyName_ReportsDT00006()
    {
        var test = Test("""
            public sealed class Invoice
            {
                public double {|#0:TotalPrice|} { get; set; }
            }
            """);
        test.ExpectedDiagnostics.Add(Info(0, "TotalPrice", "double"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FloatParameter_MoneyName_ReportsDT00006()
    {
        var test = Test("""
            public static class Billing
            {
                public static void Charge(float {|#0:feeAmount|}) => System.GC.KeepAlive(feeAmount);
            }
            """);
        test.ExpectedDiagnostics.Add(Info(0, "feeAmount", "float"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DecimalMoney_And_DoubleNonMoney_Clean()
    {
        var test = Test("""
            public sealed class Reading
            {
                public decimal Price { get; set; }

                public double TemperatureCelsius { get; set; }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // Regression: '<Main>$' and its 'args' have no source location to index.
    [Fact]
    public async Task TopLevelStatements_NoDiagnosticAndNoCrash()
    {
        var test = Test("""
            System.Console.WriteLine(args.Length);
            """);
        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
