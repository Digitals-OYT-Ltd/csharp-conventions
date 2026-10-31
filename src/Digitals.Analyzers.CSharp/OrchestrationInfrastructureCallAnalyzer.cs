using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00018 — infrastructure API called from an orchestration method
/// (code-design.md#dependencies-point-inward): a method that coordinates collaborators and also
/// calls EF Core, HttpClient, a storage or messaging SDK, or System.IO directly reaches past its
/// ports, and its body drops from use-case steps to wire-protocol detail.
///
/// Reported once per infrastructure namespace per method, at the first call, object creation or
/// property read whose declaring type lives under one of the prefixes. The default list covers
/// data access, HTTP, storage, messaging, mail and file I/O; 'digitals_infrastructure_namespaces'
/// (comma-separated prefixes) replaces it. System.IO.Path is exempt: it is string manipulation.
/// An object created as the method's return value ('return new MemoryStream(bytes);') is exempt
/// too: handing back the result in the shape the caller asked for is not a step of the recipe.
/// Only orchestration methods are checked (see <see cref="OrchestrationMethods"/>); the adapters
/// and helpers below them are where these calls belong.
/// A type that holds a client of an area itself, as a field or a constructor parameter (a
/// ServiceBusClient field, an injected DbContext), is that area's adapter: its calls into the area
/// are its job and are not reported, while a reach into any other area still is. This reads the
/// types a class depends on, never its namespace or project, so it holds for any solution layout.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OrchestrationInfrastructureCallAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The DT00018 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00018",
        title: "Do not call infrastructure directly from an orchestration method",
        messageFormat: "'{0}' coordinates other calls and also uses {1} directly — move that call into a named helper or an adapter class",
        category: "Digitals.Design",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00018",
        description: "An orchestration method that also calls data access, HTTP, storage, messaging, mail or file APIs"
            + " directly mixes wire-level detail into its steps. Reported once per API area per method. Not reported:"
            + " System.IO.Path, an object created as the return value, and an area whose client the type holds itself"
            + " as a field or constructor parameter (an injected DbContext or HttpClient makes the type that area's"
            + " adapter). The areas are namespace prefixes; digitals_infrastructure_namespaces replaces the default"
            + " list."
            + OrchestrationMethods.Definition);

    private const string NamespacesOption = "digitals_infrastructure_namespaces";

    private static readonly ImmutableArray<string> DefaultPrefixes = ImmutableArray.Create(
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.SqlClient",
        "System.Data",
        "Dapper",
        "Npgsql",
        "MongoDB.Driver",
        "Microsoft.Azure.Cosmos",
        "StackExchange.Redis",
        "System.Net.Http",
        "Azure.Storage",
        "Azure.Messaging",
        "Azure.Data",
        "Amazon",
        "RabbitMQ.Client",
        "Confluent.Kafka",
        "MailKit",
        "MimeKit",
        "SendGrid",
        "System.Net.Mail",
        "System.IO");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationBlockStartAction(StartBlock);
    }

    private static void StartBlock(OperationBlockStartAnalysisContext context)
    {
        if (context.OwningSymbol is not IMethodSymbol method
            || method.DeclaringSyntaxReferences.Length == 0)
        {
            return;
        }

        // Most methods touch no infrastructure, so the collaborator count runs only once one does.
        var tree = method.DeclaringSyntaxReferences[0].SyntaxTree;
        var blocks = context.OperationBlocks;
        var options = context.Options;
        var orchestration = new Lazy<bool>(() => OrchestrationMethods.IsOrchestration(method, blocks, options, tree));
        var prefixes = Prefixes(context.Options, tree);
        var reported = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        foreach (var owned in OwnedAreas(method.ContainingType, prefixes))
        {
            reported.TryAdd(owned, true);
        }

        context.RegisterOperationAction(
            operationContext => AnalyzeOperation(operationContext, method, orchestration, prefixes, reported),
            OperationKind.Invocation,
            OperationKind.ObjectCreation,
            OperationKind.PropertyReference);
    }

    private static void AnalyzeOperation(
        OperationAnalysisContext context,
        IMethodSymbol method,
        Lazy<bool> orchestration,
        ImmutableArray<string> prefixes,
        ConcurrentDictionary<string, bool> reported)
    {
        var declaringType = context.Operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod.ContainingType,
            IObjectCreationOperation creation => creation.Constructor?.ContainingType,
            IPropertyReferenceOperation property => property.Property.ContainingType,
            _ => null,
        };

        if (declaringType is null
            || IsSystemIOPath(declaringType)
            || (context.Operation is IObjectCreationOperation && IsReturnedValue(context.Operation)))
        {
            return;
        }

        if (MatchingPrefix(declaringType, prefixes) is { } prefix
            && orchestration.Value
            && reported.TryAdd(prefix, true))
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Rule, context.Operation.Syntax.GetLocation(), method.Name, $"{declaringType.Name} ({prefix})"));
        }
    }

    // The areas whose clients the type holds: field types and constructor parameter types (which
    // covers primary constructors), matched on the type or any base type ('AppDbContext : DbContext').
    private static IEnumerable<string> OwnedAreas(INamedTypeSymbol type, ImmutableArray<string> prefixes)
    {
        var dependencies = new List<ITypeSymbol>();
        foreach (var member in type.GetMembers())
        {
            switch (member)
            {
                case IFieldSymbol { IsStatic: false } field:
                    dependencies.Add(field.Type);
                    break;

                case IMethodSymbol { MethodKind: MethodKind.Constructor, IsStatic: false } constructor:
                    foreach (var parameter in constructor.Parameters)
                    {
                        dependencies.Add(parameter.Type);
                    }

                    break;

                default:
                    break;
            }
        }

        foreach (var dependency in dependencies)
        {
            for (var current = dependency; current is not null; current = current.BaseType)
            {
                var prefix = MatchingPrefix(current, prefixes);
                if (prefix is not null)
                {
                    yield return prefix;
                }
            }
        }
    }

    private static bool IsSystemIOPath(ITypeSymbol type) =>
        type.Name == "Path" && type.ContainingNamespace?.ToDisplayString() == "System.IO";

    private static string? MatchingPrefix(ITypeSymbol type, ImmutableArray<string> prefixes)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return prefixes.FirstOrDefault(prefix =>
            ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal));
    }

    // The operation is what the method returns, possibly through a conversion or a conditional.
    private static bool IsReturnedValue(IOperation operation)
    {
        for (var parent = operation.Parent; parent is not null; parent = parent.Parent)
        {
            switch (parent)
            {
                case IReturnOperation:
                    return true;

                case IConversionOperation or IConditionalOperation or ICoalesceOperation:
                    continue;

                default:
                    return false;
            }
        }

        return false;
    }

    private static ImmutableArray<string> Prefixes(AnalyzerOptions options, SyntaxTree tree)
    {
        if (!options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(NamespacesOption, out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return DefaultPrefixes;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var part in raw.Split(','))
        {
            var prefix = part.Trim();
            if (prefix.Length > 0)
            {
                builder.Add(prefix);
            }
        }

        return builder.ToImmutable();
    }
}
