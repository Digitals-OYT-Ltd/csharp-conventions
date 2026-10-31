using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.LargeObjectInitializerAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class LargeObjectInitializerAnalyzerTests
{
    private static Verify Test(string body, string helpers = "") => new()
    {
        TestCode = OrchestrationStubs.Source + $$"""


            namespace App
            {
                using System.Collections.Generic;
                using System.Linq;
                using System.Threading;
                using System.Threading.Tasks;

                public sealed class Doc
                {
                    public int A { get; set; }
                    public int B { get; set; }
                    public int C { get; set; }
                    public int D { get; set; }
                    public int E { get; set; }
                }

                public sealed class ImportHandler
                {
            {{OrchestrationStubs.Collaborators}}

                    private static void Save(Doc doc) { }

                    public async Task<Doc> Handle(int x, CancellationToken cancellationToken)
                    {
                        await store.SaveAsync("import");
                        await notifier.NotifyAsync("import");
            {{body}}
                    }
            {{helpers}}
                }
            }
            """,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Warning(string type, int members) =>
        new DiagnosticResult("DT00014", Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .WithLocation(0)
            .WithArguments(type, members);

    [Fact]
    public async Task FiveMembersInHandler_ReportsDT00014()
    {
        var test = Test("""
                        var doc = {|#0:new Doc |}{ A = x, B = x, C = x, D = x, E = x };
                        Save(doc);
                        return doc;
            """);
        test.ExpectedDiagnostics.Add(Warning("Doc", 5));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FiveMembersInSelectLambda_ReportsDT00014()
    {
        var test = Test("""
                        var docs = new List<int> { x }.Select(y => {|#0:new Doc |}{ A = y, B = y, C = y, D = y, E = y }).ToList();
                        docs.ForEach(Save);
                        return docs[0];
            """);
        test.ExpectedDiagnostics.Add(Warning("Doc", 5));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TargetTypedNew_ReportsDT00014WithTypeName()
    {
        var test = Test("""
                        Doc doc = {|#0:new() |}{ A = x, B = x, C = x, D = x, E = x };
                        Save(doc);
                        return doc;
            """);
        test.ExpectedDiagnostics.Add(Warning("Doc", 5));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ResponseInFinalReturn_NoDiagnostic()
    {
        var test = Test("""
                        Save(new Doc());
                        return new Doc { A = x, B = x, C = x, D = x, E = x };
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ProjectionInFinalReturn_NoDiagnostic()
    {
        var test = Test("""
                        var xs = new List<int> { x };
                        return xs.Select(y => new Doc { A = y, B = y, C = y, D = y, E = y }).First();
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task EarlyReturnInsideBranch_ReportsDT00014()
    {
        var test = Test("""
                        if (x > 0)
                        {
                            return {|#0:new Doc |}{ A = x, B = x, C = x, D = x, E = x };
                        }

                        return new Doc();
            """);
        test.ExpectedDiagnostics.Add(Warning("Doc", 5));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FourMembers_NoDiagnostic()
    {
        var test = Test("""
                        return new Doc { A = x, B = x, C = x, D = x };
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task HelperBelowHandler_NoDiagnostic()
    {
        var test = Test(
            """
                        return ToDoc(x);
            """,
            """
                    private static Doc ToDoc(int x)
                    {
                        if (x < 0)
                        {
                            return new Doc();
                        }

                        return new Doc { A = x, B = x, C = x, D = x, E = x };
                    }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task LocalFunction_NoDiagnostic()
    {
        var test = Test("""
                        return ToDoc();

                        Doc ToDoc() => new Doc { A = x, B = x, C = x, D = x, E = x };
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExpressionTreeProjection_NoDiagnostic()
    {
        var test = Test("""
                        var query = new List<int> { x }.AsQueryable();
                        var doc = query.Select(y => new Doc { A = y, B = y, C = y, D = y, E = y }).First();
                        return doc;
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FrameworkType_ReportsDT00014()
    {
        var test = Test("""
                        var start = {|#0:new System.Diagnostics.ProcessStartInfo
                        |}{
                            FileName = "qpdf",
                            Arguments = "--flatten",
                            UseShellExecute = false,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                        };
                        return new Doc { A = start.Arguments.Length };
            """);
        test.ExpectedDiagnostics.Add(Warning("ProcessStartInfo", 5));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IndexInitializer_NoDiagnostic()
    {
        var test = Test("""
                        var tags = new Dictionary<string, int> { ["a"] = x, ["b"] = x, ["c"] = x, ["d"] = x, ["e"] = x };
                        return new Doc { A = tags.Count };
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OrdinaryClass_NoDiagnostic()
    {
        var test = new Verify
        {
            TestCode = """
                public sealed class Doc
                {
                    public int A { get; set; }
                    public int B { get; set; }
                    public int C { get; set; }
                    public int D { get; set; }
                    public int E { get; set; }
                }

                public static class Mapper
                {
                    public static Doc Map(int x)
                    {
                        var doc = new Doc { A = x, B = x, C = x, D = x, E = x };
                        return doc;
                    }
                }
                """,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Suppressed_NoDiagnostic()
    {
        var test = Test("""
            #pragma warning disable DT00014
                        var doc = new Doc { A = x, B = x, C = x, D = x, E = x };
            #pragma warning restore DT00014
                        return doc;
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
