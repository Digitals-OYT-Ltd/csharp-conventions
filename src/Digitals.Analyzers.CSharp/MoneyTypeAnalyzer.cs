using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00006 — money held as double/float (language-rules.md#use-decimal-for-money):
/// binary floating point loses cents; use decimal. Name-heuristic (suggestion
/// severity): fields, properties, and parameters of type double/float whose
/// identifier contains a money term.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MoneyTypeAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableArray<string> MoneyTerms = ImmutableArray.Create(
        "price", "cost", "amount", "fee", "charge", "payment", "refund", "balance", "salary", "tax", "discount");

    /// <summary>The DT00006 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00006",
        title: "Money must not be double or float",
        messageFormat: "'{0}' looks like a money value held as {1} — binary floating point loses cents; use decimal",
        category: "Digitals.Types",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeMethodParameters, SymbolKind.Method);
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        Check(context, field, field.Type);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        Check(context, property, property.Type);
    }

    private static void AnalyzeMethodParameters(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        foreach (var parameter in method.Parameters)
        {
            Check(context, parameter, parameter.Type);
        }
    }

    // A synthesized symbol ('<Main>$'s 'args' for top-level statements) has an empty Locations
    // array; indexing it threw AD0001 on every top-level-statements project.
    private static void Check(SymbolAnalysisContext context, ISymbol symbol, ITypeSymbol type)
    {
        if (type.SpecialType is not (SpecialType.System_Double or SpecialType.System_Single)
            || symbol.IsImplicitlyDeclared
            || symbol.Locations.IsEmpty)
        {
            return;
        }

        var name = symbol.Name;
        if (MoneyTerms.Any(term => name.IndexOf(term, System.StringComparison.OrdinalIgnoreCase) >= 0))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, symbol.Locations[0], name, type.ToDisplayString()));
        }
    }
}
