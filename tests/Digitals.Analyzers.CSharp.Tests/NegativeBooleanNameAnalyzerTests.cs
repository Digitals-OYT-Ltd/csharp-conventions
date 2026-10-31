using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.NegativeBooleanNameAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class NegativeBooleanNameAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Warning(int marker, string name) =>
        new DiagnosticResult("DT00010", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(marker)
            .WithArguments(name);

    [Fact]
    public async Task Property_NotInName_ReportsDT00010()
    {
        var test = Test("""
            public sealed class Booking
            {
                public bool {|#0:IsNotEligible|} { get; init; }
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "IsNotEligible"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FieldParameterLocalAndMethod_NegatingWords_ReportEach()
    {
        var test = Test("""
            public sealed class Renewal
            {
                private readonly bool {|#0:_noConsent|};

                public Renewal(bool {|#1:cannotRenew|}) => _noConsent = cannotRenew;

                public bool {|#2:IsNonUrgent|}()
                {
                    var {|#3:dontNotify|} = _noConsent;
                    return dontNotify;
                }
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "_noConsent"));
        test.ExpectedDiagnostics.Add(Warning(1, "cannotRenew"));
        test.ExpectedDiagnostics.Add(Warning(2, "IsNonUrgent"));
        test.ExpectedDiagnostics.Add(Warning(3, "dontNotify"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableBool_NotInName_ReportsDT00010()
    {
        var test = Test("""
            public sealed class Consent
            {
                public bool? {|#0:HasNotResponded|} { get; init; }
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "HasNotResponded"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PositionalRecord_NegativeParameter_ReportsOnce()
    {
        var test = Test("""
            public sealed record Invoice(bool {|#0:IsNotPaid|});

            namespace System.Runtime.CompilerServices
            {
                internal static class IsExternalInit { }
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "IsNotPaid"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InterfaceMember_ReportedOnInterfaceOnly()
    {
        var test = Test("""
            public interface IGate
            {
                bool {|#0:IsNotOpen|} { get; }
            }

            public abstract class GateBase
            {
                public abstract bool {|#1:IsNotLocked|}(bool {|#2:noForce|});
            }

            public sealed class Gate : GateBase, IGate
            {
                public bool IsNotOpen => false;

                public override bool IsNotLocked(bool noForce) => noForce;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "IsNotOpen"));
        test.ExpectedDiagnostics.Add(Warning(1, "IsNotLocked"));
        test.ExpectedDiagnostics.Add(Warning(2, "noForce"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AffirmativeAndLookalikeNames_Clean()
    {
        var test = Test("""
            public sealed class Notification
            {
                public bool IsNotified { get; init; }

                public bool HasNonce { get; init; }

                public bool IsNoteAttached { get; init; }

                public bool IsActive { get; init; }

                public bool CanRenew(bool hasConsent) => hasConsent;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NonBooleanNegativeName_Clean()
    {
        var test = Test("""
            public sealed class Metrics
            {
                public int NotFoundCount { get; init; }

                public string NoReplyAddress { get; init; } = "";

                public void Record(int nonZeroCount) => System.GC.KeepAlive(nonZeroCount);
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    // Regression: '<Main>$' and its 'args' have no source location, and indexing Locations[0]
    // threw AD0001 IndexOutOfRangeException on every top-level-statements project (0.9.0, 0.10.0).
    [Fact]
    public async Task TopLevelStatements_NoDiagnosticAndNoCrash()
    {
        var test = Test("""
            System.Console.WriteLine();
            await System.Threading.Tasks.Task.Yield();
            """);
        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TopLevelLocalFunction_NegativeName_ReportsDT00010()
    {
        var test = Test("""
            System.Console.WriteLine(IsNotReady(false));

            bool {|#0:IsNotReady|}(bool {|#1:noWait|}) => noWait;
            """);
        test.TestState.OutputKind = Microsoft.CodeAnalysis.OutputKind.ConsoleApplication;
        test.ExpectedDiagnostics.Add(Warning(0, "IsNotReady"));
        test.ExpectedDiagnostics.Add(Warning(1, "noWait"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
