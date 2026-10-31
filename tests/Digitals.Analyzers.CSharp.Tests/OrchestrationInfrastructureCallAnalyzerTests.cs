using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.OrchestrationInfrastructureCallAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class OrchestrationInfrastructureCallAnalyzerTests
{
    private static Verify Test(string types, string? config = null)
    {
        var test = new Verify
        {
            TestCode = OrchestrationStubs.Source + "\n\nnamespace App\n{\n    using System;\n    using System.IO;\n    using System.Net.Http;\n    using System.Threading;\n    using System.Threading.Tasks;\n\n" + types + "\n}\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        if (config is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true\n" + config));
        }

        return test;
    }

    private static DiagnosticResult Warning(int marker, string method, string what) =>
        new DiagnosticResult("DT00018", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(marker)
            .WithArguments(method, what);

    // A handler that calls both collaborators, with <paramref name="body"/> after those two steps.
    private static string Handler(string returns, string body, string helpers = "") => $$"""
                public sealed class ImportHandler
                {
            {{OrchestrationStubs.Collaborators}}

                    public async Task<{{returns}}> Handle(string request, CancellationToken cancellationToken)
                    {
                        await store.SaveAsync(request);
                        await notifier.NotifyAsync(request);
            {{body}}
                    }
            {{helpers}}
                }
            """;

    [Fact]
    public async Task HandlerUsesFileAndHttp_ReportsOncePerNamespace()
    {
        var test = Test(Handler("int", """
                        var text = {|#0:File.ReadAllText(request)|};
                        var more = File.ReadAllText(text);
                        using var http = {|#1:new HttpClient()|};
                        var body = await http.GetStringAsync(more, cancellationToken);
                        return body.Length;
            """));
        test.ExpectedDiagnostics.Add(Warning(0, "Handle", "File (System.IO)"));
        test.ExpectedDiagnostics.Add(Warning(1, "Handle", "HttpClient (System.Net.Http)"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandlerCreatesStream_ReportsDT00018()
    {
        var test = Test(Handler("long", """
                        using var stream = {|#0:new MemoryStream(Convert.FromBase64String(request))|};
                        return stream.Length;
            """));
        test.ExpectedDiagnostics.Add(Warning(0, "Handle", "MemoryStream (System.IO)"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StreamAsReturnValue_NoDiagnostic()
    {
        var test = Test(Handler("Stream?", """
                        if (request.Length == 0)
                        {
                            return new MemoryStream();
                        }

                        return request == "-" ? null : new MemoryStream(Convert.FromBase64String(request));
            """));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PathHelpers_NoDiagnostic()
    {
        var test = Test(Handler("string", """
                        return Path.GetExtension(request);
            """));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PathOutsideSystemIO_Reports()
    {
        var test = Test(
            Handler("string", """
                        return {|#0:Storage.Path.Resolve(request)|};
            """) + """

                namespace Storage
                {
                    public static class Path
                    {
                        public static string Resolve(string key) => key;
                    }
                }
            """,
            "digitals_infrastructure_namespaces = App.Storage");
        test.ExpectedDiagnostics.Add(Warning(0, "Handle", "Path (App.Storage)"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HelperBelowHandler_NoDiagnostic()
    {
        var test = Test(Handler(
            "int",
            """
                        return Read(request).Length;
            """,
            """
                    private static string Read(string path) => File.ReadAllText(path);
            """));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AdapterCallingOnlyItsClient_NoDiagnostic()
    {
        var test = Test("""
                public sealed class StatusReader
                {
                    private readonly HttpClient http = new();

                    public async Task<int> ReadAsync(string url, CancellationToken cancellationToken)
                    {
                        var body = await http.GetStringAsync(url, cancellationToken);
                        File.WriteAllText("status.txt", body);
                        return body.Length;
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AdapterHoldingClient_NoDiagnostic()
    {
        var test = Test("""
                public sealed class StatusPublisher
                {
                    private readonly HttpClient http;
                    private readonly Acme.Ports.INotifier notifier;

                    public StatusPublisher(HttpClient http, Acme.Ports.INotifier notifier)
                    {
                        this.http = http;
                        this.notifier = notifier;
                    }

                    public async Task PublishAsync(string url, CancellationToken cancellationToken)
                    {
                        var body = await http.GetStringAsync(url, cancellationToken);
                        await notifier.NotifyAsync(body);
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AdapterReachingAnotherArea_ReportsDT00018()
    {
        var test = Test("""
                public sealed class StatusPublisher
                {
                    private readonly HttpClient http = new();
                    private readonly Acme.Ports.INotifier notifier = null!;

                    public async Task PublishAsync(string url, CancellationToken cancellationToken)
                    {
                        var body = await http.GetStringAsync(url, cancellationToken);
                        {|#0:File.WriteAllText("status.txt", body)|};
                        await notifier.NotifyAsync(body);
                    }
                }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "PublishAsync", "File (System.IO)"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfiguredPrefixes_ReplaceDefaults()
    {
        var test = Test(
            Handler("int", """
                        var text = File.ReadAllText(request);
                        return await {|#0:Task.FromResult(text.Length)|};
            """),
            "digitals_infrastructure_namespaces = System.Threading.Tasks");
        test.ExpectedDiagnostics.Add(Warning(0, "Handle", "Task (System.Threading.Tasks)"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suppressed_NoDiagnostic()
    {
        var test = Test(Handler("int", """
            #pragma warning disable DT00018
                        var text = File.ReadAllText(request);
            #pragma warning restore DT00018
                        return text.Length;
            """));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
