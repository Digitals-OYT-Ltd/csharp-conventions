using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00010 — negative boolean name (code-style.md#names-keep-a-promise): 'IsNotEligible' makes
/// every caller parse '!IsNotEligible', a double negative. Fires on a bool (or bool?) field,
/// property, parameter, local, or bool-returning method whose name has a negating word. Matching
/// is per PascalCase/camelCase word, so 'NoteId', 'Nonce', and 'IsNotified' stay clean.
/// Overrides and interface implementations are skipped: the name belongs to the declaration they
/// satisfy, which is reported where it is declared (or can't be renamed at all). Compiler-made
/// symbols are skipped: '&lt;Main&gt;$' and its 'args' for top-level statements, record members. They
/// have no source location to report on, and nobody chose their names.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NegativeBooleanNameAnalyzer : DiagnosticAnalyzer
{
    private static readonly string[] NegatingWords = ["not", "no", "non", "cannot", "dont"];

    /// <summary>The DT00010 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00010",
        title: "Boolean name must be affirmative",
        messageFormat: "'{0}' is a negative boolean — name the positive condition instead, so no caller has to read a double negative",
        category: "Digitals.Naming",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        context.RegisterSymbolAction(AnalyzeProperty, SymbolKind.Property);
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterOperationAction(AnalyzeLocal, OperationKind.VariableDeclarator);
        // Local functions never reach a Method symbol action, so they need their own hook.
        context.RegisterOperationAction(AnalyzeLocalFunction, OperationKind.LocalFunction);
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;
        Check(context.ReportDiagnostic, field, field.Type);
    }

    private static void AnalyzeProperty(SymbolAnalysisContext context)
    {
        var property = (IPropertySymbol)context.Symbol;
        // Name and type first: they rule out almost every property, and the interface lookup is
        // the expensive check.
        if (!IsNegativeBoolean(property, property.Type)
            || property.IsOverride
            || ImplementsInterfaceMember(property)
            || IsPositionalRecordProperty(property))
        {
            return;
        }

        Check(context.ReportDiagnostic, property, property.Type);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;

        // Accessor and operator parameters are compiler-named ('value') or fixed by the language.
        if (method.IsImplicitlyDeclared
            || method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)
            || !HasNegativeBoolean(method)
            || method.IsOverride
            || ImplementsInterfaceMember(method))
        {
            return;
        }

        CheckMethod(context.ReportDiagnostic, method);
    }

    private static void AnalyzeLocal(OperationAnalysisContext context)
    {
        var local = ((IVariableDeclaratorOperation)context.Operation).Symbol;
        Check(context.ReportDiagnostic, local, local.Type);
    }

    private static void AnalyzeLocalFunction(OperationAnalysisContext context) =>
        CheckMethod(context.ReportDiagnostic, ((ILocalFunctionOperation)context.Operation).Symbol);

    private static bool HasNegativeBoolean(IMethodSymbol method)
    {
        if (ChecksReturn(method) && IsNegativeBoolean(method, method.ReturnType))
        {
            return true;
        }

        return method.Parameters.Any(parameter => IsNegativeBoolean(parameter, parameter.Type));
    }

    private static bool ChecksReturn(IMethodSymbol method) =>
        method.MethodKind is MethodKind.Ordinary or MethodKind.LocalFunction;

    private static void CheckMethod(Action<Diagnostic> report, IMethodSymbol method)
    {
        if (ChecksReturn(method))
        {
            Check(report, method, method.ReturnType);
        }

        foreach (var parameter in method.Parameters)
        {
            Check(report, parameter, parameter.Type);
        }
    }

    // Guards the location as well as the flag: a synthesized symbol has an empty Locations array,
    // and indexing it is what crashed on '<Main>$'s 'args' (AD0001, 0.9.0 and 0.10.0).
    private static void Check(Action<Diagnostic> report, ISymbol symbol, ITypeSymbol type)
    {
        if (IsNegativeBoolean(symbol, type) && !symbol.IsImplicitlyDeclared && !symbol.Locations.IsEmpty)
        {
            report(Diagnostic.Create(Rule, symbol.Locations[0], symbol.Name));
        }
    }

    private static bool IsNegativeBoolean(ISymbol symbol, ITypeSymbol type) =>
        IsBoolean(type) && HasNegatingWord(symbol.Name);

    private static bool IsBoolean(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Boolean
        || (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable
            && nullable.TypeArguments[0].SpecialType == SpecialType.System_Boolean);

    // A positional record parameter declares both a parameter and a property at the same
    // location. The parameter is reported; reporting the property too would double it.
    private static bool IsPositionalRecordProperty(IPropertySymbol property) =>
        property.DeclaringSyntaxReferences.Length > 0
        && property.DeclaringSyntaxReferences[0].GetSyntax() is ParameterSyntax;

    private static bool ImplementsInterfaceMember(ISymbol member)
    {
        if (member is IMethodSymbol { ExplicitInterfaceImplementations.Length: > 0 }
            or IPropertySymbol { ExplicitInterfaceImplementations.Length: > 0 })
        {
            return true;
        }

        var type = member.ContainingType;
        return type.AllInterfaces.Any(@interface => @interface.GetMembers(member.Name).Any(interfaceMember =>
            SymbolEqualityComparer.Default.Equals(type.FindImplementationForInterfaceMember(interfaceMember), member)));
    }

    // Walks the words in place: 'isNotEligible' -> is, Not, Eligible; 'HTTPNotFound' -> HTTP, Not,
    // Found; '_noRetry' -> no, Retry. Runs on every bool name, so it allocates nothing.
    private static bool HasNegatingWord(string name)
    {
        var start = 0;
        while (start < name.Length && name[start] == '_')
        {
            start++;
        }

        for (var i = start + 1; i < name.Length; i++)
        {
            if (StartsWord(name, i))
            {
                if (i > start && IsNegatingWord(name, start, i - start))
                {
                    return true;
                }

                start = name[i] == '_' ? i + 1 : i;
            }
        }

        return start < name.Length && IsNegatingWord(name, start, name.Length - start);
    }

    // An underscore, a capital after a lower-case letter or digit, or the last capital of an
    // acronym that runs into a word ('HTTPNotFound' starts 'Not' at the N).
    private static bool StartsWord(string name, int i)
    {
        var current = name[i];
        var previous = name[i - 1];
        return current == '_'
            || (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
            || (char.IsUpper(current) && char.IsUpper(previous) && i + 1 < name.Length && char.IsLower(name[i + 1]));
    }

    private static bool IsNegatingWord(string name, int start, int length)
    {
        foreach (var word in NegatingWords)
        {
            if (word.Length == length
                && string.Compare(name, start, word, 0, length, System.StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
