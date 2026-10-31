using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00009 — '#region' (code-style.md#file--type-organisation): a region collapses code out of
/// sight, so a type outgrows its job without anyone seeing it and the fixed member order stops
/// being visible. No vendor pack in this package carries this rule — Roslynator only formats
/// regions (RCS0002/RCS0005/RCS1189), and StyleCop's SA1124 is not shipped here.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RegionDirectiveAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00009 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00009",
        title: "Do not use #region",
        messageFormat: "Delete the #region — extract a type or a method instead of folding the code away",
        category: "Digitals.Style",
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
        // A tree action, not a node action: directives are structured trivia, and walking the
        // directive chain skips the whole syntax tree. #endregion is left alone — one diagnostic
        // per region, on the line that opens it.
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var root = (CSharpSyntaxNode)context.Tree.GetRoot(context.CancellationToken);

        for (var directive = root.GetFirstDirective();
             directive is not null;
             directive = directive.GetNextDirective())
        {
            if (directive.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, directive.GetLocation()));
            }
        }
    }
}
