using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Digitals.Testing.CSharp;

/// <summary>
/// DT10005 — every test reaches an assertion. A test that runs the code and checks nothing passes
/// whatever the code does, and coverage counts its lines as tested all the same. At a 100% floor
/// that is the one way left to look tested without being tested, and no coverage number shows it.
/// <para>
/// This replaces Sonar's S2699, which the packaged globalconfig turns off because it cannot see an
/// assertion made inside a helper or a harness. Here assertions are followed through any method
/// declared in the same test project, however deep, so a test that calls its own
/// <c>AssertValid(result)</c> passes. Calls into another assembly are only recognised by name: the
/// defaults below, plus whatever a repo lists in <c>digitals_testing.assertion_methods</c>.
/// </para>
/// <para>
/// Reported at the end of the compilation, because whether a helper asserts is only known once
/// every method body has been seen. The diagnostic therefore shows on build and in CI, and in the
/// IDE only with full solution analysis on.
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestAssertionAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Comma-separated extra assertion methods, added to the defaults and never replacing them.
    /// Each entry is <c>Method</c>, <c>Type.Method</c> or <c>Namespace.Type.Method</c>; a method
    /// part ending in <c>*</c> matches by prefix.
    /// </summary>
    public const string AssertionMethodsOption = "digitals_testing.assertion_methods";

    /// <summary>The DT10005 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT10005",
        title: "Test must reach an assertion",
        messageFormat: "Test '{0}' reaches no assertion, so it passes whatever the code under test does; assert on the outcome, write a does-not-throw test as Assert.Null(Record.Exception(...)), or name an asserting harness in digitals_testing.assertion_methods",
        category: "Digitals.Testing",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/engineering-standards/blob/main/docs/standards/testing/csharp/unit-tests.md",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    // Namespace-qualified where the type name alone is common enough to collide with production
    // code: a class called Verifier with a VerifySignature method is not a snapshot assertion.
    // Assert is deliberately unqualified, so a repo's own Assert class counts too.
    private static readonly string[] DefaultAssertionMethods =
    [
        "Assert.*",
        "VerifyXunit.Verifier.Verify*",
        "VerifyXunit.VerifyBase.Verify*",
        "NSubstitute.SubstituteExtensions.Received",
        "NSubstitute.SubstituteExtensions.ReceivedWithAnyArgs",
        "NSubstitute.SubstituteExtensions.DidNotReceive",
        "NSubstitute.SubstituteExtensions.DidNotReceiveWithAnyArgs",
        "NSubstitute.Received.InOrder",

		// Roslyn's analyzer test harness: RunAsync compares the diagnostics it gets against the
		// expected ones and throws on a mismatch. Two org repos test analyzers this way.
		"Microsoft.CodeAnalysis.Testing.AnalyzerTest.RunAsync",
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var methods = new ConcurrentDictionary<IMethodSymbol, MethodFacts>(SymbolEqualityComparer.Default);
            var matchers = new ConcurrentDictionary<string, AssertionMatcher>(StringComparer.Ordinal);
            start.RegisterOperationBlockAction(block => Collect(block, methods, matchers));
            start.RegisterCompilationEndAction(end => ReportUnasserted(end, methods));
        });
    }

    // Records, for every method body, whether it calls an assertion itself and which source methods
    // it calls. Lambdas and local functions are part of the enclosing body, so an assertion inside
    // one counts for the method that declares it.
    private static void Collect(
        OperationBlockAnalysisContext block,
        ConcurrentDictionary<IMethodSymbol, MethodFacts> methods,
        ConcurrentDictionary<string, AssertionMatcher> matchers)
    {
        if (block.OwningSymbol is not IMethodSymbol owner)
        {
            return;
        }

        var matcher = MatcherFor(block, matchers);
        var assertsDirectly = false;
        var callees = new List<IMethodSymbol>();
        foreach (var operation in block.OperationBlocks.SelectMany(root => root.DescendantsAndSelf()))
        {
            // A method group counts as a call: Array.ForEach(results, AssertValid) asserts.
            var target = operation switch
            {
                IInvocationOperation invocation => invocation.TargetMethod,
                IMethodReferenceOperation reference => reference.Method,
                _ => null,
            };

            if (target is null)
            {
                continue;
            }

            if (matcher.IsAssertion(target))
            {
                assertsDirectly = true;
                break;
            }

            if (SourceCallee(target) is { } callee)
            {
                callees.Add(callee);
            }
        }

        methods.TryAdd(
            owner.OriginalDefinition,
            new MethodFacts(
                isTest: owner.MethodKind == MethodKind.Ordinary && XunitFacts.IsTestMethod(owner),
                assertsDirectly,
                callees));
    }

    private static void ReportUnasserted(
        CompilationAnalysisContext context, ConcurrentDictionary<IMethodSymbol, MethodFacts> methods)
    {
        var asserting = FindAsserting(methods);
        foreach (var pair in methods)
        {
            if (pair.Value.IsTest && !asserting.Contains(pair.Key))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, pair.Key.Locations[0], pair.Key.Name));
            }
        }
    }

    // A method asserts when it calls an assertion itself, or calls a source method that does, at any
    // depth. Worked backwards from the direct asserters along reversed call edges, so each edge is
    // walked once however many tests share a helper, and a recursive helper cannot loop.
    private static HashSet<IMethodSymbol> FindAsserting(ConcurrentDictionary<IMethodSymbol, MethodFacts> methods)
    {
        var callers = new Dictionary<IMethodSymbol, List<IMethodSymbol>>(SymbolEqualityComparer.Default);
        foreach (var pair in methods)
        {
            foreach (var callee in pair.Value.Callees)
            {
                if (!callers.TryGetValue(callee, out var list))
                {
                    list = [];
                    callers[callee] = list;
                }

                list.Add(pair.Key);
            }
        }

        var asserting = new HashSet<IMethodSymbol>(
            methods.Where(pair => pair.Value.AssertsDirectly).Select(pair => pair.Key),
            SymbolEqualityComparer.Default);
        var pending = new Stack<IMethodSymbol>(asserting);
        while (pending.Count > 0)
        {
            if (!callers.TryGetValue(pending.Pop(), out var list))
            {
                continue;
            }

            foreach (var caller in list.Where(asserting.Add))
            {
                pending.Push(caller);
            }
        }

        return asserting;
    }

    // Only methods with a body in this compilation can be followed. Local functions and lambdas are
    // already inside the body that declares them.
    private static IMethodSymbol? SourceCallee(IMethodSymbol target)
    {
        var method = (target.ReducedFrom ?? target).OriginalDefinition;
        return method.MethodKind is MethodKind.LocalFunction or MethodKind.AnonymousFunction
            || method.DeclaringSyntaxReferences.IsEmpty
                ? null
                : method;
    }

    // Per-file options first so a repo can scope the setting in .editorconfig, then the global
    // config. Matchers are cached by the raw option value, which is the same for nearly every file.
    private static AssertionMatcher MatcherFor(
        OperationBlockAnalysisContext block, ConcurrentDictionary<string, AssertionMatcher> matchers)
    {
        var provider = block.Options.AnalyzerConfigOptionsProvider;
        var options = block.OwningSymbol.Locations.FirstOrDefault()?.SourceTree is { } tree
            ? provider.GetOptions(tree)
            : provider.GlobalOptions;
        var configured = options.TryGetValue(AssertionMethodsOption, out var value) ? value : string.Empty;
        return matchers.GetOrAdd(configured, AssertionMatcher.Create);
    }

    private sealed class MethodFacts(bool isTest, bool assertsDirectly, List<IMethodSymbol> callees)
    {
        public bool IsTest { get; } = isTest;

        public bool AssertsDirectly { get; } = assertsDirectly;

        public List<IMethodSymbol> Callees { get; } = callees;
    }

    private sealed class AssertionMatcher(Pattern[] patterns)
    {
        public static AssertionMatcher Create(string configured)
            => new([.. DefaultAssertionMethods
                .Concat(configured.Split(','))
                .Select(entry => entry.Trim())
                .Where(entry => entry.Length > 0)
                .Select(Pattern.Parse)]);

        // An override counts when the method it overrides would: a derived harness that overrides
        // RunAsync still asserts.
        public bool IsAssertion(IMethodSymbol method)
        {
            for (var current = method.ReducedFrom ?? method; current is not null; current = current.OverriddenMethod)
            {
                if (patterns.Any(pattern => pattern.Matches(current)))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed class Pattern(string? containingNamespace, string? typeName, string methodName, bool isPrefix)
    {
        public static Pattern Parse(string entry)
        {
            var methodStart = entry.LastIndexOf('.');
            var method = entry.Substring(methodStart + 1);
            var owner = methodStart < 0 ? null : entry.Substring(0, methodStart);
            var typeStart = owner?.LastIndexOf('.') ?? -1;
            var isPrefix = method.EndsWith("*", StringComparison.Ordinal);
            return new Pattern(
                containingNamespace: typeStart < 0 ? null : owner!.Substring(0, typeStart),
                typeName: owner?.Substring(typeStart + 1),
                methodName: isPrefix ? method.Substring(0, method.Length - 1) : method,
                isPrefix);
        }

        public bool Matches(IMethodSymbol method)
        {
            var nameMatches = isPrefix
                ? method.Name.StartsWith(methodName, StringComparison.Ordinal)
                : string.Equals(method.Name, methodName, StringComparison.Ordinal);
            if (!nameMatches)
            {
                return false;
            }

            if (typeName is null)
            {
                return true;
            }

            var type = method.ContainingType;
            return type is not null
                && string.Equals(type.Name, typeName, StringComparison.Ordinal)
                && (containingNamespace is null
                    || string.Equals(
                        type.ContainingNamespace?.ToDisplayString(), containingNamespace, StringComparison.Ordinal));
        }
    }
}
