using System.Globalization;

namespace Digitals.Testing.Coverage;

/// <summary>
/// One method's cyclomatic complexity and line counts, and the CRAP score derived from both.
/// </summary>
public sealed class MethodMetrics
{
    internal MethodMetrics(string name, double complexity, long linesCovered, long linesValid)
    {
        Name = name;
        Complexity = complexity;
        LinesCovered = linesCovered;
        LinesValid = linesValid;
    }

    /// <summary>Gets the method, as type, name and signature.</summary>
    public string Name { get; }

    /// <summary>Gets the cyclomatic complexity the report gave.</summary>
    public double Complexity { get; }

    /// <summary>Gets the number of covered lines.</summary>
    public long LinesCovered { get; }

    /// <summary>Gets the number of coverable lines.</summary>
    public long LinesValid { get; }

    /// <summary>Gets line coverage as a fraction, or 1 when there is nothing coverable.</summary>
    public double LineCoverage => LinesValid <= 0 ? 1d : LinesCovered / (double)LinesValid;

    /// <summary>Gets the CRAP score.</summary>
    public double CrapScore => (Complexity * Complexity * Math.Pow(1d - LineCoverage, 3d)) + Complexity;

    /// <summary>Returns the CRAP score, the method and the numbers it was computed from.</summary>
    /// <returns>A single line for an error message.</returns>
    public string DescribeCrapScore()
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} scores {1:N1} (complexity {2:N0}, {3} of {4} lines covered)",
            Name,
            CrapScore,
            Complexity,
            LinesCovered,
            LinesValid);

    /// <summary>Returns the complexity and the method it belongs to.</summary>
    /// <returns>A single line for an error message.</returns>
    public string DescribeComplexity()
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} has complexity {1:N0}",
            Name,
            Complexity);
}
