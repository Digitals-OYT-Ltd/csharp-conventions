using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Digitals.Analyzers.CSharp;

/// <summary>Reads the 'digitals_*' threshold keys a repo sets in .editorconfig or a globalconfig.</summary>
internal static class AnalyzerOptionReader
{
    /// <summary>The positive integer under <paramref name="key"/>, or <paramref name="fallback"/>.</summary>
    public static int PositiveInt(AnalyzerOptions options, SyntaxTree tree, string key, int fallback) =>
        options.AnalyzerConfigOptionsProvider.GetOptions(tree).TryGetValue(key, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        && value > 0
            ? value
            : fallback;
}
