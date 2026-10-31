using System.Globalization;
using System.Xml.Linq;

namespace Digitals.Testing.Coverage;

/// <summary>
/// Per-method metrics read from one or more Cobertura reports.
/// </summary>
/// <remarks>
/// Two limits are judged from these numbers, and they answer different questions.
/// <para>
/// CRAP (Change Risk Anti-Patterns) is <c>complexity² × (1 − line coverage)³ + complexity</c>. It
/// catches what the run-wide floors cannot: a repo can sit at 85% overall while the one method every
/// bug lands in has a dozen branches and no test at all. Complexity alone would flag methods that
/// are long but thoroughly tested, and coverage alone would clear a trivial gap in a hairy method.
/// The cube on the uncovered fraction is what makes the two interact — covering a complex method
/// drops its score fast, and a fully covered method scores exactly its complexity.
/// </para>
/// <para>
/// Cyclomatic complexity is judged on its own as well, because that interaction is also the CRAP
/// score's blind spot: tests buy a method its way back under the limit however tangled it is, and a
/// fully covered 40-branch method is still a method nobody can change safely. The complexity cap
/// holds whatever the coverage.
/// </para>
/// <para>
/// Methods are keyed by type, name and signature and merged across reports by summing covered and
/// coverable lines, for the same reason <see cref="CoverageSummary"/> sums counts: a test project
/// that instruments several assemblies emits one report per run, and averaging rates would weight a
/// two-line file the same as a two-hundred-line one.
/// </para>
/// </remarks>
public sealed class MethodSummary
{
    private readonly Dictionary<string, Accumulator> _methods = new(StringComparer.Ordinal);

    /// <summary>Gets the number of distinct methods seen.</summary>
    public int MethodCount => _methods.Count;

    /// <summary>Adds the methods of one Cobertura report.</summary>
    /// <param name="report">The parsed report.</param>
    /// <exception cref="ArgumentNullException"><paramref name="report"/> is null.</exception>
    public void Add(XDocument report)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (report.Root is not { } root)
        {
            return;
        }

        foreach (var type in root.Descendants("class"))
        {
            var typeName = type.Attribute("name")?.Value ?? string.Empty;
            foreach (var method in type.Descendants("method"))
            {
                AddMethod(typeName, method);
            }
        }
    }

    /// <summary>Adds the methods of one Cobertura report read from disk.</summary>
    /// <param name="path">Path to the report.</param>
    public void AddFile(string path) => Add(XDocument.Load(path));

    /// <summary>
    /// Gets every method scoring above <paramref name="maximum"/>, worst first.
    /// </summary>
    /// <param name="maximum">The highest CRAP score a method may have.</param>
    /// <returns>The offenders, ordered by score descending then by name.</returns>
    public IReadOnlyList<MethodMetrics> AboveCrapScore(double maximum)
        => [.. Ranked(metrics => metrics.CrapScore).Where(metrics => metrics.CrapScore > maximum)];

    /// <summary>
    /// Gets every method more complex than <paramref name="maximum"/>, worst first.
    /// </summary>
    /// <param name="maximum">The highest cyclomatic complexity a method may have.</param>
    /// <returns>The offenders, ordered by complexity descending then by name.</returns>
    public IReadOnlyList<MethodMetrics> AboveComplexity(double maximum)
        => [.. Ranked(metrics => metrics.Complexity).Where(metrics => metrics.Complexity > maximum)];

    /// <summary>Gets the highest-scoring method, or null when none were seen.</summary>
    /// <returns>The worst method by CRAP score.</returns>
    public MethodMetrics? WorstCrapScore() => Ranked(metrics => metrics.CrapScore).FirstOrDefault();

    /// <summary>Gets the most complex method, or null when none were seen.</summary>
    /// <returns>The worst method by cyclomatic complexity.</returns>
    public MethodMetrics? WorstComplexity() => Ranked(metrics => metrics.Complexity).FirstOrDefault();

    private static double ReadNumber(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return value is not null
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0d;
    }

    // Ordered before filtering, so both queries and both "worst" lookups rank the same way: by the
    // measure asked for, then by name, which keeps the named offenders in an error message stable
    // across runs when several methods tie.
    private IEnumerable<MethodMetrics> Ranked(Func<MethodMetrics, double> measure)
        => _methods.Values
            .Select(accumulator => accumulator.ToMetrics())
            .OrderByDescending(measure)
            .ThenBy(metrics => metrics.Name, StringComparer.Ordinal);

    private void AddMethod(string typeName, XElement method)
    {
        var name = method.Attribute("name")?.Value ?? string.Empty;
        var signature = method.Attribute("signature")?.Value ?? string.Empty;
        var key = $"{typeName}.{name}{signature}";

        if (!_methods.TryGetValue(key, out var accumulator))
        {
            accumulator = new Accumulator(key);
            _methods[key] = accumulator;
        }

        accumulator.Complexity = Math.Max(accumulator.Complexity, ReadNumber(method, "complexity"));

        // Counted from the line elements rather than taken from the method's own line-rate, so that
        // two reports covering the same method merge by addition instead of by guesswork. A method
        // with no line elements has nothing measurable and counts as covered: its CRAP score is then
        // its complexity, which the complexity cap judges anyway.
        foreach (var line in method.Elements("lines").Elements("line"))
        {
            accumulator.LinesValid++;
            if (ReadNumber(line, "hits") > 0)
            {
                accumulator.LinesCovered++;
            }
        }
    }

    private sealed class Accumulator(string name)
    {
        public double Complexity { get; set; }

        public long LinesCovered { get; set; }

        public long LinesValid { get; set; }

        public MethodMetrics ToMetrics() => new(name, Complexity, LinesCovered, LinesValid);
    }
}
