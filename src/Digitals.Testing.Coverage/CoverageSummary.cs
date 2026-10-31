using System.Globalization;
using System.Xml.Linq;

namespace Digitals.Testing.Coverage;

/// <summary>
/// Totals read from one or more Cobertura reports.
/// </summary>
/// <remarks>
/// Counts, not the <c>line-rate</c>/<c>branch-rate</c> attributes: rates cannot be averaged across
/// reports without weighting, and a test project that instruments several assemblies emits one
/// report per run. Summing covered/valid and dividing once is the only aggregation that stays
/// correct as reports are added. Both Cobertura writers in play (Microsoft's code coverage engine
/// and coverlet) emit the four count attributes on the root element.
/// </remarks>
public sealed class CoverageSummary
{
    /// <summary>Gets the number of covered lines.</summary>
    public long LinesCovered { get; private set; }

    /// <summary>Gets the number of coverable lines.</summary>
    public long LinesValid { get; private set; }

    /// <summary>Gets the number of covered branches.</summary>
    public long BranchesCovered { get; private set; }

    /// <summary>Gets the number of branches in decision points.</summary>
    public long BranchesValid { get; private set; }

    /// <summary>Gets line coverage as a percentage, or 100 when there is nothing coverable.</summary>
    public double LinePercentage => Percentage(LinesCovered, LinesValid);

    /// <summary>Gets branch coverage as a percentage, or 100 when there are no branches.</summary>
    public double BranchPercentage => Percentage(BranchesCovered, BranchesValid);

    /// <summary>Adds the totals of one Cobertura report.</summary>
    /// <param name="report">The parsed report.</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    public void Add(XDocument report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        var root = report.Root;
        if (root is null)
        {
            return;
        }

        LinesCovered += ReadCount(root, "lines-covered");
        LinesValid += ReadCount(root, "lines-valid");
        BranchesCovered += ReadCount(root, "branches-covered");
        BranchesValid += ReadCount(root, "branches-valid");
    }

    /// <summary>Adds the totals of one Cobertura report read from disk.</summary>
    /// <param name="path">Path to the report.</param>
    public void AddFile(string path) => Add(XDocument.Load(path));

    // An empty denominator is 100%, not 0%: a project with nothing coverable (every type excluded,
    // or a pure integration suite) has no gap for a test to close, and reporting 0 would make the
    // gate unpassable rather than satisfied.
    private static double Percentage(long covered, long valid)
        => valid <= 0 ? 100d : covered * 100d / valid;

    private static long ReadCount(XElement root, string name)
    {
        var value = root.Attribute(name)?.Value;
        return value is not null
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
    }
}
