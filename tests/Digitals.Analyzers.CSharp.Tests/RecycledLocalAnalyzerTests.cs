using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.RecycledLocalAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

public sealed class RecycledLocalAnalyzerTests
{
    private static Verify Test(string body) => new()
    {
        TestCode = $$"""
            using System;
            using System.Collections.Generic;
            using System.IO;

            public sealed class Node
            {
                public Node? Next { get; init; }
            }

            public static class Sample
            {
                private static string A() => "a";

                private static string B() => "b";

                private static void Use(object? value) => GC.KeepAlive(value);

                public static void Run(bool condition, TextReader reader, Node head)
                {
            {{body}}
                }
            }
            """,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Info(int marker, string name) =>
        new DiagnosticResult("DT00011", Microsoft.CodeAnalysis.DiagnosticSeverity.Info)
            .WithLocation(marker)
            .WithArguments(name);

    [Fact]
    public async Task ReadThenUnrelatedValue_ReportsDT00011()
    {
        var test = Test("""
                    var result = A();
                    Use(result);
                    {|#0:result|} = B();
                    Use(result);
            """);
        test.ExpectedDiagnostics.Add(Info(0, "result"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UninitializedDeclaration_ReadThenUnrelatedValue_ReportsDT00011()
    {
        var test = Test("""
                    string temp;
                    temp = A();
                    Use(temp);
                    try
                    {
                        {|#0:temp|} = B();
                        Use(temp);
                    }
                    finally
                    {
                        Use(null);
                    }
            """);
        test.ExpectedDiagnostics.Add(Info(0, "temp"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SameBranch_ReadThenUnrelatedValue_ReportsDT00011()
    {
        var test = Test("""
                    var value = A();
                    if (condition)
                    {
                        Use(value);
                        {|#0:value|} = B();
                        Use(value);
                    }
            """);
        test.ExpectedDiagnostics.Add(Info(0, "value"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UpdatesThatReferToTheLocal_Clean()
    {
        var test = Test("""
                    var text = A();
                    Use(text);
                    text = text.Trim();
                    text += B();
                    var node = head;
                    Use(node);
                    var next = node.Next;
                    node = next;
                    Use(node);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DerivedThroughChainOrLambda_Clean()
    {
        var test = Test("""
                    var node = head;
                    Use(node);
                    var first = node.Next;
                    var second = first;
                    node = second;
                    Use(node);
                    Func<Node?> step = () => node.Next;
                    node = step();
                    Use(node);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task UnrelatedChain_ReportsDT00011()
    {
        var test = Test("""
                    var result = A();
                    Use(result);
                    var other = B();
                    var copy = other;
                    {|#0:result|} = copy;
                    Use(result);
            """);
        test.ExpectedDiagnostics.Add(Info(0, "result"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReassignedInLoop_Clean()
    {
        var test = Test("""
                    string? line = reader.ReadLine();
                    while (line is not null)
                    {
                        Use(line);
                        line = reader.ReadLine();
                    }

                    for (var i = 0; i < 10; i++)
                    {
                        if (condition)
                        {
                            i = 10;
                        }
                    }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RefinementUnderConditionReadingTheLocal_Clean()
    {
        var test = Test("""
                    string? name = condition ? A() : null;
                    Use(name);
                    if (name is null)
                    {
                        name = B();
                    }

                    Use(name);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReadAndAssignmentOnDifferentBranches_Clean()
    {
        var test = Test("""
                    string value;
                    if (condition)
                    {
                        value = A();
                        Use(value);
                    }
                    else
                    {
                        value = B();
                    }

                    Use(value);
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ClearingAndCapturedAssignment_Clean()
    {
        var test = Test("""
                    string? cached = A();
                    Use(cached);
                    cached = null;
                    var count = 1;
                    Use(count);
                    Action reset = () => count = 0;
                    reset();
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
