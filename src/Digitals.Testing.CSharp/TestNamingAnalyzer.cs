using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Testing.CSharp;

/// <summary>
/// DT10001 — xUnit test names are PascalCase segments separated by single underscores
/// (testing/csharp/unit-tests.md). Method_Scenario_ExpectedResult is the shape to aim for, but
/// the segment count is not enforced: a one-segment name and a four-segment name are both legal,
/// because a rigid count pushes people to pad or to truncate a name that already reads correctly.
/// What is enforced is the shape of each segment, which is what IDE1006 cannot express once a
/// name contains underscores.
/// Scoped by attribute, not project: only methods carrying [Fact]/[Theory]
/// (or an attribute derived from them) are checked, so the rule is inert
/// outside test assemblies.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestNamingAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT10001 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT10001",
        title: "Test name must be PascalCase segments separated by single underscores",
        messageFormat: "Test '{0}' is not made of PascalCase segments separated by single underscores (aim for Method_Scenario_ExpectedResult)",
        category: "Digitals.Testing",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/testing.md#dt10001");

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
        if (method.MethodKind != MethodKind.Ordinary || !XunitFacts.IsTestMethod(method))
        {
            return;
        }

        if (!HasPascalSegments(method.Name))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, method.Locations[0], method.Name));
        }
    }

    // No segment count: one segment and four segments are both fine. An empty segment is not,
    // which is what rejects a leading, trailing or doubled underscore.
    private static bool HasPascalSegments(string name)
        => name.Split('_').All(segment =>
            segment.Length > 0 && char.IsUpper(segment[0]) && segment.All(char.IsLetterOrDigit));
}
