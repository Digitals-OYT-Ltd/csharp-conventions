using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace Digitals.Testing.CSharp.Tests;

/// <summary>
/// An IDE opens a rule's help link when the ID is clicked, so each DT rule's link has to land on
/// its own section of docs/rules/testing.md. A renamed heading breaks the link with no warning.
/// </summary>
[Trait("Category", "Unit")]
public sealed partial class RuleDocsTests
{
    private const string Repo = "https://github.com/Digitals-OYT-Ltd/csharp-conventions";
    private const string RulesPage = Repo + "/blob/main/docs/rules/testing.md";

    [GeneratedRegex(@"^###\s+`(?<id>DT\d{5})`", RegexOptions.Multiline)]
    private static partial Regex RuleHeading();

    [Fact]
    public void HelpLink_EveryRule_PointsAtItsOwnSection()
    {
        var wrong = Descriptors()
            .Where(rule => rule.HelpLinkUri != $"{RulesPage}#{rule.Id.ToLowerInvariant()}")
            .Select(rule => $"{rule.Id}: '{rule.HelpLinkUri}'")
            .ToList();

        Assert.Empty(wrong);
    }

    [Fact]
    public void RulesPage_EveryRule_HasASection()
    {
        var documented = RuleHeading().Matches(Page()).Select(match => match.Groups["id"].Value).ToHashSet();

        var missing = Descriptors().Select(rule => rule.Id).Where(id => !documented.Contains(id)).ToList();

        Assert.Empty(missing);
    }

    private static List<DiagnosticDescriptor> Descriptors() =>
        [.. typeof(TestNamingAnalyzer).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(DiagnosticAnalyzer).IsAssignableFrom(type))
            .Select(type => (DiagnosticAnalyzer)Activator.CreateInstance(type)!)
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .DistinctBy(rule => rule.Id)];

    private static string Page()
    {
        using var stream = typeof(RuleDocsTests).Assembly.GetManifestResourceStream("rules.md")
            ?? throw new InvalidOperationException("docs/rules/testing.md is not embedded; see the test csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
