namespace Digitals.Testing.Coverage;

/// <summary>
/// Command line option names understood by the coverage gate.
/// </summary>
/// <remarks>
/// Microsoft.Testing.Platform rejects any option no extension has registered, so these names are
/// the contract between <c>build/Digitals.Testing.props</c> (which injects them from MSBuild
/// properties) and the gate. Renaming one without changing the props fails every consumer's test
/// run with "unknown option".
/// </remarks>
public static class CoverageGateOptions
{
    /// <summary>Turns the gate on. The gate does nothing unless this is present.</summary>
    public const string Enable = "digitals-coverage-gate";

    /// <summary>Minimum line coverage percentage.</summary>
    public const string MinimumLine = "digitals-coverage-min-line";

    /// <summary>Minimum branch coverage percentage.</summary>
    public const string MinimumBranch = "digitals-coverage-min-branch";

    /// <summary>
    /// Highest CRAP score any single method may have. Absent means the check is off, which is how
    /// <c>DigitalsSkipCrapGate</c> disables it: the props stops injecting the option entirely.
    /// </summary>
    public const string MaximumCrap = "digitals-coverage-max-crap";

    /// <summary>
    /// Highest cyclomatic complexity any single method may have, whatever its coverage. Absent means
    /// the check is off, the same way <c>DigitalsSkipComplexityGate</c> disables it.
    /// </summary>
    public const string MaximumComplexity = "digitals-coverage-max-complexity";
}
