using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Testing.CSharp;

/// <summary>
/// DT10003 — every test carries a category trait. CI selects what to run by trait
/// (<c>dotnet test -- --filter-trait "Category=Unit"</c>), so a test with no trait runs in no job
/// at all: that is not a failure, it is an absence, and nothing else reports it.
/// The set of values is deliberately open. A repo using Unit, Integration and Manual is as valid
/// as one using two, and this rule has no business knowing which. Pin the set per repo with
/// <c>digitals_testing.allowed_trait_values</c> when you want one.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TestTraitAnalyzer : DiagnosticAnalyzer
{
    /// <summary>Configures the trait name the rule requires. Defaults to <c>Category</c>.</summary>
    public const string TraitNameOption = "digitals_testing.required_trait";

    /// <summary>
    /// Optional comma-separated allow-list for the trait's value. Unset means any value passes,
    /// which is the default: only the presence of the trait is org policy.
    /// </summary>
    public const string AllowedValuesOption = "digitals_testing.allowed_trait_values";

    private const string DefaultTraitName = "Category";

    /// <summary>The DT10003 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT10003",
        title: "Test must carry a category trait",
        messageFormat: "Test '{0}' {1}",
        category: "Digitals.Testing",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/testing.md#dt10003");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method.MethodKind != MethodKind.Ordinary || !XunitFacts.IsTestMethod(method))
        {
            return;
        }

        var location = method.Locations[0];
        var options = GetOptions(context, location);
        var traitName = Read(options, TraitNameOption) is { Length: > 0 } configured
            ? configured
            : DefaultTraitName;

        var value = XunitFacts.FindTraitValue(method, traitName);
        if (value is null)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                location,
                method.Name,
                $"has no [Trait(\"{traitName}\", ...)], so no CI filter selects it and it runs nowhere"));
            return;
        }

        var permitted = ParseAllowed(Read(options, AllowedValuesOption));
        if (permitted.Length > 0 && !permitted.Contains(value, StringComparer.Ordinal))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                location,
                method.Name,
                $"has [Trait(\"{traitName}\", \"{value}\")], which is not one of the values this repo allows "
                    + $"({string.Join(", ", permitted)})"));
        }
    }

    private static string[] ParseAllowed(string? configured)
        => configured is not { Length: > 0 }
            ? []
            : [.. configured.Split(',').Select(entry => entry.Trim()).Where(entry => entry.Length > 0)];

    // Per-file options first so a repo can scope the setting in .editorconfig, then the global
    // config, which is how the packaged globalconfig would set it for everyone.
    private static AnalyzerConfigOptions GetOptions(SymbolAnalysisContext context, Location location)
        => location.SourceTree is { } tree
            ? context.Options.AnalyzerConfigOptionsProvider.GetOptions(tree)
            : context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;

    private static string? Read(AnalyzerConfigOptions options, string key)
        => options.TryGetValue(key, out var value) ? value : null;
}
