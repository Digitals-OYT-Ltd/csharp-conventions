using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00008 — a method returning a nullable collection (language-rules.md#require-nullable-enable):
/// 'null' and 'empty' are the same answer for a collection — nothing to iterate — so the '?'
/// buys no information and costs every call site a guard it will eventually forget. Serialized,
/// it also breaks the wire contract (rest-api-design.md#use-consistent-json-payloads: an empty
/// collection is '[]', never null).
/// Declaration-site only: 'return null' from a non-nullable collection is already CS8603.
/// 'string' is excluded on purpose — the standard makes absence of a string 'null', not "".
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NullableCollectionReturnAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00008 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00008",
        title: "Do not return a nullable collection",
        messageFormat: "'{0}' returns a nullable collection — null and empty mean the same thing to a caller; return an empty collection instead",
        category: "Digitals.Types",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00008");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        // Ordinary methods only: property accessors, constructors and operators are not
        // "a method that hands you a collection", and DTO properties are out of scope.
        if (method.MethodKind != MethodKind.Ordinary || method.ReturnsVoid)
        {
            return;
        }

        var returned = Unwrap(method.ReturnType);
        if (!IsNullable(returned) || !IsCollection(StripNullableValueType(returned)))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations[0], method.Name));
    }

    /// <summary>
    /// Task&lt;T&gt;/ValueTask&lt;T&gt; hold the null in their result, so the annotation that matters
    /// is T's. A nullable Task itself is a different defect and not this rule's business.
    /// </summary>
    private static ITypeSymbol Unwrap(ITypeSymbol type)
    {
        if (IsNullable(type) || type is not INamedTypeSymbol { IsGenericType: true, Arity: 1 } named)
        {
            return type;
        }

        var isTask = named.OriginalDefinition is
        {
            MetadataName: "Task`1" or "ValueTask`1",
            ContainingNamespace:
            {
                Name: "Tasks",
                ContainingNamespace:
                {
                    Name: "Threading",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
                },
            },
        };

        return isTask ? named.TypeArguments[0] : type;
    }

    private static bool IsNullable(ITypeSymbol type) =>
        type.NullableAnnotation == NullableAnnotation.Annotated;

    /// <summary>
    /// A struct collection is nullable via Nullable&lt;T&gt; — 'ImmutableArray&lt;T&gt;?' is the
    /// same defect as 'List&lt;T&gt;?', but the outer symbol is Nullable, which is no collection.
    /// </summary>
    private static ITypeSymbol StripNullableValueType(ITypeSymbol type) =>
        type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            ? nullable.TypeArguments[0]
            : type;

    private static bool IsCollection(ITypeSymbol type)
    {
        // string is IEnumerable<char>, and the standard makes an absent string null, not "".
        if (type.SpecialType == SpecialType.System_String)
        {
            return false;
        }

        if (type.TypeKind == TypeKind.Array)
        {
            return true;
        }

        // An unresolved type says nothing about what it will turn out to be.
        if (type.TypeKind == TypeKind.Error)
        {
            return false;
        }

        // A type parameter's shape lives in its constraints, not in AllInterfaces, which is
        // empty for it. 'where T : IList<int>' is provably a collection; a bare 'T?' is not.
        if (type is ITypeParameterSymbol parameter)
        {
            return parameter.ConstraintTypes.Any(IsCollection);
        }

        if (type.SpecialType is SpecialType.System_Collections_IEnumerable
            or SpecialType.System_Collections_Generic_IEnumerable_T
            || type.AllInterfaces.Any(i => i.SpecialType == SpecialType.System_Collections_IEnumerable))
        {
            return true;
        }

        // IAsyncEnumerable<T> implements no IEnumerable, but an async stream that is null
        // instead of empty forces the same guard at every 'await foreach'.
        return IsAsyncEnumerable(type) || type.AllInterfaces.Any(IsAsyncEnumerable);
    }

    // Matched by name and namespace, not GetTypeByMetadataName: that returns null when two
    // references both define the type (Microsoft.Bcl.AsyncInterfaces on older targets), and the
    // rule would go quiet. Walking the namespace also avoids ToDisplayString's string per call.
    private static bool IsAsyncEnumerable(ITypeSymbol type) =>
        type.OriginalDefinition is
        {
            MetadataName: "IAsyncEnumerable`1",
            ContainingNamespace:
            {
                Name: "Generic",
                ContainingNamespace:
                {
                    Name: "Collections",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
                },
            },
        };
}
