using System.Text.RegularExpressions;
using Xunit;

namespace Digitals.Analyzers.CSharp.Tests;

/// <summary>
/// Every vendor or DT ID the globalconfig names must be declared by an analyzer the package
/// ships. An unknown ID is ignored in silence, so a vendor that drops a rule leaves a line that
/// looks live and enforces nothing (Roslynator 5.0 dropped 28). This is the check AGENTS.md asks
/// for on every bump, run on every build instead. SDK-owned IDs (CA, IDE, SYSLIB, CS) come from
/// the consumer's own SDK and AnalysisLevel, not from this package, and are not checked.
/// A build-artifact test on purpose: it reads the restored packages from disk.
/// </summary>
public sealed partial class VendorDiagnosticIdsTests
{
    [GeneratedRegex(@"^(S\d|RCS|VSTHRD|RS\d|DT)")]
    private static partial Regex ShippedPrefix();

    [Fact]
    public void GlobalConfig_ShippedIds_DeclaredByAnAnalyzerInThePackage()
    {
        var declared = ShippedAnalyzers.Vendors().Values
            .SelectMany(analyzers => analyzers)
            .Concat(ShippedAnalyzers.Custom())
            .SelectMany(analyzer => analyzer.SupportedDiagnostics)
            .Select(descriptor => descriptor.Id)
            .ToHashSet(StringComparer.Ordinal);
        var configured = GlobalConfig.Severities()
            .Select(entry => entry.Id)
            .Where(id => ShippedPrefix().IsMatch(id))
            .ToList();

        var missing = configured.Where(id => !declared.Contains(id)).ToList();

        Assert.NotEmpty(configured);
        Assert.Empty(missing);
    }

    [Fact]
    public void VendorPackages_EachOne_LoadsAtLeastOneAnalyzer()
    {
        var empty = ShippedAnalyzers.Vendors()
            .Where(package => package.Value.Count == 0)
            .Select(package => package.Key)
            .ToList();

        Assert.Equal(5, ShippedAnalyzers.Vendors().Count);
        Assert.Empty(empty);
    }
}
