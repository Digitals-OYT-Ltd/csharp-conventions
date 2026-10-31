using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00003 — 'new HttpClient()' (language-rules.md#require-dispose-owned-resources):
/// a client per call exhausts sockets; use IHttpClientFactory (AddHttpClient).
/// Own ID, not RS0030: an interop #pragma must never disable the clock bans.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HttpClientCreationAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00003 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00003",
        title: "Do not construct HttpClient directly",
        messageFormat: "Use IHttpClientFactory (AddHttpClient) — a new HttpClient per call exhausts sockets",
        category: "Digitals.Http",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00003");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        // Operation-based so target-typed 'HttpClient c = new();' is caught too.
        context.RegisterOperationAction(AnalyzeCreation, OperationKind.ObjectCreation);
    }

    private static void AnalyzeCreation(OperationAnalysisContext context)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is
            {
                Name: "HttpClient",
                ContainingNamespace:
                {
                    Name: "Http",
                    ContainingNamespace:
                    {
                        Name: "Net",
                        ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true },
                    },
                },
            })
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, creation.Syntax.GetLocation()));
        }
    }
}
