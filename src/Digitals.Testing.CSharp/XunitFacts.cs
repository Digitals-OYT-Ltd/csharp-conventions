using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Digitals.Testing.CSharp;

/// <summary>
/// Recognising xUnit shapes from symbols. Matching on names rather than on a referenced
/// <c>xunit.v3</c> assembly keeps the analyzers working in an analyzer test harness, where the
/// snippet declares its own stub attributes.
/// </summary>
internal static class XunitFacts
{
    /// <summary>
    /// True when the method carries [Fact] or [Theory], or an attribute derived from either.
    /// </summary>
    internal static bool IsTestMethod(IMethodSymbol method)
    {
        foreach (var attributeClass in method.GetAttributes().Select(attribute => attribute.AttributeClass))
        {
            for (var type = attributeClass; type is not null; type = type.BaseType)
            {
                if (type.Name is "FactAttribute" or "TheoryAttribute" && IsXunit(type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The value of the first [Trait(name, value)] whose name matches, looking at the method, then
    /// at its containing type and that type's bases, which is how xUnit itself resolves traits.
    /// Returns null when there is none.
    /// </summary>
    internal static string? FindTraitValue(IMethodSymbol method, string traitName)
    {
        var fromMethod = FindTraitValue(method.GetAttributes(), traitName);
        if (fromMethod is not null)
        {
            return fromMethod;
        }

        for (var type = method.ContainingType; type is not null; type = type.BaseType)
        {
            var fromType = FindTraitValue(type.GetAttributes(), traitName);
            if (fromType is not null)
            {
                return fromType;
            }
        }

        return null;
    }

    private static string? FindTraitValue(ImmutableArray<AttributeData> attributes, string traitName)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeClass is not { Name: "TraitAttribute" } attributeClass
                || !IsXunit(attributeClass)
                || attribute.ConstructorArguments.Length < 2)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is string name
                && string.Equals(name, traitName, StringComparison.Ordinal))
            {
                // An empty value is not a trait anyone can filter on, so it counts as absent.
                return attribute.ConstructorArguments[1].Value as string is { Length: > 0 } value
                    ? value
                    : null;
            }
        }

        return null;
    }

    private static bool IsXunit(INamedTypeSymbol type)
        => type.ContainingNamespace is { Name: "Xunit", ContainingNamespace.IsGlobalNamespace: true };
}
