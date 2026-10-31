using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00014 — large inline object initializer:
/// an object built field by field in the middle of a handler drops the reader from use-case level
/// to property level for a dozen lines. Five or more assigned members belong in a factory or an
/// extension ('item.ToBatchDocument(ctx)'), where the mapping is also unit-testable.
///
/// Only orchestration methods are checked (see <see cref="OrchestrationMethods"/>): a mapper, a
/// factory or a repository projection is the level below, where building objects is the job.
/// Inside an orchestration method these are exempt:
///   - a creation in the method's final return statement: assembling the response is the recipe's
///     last step, and a load-map-return handler already reads straight (the page's Limit section);
///   - a creation inside a local function (already a named helper);
///   - a creation inside an expression-tree lambda (an EF or OData projection must stay inline);
///   - index initializers ('["key"] = value'), which are dictionary data, not members.
///
/// Threshold: 'digitals_max_initializer_members' (default 4 — five or more is reported).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LargeObjectInitializerAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00014 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00014",
        title: "Do not build a large object inline in an orchestration method",
        messageFormat: "'{0}' is built inline with {1} members between other steps — move the construction into a factory or extension method",
        category: "Digitals.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00014",
        description: "An orchestration method should read as a list of steps. An object built inline with five or more"
            + " named members drops the reader to property level between those steps. A factory or extension method"
            + " ('item.ToOrderLine(order)') names the step and can be tested on its own. Not reported: the object the"
            + " method returns as its last statement, local functions, database query projections (expression trees)"
            + " and index initializers ('[\"key\"] = value'). Threshold: digitals_max_initializer_members (default 4)."
            + OrchestrationMethods.Definition);

    private const string MaxMembersOption = "digitals_max_initializer_members";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeCreation, SyntaxKind.ObjectCreationExpression, SyntaxKind.ImplicitObjectCreationExpression);
    }

    private static void AnalyzeCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (BaseObjectCreationExpressionSyntax)context.Node;
        if (creation.Initializer is not { RawKind: (int)SyntaxKind.ObjectInitializerExpression } initializer)
        {
            return;
        }

        // Named members only: an index initializer ('["key"] = value') is a dictionary literal, which is data.
        var members = initializer.Expressions.Count(expression =>
            expression is AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                Left: IdentifierNameSyntax,
            });
        var max = AnalyzerOptionReader.PositiveInt(context.Options, creation.SyntaxTree, MaxMembersOption, 4);
        if (members <= max || !InOrchestrationBody(context, creation))
        {
            return;
        }

        var type = context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type;
        if (type is null)
        {
            return;
        }

        var head = Location.Create(creation.SyntaxTree, TextSpan.FromBounds(creation.SpanStart, initializer.SpanStart));
        var name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(Diagnostic.Create(Rule, head, name, members));
    }

    // Walks out to the enclosing method. A local function is a named helper and an expression-tree
    // lambda is a query projection, so meeting either first means the creation is not in the recipe.
    private static bool InOrchestrationBody(SyntaxNodeAnalysisContext context, SyntaxNode creation)
    {
        for (var node = creation.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case LocalFunctionStatementSyntax:
                    return false;

                case AnonymousFunctionExpressionSyntax lambda when IsExpressionTree(context, lambda):
                    return false;

                case MethodDeclarationSyntax declaration:
                    return !IsFinalReturn(declaration, creation)
                        && OrchestrationMethods.IsOrchestration(
                            context.SemanticModel, declaration, context.Options, context.CancellationToken);

                case MemberDeclarationSyntax:
                    return false;

                default:
                    break;
            }
        }

        return false;
    }

    // The creation sits in the expression body, or in a return that is the body's last statement.
    private static bool IsFinalReturn(MethodDeclarationSyntax declaration, SyntaxNode creation)
    {
        if (declaration.ExpressionBody is { } arrow)
        {
            return arrow.Span.Contains(creation.Span);
        }

        return declaration.Body is { Statements.Count: > 0 } body
            && body.Statements[body.Statements.Count - 1] is ReturnStatementSyntax last
            && last.Span.Contains(creation.Span);
    }

    private static bool IsExpressionTree(SyntaxNodeAnalysisContext context, AnonymousFunctionExpressionSyntax lambda) =>
        context.SemanticModel.GetTypeInfo(lambda, context.CancellationToken).ConvertedType is INamedTypeSymbol
        {
            Name: "Expression",
            ContainingNamespace:
            {
                Name: "Expressions",
                ContainingNamespace: { Name: "Linq", ContainingNamespace.Name: "System" },
            },
        };
}
