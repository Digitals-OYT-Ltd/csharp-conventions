using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.HttpClientCreationAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class HttpClientCreationAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    [Fact]
    public async Task NewHttpClient_ReportsDT00003()
    {
        var test = Test("""
            public static class C
            {
                public static object M() => {|DT00003:new System.Net.Http.HttpClient()|};
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TargetTypedNew_ReportsDT00003()
    {
        var test = Test("""
            public static class C
            {
                public static void M()
                {
                    System.Net.Http.HttpClient client = {|DT00003:new()|};
                    client.Dispose();
                }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InjectedHttpClient_Clean()
    {
        var test = Test("""
            public sealed class ApiClient
            {
                public ApiClient(System.Net.Http.HttpClient client) => Client = client;

                public System.Net.Http.HttpClient Client { get; }
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
