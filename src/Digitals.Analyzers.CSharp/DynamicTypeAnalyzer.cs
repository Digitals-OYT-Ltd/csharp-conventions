using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00004 — 'dynamic' as a type (language-rules.md#require-generic-constraints):
/// no object/dynamic in domain or application code; resolve types at compile time.
/// BannedSymbols cannot express this — 'dynamic' compiles to object plus an attribute.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DynamicTypeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00004 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00004",
        title: "Do not use dynamic",
        messageFormat: "Resolve types at compile time — generics, DI, or a typed model instead of dynamic",
        category: "Digitals.Types",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00004");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
    }

    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        var identifier = (IdentifierNameSyntax)context.Node;
        if (identifier.Identifier.ValueText != "dynamic")
        {
            return;
        }

        // A user type may legally be named 'dynamic'; only the built-in dynamic type counts.
        if (context.SemanticModel.GetTypeInfo(identifier, context.CancellationToken).Type is IDynamicTypeSymbol)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, identifier.GetLocation()));
        }
    }
}
