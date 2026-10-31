using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.TemporalTypeAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class TemporalTypeAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    [Fact]
    public async Task BareDeclaration_PropertyOfTypeDateTime_ReportsDT00001()
    {
        var test = Test("""
            public sealed class Booking
            {
                public System.{|DT00001:DateTime|} LegacyStamp { get; set; }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LocalVariable_DefaultDateTime_ReportsDT00001()
    {
        var test = Test("""
            public static class C
            {
                public static void M()
                {
                    System.{|DT00001:DateTime|} local = default;
                    System.GC.KeepAlive(local);
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StaticMemberUse_DateTimeParse_ReportsDT00001()
    {
        var test = Test("""
            public static class C
            {
                public static object M() => System.{|DT00001:DateTime|}.Parse("2026-01-01");
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RecordParameter_DateTime_ReportsDT00001()
    {
        var test = Test("""
            public sealed record Audit(System.{|DT00001:DateTime|} At);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DateTimeOffset_LocalDateTime_ReportsDT00002()
    {
        var test = Test("""
            public static class C
            {
                public static object M(System.DateTimeOffset stamp) => stamp.{|DT00002:LocalDateTime|};
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DateTimeOffset_DateTimeProperty_ReportsDT00002Only()
    {
        var test = Test("""
            public static class C
            {
                public static object M(System.DateTimeOffset stamp) => stamp.{|DT00002:DateTime|};
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullConditionalAccess_LocalDateTimeAndDateTime_ReportsDT00002()
    {
        var test = Test("""
            public static class C
            {
                public static object M(System.DateTimeOffset? stamp) => stamp?.{|DT00002:LocalDateTime|};

                public static object N(System.DateTimeOffset? stamp) => stamp?.{|DT00002:DateTime|};
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UserPropertyNamedDateTime_Clean()
    {
        var test = Test("""
            public sealed class Reading
            {
                public System.DateTimeOffset DateTime { get; init; }

                public object M(Reading? other) => other?.DateTime ?? DateTime;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PreciseTemporalTypes_Clean()
    {
        var test = Test("""
            public sealed record Booking(
                System.DateOnly StartsOn,
                System.TimeOnly ReceptionOpensAt,
                System.TimeSpan TokenLifetime,
                System.DateTimeOffset BookedAt);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UtcDateTime_BoundaryConversion_Clean()
    {
        var test = Test("""
            public static class C
            {
                public static object M(System.DateTimeOffset stamp) => stamp.UtcDateTime;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PragmaSuppression_DT00001_Clean()
    {
        var test = Test("""
            public static class C
            {
                public static void M()
                {
            #pragma warning disable DT00001 // NAV proxy boundary
                    System.DateTime legacy = default;
            #pragma warning restore DT00001
                    System.GC.KeepAlive(legacy);
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
