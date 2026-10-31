using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.OrchestrationStatementBudgetAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class OrchestrationStatementBudgetAnalyzerTests
{
    private static Verify Test(string types, string? config = null)
    {
        var test = new Verify
        {
            TestCode = OrchestrationStubs.Source + "\n\nnamespace App\n{\n    using Acme.Ports;\n    using System.Threading;\n    using System.Threading.Tasks;\n\n" + types + "\n}\n",
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        if (config is not null)
        {
            test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", "is_global = true\n" + config));
        }

        return test;
    }

    private static DiagnosticResult Warning(string method, int total, int topLevel, int max = 20) =>
        new DiagnosticResult("DT00015", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(method, total, topLevel, max);

    private static string Handler(int statements, string extra = "") => $$"""
            public sealed class PlaceOrderHandler
            {
        {{OrchestrationStubs.Collaborators}}

                private static void Use() { }

                public async Task<int> {|#0:Handle|}(string request, CancellationToken cancellationToken)
                {
                    await store.SaveAsync(request);
                    await notifier.NotifyAsync(request);
        {{OrchestrationStubs.Statements(statements - 3)}}
        {{extra}}
                    return 0;
                }
            }
        """;

    [Fact]
    public async Task HandlerOverBudget_ReportsDT00015()
    {
        var test = Test(Handler(21));
        test.ExpectedDiagnostics.Add(Warning("Handle", 21, 21));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HandlerAtBudget_NoDiagnostic()
    {
        var test = Test(Handler(20).Replace("{|#0:Handle|}", "Handle", StringComparison.Ordinal));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NestedStatementsCount_TopLevelReportedSeparately()
    {
        var test = Test(Handler(10, "            if (request.Length > 0) { Use(); Use(); Use(); Use(); Use(); Use(); Use(); Use(); Use(); Use(); }"));
        test.ExpectedDiagnostics.Add(Warning("Handle", 21, 11));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LocalFunctionStatements_NotCounted()
    {
        var body = Handler(20, "            void Helper() { Use(); Use(); Use(); Use(); Use(); }")
            .Replace("{|#0:Handle|}", "Handle", StringComparison.Ordinal);
        await Test(body).RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NoCollaborators_NoDiagnostic()
    {
        var test = Test($$"""
                public static class Mapper
                {
                    private static void Use() { }

                    public static void Map()
                    {
            {{OrchestrationStubs.Statements(40)}}
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OneCollaborator_NoDiagnostic()
    {
        var test = Test($$"""
                public sealed class OrderArchive
                {
                    private readonly Acme.Ports.IOrderStore store = null!;

                    private static void Use() { }

                    public async Task ArchiveAsync(string order)
                    {
                        await store.SaveAsync(order);
                        await store.SaveAsync(order + ".bak");
            {{OrchestrationStubs.Statements(30)}}
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LoggerIsPlumbing_NoDiagnostic()
    {
        var test = Test($$"""
                public sealed class OrderArchive
                {
                    private readonly Acme.Ports.IOrderStore store = null!;
                    private readonly Microsoft.Extensions.Logging.ILogger logger = null!;

                    private static void Use() { }

                    public async Task ArchiveAsync(string order)
                    {
                        logger.LogInformation(order);
                        await store.SaveAsync(order);
            {{OrchestrationStubs.Statements(30)}}
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CollaboratorPassedOn_NotCounted()
    {
        var test = Test($$"""
                public sealed class OrderArchive
                {
            {{OrchestrationStubs.Collaborators}}

                    private static void Use() { }

                    private static void Forward(Acme.Ports.INotifier target) { }

                    public async Task ArchiveAsync(string order)
                    {
                        await store.SaveAsync(order);
                        Forward(notifier);
            {{OrchestrationStubs.Statements(30)}}
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task BaseFieldAndInterfaceProperty_ReportsDT00015()
    {
        var test = Test($$"""
                public abstract class PageBase
                {
                    protected readonly Acme.Ports.IOrderStore store = null!;
                }

                public sealed class CheckoutPage : PageBase
                {
                    public Acme.Ports.INotifier Notifier { get; set; } = null!;

                    private static void Use() { }

                    public async Task {|#0:SubmitAsync|}(string order)
                    {
                        await store.SaveAsync(order);
                        await Notifier.NotifyAsync(order);
            {{OrchestrationStubs.Statements(19)}}
                    }
                }
            """);
        test.ExpectedDiagnostics.Add(Warning("SubmitAsync", 21, 21));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PrimaryConstructorAndExtensionCall_ReportsDT00015()
    {
        var test = Test($$"""
                public sealed class PlaceOrderHandler(Acme.Ports.IOrderStore store, Acme.Ports.INotifier notifier)
                {
                    private static void Use() { }

                    public async Task {|#0:Handle|}(string request)
                    {
                        await store.SaveAsync(request);
                        await notifier.ShoutAsync(request);
            {{OrchestrationStubs.Statements(19)}}
                    }
                }
            """);
        test.SolutionTransforms.Add((solution, projectId) =>
            solution.WithProjectParseOptions(
                projectId,
                ((CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!).WithLanguageVersion(LanguageVersion.Preview)));
        test.ExpectedDiagnostics.Add(Warning("Handle", 21, 21));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task MinCollaboratorsOption_ReportsOneCollaborator()
    {
        var test = Test(
            $$"""
                public sealed class OrderArchive
                {
                    private readonly Acme.Ports.IOrderStore store = null!;

                    private static void Use() { }

                    public async Task {|#0:ArchiveAsync|}(string order)
                    {
                        await store.SaveAsync(order);
            {{OrchestrationStubs.Statements(20)}}
                    }
                }
            """,
            "digitals_orchestration_min_collaborators = 1");
        test.ExpectedDiagnostics.Add(Warning("ArchiveAsync", 21, 21));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IgnoredCollaboratorsOption_ReplacesDefaults()
    {
        var test = Test(
            Handler(21).Replace("{|#0:Handle|}", "Handle", StringComparison.Ordinal),
            "digitals_orchestration_ignored_collaborators = Acme.Ports.INotifier");
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ConfiguredJobBase_ReportsWithoutCollaborators()
    {
        var test = Test(
            $$"""
                public sealed class ImportJob : Acme.Jobs.JobProcessor<string>
                {
                    private static void Use() { }

                    protected override Task {|#0:ProcessAsync|}(string payload)
                    {
            {{OrchestrationStubs.Statements(15)}}
                        return Task.CompletedTask;
                    }
                }
            """,
            "digitals_orchestration_types = Acme.Jobs.JobProcessor`1\ndigitals_orchestration_max_statements = 10");
        test.ExpectedDiagnostics.Add(Warning("ProcessAsync", 16, 16, 10));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnconfiguredJobBase_NoDiagnostic()
    {
        var test = Test($$"""
                public sealed class ImportJob : Acme.Jobs.JobProcessor<string>
                {
                    private static void Use() { }

                    protected override Task ProcessAsync(string payload)
                    {
            {{OrchestrationStubs.Statements(30)}}
                        return Task.CompletedTask;
                    }
                }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suppressed_NoDiagnostic()
    {
        var body = "#pragma warning disable DT00015\n" + Handler(21).Replace("{|#0:Handle|}", "Handle", StringComparison.Ordinal) + "\n#pragma warning restore DT00015";
        await Test(body).RunAsync(TestContext.Current.CancellationToken);
    }
}
