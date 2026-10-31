using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00017 — result unwrapped inline:
/// 'var download = await blob.DownloadAsync(...); if (!download.IsSuccess) throw ...;' is two lines
/// at the API's level where the recipe wants one at the use case's ('await DownloadSourceAsync()').
///
/// The pattern: a local declared from a call, then immediately an 'if' that tests a success flag
/// on it (IsSuccess, IsFailure, Succeeded, IsSuccessStatusCode, ...) and leaves by throwing.
/// The call-and-check is exactly what a small helper is for, so it is reported only where it mixes
/// levels: in an orchestration method (see <see cref="OrchestrationMethods"/>). A helper or adapter
/// that unwraps two calls in a row is working at one level, the API's, and is left alone. So is a
/// single-call handler, whose awaited unwrap is the only await in the body: call, check, return is
/// one step that already reads straight (the page's Limit section).
/// Early-return propagation ('if (r.IsFailure) return r.Error;') is the result-type idiom, one step
/// at the caller's level, and is not reported.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InlineResultUnwrapAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00017 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00017",
        title: "Do not unwrap a result inline beside other steps",
        messageFormat: "'{0}' is checked and thrown on inline between other steps — move the call and its check into a helper that returns the value",
        category: "Digitals.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "In an orchestration method, a call whose result is checked and thrown on in the next statement"
            + " ('var upload = await blob.UploadAsync(...); if (!upload.IsSuccess) throw ...;') spends two or more"
            + " lines of the recipe on one step's error handling. A helper that makes the call and throws on failure"
            + " ('await UploadInvoiceAsync(...)') keeps the step to one line. Not reported: early-return propagation"
            + " ('if (r.IsFailure) return r.Error;') and a method whose only awaited call is the checked one."
            + OrchestrationMethods.Definition);

    private static readonly ImmutableHashSet<string> SuccessFlags = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "IsSuccess",
        "IsSuccessful",
        "Succeeded",
        "Success",
        "IsOk",
        "IsSuccessStatusCode",
        "IsFailure",
        "IsFailed",
        "Failed",
        "IsError",
        "HasError",
        "HasErrors");

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

        var sites = new List<(SyntaxToken Local, IfStatementSyntax Check)>();
        var blocks = body
            .DescendantNodesAndSelf(descendIntoChildren: node => node is not LocalFunctionStatementSyntax)
            .OfType<BlockSyntax>();
        foreach (var block in blocks)
        {
            CollectSites(block.Statements, sites);
        }

        if (sites.Count == 0
            || (sites.Count == 1 && IsOnlyAwait(body, sites[0].Local))
            || !OrchestrationMethods.IsOrchestration(
                context.SemanticModel, declaration, context.Options, context.CancellationToken))
        {
            return;
        }

        foreach (var (local, check) in sites)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, check.IfKeyword.GetLocation(), local.ValueText));
        }
    }

    private static void CollectSites(
        SyntaxList<StatementSyntax> statements,
        List<(SyntaxToken, IfStatementSyntax)> sites)
    {
        for (var i = 0; i + 1 < statements.Count; i++)
        {
            if (statements[i] is LocalDeclarationStatementSyntax { Declaration.Variables: { Count: 1 } variables }
                && variables[0] is { Initializer.Value: var value } variable
                && IsCall(value)
                && statements[i + 1] is IfStatementSyntax check
                && TestsSuccessFlag(check.Condition, variable.Identifier.ValueText)
                && Throws(check.Statement))
            {
                sites.Add((variable.Identifier, check));
            }
        }
    }

    // The site's call is awaited and nothing else in the body awaits: the handler has one step.
    private static bool IsOnlyAwait(BlockSyntax body, SyntaxToken local)
    {
        if (local.Parent is not VariableDeclaratorSyntax { Initializer.Value: AwaitExpressionSyntax })
        {
            return false;
        }

        var awaits = body
            .DescendantNodes(child => child is not (LocalFunctionStatementSyntax or AnonymousFunctionExpressionSyntax))
            .Count(node => node is AwaitExpressionSyntax
                or CommonForEachStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword }
                or UsingStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword }
                or LocalDeclarationStatementSyntax { AwaitKeyword.RawKind: (int)SyntaxKind.AwaitKeyword });

        return awaits == 1;
    }

    private static bool IsCall(ExpressionSyntax value)
    {
        while (true)
        {
            switch (value)
            {
                case AwaitExpressionSyntax awaited:
                    value = awaited.Expression;
                    break;

                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is not MemberAccessExpressionSyntax
                        {
                            Name.Identifier.ValueText: "ConfigureAwait",
                        } access)
                    {
                        return true;
                    }

                    value = access.Expression;
                    break;

                case ParenthesizedExpressionSyntax parenthesized:
                    value = parenthesized.Expression;
                    break;

                default:
                    return false;
            }
        }
    }

    private static bool TestsSuccessFlag(ExpressionSyntax condition, string local)
    {
        foreach (var node in condition.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver, Name: var name }
                    when receiver.Identifier.ValueText == local && SuccessFlags.Contains(name.Identifier.ValueText):
                    return true;

                case IsPatternExpressionSyntax { Expression: IdentifierNameSyntax receiver, Pattern: var pattern }
                    when receiver.Identifier.ValueText == local && NamesSuccessFlag(pattern):
                    return true;

                default:
                    break;
            }
        }

        return false;
    }

    private static bool NamesSuccessFlag(PatternSyntax pattern)
    {
        foreach (var node in pattern.DescendantNodesAndSelf())
        {
            if (node is SubpatternSyntax { NameColon.Name.Identifier.ValueText: var name }
                && SuccessFlags.Contains(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Throws(StatementSyntax statement) => statement switch
    {
        ThrowStatementSyntax => true,
        BlockSyntax block => block.Statements.LastOrDefault() is ThrowStatementSyntax,
        _ => false,
    };
}
