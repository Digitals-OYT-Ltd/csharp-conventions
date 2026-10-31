using System.Globalization;
using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions;
using Microsoft.Testing.Platform.Extensions.CommandLine;

// ValidationResult.ValidTask and InvalidTask are cached, already-completed tasks owned by the
// platform. There is no work to start and no context to deadlock on, so VSTHRD003 does not apply.
#pragma warning disable VSTHRD003

namespace Digitals.Testing.Coverage;

/// <summary>
/// Registers the gate's command line options so the platform accepts them.
/// </summary>
internal sealed class CoverageGateCommandLineProvider : ICommandLineOptionsProvider
{
    public string Uid => CoverageGate.ExtensionUid;

    public string Version => CoverageGate.ExtensionVersion;

    public string DisplayName => CoverageGate.ExtensionDisplayName;

    public string Description => CoverageGate.ExtensionDescription;

    public Task<bool> IsEnabledAsync() => Task.FromResult(true);

    public IReadOnlyCollection<CommandLineOption> GetCommandLineOptions() =>
    [
        new CommandLineOption(
            CoverageGateOptions.Enable,
            "Fail the test run when code coverage is below the configured floor",
            ArgumentArity.Zero,
            isHidden: false),
        new CommandLineOption(
            CoverageGateOptions.MinimumLine,
            "Minimum line coverage percentage (0-100)",
            ArgumentArity.ExactlyOne,
            isHidden: false),
        new CommandLineOption(
            CoverageGateOptions.MinimumBranch,
            "Minimum branch coverage percentage (0-100)",
            ArgumentArity.ExactlyOne,
            isHidden: false),
        new CommandLineOption(
            CoverageGateOptions.MaximumCrap,
            "Highest CRAP score allowed for any one method (1 or more)",
            ArgumentArity.ExactlyOne,
            isHidden: false),
        new CommandLineOption(
            CoverageGateOptions.MaximumComplexity,
            "Highest cyclomatic complexity allowed for any one method (a whole number, 1 or more)",
            ArgumentArity.ExactlyOne,
            isHidden: false),
    ];

    public Task<ValidationResult> ValidateOptionArgumentsAsync(CommandLineOption commandOption, string[] arguments)
    {
        if (commandOption.Name is not (CoverageGateOptions.MinimumLine
            or CoverageGateOptions.MinimumBranch
            or CoverageGateOptions.MaximumCrap
            or CoverageGateOptions.MaximumComplexity))
        {
            // Enable takes no argument, so arguments is empty for it.
            return ValidationResult.ValidTask;
        }

        // Reject a bad threshold here rather than at the end of the run: a typo that silently
        // parsed as 0 would turn the gate into a no-op and nothing downstream would say so.
        var raw = arguments[0];
        var parsed = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        var whole = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count);

        // A fully covered method scores exactly its complexity, and the lowest complexity a method
        // can have is 1, so any limit below 1 fails every method in the repo. Complexity is a count
        // of decision points, so a fractional limit there is a typo rather than a finer setting.
        return commandOption.Name switch
        {
            CoverageGateOptions.MinimumLine or CoverageGateOptions.MinimumBranch
                when !parsed || value < 0 || value > 100
                => ValidationResult.InvalidTask(
                    $"--{commandOption.Name} must be a number between 0 and 100, got '{raw}'."),
            CoverageGateOptions.MaximumCrap when !parsed || value < 1
                => ValidationResult.InvalidTask(
                    $"--{commandOption.Name} must be a number of at least 1, got '{raw}'."),
            CoverageGateOptions.MaximumComplexity when !whole || count < 1
                => ValidationResult.InvalidTask(
                    $"--{commandOption.Name} must be a whole number of at least 1, got '{raw}'."),
            _ => ValidationResult.ValidTask,
        };
    }

    public Task<ValidationResult> ValidateCommandLineOptionsAsync(ICommandLineOptions commandLineOptions)
        => ValidationResult.ValidTask;
}
