using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00007 — a hand-rolled TimeProvider subclass (testing/csharp/index.md#use-xunit-nsubstitute:
/// "FakeTimeProvider — set/advance time; don't substitute TimeProvider"). A homegrown fake
/// silently reimplements a controllable clock, usually with fewer members overridden than
/// the code under test calls.
/// Not expressible as a banned symbol: RS0030 matches references to symbols that already
/// exist in a referenced assembly, never a type the consumer declares in its own source.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TimeProviderSubclassAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00007 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00007",
        title: "Do not implement a custom TimeProvider",
        messageFormat: "'{0}' derives from TimeProvider — use FakeTimeProvider (Microsoft.Extensions.TimeProvider.Testing) in tests, TimeProvider.System in production",
        category: "Digitals.Temporal",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Symbol action, not syntax: the check is on the resolved base type, and a partial
        // class collapses to one symbol instead of firing per declaration.
        context.RegisterSymbolAction(AnalyzeNamedType, SymbolKind.NamedType);
    }

    private static void AnalyzeNamedType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        // Direct base only. Deriving from the real FakeTimeProvider to add test helpers
        // stays legal, and a deeper chain is already reported at the type that introduced it.
        if (type.BaseType is
            {
                Name: "TimeProvider",
                ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, type.Locations[0], type.Name));
        }
    }
}
