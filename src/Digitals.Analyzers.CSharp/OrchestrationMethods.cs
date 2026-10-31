using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// Recognises orchestration methods: the ones that should read as a use-case recipe.
/// Shared by DT00014, DT00015, DT00017 and DT00018.
///
/// A method is one when it calls into two or more of its type's collaborators. That is what
/// coordinating means, so it holds for a MediatR handler, a plain service, a controller, a worker or
/// a console command alike, without knowing any framework. A collaborator is
///   - a field of the type (or of a base type declared in the same source),
///   - a primary-constructor parameter of the type, or
///   - a property of the type whose type is an interface ('[Inject] IOrderService Orders'),
/// whose type is a class or interface, not a string or collection, and not plumbing. Plumbing is
/// logging, options, configuration, the DI container, TimeProvider, System.Threading and
/// System.Diagnostics by default; 'digitals_orchestration_ignored_collaborators' (comma-separated
/// namespaces or type names) replaces that list. "Calls into" means the collaborator is the
/// receiver of a call or member access ('_db.Orders', 'email.SendAsync(...)'); passing it on as an
/// argument does not count.
///
/// So a repository that only touches its DbContext, a helper that only calls the blob client, and
/// a mapper with no collaborators are not orchestration, which is where the detail they hold belongs.
///
/// 'digitals_orchestration_min_collaborators' (default 2) sets the count. 'digitals_orchestration_types'
/// (comma-separated metadata names) marks any method that overrides or implements a member of the
/// listed types as orchestration regardless of the count, for a template that wants its job base
/// class or handler interface judged even when a handler calls one collaborator: 'Acme.Jobs.JobProcessor`1'.
/// </summary>
internal static class OrchestrationMethods
{
    /// <summary>What an orchestration method is, for the rule descriptions: they must read without the docs.</summary>
    public const string Definition =
        " An orchestration method is one that calls two or more of its type's collaborators: fields,"
        + " primary-constructor parameters or interface-typed properties, not counting logging, options,"
        + " configuration, the DI container, TimeProvider, System.Threading or System.Diagnostics. Tune it with"
        + " digitals_orchestration_min_collaborators and digitals_orchestration_ignored_collaborators.";

    private const string TypesOption = "digitals_orchestration_types";
    private const string MinCollaboratorsOption = "digitals_orchestration_min_collaborators";
    private const string IgnoredOption = "digitals_orchestration_ignored_collaborators";

    private static readonly ImmutableArray<string> DefaultIgnored = ImmutableArray.Create(
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.DependencyInjection",
        "System.IServiceProvider",
        "System.TimeProvider",
        "System.Threading",
        "System.Diagnostics");

    /// <summary>Whether the method <paramref name="declaration"/> declares is an orchestration method.</summary>
    public static bool IsOrchestration(
        SemanticModel model,
        MethodDeclarationSyntax declaration,
        AnalyzerOptions options,
        CancellationToken cancellationToken) =>
        model.GetDeclaredSymbol(declaration, cancellationToken) is { } method
        && model.GetOperation(declaration, cancellationToken) is { } body
        && IsOrchestration(method, new[] { body }, options, declaration.SyntaxTree);

    /// <summary>
    /// Whether <paramref name="method"/>, whose body is <paramref name="body"/>, is an orchestration method.
    /// </summary>
    public static bool IsOrchestration(
        IMethodSymbol method,
        IEnumerable<IOperation> body,
        AnalyzerOptions options,
        SyntaxTree tree)
    {
        if (method.MethodKind != MethodKind.Ordinary || method.IsAbstract)
        {
            return false;
        }

        var config = options.AnalyzerConfigOptionsProvider.GetOptions(tree);
        var configured = ReadList(config, TypesOption, []);
        if (configured.Length > 0
            && (OverridesConfigured(method, configured) || ImplementsConfigured(method, configured)))
        {
            return true;
        }

        var min = AnalyzerOptionReader.PositiveInt(options, tree, MinCollaboratorsOption, 2);
        var ignored = ReadList(config, IgnoredOption, DefaultIgnored);
        var called = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var root in body)
        {
            foreach (var operation in root.DescendantsAndSelf())
            {
                if (ReferencedMember(operation) is { } symbol
                    && IsReceiver(operation)
                    && CollaboratorType(symbol, method.ContainingType) is { } type
                    && IsService(type, ignored)
                    && called.Add(symbol)
                    && called.Count >= min)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static ISymbol? ReferencedMember(IOperation operation) => operation switch
    {
        IFieldReferenceOperation field => field.Field,
        IPropertyReferenceOperation property => property.Property,
        IParameterReferenceOperation parameter => parameter.Parameter,
        _ => null,
    };

    // 'x.Member', 'x.Call()', 'x?.Call()', or the receiver of an extension call ('x.Orders' feeding 'Where').
    private static bool IsReceiver(IOperation reference)
    {
        switch (reference.Parent)
        {
            case IMemberReferenceOperation member when member.Instance == reference:
            case IInvocationOperation invocation when invocation.Instance == reference:
            case IConditionalAccessOperation conditional when conditional.Operation == reference:
                return true;

            default:
                break;
        }

        var current = reference;
        while (current.Parent is IConversionOperation)
        {
            current = current.Parent;
        }

        return current.Parent is IArgumentOperation { Parameter.Ordinal: 0 } argument
            && argument.Parent is IInvocationOperation { TargetMethod.IsExtensionMethod: true };
    }

    private static ITypeSymbol? CollaboratorType(ISymbol symbol, INamedTypeSymbol type) => symbol switch
    {
        IFieldSymbol { IsConst: false } field when IsOwnMember(field, type) => field.Type,
        IPropertySymbol { Type.TypeKind: TypeKind.Interface } property
            when IsOwnMember(property, type) => property.Type,
        IParameterSymbol { ContainingSymbol: IMethodSymbol { MethodKind: MethodKind.Constructor } ctor } parameter
            when SymbolEqualityComparer.Default.Equals(ctor.ContainingType, type) => parameter.Type,
        _ => null,
    };

    // Declared in source on the type or one of its base types; ControllerBase.User and other
    // framework base members are not the type's collaborators.
    private static bool IsOwnMember(ISymbol member, INamedTypeSymbol type)
    {
        if (member.Locations.IsEmpty || !member.Locations[0].IsInSource)
        {
            return false;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(member.ContainingType, current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsService(ITypeSymbol type, ImmutableArray<string> ignored)
    {
        if (type.TypeKind is not (TypeKind.Class or TypeKind.Interface)
            || type.SpecialType != SpecialType.None
            || IsEnumerable(type))
        {
            return false;
        }

        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        var fullName = ns.Length == 0 ? type.Name : ns + "." + type.Name;
        return !ignored.Any(entry =>
            ns == entry || ns.StartsWith(entry + ".", StringComparison.Ordinal) || fullName == entry);
    }

    private static bool IsEnumerable(ITypeSymbol type)
    {
        if (type.OriginalDefinition.SpecialType
            is SpecialType.System_Collections_IEnumerable
            or SpecialType.System_Collections_Generic_IEnumerable_T)
        {
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.SpecialType == SpecialType.System_Collections_IEnumerable)
            {
                return true;
            }
        }

        return false;
    }

    private static bool OverridesConfigured(IMethodSymbol method, ImmutableArray<string> configured)
    {
        for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (configured.Contains(MetadataName(overridden.ContainingType)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsConfigured(IMethodSymbol method, ImmutableArray<string> configured)
    {
        var type = method.ContainingType;
        foreach (var @interface in type.AllInterfaces)
        {
            if (!configured.Contains(MetadataName(@interface)))
            {
                continue;
            }

            if (@interface.GetMembers().Any(member => member is IMethodSymbol
                && SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(member), method)))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<string> ReadList(
        AnalyzerConfigOptions config,
        string key,
        ImmutableArray<string> fallback)
    {
        if (!config.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var part in value.Split(','))
        {
            var name = part.Trim();
            if (name.Length > 0)
            {
                builder.Add(name);
            }
        }

        return builder.ToImmutable();
    }

    // Namespace-qualified metadata name ('Acme.Jobs.JobProcessor`1'), which is what a repo writes in the option.
    private static string MetadataName(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        var parts = new Stack<string>();
        parts.Push(definition.MetadataName);
        for (var outer = definition.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            parts.Push(outer.MetadataName);
        }

        for (var ns = definition.ContainingNamespace; ns is { IsGlobalNamespace: false }; ns = ns.ContainingNamespace)
        {
            parts.Push(ns.Name);
        }

        return string.Join(".", parts);
    }
}
