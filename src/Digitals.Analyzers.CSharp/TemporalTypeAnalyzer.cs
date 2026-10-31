using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// Enforces the temporal-types rule (language-rules.md#prefer-precise-temporal-types):
/// DT00001 — any reference to System.DateTime, including bare declarations that
///           the RS0030 banned-symbol mechanism cannot see.
/// DT00002 — DateTimeOffset.LocalDateTime / .DateTime, which silently reintroduce
///           DateTime; UtcDateTime is the sanctioned boundary conversion.
/// Separate IDs on purpose: suppressing a temporal ban at an interop boundary must
/// not also disable the RS0030 ambient-clock ban (TimeProvider enforcement).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TemporalTypeAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00001 descriptor.</summary>
    public static readonly DiagnosticDescriptor DateTimeUsage = new(
        id: "DT00001",
        title: "Do not use System.DateTime",
        messageFormat: "Use DateOnly (calendar date), TimeOnly (time of day), TimeSpan (duration) or DateTimeOffset in UTC (instant); DateTime only at interop boundaries, converted immediately",
        category: "Digitals.Temporal",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00001");

    /// <summary>The DT00002 descriptor.</summary>
    public static readonly DiagnosticDescriptor AmbientConversion = new(
        id: "DT00002",
        title: "Do not convert DateTimeOffset to an ambient DateTime",
        messageFormat: "'DateTimeOffset.{0}' reintroduces an ambient-zone DateTime; convert at boundaries with UtcDateTime",
        category: "Digitals.Temporal",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00002");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(DateTimeUsage, AmbientConversion);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // One identifier action serves both rules. The name in 'x.DateTime' and in 'x?.DateTime'
        // is an IdentifierName either way, so this also sees the member binding behind '?.',
        // which a SimpleMemberAccessExpression action never does.
        context.RegisterSyntaxNodeAction(AnalyzeIdentifier, SyntaxKind.IdentifierName);
    }

    private static void AnalyzeIdentifier(SyntaxNodeAnalysisContext context)
    {
        var identifier = (IdentifierNameSyntax)context.Node;
        // Fast path: skip the semantic model for every other identifier in the file.
        // A 'using DT = System.DateTime;' alias is still reported once, at the alias
        // declaration itself (its right side is this identifier).
        var name = identifier.Identifier.ValueText;
        if (name is not ("DateTime" or "LocalDateTime"))
        {
            return;
        }

        // The same text names the type (DT00001) or a DateTimeOffset property (DT00002); the
        // symbol tells them apart. SpecialType has no DateTimeOffset entry, so the property's
        // owner is matched by name and namespace.
        switch (context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol)
        {
            case INamedTypeSymbol { SpecialType: SpecialType.System_DateTime }:
                context.ReportDiagnostic(Diagnostic.Create(DateTimeUsage, identifier.GetLocation()));
                break;

            case IPropertySymbol
            {
                ContainingType:
                {
                    Name: "DateTimeOffset",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
                },
            }:
                context.ReportDiagnostic(Diagnostic.Create(AmbientConversion, identifier.GetLocation(), name));
                break;

            default:
                break;
        }
    }
}
