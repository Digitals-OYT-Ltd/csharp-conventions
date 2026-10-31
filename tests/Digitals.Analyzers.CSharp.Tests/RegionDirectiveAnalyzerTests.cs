using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.RegionDirectiveAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class RegionDirectiveAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    [Fact]
    public async Task NamedRegion_ReportsDT00009()
    {
        var test = Test("""
            public static class C
            {
                {|DT00009:#region Helpers|}
                public static void M() { }
                #endregion
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnnamedRegion_ReportsDT00009()
    {
        var test = Test("""
            {|DT00009:#region|}
            public static class C
            {
                public static void M() { }
            }
            #endregion
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NestedRegions_ReportOnceEach()
    {
        var test = Test("""
            public static class C
            {
                {|DT00009:#region Outer|}
                {|DT00009:#region Inner|}
                public static void M() { }
                #endregion
                #endregion
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RegionInsideMethodBody_ReportsDT00009()
    {
        var test = Test("""
            public static class C
            {
                public static void M()
                {
                    {|DT00009:#region Step 1|}
                    System.GC.KeepAlive("x");
                    #endregion
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OtherDirectives_Clean()
    {
        var test = Test("""
            #define FEATURE
            #nullable enable
            public static class C
            {
            #if FEATURE
                public static void M() { }
            #endif
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NoDirectives_Clean()
    {
        var test = Test("""
            public static class C
            {
                public static void M() { }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
