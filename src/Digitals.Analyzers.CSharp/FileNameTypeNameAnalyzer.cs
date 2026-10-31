using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>
/// DT00012 — file name must match the type it declares (code-style.md#file--type-organisation):
/// a reader looking for 'Booking' opens 'Booking.cs'. Passes when any top-level type in the file
/// matches, so a record that shares its file with its validator is fine. The file name is read up to
/// the first '.', '{' or '`', which accepts partial-class files ('Booking.Validation.cs') and both
/// generic spellings ('Result{T}.cs', 'Result`1.cs'). Skipped: files with no type, files with
/// top-level statements (Program.cs keeps its helper types), and 'file'-scoped types, which no other
/// file can name anyway. Generated code is exempt through ConfigureGeneratedCodeAnalysis.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileNameTypeNameAnalyzer : DiagnosticAnalyzer
{
    private static readonly char[] NameTerminators = ['.', '{', '`'];

    /// <summary>The DT00012 descriptor.</summary>
    public static readonly DiagnosticDescriptor Rule = new(
        id: "DT00012",
        title: "File name must match the type it declares",
        messageFormat: "File '{0}' declares '{1}' — rename the file to '{1}.cs' so a reader finds the type by its name",
        category: "Digitals.Style",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        helpLinkUri: "https://github.com/Digitals-OYT-Ltd/csharp-conventions/blob/main/docs/rules/analyzers.md#dt00012");

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Rule);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var fileName = Path.GetFileName(context.Tree.FilePath);
        if (string.IsNullOrEmpty(fileName))
        {
            return;
        }

        var root = (CompilationUnitSyntax)context.Tree.GetRoot(context.CancellationToken);
        if (root.Members.Any(member => member is GlobalStatementSyntax))
        {
            return;
        }

        var types = TopLevelTypes(root.Members)
            .Where(type => !type.Modifiers.Any(SyntaxKind.FileKeyword))
            .ToList();
        if (types.Count == 0)
        {
            return;
        }

        var end = fileName.IndexOfAny(NameTerminators);
        var expected = end < 0 ? fileName : fileName.Substring(0, end);
        if (types.Any(type => Identifier(type).ValueText == expected))
        {
            return;
        }

        var first = Identifier(types[0]);
        context.ReportDiagnostic(Diagnostic.Create(Rule, first.GetLocation(), fileName, first.ValueText));
    }

    private static IEnumerable<MemberDeclarationSyntax> TopLevelTypes(SyntaxList<MemberDeclarationSyntax> members)
    {
        foreach (var member in members)
        {
            switch (member)
            {
                case BaseNamespaceDeclarationSyntax @namespace:
                    foreach (var type in TopLevelTypes(@namespace.Members))
                    {
                        yield return type;
                    }

                    break;

                case BaseTypeDeclarationSyntax or DelegateDeclarationSyntax:
                    yield return member;
                    break;

                default:
                    break;
            }
        }
    }

    private static SyntaxToken Identifier(MemberDeclarationSyntax type) => type switch
    {
        BaseTypeDeclarationSyntax declaration => declaration.Identifier,
        DelegateDeclarationSyntax @delegate => @delegate.Identifier,
        _ => throw new InvalidOperationException($"Not a type declaration: {type.Kind()}"),
    };
}
