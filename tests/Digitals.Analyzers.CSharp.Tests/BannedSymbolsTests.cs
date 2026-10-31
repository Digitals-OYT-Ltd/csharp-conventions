using Xunit;

namespace Digitals.Analyzers.CSharp.Tests;

/// <summary>
/// The shape of the packaged ban lists. BannedApiAnalyzers reads only '//' as a comment: any other
/// line is an entry, and an entry that names no symbol is skipped in silence. Two such lines with the
/// same text across the files are reported as RS0031, which fails every consumer build under
/// TreatWarningsAsErrors. 0.12.0 shipped '#' comments and did exactly that.
/// </summary>
[Trait("Category", "Unit")]
public sealed class BannedSymbolsTests
{
    private static readonly string[] Files = ["BannedSymbols.txt", "BannedSymbols.Production.txt"];

    private static readonly string[] DocIdPrefixes = ["T:", "M:", "P:", "F:", "E:", "N:"];

    [Fact]
    public void BannedSymbols_EveryLine_IsACommentOrADocId()
    {
        var stray = Entries()
            .Where(entry => !DocIdPrefixes.Any(prefix => entry.Line.StartsWith(prefix, StringComparison.Ordinal)))
            .Select(entry => $"{entry.File}: {entry.Line}")
            .ToList();

        Assert.Empty(stray);
    }

    [Fact]
    public void BannedSymbols_AcrossFiles_NoSymbolTwice()
    {
        var duplicates = Entries()
            .GroupBy(entry => entry.Line.Split(';')[0].Trim(), StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    private static List<(string File, string Line)> Entries()
    {
        var entries = new List<(string File, string Line)>();
        foreach (var file in Files)
        {
            using var stream = typeof(BannedSymbolsTests).Assembly.GetManifestResourceStream(file)
                ?? throw new InvalidOperationException($"{file} is not embedded; see the test csproj.");
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    entries.Add((file, trimmed));
                }
            }
        }

        return entries;
    }
}
