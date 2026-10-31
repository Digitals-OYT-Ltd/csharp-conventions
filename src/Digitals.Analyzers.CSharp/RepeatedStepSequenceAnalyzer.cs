using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00013 — a run of steps copied inside one member: 'batch.Reject(...); await
/// unitOfWork.CommitAsync(...); await notifications.SendAsync(...);' pasted into each failure
/// branch is a protocol that wants one named helper. The copies drift: the next step lands in one
/// branch and not the others.
///
/// Statements are compared by shape, not text: argument lists, literals, lambda bodies,
/// initializers, declared local names and the right side of a plain assignment are erased, so
/// copies that differ only in the reason passed or the message logged still match. Receivers and
/// member names are kept. A compound statement (if, foreach, using, ...) is compared by its header
/// alone. A run counts only when it holds at least two calls on two different receivers, so a
/// builder chain or a block of 'list.Add(...)' lines is not a protocol, and when its statements are
/// not all the same shape.
///
/// Thresholds: 'digitals_repeated_sequence_length' (statements per run, default 3) and
/// 'digitals_repeated_sequence_occurrences' (non-overlapping copies, default 2). One diagnostic per
/// member, on the first copy, with the other copies as additional locations. Local functions are
/// analysed as members of their own.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RepeatedStepSequenceAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00013 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00013",
        title: "Do not copy a sequence of steps inside one member",
        messageFormat: "These {1} statements calling {2} repeat {3} times in '{0}' — move them into one helper named for what they do",
        category: "Digitals.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The same run of calls, matched on receiver and member with arguments ignored, appears more than"
            + " once in one member. It is a protocol copied by hand, and the copies drift when a step is added to one"
            + " and not the others. Tune it with digitals_repeated_sequence_length (statements per run, default 3) and"
            + " digitals_repeated_sequence_occurrences (copies, default 2).");

    private const string LengthOption = "digitals_repeated_sequence_length";
    private const string OccurrencesOption = "digitals_repeated_sequence_occurrences";
    private const int ListedSteps = 4;

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(
            AnalyzeMember,
            SyntaxKind.MethodDeclaration,
            SyntaxKind.ConstructorDeclaration,
            SyntaxKind.LocalFunctionStatement,
            SyntaxKind.GetAccessorDeclaration,
            SyntaxKind.SetAccessorDeclaration,
            SyntaxKind.InitAccessorDeclaration);
    }

    // Every run of 'length' steps on two or more receivers, keyed by its step IDs, with
    // where each copy starts.
    private static Dictionary<string, List<Occurrence>> ProtocolWindows(List<Step[]> steps, int length)
    {
        var windows = new Dictionary<string, List<Occurrence>>();
        for (var s = 0; s < steps.Count; s++)
        {
            for (var start = 0; start + length <= steps[s].Length; start++)
            {
                var window = new ArraySegment<Step>(steps[s], start, length);
                if (!IsProtocol(window, length))
                {
                    continue;
                }

                var key = string.Join(",", window.Select(step => step.Id));
                if (!windows.TryGetValue(key, out var list))
                {
                    list = [];
                    windows.Add(key, list);
                }

                list.Add(new Occurrence(s, start));
            }
        }

        return windows;
    }

    private static void AnalyzeMember(SyntaxNodeAnalysisContext context)
    {
        var (body, name) = BodyAndName(context.Node);
        if (body is null)
        {
            return;
        }

        var tree = context.Node.SyntaxTree;
        var length = AnalyzerOptionReader.PositiveInt(context.Options, tree, LengthOption, 3);
        var occurrences = AnalyzerOptionReader.PositiveInt(context.Options, tree, OccurrencesOption, 2);

        var lists = StatementLists(body).Where(list => list.Count >= length).ToList();
        if (lists.Sum(list => list.Count) < length * occurrences)
        {
            return;
        }

        var ids = new Dictionary<string, int>();
        var steps = lists.ConvertAll(list => list.Select(statement => Step.Of(statement, ids)).ToArray());

        var windows = ProtocolWindows(steps, length);

        // Earliest window in source order that still has enough non-overlapping copies. One
        // diagnostic per member: a copied region produces a matching window at every offset.
        List<Occurrence>? best = null;
        foreach (var candidate in windows.Values)
        {
            var copies = NonOverlapping(candidate, length);
            if (copies.Count >= occurrences
                && (best is null || Position(lists, copies[0]) < Position(lists, best[0])))
            {
                best = copies;
            }
        }

        if (best is null)
        {
            return;
        }

        var run = Extend(steps, best, length);
        var calls = Enumerable.Range(best[0].Start, run)
            .Select(i => steps[best[0].Sequence][i].Method)
            .Where(method => method is not null)
            .ToList();
        var listed = string.Join(", ", calls.Take(ListedSteps)) + (calls.Count > ListedSteps ? ", ..." : string.Empty);

        var locations = best.ConvertAll(copy => lists[copy.Sequence][copy.Start].GetLocation());
        context.ReportDiagnostic(Diagnostic.Create(
            Rule, locations[0], locations.Skip(1), name, run, listed, best.Count));
    }

    private static (SyntaxNode? Body, string Name) BodyAndName(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => (method.Body, method.Identifier.ValueText),
        ConstructorDeclarationSyntax constructor => (constructor.Body, constructor.Identifier.ValueText),
        LocalFunctionStatementSyntax local => (local.Body, local.Identifier.ValueText),
        AccessorDeclarationSyntax accessor => (accessor.Body, accessor.Keyword.ValueText),
        _ => (null, string.Empty),
    };

    // Every statement list in the body: blocks and switch sections, at any depth, but not inside
    // a local function, which is reported on its own.
    private static IEnumerable<SyntaxList<StatementSyntax>> StatementLists(SyntaxNode body)
    {
        var nodes = body.DescendantNodesAndSelf(
            descendIntoChildren: child => child is not LocalFunctionStatementSyntax);
        foreach (var node in nodes)
        {
            switch (node)
            {
                case BlockSyntax block:
                    yield return block.Statements;
                    break;

                case SwitchSectionSyntax section:
                    yield return section.Statements;
                    break;

                default:
                    break;
            }
        }
    }

    // Two calls on two receivers, and not one shape repeated (twelve identical log lines).
    private static bool IsProtocol(ArraySegment<Step> window, int length) =>
        window.Select(step => step.Id).Distinct().Count() >= length - 1
        && window.Select(step => step.Receiver).Where(receiver => receiver is not null).Distinct().Count() >= 2;

    private static List<Occurrence> NonOverlapping(List<Occurrence> occurrences, int length)
    {
        var kept = new List<Occurrence>();
        foreach (var occurrence in occurrences)
        {
            if (kept.Count == 0
                || kept[kept.Count - 1].Sequence != occurrence.Sequence
                || occurrence.Start >= kept[kept.Count - 1].Start + length)
            {
                kept.Add(occurrence);
            }
        }

        return kept;
    }

    // Grows the matched run while every copy continues with the same step and stays clear of the next copy.
    private static int Extend(List<Step[]> steps, List<Occurrence> copies, int length)
    {
        var run = length;
        while (true)
        {
            var first = copies[0];
            if (first.Start + run >= steps[first.Sequence].Length)
            {
                return run;
            }

            var id = steps[first.Sequence][first.Start + run].Id;
            for (var c = 0; c < copies.Count; c++)
            {
                var copy = copies[c];
                var row = steps[copy.Sequence];
                var overlapsNext = c + 1 < copies.Count
                    && copies[c + 1].Sequence == copy.Sequence
                    && copy.Start + run >= copies[c + 1].Start;
                if (copy.Start + run >= row.Length || row[copy.Start + run].Id != id || overlapsNext)
                {
                    return run;
                }
            }

            run++;
        }
    }

    private static int Position(List<SyntaxList<StatementSyntax>> lists, Occurrence occurrence) =>
        lists[occurrence.Sequence][occurrence.Start].SpanStart;

    private readonly struct Occurrence(int sequence, int start)
    {
        public int Sequence { get; } = sequence;

        public int Start { get; } = start;
    }

    // One statement reduced to its shape, plus the call it makes, if any.
    private readonly struct Step(int id, string? receiver, string? method)
    {
        public int Id { get; } = id;

        public string? Receiver { get; } = receiver;

        public string? Method { get; } = method;

        public static Step Of(StatementSyntax statement, Dictionary<string, int> ids)
        {
            var shape = Shape(statement);
            if (!ids.TryGetValue(shape, out var id))
            {
                id = ids.Count;
                ids.Add(shape, id);
            }

            return Call(statement) is { } call
                ? new Step(id, ReceiverOf(call), MethodOf(call))
                : new Step(id, null, null);
        }

        private static string Shape(StatementSyntax statement) => statement switch
        {
            ReturnStatementSyntax => "return",
            ThrowStatementSyntax => "throw",
            IfStatementSyntax node => Keyed("if ", node.Condition),
            CommonForEachStatementSyntax node => Keyed("foreach ", node.Expression),
            UsingStatementSyntax node => Keyed("using ", (SyntaxNode?)node.Declaration ?? node.Expression!),
            WhileStatementSyntax node => Keyed("while ", node.Condition),
            LockStatementSyntax node => Keyed("lock ", node.Expression),
            SwitchStatementSyntax node => Keyed("switch ", node.Expression),
            TryStatementSyntax or BlockSyntax or ForStatementSyntax or DoStatementSyntax => statement.Kind().ToString(),
            _ => Keyed(string.Empty, statement),
        };

        private static string Keyed(string keyword, SyntaxNode node)
        {
            var builder = new StringBuilder(keyword);
            Append(builder, node);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, SyntaxNodeOrToken item)
        {
            if (item.IsToken)
            {
                var token = item.AsToken();
                builder.Append(IsLiteral(token) ? "$lit" : token.ValueText).Append(' ');
                return;
            }

            switch (item.AsNode())
            {
                case ArgumentListSyntax:
                    builder.Append("(...) ");
                    return;

                case BracketedArgumentListSyntax:
                    builder.Append("[...] ");
                    return;

                case AnonymousFunctionExpressionSyntax:
                    builder.Append("$fn ");
                    return;

                case InterpolatedStringExpressionSyntax:
                    builder.Append("$lit ");
                    return;

                case InitializerExpressionSyntax:
                    builder.Append("{...} ");
                    return;

                case VariableDeclaratorSyntax declarator:
                    builder.Append("$local ");
                    if (declarator.Initializer is { } initializer)
                    {
                        Append(builder, initializer);
                    }

                    return;

                case AssignmentExpressionSyntax assignment when Invocation(assignment.Right) is null:
                    Append(builder, assignment.Left);
                    builder.Append(assignment.OperatorToken.ValueText).Append(" ... ");
                    return;

                case { } node:
                    foreach (var child in node.ChildNodesAndTokens())
                    {
                        Append(builder, child);
                    }

                    return;

                default:
                    break;
            }
        }

        private static bool IsLiteral(SyntaxToken token) => token.Kind() is
            SyntaxKind.StringLiteralToken
            or SyntaxKind.NumericLiteralToken
            or SyntaxKind.CharacterLiteralToken
            or SyntaxKind.SingleLineRawStringLiteralToken
            or SyntaxKind.MultiLineRawStringLiteralToken
            or SyntaxKind.Utf8StringLiteralToken;

        // The call a statement makes: an expression statement, the right side of an assignment or
        // the initializer of a single local.
        private static ExpressionSyntax? Call(StatementSyntax statement) => statement switch
        {
            ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } =>
                Invocation(assignment.Right),
            ExpressionStatementSyntax expression => Invocation(expression.Expression),
            LocalDeclarationStatementSyntax { Declaration.Variables: { Count: 1 } variables } =>
                variables[0].Initializer is { } initializer ? Invocation(initializer.Value) : null,
            _ => null,
        };

        // Unwraps await, parentheses and '.ConfigureAwait(...)' down to an invocation or a '?.' call.
        private static ExpressionSyntax? Invocation(ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case AwaitExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case ParenthesizedExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case InvocationExpressionSyntax
                    {
                        Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ConfigureAwait" } access,
                    }:
                        expression = access.Expression;
                        break;

                    case InvocationExpressionSyntax
                        or ConditionalAccessExpressionSyntax { WhenNotNull: InvocationExpressionSyntax }:
                        return expression;

                    default:
                        return null;
                }
            }
        }

        // The first name in the call's chain: 'store' for 'this.store.Items.AddAsync(...)', 'File'
        // for 'File.ReadAllText(...)', the method itself for an unqualified 'Validate(...)'.
        private static string ReceiverOf(ExpressionSyntax call)
        {
            var expression = call;
            while (true)
            {
                switch (expression)
                {
                    case MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax } node:
                        return node.Name.Identifier.ValueText;

                    case MemberAccessExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case InvocationExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case ConditionalAccessExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case ElementAccessExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case ParenthesizedExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case AwaitExpressionSyntax node:
                        expression = node.Expression;
                        break;

                    case SimpleNameSyntax node:
                        return node.Identifier.ValueText;

                    case PredefinedTypeSyntax node:
                        return node.Keyword.ValueText;

                    default:
                        return expression.ToString();
                }
            }
        }

        private static string? MethodOf(ExpressionSyntax call) => call switch
        {
            InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax access } =>
                access.Name.Identifier.ValueText,
            InvocationExpressionSyntax { Expression: SimpleNameSyntax name } => name.Identifier.ValueText,
            ConditionalAccessExpressionSyntax
            {
                WhenNotNull: InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding },
            } => binding.Name.Identifier.ValueText,
            _ => null,
        };
    }
}
