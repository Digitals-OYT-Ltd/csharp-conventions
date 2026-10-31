using System.Globalization;
using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Extensions.OutputDevice;
using Microsoft.Testing.Platform.Extensions.TestHost;
using Microsoft.Testing.Platform.OutputDevice;

namespace Digitals.Testing.Coverage;

/// <summary>
/// Fails a test run whose code coverage is below the configured floor, or whose methods are past the
/// configured CRAP and complexity limits.
/// </summary>
/// <remarks>
/// This is a Microsoft.Testing.Platform extension rather than an MSBuild target because under
/// .NET 10 <c>dotnet test</c> with the MTP runner, no MSBuild post-test target runs at all —
/// <c>AfterTargets</c> on <c>InvokeTestingPlatform</c>, <c>Test</c> and <c>VSTest</c> are all dead.
/// Microsoft's coverage extension has no threshold option of its own, so the check lives here.
/// The gate collects the Cobertura artifacts the coverage extension publishes, then evaluates them
/// once the run is over.
/// </remarks>
internal sealed class CoverageGate(ICommandLineOptions commandLineOptions, IOutputDevice outputDevice)
    : IDataConsumer, ITestHostApplicationLifetime, IOutputDeviceDataProducer
{
    internal const string ExtensionUid = "Digitals.Testing.Coverage";
    internal const string ExtensionVersion = "1.0.0";
    internal const string ExtensionDisplayName = "Digitals coverage gate";

    internal const string ExtensionDescription =
        "Fails the run when coverage is below the org floor or a method is past a limit.";

    private const int MaximumNamedOffenders = 5;

    private readonly List<string> _reports = [];

    public string Uid => ExtensionUid;

    public string Version => ExtensionVersion;

    public string DisplayName => ExtensionDisplayName;

    public string Description => ExtensionDescription;

    public Type[] DataTypesConsumed { get; } = [typeof(SessionFileArtifact)];

    public Task<bool> IsEnabledAsync()
        => Task.FromResult(commandLineOptions.IsOptionSet(CoverageGateOptions.Enable));

    public Task ConsumeAsync(IDataProducer dataProducer, IData value, CancellationToken cancellationToken)
    {
        // Match on the file name, not on the producing extension: the artifact is claimed by
        // whichever coverage extension is registered, and only the Cobertura shape is parseable here.
        if (value is SessionFileArtifact artifact
            && artifact.FileInfo.Name.EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase))
        {
            _reports.Add(artifact.FileInfo.FullName);
        }

        return Task.CompletedTask;
    }

    public Task BeforeRunAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task AfterRunAsync(int exitCode, CancellationToken cancellationToken)
    {
        // A failing run has already said why it failed. Adding a coverage error on top buries the
        // real cause, and coverage measured from a partial run is not the number to judge anyway.
        if (exitCode != 0)
        {
            return;
        }

        var minimumLine = GetThreshold(CoverageGateOptions.MinimumLine);
        var minimumBranch = GetThreshold(CoverageGateOptions.MinimumBranch);

        if (_reports.Count == 0)
        {
            // Deliberately an error, not a skip. A gate that goes quiet when it finds no data is
            // worse than no gate: the build stays green while nothing is being enforced.
            throw new InvalidOperationException(
                $"Coverage gate is enabled but no Cobertura report was produced. Expected the coverage "
                + $"extension to publish a '*.cobertura.xml' artifact. Pass --{CoverageGateOptions.Enable} only "
                + "when coverage is collected, or set <DigitalsSkipCoverageGate>true</DigitalsSkipCoverageGate> "
                + "in the test project with a comment saying why.");
        }

        var summary = new CoverageSummary();
        foreach (var report in _reports)
        {
            summary.AddFile(report);
        }

        await outputDevice.DisplayAsync(
            this,
            new FormattedTextOutputDeviceData(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Coverage: line {0:N1}% ({1}/{2}), branch {3:N1}% ({4}/{5}) from {6} report(s). "
                        + "Floors: line {7:N1}%, branch {8:N1}%.",
                    summary.LinePercentage,
                    summary.LinesCovered,
                    summary.LinesValid,
                    summary.BranchPercentage,
                    summary.BranchesCovered,
                    summary.BranchesValid,
                    _reports.Count,
                    minimumLine,
                    minimumBranch)),
            cancellationToken).ConfigureAwait(false);

        var failures = new List<string>();
        if (summary.LinePercentage < minimumLine)
        {
            failures.Add(Describe(
                "Line", summary.LinePercentage, minimumLine, summary.LinesCovered, summary.LinesValid));
        }

        if (summary.BranchPercentage < minimumBranch)
        {
            failures.Add(Describe(
                "Branch", summary.BranchPercentage, minimumBranch, summary.BranchesCovered, summary.BranchesValid));
        }

        if (failures.Count > 0)
        {
            failures.Add("Add tests; do not lower the floor.");
        }

        failures.AddRange(await CheckMethodsAsync(cancellationToken).ConfigureAwait(false));

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", failures));
        }
    }

    /// <summary>
    /// Runs whichever per-method limits are switched on and returns their failure messages.
    /// </summary>
    private async Task<IReadOnlyList<string>> CheckMethodsAsync(CancellationToken cancellationToken)
    {
        var maximumCrap = GetOptionalThreshold(CoverageGateOptions.MaximumCrap);
        var maximumComplexity = GetOptionalThreshold(CoverageGateOptions.MaximumComplexity);
        if (maximumCrap is null && maximumComplexity is null)
        {
            return [];
        }

        // Parsed once and shared. Both limits read the same <method> elements, and the reports of a
        // large repo are not cheap to walk twice.
        var methods = new MethodSummary();
        foreach (var report in _reports)
        {
            methods.AddFile(report);
        }

        var failures = new List<string>();
        if (maximumCrap is { } crapLimit)
        {
            var failure = await CheckCrapAsync(methods, crapLimit, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        if (maximumComplexity is { } complexityLimit)
        {
            var failure = await CheckComplexityAsync(methods, complexityLimit, cancellationToken)
                .ConfigureAwait(false);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        return failures;
    }

    /// <summary>
    /// Judges per-method CRAP scores and returns the failure message, or null when all are within
    /// the limit.
    /// </summary>
    private async Task<string?> CheckCrapAsync(
        MethodSummary methods, double maximum, CancellationToken cancellationToken)
    {
        var offenders = methods.AboveCrapScore(maximum);
        await outputDevice.DisplayAsync(
            this,
            new FormattedTextOutputDeviceData(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "CRAP: {0} of {1} method(s) above the {2:N1} limit. Worst: {3}.",
                    offenders.Count,
                    methods.MethodCount,
                    maximum,
                    methods.WorstCrapScore()?.DescribeCrapScore() ?? "none")),
            cancellationToken).ConfigureAwait(false);

        return offenders.Count == 0
            ? null
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} method(s) score above the CRAP limit of {1:N1}: {2}. "
                    + "Cover the branches or split the method; raising the limit is the last resort.",
                offenders.Count,
                maximum,
                NameWorst(offenders, method => method.DescribeCrapScore()));
    }

    /// <summary>
    /// Judges per-method cyclomatic complexity and returns the failure message, or null when every
    /// method is within the limit.
    /// </summary>
    /// <remarks>
    /// Separate from the CRAP limit because tests buy a method its way back under that one: a fully
    /// covered method scores exactly its complexity, so a 40-branch method passes a CRAP limit of 45
    /// on coverage alone while still being a method nobody can change safely. This cap does not move
    /// with coverage.
    /// </remarks>
    private async Task<string?> CheckComplexityAsync(
        MethodSummary methods, double maximum, CancellationToken cancellationToken)
    {
        var offenders = methods.AboveComplexity(maximum);
        await outputDevice.DisplayAsync(
            this,
            new FormattedTextOutputDeviceData(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Complexity: {0} of {1} method(s) above the {2:N0} limit. Worst: {3}.",
                    offenders.Count,
                    methods.MethodCount,
                    maximum,
                    methods.WorstComplexity()?.DescribeComplexity() ?? "none")),
            cancellationToken).ConfigureAwait(false);

        return offenders.Count == 0
            ? null
            : string.Format(
                CultureInfo.InvariantCulture,
                "{0} method(s) are above the cyclomatic complexity limit of {1:N0}: {2}. "
                    + "Split the method or flatten the branching; covering it does not help here.",
                offenders.Count,
                maximum,
                NameWorst(offenders, method => method.DescribeComplexity()));
    }

    // Name the worst few rather than all of them. A repo adopting a limit can have hundreds, and a
    // wall of text is read as noise; the ordering makes the next ones obvious anyway.
    private static string NameWorst(
        IReadOnlyList<MethodMetrics> offenders, Func<MethodMetrics, string> describe)
    {
        var named = string.Join("; ", offenders.Take(MaximumNamedOffenders).Select(describe));
        var remainder = offenders.Count - Math.Min(offenders.Count, MaximumNamedOffenders);
        return remainder > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0} and {1} more", named, remainder)
            : named;
    }

    internal static string Describe(string kind, double actual, double floor, long covered, long valid)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0} coverage {1:N1}% is below the {2:N1}% floor ({3} of {4} covered).",
            kind,
            actual,
            floor,
            covered,
            valid);

    // Absent or unparseable means 0, which passes. The props always injects both values, and
    // ValidateOptionArgumentsAsync has already rejected anything unparseable, so reaching the
    // default means the gate was enabled by hand without thresholds.
    private double GetThreshold(string optionName)
        => commandLineOptions.TryGetOptionArgumentList(optionName, out var arguments)
            && arguments.Length > 0
            && double.TryParse(arguments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0d;

    // Absent means the check is off, which is a different answer from zero: both per-method limits
    // are opt-out (DigitalsSkipCrapGate and DigitalsSkipComplexityGate stop the props injecting
    // them), and a limit of 0 would fail every method ever written.
    private double? GetOptionalThreshold(string optionName)
        => commandLineOptions.TryGetOptionArgumentList(optionName, out var arguments)
            && arguments.Length > 0
            && double.TryParse(arguments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
}
