using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Digitals.Testing.CSharp;

/// <summary>
/// DT10004 — no sleeping in tests. A sleep is a guess about how long something takes, and the
/// guess is calibrated on the developer's idle machine rather than on a loaded CI agent: too short
/// and the test flakes, too long and the suite crawls. Neither shows up as a failure anyone can
/// read.
/// <para>
/// Waiting for time to pass is <c>FakeTimeProvider.Advance</c>. Waiting for a condition is a poll
/// with a deadline, or the wait strategy the library already provides. Where a real delay is
/// genuinely needed, <c>Task.Delay(delay, timeProvider, cancellationToken)</c> takes a
/// <c>TimeProvider</c> and stays controllable, so that overload is allowed.
/// </para>
/// <para>
/// Scoped by package reference rather than by attribute: only test projects reference
/// Digitals.Testing, so every invocation in the compilation is fair game, fixtures and helpers
/// included. A retry loop in an integration fixture that genuinely needs wall-clock delay is the
/// case to suppress, with a comment saying why.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestSleepAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT10004 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT10004",
        title: "Do not sleep in tests",
        messageFormat: "'{0}' makes this test wait on the wall clock — advance a FakeTimeProvider, poll for the condition, or pass a TimeProvider to Task.Delay",
        category: "Digitals.Testing",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/testing.md#dt10004");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Operation action, not syntax: 'Sleep' and 'Delay' are ordinary identifiers, and only the
        // resolved symbol says whether this is Thread.Sleep or someone's own method.
        context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var containingType = method.ContainingType;

        if (containingType is null)
        {
            return;
        }

        if (method.Name == "Sleep" && IsType(containingType, "Thread", "System.Threading"))
        {
            Report(context, invocation, "Thread.Sleep");
            return;
        }

        // Task.Delay is only a problem without a TimeProvider. The overload that takes one is
        // controllable from a test, which is the whole point of the rule.
        if (method.Name == "Delay"
            && IsType(containingType, "Task", "System.Threading.Tasks")
            && !method.Parameters.Any(parameter => IsTimeProvider(parameter.Type)))
        {
            Report(context, invocation, "Task.Delay");
        }
    }

    private static void Report(OperationAnalysisContext context, IInvocationOperation invocation, string display)
        => context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), display));

    private static bool IsType(INamedTypeSymbol type, string name, string containingNamespace)
        => type.Name == name && type.ContainingNamespace?.ToDisplayString() == containingNamespace;

    private static bool IsTimeProvider(ITypeSymbol type)
        => type is INamedTypeSymbol { Name: "TimeProvider" } named
            && named.ContainingNamespace is { Name: "System", ContainingNamespace.IsGlobalNamespace: true };
}
