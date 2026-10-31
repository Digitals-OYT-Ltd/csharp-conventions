using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

using Verify = Microsoft.CodeAnalysis.CSharp.Testing.CSharpAnalyzerTest<
    Digitals.Analyzers.CSharp.NullableCollectionReturnAnalyzer,
    Microsoft.CodeAnalysis.Testing.DefaultVerifier>;

namespace Digitals.Analyzers.CSharp.Tests;

[Trait("Category", "Unit")]
public sealed class NullableCollectionReturnAnalyzerTests
{
    private static Verify Test(string source) => new()
    {
        TestCode = source,
        ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
    };

    private static DiagnosticResult Warning(int marker, string method) =>
        new DiagnosticResult("DT00008", DiagnosticSeverity.Warning)
            .WithLocation(marker)
            .WithArguments(method);

    [Fact]
    public async Task NullableReadOnlyCollection_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;

            public sealed class OrderService
            {
                public IReadOnlyCollection<string>? {|#0:GetOrders|}() => null;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "GetOrders"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableArray_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            public sealed class OrderService
            {
                public string[]? {|#0:GetCodes|}() => null;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "GetCodes"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TaskOfNullableCollection_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public sealed class OrderService
            {
                public Task<IReadOnlyList<string>?> {|#0:GetOrdersAsync|}() =>
                    Task.FromResult<IReadOnlyList<string>?>(null);
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "GetOrdersAsync"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableAsyncEnumerable_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;

            public sealed class OrderService
            {
                public IAsyncEnumerable<string>? {|#0:StreamOrders|}() => null;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "StreamOrders"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableImmutableArray_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Immutable;

            public sealed class OrderService
            {
                public ImmutableArray<string>? {|#0:GetOrders|}() => null;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "GetOrders"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableDictionary_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;

            public sealed class OrderService
            {
                public Dictionary<string, int>? {|#0:GetTotals|}() => null;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "GetTotals"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task VariousCollectionShapes_Nullable_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            using System;
            using System.Collections;
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Shapes
            {
                public IEnumerable? {|#0:NonGeneric|}() => null;
                public Hashtable? {|#1:Legacy|}() => null;
                public ArraySegment<string>? {|#2:Segment|}() => null;
                public string[][]? {|#3:Jagged|}() => null;
                public IQueryable<string>? {|#4:Query|}() => null;
                public ILookup<string, int>? {|#5:Lookup|}() => null;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "NonGeneric"));
        test.ExpectedDiagnostics.Add(Warning(1, "Legacy"));
        test.ExpectedDiagnostics.Add(Warning(2, "Segment"));
        test.ExpectedDiagnostics.Add(Warning(3, "Jagged"));
        test.ExpectedDiagnostics.Add(Warning(4, "Query"));
        test.ExpectedDiagnostics.Add(Warning(5, "Lookup"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CollectionConstrainedTypeParameter_ReportsDT00008()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;

            public sealed class Repository
            {
                public T? {|#0:Find|}<T>() where T : class, IReadOnlyCollection<string> => null;
            }
            """);
        test.ExpectedDiagnostics.Add(Warning(0, "Find"));
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TypeParameterWithoutCollectionConstraint_Clean()
    {
        // 'T?' proves nothing on its own — T may close as int, string or an entity.
        var test = Test("""
            #nullable enable
            public sealed class Repository
            {
                public T? FindAny<T>() => default;

                public T? FindReference<T>() where T : class => null;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NonNullableCollections_Clean()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public sealed class OrderService
            {
                public IReadOnlyCollection<string> GetOrders() => System.Array.Empty<string>();

                public Task<IReadOnlyList<string>> GetOrdersAsync() =>
                    Task.FromResult<IReadOnlyList<string>>(System.Array.Empty<string>());
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableStringAndNullableValue_Clean()
    {
        var test = Test("""
            #nullable enable
            public sealed class OrderService
            {
                // Absence of a string is null, not "" — language-rules #require-nullable-enable.
                public string? GetReference() => null;

                public int? GetCount() => null;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullablePropertyAndField_Clean()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;

            public sealed class OrderService
            {
                private List<string>? _cache;

                public IReadOnlyCollection<string>? Orders => _cache;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableTaskItself_Clean()
    {
        var test = Test("""
            #nullable enable
            using System.Collections.Generic;
            using System.Threading.Tasks;

            public sealed class OrderService
            {
                public Task<IReadOnlyList<string>>? GetOrdersAsync() => null;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task NullableObliviousContext_Clean()
    {
        var test = Test("""
            #nullable disable
            using System.Collections.Generic;

            public sealed class OrderService
            {
                public IReadOnlyCollection<string> GetOrders() => null;
            }
            """);
        await test.RunAsync(TestContext.Current.CancellationToken);
    }
}
