using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.TimeProviderSubclassAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class TimeProviderSubclassAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    [Fact]
    public async Task HandRolledFakeTimeProvider_ReportsDT00007()
    {
        var test = Test("""
            public sealed class {|DT00007:FakeTimeProvider|} : System.TimeProvider
            {
                public override System.DateTimeOffset GetUtcNow() => default;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RenamedSubclass_ReportsDT00007()
    {
        // The check is the base type, not the name — a rename must not evade it.
        var test = Test("""
            public sealed class {|DT00007:TestClock|} : System.TimeProvider
            {
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DerivedFromSubclass_ReportsOnlyTheTypeThatIntroducedIt()
    {
        // Direct-base semantics: the same shape lets a consumer derive from the real
        // FakeTimeProvider to add helpers without being flagged.
        var test = Test("""
            public class {|DT00007:BaseClock|} : System.TimeProvider
            {
            }

            public sealed class DerivedClock : BaseClock
            {
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InjectedTimeProvider_Clean()
    {
        var test = Test("""
            public sealed class Service
            {
                private readonly System.TimeProvider _time;

                public Service(System.TimeProvider time) => _time = time;

                public System.DateTimeOffset Now() => _time.GetUtcNow();
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PragmaDisable_Clean()
    {
        var test = Test("""
            #pragma warning disable DT00007 // vendor SDK demands its own clock type
            public sealed class VendorClock : System.TimeProvider
            {
            }
            #pragma warning restore DT00007
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
