using Xunit;

namespace Digitals.Analyzers.CSharp.Tests;

/// <summary>
/// The shape of the packaged globalconfig. Roslyn reports none of these as an error: a section
/// header drops every entry after it (the whole severity policy), an unknown severity value or a
/// duplicated ID is ignored in silence, and a missing is_global turns the file into an ordinary
/// editorconfig that applies to nothing. Each is a footgun AGENTS.md documents from experience.
/// </summary>
[Trait("Category", "Unit")]
public sealed class GlobalConfigTests
{
    private static readonly string[] KnownSeverities = ["none", "silent", "suggestion", "warning", "error", "default"];

    [Fact]
    public void GlobalConfig_Header_IsGlobalAtLevel100()
    {
        var lines = GlobalConfig.Lines();

        Assert.Contains("is_global = true", lines);
        Assert.Contains("global_level = 100", lines);
    }

    [Fact]
    public void GlobalConfig_SectionHeaders_None()
    {
        var sections = GlobalConfig.Lines().Where(line => line.StartsWith('[')).ToList();

        Assert.Empty(sections);
    }

    [Fact]
    public void GlobalConfig_Severities_AreKnownValues()
    {
        var unknown = GlobalConfig.Severities()
            .Where(entry => !KnownSeverities.Contains(entry.Severity))
            .Select(entry => $"{entry.Id} = {entry.Severity}")
            .ToList();

        Assert.Empty(unknown);
    }

    [Fact]
    public void GlobalConfig_DiagnosticIds_AreUnique()
    {
        var duplicates = GlobalConfig.Severities()
            .GroupBy(entry => entry.Id)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }
}
