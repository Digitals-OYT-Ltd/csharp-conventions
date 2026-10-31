using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00015 — orchestration method over its statement budget: a method that coordinates
/// collaborators and reads as the use-case recipe is short, because each step is one line. Past
/// the budget the body is carrying the detail of its steps as well.
///
/// Counts statements, not lines, so formatting never moves the number: every statement at any
/// depth, including inside lambdas, except blocks themselves, empty statements and local
/// functions (those are helpers). Only orchestration methods are measured (see
/// <see cref="OrchestrationMethods"/>), which is what lets the budget be tight: Sonar's S138
/// counts lines in every method, EF configurations and DI setup included, so it can only be a
/// suggestion.
///
/// Threshold: 'digitals_orchestration_max_statements' (default 20).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrchestrationStatementBudgetAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00015 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00015",
        title: "Keep an orchestration method short",
        messageFormat: "'{0}' calls several collaborators and holds {1} statements ({2} top-level), over the limit of {3} — move step detail into named helpers so the method reads as its steps",
        category: "Digitals.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00015",
        description: "An orchestration method reads as a recipe when each step is one line, so it stays short. Past the"
            + " limit it carries the detail of its steps as well. Statements are counted at any depth, including inside"
            + " lambdas, but blocks, empty statements and local functions are not counted, so formatting never moves"
            + " the number. Complexity metrics count branches; this counts length, which a straight-line method can"
            + " grow without adding a branch. Threshold: digitals_orchestration_max_statements (default 20)."
            + OrchestrationMethods.Definition);

    private const string MaxStatementsOption = "digitals_orchestration_max_statements";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var declaration = (MethodDeclarationSyntax)context.Node;
        if (declaration.Body is not { } body)
        {
            return;
        }

        var tree = declaration.SyntaxTree;
        var max = AnalyzerOptionReader.PositiveInt(context.Options, tree, MaxStatementsOption, 20);
        var total = CountStatements(body);
        if (total <= max)
        {
            return;
        }

        if (!OrchestrationMethods.IsOrchestration(
            context.SemanticModel, declaration, context.Options, context.CancellationToken))
        {
            return;
        }

        var topLevel = body.Statements.Count(statement => statement is not LocalFunctionStatementSyntax);
        context.ReportDiagnostic(Diagnostic.Create(
            Rule, declaration.Identifier.GetLocation(), declaration.Identifier.ValueText, total, topLevel, max));
    }

    /// <summary>Statements at any depth, except blocks, empty statements and local functions.</summary>
    internal static int CountStatements(BlockSyntax body) =>
        body.DescendantNodes(descendIntoChildren: node => node is not LocalFunctionStatementSyntax)
            .Count(node => node is StatementSyntax
                and not BlockSyntax
                and not EmptyStatementSyntax
                and not LocalFunctionStatementSyntax);
}
