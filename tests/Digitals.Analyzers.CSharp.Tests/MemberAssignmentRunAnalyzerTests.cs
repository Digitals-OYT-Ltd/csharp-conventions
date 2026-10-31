using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.MemberAssignmentRunAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class MemberAssignmentRunAnalyzerTests
{
    private static Verify Test(string body) => new()
    {
        TestCode = $$"""
            using System;
            using System.Net.Http;
            using App.Orders;

            namespace App.Orders
            {
                public sealed class Batch
                {
                    public int Status { get; set; }
                    public int Reason { get; set; }
                    public DateTimeOffset ProcessedAt { get; set; }
                }

                public sealed class Settings
                {
                    public int A { get; set; }
                    public int B { get; set; }
                    public int C { get; set; }
                }

                public static class BatchExtensions
                {
                    public static void Reject(this Batch batch, int reason, DateTimeOffset now)
                    {
                        batch.Status = 2;
                        batch.Reason = reason;
                        batch.ProcessedAt = now;
                    }
                }
            }

            namespace App.Web
            {
                public sealed class BatchResponse
                {
                    public int Status { get; set; }
                    public int Reason { get; set; }
                    public DateTimeOffset ProcessedAt { get; set; }
                }
            }

            public sealed class Sample
            {
                private readonly Batch _current = new();

                public int X { get; set; }
                public int Y { get; set; }
                public int Z { get; set; }

                private static void Configure(Action<Settings> configure) => configure(new Settings());

                private static void Save(Batch batch) { }

                private static void Notify(DateTimeOffset now) { }

                public void Run(Batch batch, Batch other, App.Web.BatchResponse response, DateTimeOffset now)
                {
            {{body}}
                }
            }
            """,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Warning(int count, string receiver, string type) =>
        new DiagnosticResult("DT00016", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(count, receiver, type);

    [Fact]
    public async Task ThreeMembersOfParameter_ReportsDT00016()
    {
        var test = Test("""
                    Save(batch);
                    {|#0:batch|}.Status = 2;
                    batch.Reason = 3;
                    batch.ProcessedAt = now;
                    Save(batch);
                    Notify(now);
                    Notify(now);
            """);
        test.ExpectedDiagnostics.Add(Warning(3, "batch", "Batch"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ThreeMembersOfField_ReportsDT00016()
    {
        var test = Test("""
                    Save(batch);
                    {|#0:_current|}.Status = 2;
                    _current.Reason = 3;
                    _current.ProcessedAt = now;
                    Save(_current);
                    Notify(now);
                    Notify(now);
            """);
        test.ExpectedDiagnostics.Add(Warning(3, "_current", "Batch"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TypeFromAnotherProject_ReportsDT00016()
    {
        var test = new Verify
        {
            TestCode = """
                public static class Service
                {
                    public static void Reject(TestProject.Orders.Order order)
                    {
                        System.Console.WriteLine("loading");
                        {|#0:order|}.Status = 2;
                        order.Reason = 3;
                        order.ClosedBy = "ops";
                        System.Console.WriteLine("saving");
                        System.Console.WriteLine("notifying");
                        System.Console.WriteLine("done");
                    }
                }
                """,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        var domain = new ProjectState("TestProject.Orders", Microsoft.CodeAnalysis.LanguageNames.CSharp, "/1/Orders", "cs");
        domain.Sources.Add("""
            namespace TestProject.Orders
            {
                public sealed class Order
                {
                    public int Status { get; set; }
                    public int Reason { get; set; }
                    public string ClosedBy { get; set; } = "";
                }
            }
            """);
        test.TestState.AdditionalProjects.Add("TestProject.Orders", domain);
        test.TestState.AdditionalProjectReferences.Add("TestProject.Orders");
        test.ExpectedDiagnostics.Add(Warning(3, "order", "Order"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunIsTheWholeMethod_NoDiagnostic()
    {
        var test = Test("""
                    batch.Status = 2;
                    batch.Reason = 3;
                    batch.ProcessedAt = now;
                    Save(batch);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RunInsideBranchOfLargerMethod_ReportsDT00016()
    {
        var test = Test("""
                    Save(other);
                    Notify(now);
                    if (batch.Status == 0)
                    {
                        {|#0:batch|}.Status = 2;
                        batch.Reason = 3;
                        batch.ProcessedAt = now;
                    }

                    Save(batch);
                    Notify(now);
            """);
        test.ExpectedDiagnostics.Add(Warning(3, "batch", "Batch"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResponseEnrichedInLargerMethod_ReportsDT00016()
    {
        var test = Test("""
                    Save(batch);
                    {|#0:response|}.Status = 2;
                    response.Reason = 3;
                    response.ProcessedAt = now;
                    Save(other);
                    Notify(now);
                    Notify(now);
            """);
        test.ExpectedDiagnostics.Add(Warning(3, "response", "BatchResponse"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExtensionMethodReceiver_NoDiagnostic()
    {
        var test = Test("""
                    batch.Reject(3, now);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TestProject_NoDiagnostic()
    {
        var test = Test("""
                    batch.Status = 2;
                    batch.Reason = 3;
                    batch.ProcessedAt = now;
            """);
        test.TestState.AdditionalReferences.Add(
            Microsoft.CodeAnalysis.MetadataReference.CreateFromFile(typeof(FactAttribute).Assembly.Location));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TwoMembers_NoDiagnostic()
    {
        var test = Test("""
                    batch.Status = 2;
                    batch.Reason = 3;
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InterleavedReceivers_NoDiagnostic()
    {
        var test = Test("""
                    batch.Status = 2;
                    other.Status = 2;
                    batch.Reason = 3;
                    other.Reason = 3;
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OwnMembers_NoDiagnostic()
    {
        var test = Test("""
                    X = 1;
                    Y = 2;
                    Z = 3;
                    this.X = 4;
                    this.Y = 5;
                    this.Z = 6;
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LambdaParameterReceiver_NoDiagnostic()
    {
        var test = Test("""
                    Configure(settings =>
                    {
                        settings.A = 1;
                        settings.B = 2;
                        settings.C = 3;
                    });
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LocalCreatedWithNew_NoDiagnostic()
    {
        var test = Test("""
                    var settings = new Settings();
                    settings.A = 1;
                    settings.B = 2;
                    settings.C = 3;
                    GC.KeepAlive(settings);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suppressed_NoDiagnostic()
    {
        var test = Test("""
            #pragma warning disable DT00016
                    batch.Status = 2;
                    batch.Reason = 3;
                    batch.ProcessedAt = now;
            #pragma warning restore DT00016
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
