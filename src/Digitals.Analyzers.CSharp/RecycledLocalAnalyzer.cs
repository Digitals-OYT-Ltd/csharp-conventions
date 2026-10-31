using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00011 — recycled local (code-style.md#expressions-read-in-one-pass): a local that is read and
/// then overwritten with a value that has nothing to do with it now names two things, and the
/// reader can no longer trust what it holds. "Nothing to do with it" is approximated: the new
/// value refers neither to the local nor to a local derived from it. Sonar's S1854 already covers
/// the overwrite-before-read case, so this rule needs a read first.
///
/// Deliberately exempt, because the second value serves the same purpose as the first:
///   - updates that refer to the local ('total += x', 's = s.Trim()', 'node = node.Next');
///   - reassignment in a loop the local is declared outside of ('line = reader.ReadLine()');
///   - refinement under a condition that reads it ('if (x is null) x = Fallback()');
///   - a read and an assignment on different branches of an if/switch/try/?:;
///   - clearing to null or default, and assignment inside a lambda or local function that
///     captures the local.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RecycledLocalAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00011 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00011",
        title: "Do not recycle a local for an unrelated value",
        messageFormat: "'{0}' is read and then reassigned to an unrelated value — declare a new local named for what it now holds",
        category: "Digitals.Naming",
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00011");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Block-scoped so each body is indexed once and every assignment in it is judged against
        // that index. Judging each assignment alone re-walked the whole body per assignment.
        context.RegisterOperationBlockStartAction(block =>
        {
            var candidates = new List<Candidate>();
            block.RegisterOperationAction(
                assignmentContext => CollectCandidate(assignmentContext, candidates),
                OperationKind.SimpleAssignment);
            block.RegisterOperationBlockEndAction(endContext => AnalyzeCandidates(endContext, candidates));
        });
    }

    private static void CollectCandidate(OperationAnalysisContext context, List<Candidate> candidates)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not ILocalReferenceOperation { Local: { IsRef: false } local }
            || local.DeclaringSyntaxReferences.Length == 0
            || IsClearing(assignment.Value))
        {
            return;
        }

        var declaration = local.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken);
        if (IsInsideScopeOutsideDeclaration(assignment, declaration)
            || IsRefinementUnderCondition(assignment, local)
            || References(assignment.Value, local))
        {
            return;
        }

        lock (candidates)
        {
            candidates.Add(new Candidate(assignment, local, declaration));
        }
    }

    private static void AnalyzeCandidates(OperationBlockAnalysisContext context, List<Candidate> candidates)
    {
        // A block can hold several roots (a body plus a constructor initializer, say); each gets
        // its own index.
        var indexes = new Dictionary<IOperation, BodyIndex>();
        foreach (var candidate in candidates)
        {
            var root = Root(candidate.Assignment);
            if (!indexes.TryGetValue(root, out var index))
            {
                index = new BodyIndex(root);
                indexes.Add(root, index);
            }

            // The single walk goes first; the fixpoint only runs for an assignment that would
            // otherwise be reported.
            if (HasSequentialReadBefore(index, candidate)
                && !References(candidate.Assignment.Value, index.DerivedLocals(candidate.Local)))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule, candidate.Assignment.Target.Syntax.GetLocation(), candidate.Local.Name));
            }
        }
    }

    private static bool IsClearing(IOperation value)
    {
        while (value is IConversionOperation conversion)
        {
            value = conversion.Operand;
        }

        return value is IDefaultValueOperation
            or ILiteralOperation { ConstantValue: { HasValue: true, Value: null } };
    }

    // A loop, lambda, or local function between the assignment and the local's declaration: the
    // loop reassigns per iteration on purpose, and a capture runs out of source order.
    private static bool IsInsideScopeOutsideDeclaration(IOperation operation, SyntaxNode declaration)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is ILoopOperation or IAnonymousFunctionOperation or ILocalFunctionOperation
                && !current.Syntax.Span.Contains(declaration.Span))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRefinementUnderCondition(IOperation assignment, ILocalSymbol local)
    {
        for (var current = assignment.Parent; current is not null; current = current.Parent)
        {
            var guard = current switch
            {
                IConditionalOperation conditional => conditional.Condition,
                ISwitchOperation @switch => @switch.Value,
                _ => null,
            };

            if (guard is not null && References(guard, local))
            {
                return true;
            }
        }

        return false;
    }

    private static IOperation Root(IOperation operation)
    {
        var current = operation;
        while (current.Parent is not null)
        {
            current = current.Parent;
        }

        return current;
    }

    private static bool References(IOperation operation, ILocalSymbol local)
    {
        foreach (var descendant in operation.DescendantsAndSelf())
        {
            if (descendant is ILocalReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Local, local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool References(IOperation operation, HashSet<ILocalSymbol> locals)
    {
        foreach (var descendant in operation.DescendantsAndSelf())
        {
            if (descendant is ILocalReferenceOperation reference && locals.Contains(reference.Local))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSequentialReadBefore(BodyIndex index, Candidate candidate)
    {
        var assignment = candidate.Assignment;
        var assignmentStart = assignment.Syntax.SpanStart;
        foreach (var reference in index.ReferencesTo(candidate.Local))
        {
            if (reference.Syntax.SpanStart >= assignmentStart
                || IsAssignmentTarget(reference)
                || IsInsideScopeOutsideDeclaration(reference, candidate.Declaration))
            {
                continue;
            }

            if (!IsBranching(CommonAncestor(reference.Syntax, assignment.Syntax).Kind()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAssignmentTarget(ILocalReferenceOperation reference) =>
        reference.Parent is ISimpleAssignmentOperation parent && parent.Target == reference;

    // Two nodes of one tree nest or are disjoint, so the first ancestor of 'second' whose span
    // covers 'first' is their common ancestor. No ancestor set to build.
    private static SyntaxNode CommonAncestor(SyntaxNode first, SyntaxNode second)
    {
        var current = second;
        while (!current.Span.Contains(first.Span) && current.Parent is not null)
        {
            current = current.Parent;
        }

        return current;
    }

    // Syntax that splits control flow: a read and an assignment whose nearest common ancestor is
    // one of these sit on different branches (or in a loop's header and body), not in sequence.
    private static bool IsBranching(SyntaxKind kind) => kind is
        SyntaxKind.IfStatement
        or SyntaxKind.ConditionalExpression
        or SyntaxKind.SwitchStatement
        or SyntaxKind.SwitchExpression
        or SyntaxKind.TryStatement
        or SyntaxKind.ForStatement
        or SyntaxKind.ForEachStatement
        or SyntaxKind.WhileStatement
        or SyntaxKind.DoStatement
        or SyntaxKind.CoalesceExpression
        or SyntaxKind.LogicalAndExpression
        or SyntaxKind.LogicalOrExpression
        or SyntaxKind.ConditionalAccessExpression;

    private sealed class Candidate(ISimpleAssignmentOperation assignment, ILocalSymbol local, SyntaxNode declaration)
    {
        public ISimpleAssignmentOperation Assignment { get; } = assignment;

        public ILocalSymbol Local { get; } = local;

        public SyntaxNode Declaration { get; } = declaration;
    }

    /// <summary>
    /// One walk over a body: every read of each local, and a graph of which local's value feeds
    /// which ('var y = x + 1;' is the edge x -> y).
    /// </summary>
    private sealed class BodyIndex
    {
        private readonly Dictionary<ILocalSymbol, List<ILocalReferenceOperation>> _references =
            new(SymbolEqualityComparer.Default);

        private readonly Dictionary<ILocalSymbol, List<ILocalSymbol>> _feeds =
            new(SymbolEqualityComparer.Default);

        private readonly Dictionary<ILocalSymbol, HashSet<ILocalSymbol>> _derived =
            new(SymbolEqualityComparer.Default);

        public BodyIndex(IOperation body)
        {
            // An explicit stack, not recursion: a long '+' chain or fluent call nests thousands
            // deep. Each entry records how many stored-into locals enclose it, so leaving a
            // value's subtree drops that value's target again.
            var pending = new Stack<(IOperation Operation, int Depth)>();
            var targets = new List<ILocalSymbol>();
            pending.Push((body, 0));
            while (pending.Count > 0)
            {
                var (operation, depth) = pending.Pop();
                targets.RemoveRange(depth, targets.Count - depth);
                if (StoredInto(operation) is { } target)
                {
                    targets.Add(target);
                }

                if (operation is ILocalReferenceOperation reference)
                {
                    Add(reference, targets);
                }

                foreach (var child in operation.ChildOperations)
                {
                    pending.Push((child, targets.Count));
                }
            }
        }

        public IReadOnlyList<ILocalReferenceOperation> ReferencesTo(ILocalSymbol local) =>
            _references.TryGetValue(local, out var list) ? list : Array.Empty<ILocalReferenceOperation>();

        // The local plus every local whose value was computed from it, transitively:
        // 'var next = node.Next; node = next;' is still the same walk.
        public HashSet<ILocalSymbol> DerivedLocals(ILocalSymbol local)
        {
            if (_derived.TryGetValue(local, out var cached))
            {
                return cached;
            }

            var derived = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default) { local };
            var frontier = new Stack<ILocalSymbol>();
            frontier.Push(local);
            while (frontier.Count > 0)
            {
                if (!_feeds.TryGetValue(frontier.Pop(), out var fed))
                {
                    continue;
                }

                foreach (var target in fed.Where(derived.Add))
                {
                    frontier.Push(target);
                }
            }

            _derived.Add(local, derived);
            return derived;
        }

        // The local that 'operation' is stored into, when it is the initializer in
        // 'var y = <value>' or the right side of 'y = <value>' (or 'y += <value>').
        private static ILocalSymbol? StoredInto(IOperation operation) => operation.Parent switch
        {
            IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } initializer
                when initializer.Value == operation => declarator.Symbol,
            IAssignmentOperation { Target: ILocalReferenceOperation target } assignment
                when assignment.Value == operation => target.Local,
            _ => null,
        };

        private void Add(ILocalReferenceOperation reference, List<ILocalSymbol> targets)
        {
            var local = reference.Local;
            if (!_references.TryGetValue(local, out var reads))
            {
                reads = [];
                _references.Add(local, reads);
            }

            reads.Add(reference);
            if (targets.Count == 0)
            {
                return;
            }

            if (!_feeds.TryGetValue(local, out var fed))
            {
                fed = [];
                _feeds.Add(local, fed);
            }

            fed.AddRange(targets);
        }
    }
}
