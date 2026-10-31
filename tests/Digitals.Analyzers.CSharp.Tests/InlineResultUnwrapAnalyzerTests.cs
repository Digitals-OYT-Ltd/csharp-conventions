using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.InlineResultUnwrapAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class InlineResultUnwrapAnalyzerTests
{
    private const string Types = """
            public sealed class Result
            {
                public bool IsSuccess { get; init; }
                public bool IsFailure => !IsSuccess;
                public string Error { get; init; } = "";
                public byte[] Value { get; init; } = System.Array.Empty<byte>();
            }

            public interface IStore
            {
                Task<Result> DownloadAsync(string name);
                Task<Result> UploadAsync(string name);
            }

            public interface IThumbnailer
            {
                byte[] Shrink(byte[] image);
            }
        """;

    private static Verify Test(string types) => new()
    {
        TestCode = OrchestrationStubs.Source + "\n\nnamespace App\n{\n    using System;\n    using System.Threading;\n    using System.Threading.Tasks;\n\n" + Types + "\n" + types + "\n}\n",
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Warning(int marker, string local) =>
        new DiagnosticResult("DT00017", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(marker)
            .WithArguments(local);

    [Fact]
    public async Task UnwrapBesideOtherSteps_ReportsDT00017()
    {
        var test = Test("""
                public sealed class ImportHandler
                {
                    private readonly IStore store = null!;
                    private readonly Acme.Ports.INotifier notifier = null!;

                    public async Task<int> Handle(string request, CancellationToken cancellationToken)
                    {
                        var download = await store.DownloadAsync(request);
                        {|#0:if|} (!download.IsSuccess) throw new InvalidOperationException(download.Error);
                        await notifier.NotifyAsync(request);
                        return download.Value.Length;
                    }
                }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "download"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SingleCallHandler_NoDiagnostic()
    {
        var test = Test("""
                public sealed class DownloadHandler
                {
                    private readonly IStore store = null!;
                    private readonly IThumbnailer thumbnailer = null!;

                    public async Task<byte[]> Handle(string request, CancellationToken cancellationToken)
                    {
                        try
                        {
                            var download = await store.DownloadAsync(request);
                            if (!download.IsSuccess) throw new InvalidOperationException(download.Error);
                            return thumbnailer.Shrink(download.Value);
                        }
                        catch (TimeoutException ex)
                        {
                            throw new InvalidOperationException("Store timed out.", ex);
                        }
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SingleUnwrapInHelper_NoDiagnostic()
    {
        var test = Test("""
                public sealed class Importer
                {
                    private readonly IStore store = null!;

                    private async Task<byte[]> DownloadSourceAsync(string name)
                    {
                        var download = await store.DownloadAsync(name);
                        if (!download.IsSuccess) throw new InvalidOperationException(download.Error);
                        return download.Value;
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TwoUnwrapsInHandler_ReportsBoth()
    {
        var test = Test("""
                public sealed class CopyHandler
                {
                    private readonly IStore store = null!;
                    private readonly Acme.Ports.INotifier notifier = null!;

                    public async Task<int> Handle(string request, CancellationToken cancellationToken)
                    {
                        var download = await store.DownloadAsync(request).ConfigureAwait(false);
                        {|#0:if|} (download.IsFailure)
                        {
                            throw new InvalidOperationException(download.Error);
                        }

                        var upload = await store.UploadAsync(request).ConfigureAwait(false);
                        {|#1:if|} (upload is { IsSuccess: false }) throw new InvalidOperationException(upload.Error);
                        await notifier.NotifyAsync(request);
                        return download.Value.Length;
                    }
                }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "download"));
        test.ExpectedDiagnostics.Add(Warning(1, "upload"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TwoUnwrapsInAdapter_NoDiagnostic()
    {
        var test = Test("""
                public sealed class TokenClient
                {
                    private readonly IStore store = null!;

                    public async Task<byte[]> GetTokenAsync(string name)
                    {
                        var discovery = await store.DownloadAsync(name);
                        if (discovery.IsFailure) throw new InvalidOperationException(discovery.Error);
                        var token = await store.UploadAsync(name);
                        if (!token.IsSuccess) throw new InvalidOperationException(token.Error);
                        return token.Value;
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EarlyReturnPropagation_NoDiagnostic()
    {
        var test = Test("""
                public sealed class ImportHandler
                {
                    private readonly IStore store = null!;
                    private readonly Acme.Ports.INotifier notifier = null!;

                    public async Task<string> Handle(string request, CancellationToken cancellationToken)
                    {
                        var download = await store.DownloadAsync(request);
                        if (download.IsFailure) return download.Error;
                        var upload = await store.UploadAsync(request);
                        if (upload.IsFailure) return upload.Error;
                        await notifier.NotifyAsync(request);
                        return "";
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CheckNotImmediatelyAfterCall_NoDiagnostic()
    {
        var test = Test("""
                public sealed class ImportHandler
                {
                    private readonly IStore store = null!;
                    private readonly Acme.Ports.INotifier notifier = null!;

                    public async Task<int> Handle(string request, CancellationToken cancellationToken)
                    {
                        var download = await store.DownloadAsync(request);
                        var length = download.Value.Length;
                        if (!download.IsSuccess) throw new InvalidOperationException(download.Error);
                        await notifier.NotifyAsync(request);
                        return length;
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suppressed_NoDiagnostic()
    {
        var test = Test("""
                public sealed class ImportHandler
                {
                    private readonly IStore store = null!;
                    private readonly Acme.Ports.INotifier notifier = null!;

                    public async Task<int> Handle(string request, CancellationToken cancellationToken)
                    {
                        var download = await store.DownloadAsync(request);
            #pragma warning disable DT00017
                        if (!download.IsSuccess) throw new InvalidOperationException(download.Error);
            #pragma warning restore DT00017
                        await notifier.NotifyAsync(request);
                        return download.Value.Length;
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
