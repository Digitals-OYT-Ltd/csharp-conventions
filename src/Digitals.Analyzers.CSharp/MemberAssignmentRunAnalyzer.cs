using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00016 — another object's state set field by field in the middle of a larger method:
/// 'batch.Status = ...; batch.FailureReason = ...; batch.ProcessedDate = ...;' between a repository
/// call and an email send drops the reader from use-case level to property level. The run is the
/// missing step name, either a behaviour method on the object ('batch.Reject(reason, now)') or a
/// helper below the caller.
///
/// Reported: three or more consecutive statements assigning members of the same receiver, where
/// the receiver is a local, parameter, field or property. Entity or DTO makes no difference: an
/// entity's state change and a response enriched field by field mid-method are the same dip.
/// Exempt, because none of them mixes levels:
///   - a run that makes up at least half of its method's statements: that method's job is the
///     change (an 'ApplyTo' mapper, a load-update-save handler), so it already reads at one level;
///   - the 'this' parameter of an extension method, which is already the named change;
///   - a receiver that is a parameter of the enclosing lambda ('services.Configure(o => { ... })');
///   - a local the same block created with 'new' (that is construction; DT00014's territory);
///   - assignments to the method's own instance ('this.X = ...' or bare 'X = ...').
///
/// Threshold: 'digitals_max_member_assignment_run' (default 2 — three or more is reported).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MemberAssignmentRunAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00016 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00016",
        title: "Do not set another object's state field by field",
        messageFormat: "{0} members of '{1}' are set in a row inside a larger method — name the change as a method on {2} or a helper",
        category: "Digitals.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00016",
        description: "Three or more assignments in a row to members of one object ('order.Status = ...; order.ClosedAt"
            + " = ...; order.ClosedBy = ...;') inside a larger method drop the reader from the method's steps to"
            + " property level. Name the change: a behaviour method on the object ('order.Close(by, now)') or a helper."
            + " Not reported: a run that makes up at least half of its method, whose job is the change (a mapper, a"
            + " load-update-save method); the 'this' parameter of an extension method; lambda parameters; a local"
            + " created with 'new' in the same block; the method's own instance. Threshold:"
            + " digitals_max_member_assignment_run (default 2).");

    private const string MaxRunOption = "digitals_max_member_assignment_run";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBlock, SyntaxKind.Block, SyntaxKind.SwitchSection);
    }

    private static void AnalyzeBlock(SyntaxNodeAnalysisContext context)
    {
        var statements = context.Node is BlockSyntax block
            ? block.Statements
            : ((SwitchSectionSyntax)context.Node).Statements;
        if (statements.Count < 3)
        {
            return;
        }

        var max = AnalyzerOptionReader.PositiveInt(context.Options, context.Node.SyntaxTree, MaxRunOption, 2);
        var runStart = 0;
        string? runReceiver = null;
        for (var i = 0; i <= statements.Count; i++)
        {
            var receiver = i < statements.Count ? ReceiverName(statements[i]) : null;
            if (receiver is not null && receiver == runReceiver)
            {
                continue;
            }

            if (runReceiver is not null && i - runStart > max)
            {
                Report(context, statements, runStart, i - runStart);
            }

            runStart = i;
            runReceiver = receiver;
        }
    }

    private static void Report(
        SyntaxNodeAnalysisContext context,
        SyntaxList<StatementSyntax> statements,
        int start,
        int length)
    {
        var assignment = (AssignmentExpressionSyntax)((ExpressionStatementSyntax)statements[start]).Expression;
        var first = (MemberAccessExpressionSyntax)assignment.Left;
        var symbol = context.SemanticModel.GetSymbolInfo(first.Expression, context.CancellationToken).Symbol;
        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

        if (type is null
            || symbol is IParameterSymbol
            {
                ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.AnonymousFunction },
            }
            || symbol is IParameterSymbol { Ordinal: 0, ContainingSymbol: IMethodSymbol { IsExtensionMethod: true } }
            || (symbol is ILocalSymbol && IsCreatedWithNew(symbol, context.CancellationToken))
            || IsWholeJob(context.Node, length))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, first.Expression.GetLocation(), length, first.Expression.ToString(), type.Name));
    }

    // The receiver text of 'receiver.Member = value;' (or a compound assignment), or null.
    private static string? ReceiverName(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                Left: MemberAccessExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleMemberAccessExpression,
                    Expression: IdentifierNameSyntax receiver,
                },
            },
        }
            ? receiver.Identifier.ValueText
            : null;

    // The run is at least half of the enclosing method, lambda or local function: the change is that
    // body's job, not a dip below the level of its other statements.
    private static bool IsWholeJob(SyntaxNode node, int length)
    {
        for (var owner = node.Parent; owner is not null; owner = owner.Parent)
        {
            if (owner is BaseMethodDeclarationSyntax
                or AccessorDeclarationSyntax
                or LocalFunctionStatementSyntax
                or AnonymousFunctionExpressionSyntax)
            {
                return length * 2 >= StatementCount(owner);
            }
        }

        return false;
    }

    // Statements in the body, not counting blocks or anything inside a nested lambda or local function.
    private static int StatementCount(SyntaxNode owner)
    {
        return owner
            .DescendantNodes(child =>
                child == owner || child is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
            .Count(node => node is StatementSyntax and not BlockSyntax and not LocalFunctionStatementSyntax);
    }

    private static bool IsCreatedWithNew(ISymbol local, CancellationToken cancellationToken)
    {
        return local.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is VariableDeclaratorSyntax
            {
                Initializer.Value: BaseObjectCreationExpressionSyntax,
            });
    }
}
