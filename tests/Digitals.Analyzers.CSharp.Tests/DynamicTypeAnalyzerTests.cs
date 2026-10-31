using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.DynamicTypeAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class DynamicTypeAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    [Fact]
    public async Task DynamicLocal_ReportsDT00004()
    {
        var test = Test("""
            public static class C
            {
                public static void M()
                {
                    {|DT00004:dynamic|} value = 1;
                    System.GC.KeepAlive(value);
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DynamicParameter_ReportsDT00004()
    {
        var test = Test("""
            public static class C
            {
                public static void M({|DT00004:dynamic|} value) => System.GC.KeepAlive(value);
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TypedCode_Clean()
    {
        var test = Test("""
            public static class C
            {
                public static void M<T>(T value) where T : notnull => System.GC.KeepAlive(value);
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
