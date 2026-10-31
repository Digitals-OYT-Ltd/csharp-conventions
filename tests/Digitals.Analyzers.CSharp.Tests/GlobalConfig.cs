using System.Text.RegularExpressions;

namespace Digitals.Analyzers.CSharp.Tests;

/// <summary>
/// The packaged globalconfig, read from the copy embedded in this assembly at build time.
/// </summary>
internal static partial class GlobalConfig
{
    private static readonly Lazy<IReadOnlyList<string>> LoadedLines = new(Load);

    [GeneratedRegex(@"^dotnet_diagnostic\.(?<id>[A-Za-z0-9]+)\.severity\s*=\s*(?<severity>\S+)")]
    private static partial Regex SeverityLine();

    public static IReadOnlyList<string> Lines() => LoadedLines.Value;

    public static IReadOnlyList<(string Id, string Severity)> Severities() =>
        Lines()
            .Select(line => SeverityLine().Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups["id"].Value, match.Groups["severity"].Value))
            .ToList();

    private static List<string> Load()
    {
        using var stream = typeof(GlobalConfig).Assembly.GetManifestResourceStream("Digitals.Analyzers.globalconfig")
            ?? throw new InvalidOperationException("Digitals.Analyzers.globalconfig is not embedded; see the test csproj.");
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line.Trim());
        }

        return lines;
    }
}
