using Microsoft.Testing.Platform.Extensions.CommandLine;
using Xunit;

namespace Digitals.Testing.Coverage.Tests;

[Trait("Category", "Unit")]
public sealed class CoverageGateCommandLineProviderTests
{
    private static readonly CoverageGateCommandLineProvider Provider = new();

    private static CommandLineOption Option(string name)
        => Provider.GetCommandLineOptions().Single(option => option.Name == name);

    [Fact]
    public void GetCommandLineOptions_Always_RegistersEveryOptionThePropsInjects()
    {
        // The props injects these five by name. The platform rejects the whole run with
        // "unknown option" if one of them is not registered here, so this is the contract test.
        var names = Provider.GetCommandLineOptions().Select(option => option.Name).ToArray();

        Assert.Equal(
            [
                CoverageGateOptions.Enable,
                CoverageGateOptions.MinimumLine,
                CoverageGateOptions.MinimumBranch,
                CoverageGateOptions.MaximumCrap,
                CoverageGateOptions.MaximumComplexity,
            ],
            names);
    }

    [Fact]
    public void GetCommandLineOptions_EnableOption_TakesNoArgument()
    {
        var option = Option(CoverageGateOptions.Enable);

        Assert.Equal(ArgumentArity.Zero, option.Arity);
        Assert.False(option.IsHidden);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("80")]
    [InlineData("99.5")]
    [InlineData("100")]
    public async Task ValidateOptionArgumentsAsync_ThresholdInRange_ReturnsValid(string threshold)
    {
        var result = await Provider.ValidateOptionArgumentsAsync(Option(CoverageGateOptions.MinimumLine), [threshold]);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    public async Task ValidateOptionArgumentsAsync_ThresholdOutOfRange_ReturnsInvalid(string threshold)
    {
        var result = await Provider.ValidateOptionArgumentsAsync(
            Option(CoverageGateOptions.MinimumBranch), [threshold]);

        Assert.False(result.IsValid);
        Assert.Contains("between 0 and 100", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateOptionArgumentsAsync_ThresholdNotANumber_ReturnsInvalid()
    {
        // Left to itself double.TryParse would fail and the gate would fall back to a floor of 0,
        // which passes everything. The run has to stop here instead.
        var result = await Provider.ValidateOptionArgumentsAsync(Option(CoverageGateOptions.MinimumLine), ["ninety"]);

        Assert.False(result.IsValid);
        Assert.Contains("'ninety'", result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateOptionArgumentsAsync_CommaDecimalSeparator_ReturnsInvalid()
    {
        // Thresholds are parsed with the invariant culture so a build on a machine with a comma
        // separator behaves the same as CI. "90,5" is a typo, not ninety and a half.
        var result = await Provider.ValidateOptionArgumentsAsync(Option(CoverageGateOptions.MinimumLine), ["90,5"]);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateOptionArgumentsAsync_EnableOption_ReturnsValid()
    {
        var result = await Provider.ValidateOptionArgumentsAsync(Option(CoverageGateOptions.Enable), []);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("30")]
    [InlineData("120.5")]
    public async Task ValidateOptionArgumentsAsync_CrapLimitOfAtLeastOne_ReturnsValid(string limit)
    {
        var result = await Provider.ValidateOptionArgumentsAsync(Option(CoverageGateOptions.MaximumCrap), [limit]);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("-3")]
    [InlineData("thirty")]
    public async Task ValidateOptionArgumentsAsync_CrapLimitBelowOne_ReturnsInvalid(string limit)
    {
        // A fully covered method scores its own complexity, and the least a method can score is 1.
        // A limit under that fails every method in the repo, which is a typo rather than a policy.
        var result = await Provider.ValidateOptionArgumentsAsync(Option(CoverageGateOptions.MaximumCrap), [limit]);

        Assert.False(result.IsValid);
        Assert.Contains("at least 1", result.ErrorMessage);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("15")]
    [InlineData("120")]
    public async Task ValidateOptionArgumentsAsync_ComplexityLimitOfAtLeastOne_ReturnsValid(string limit)
    {
        var result = await Provider.ValidateOptionArgumentsAsync(
            Option(CoverageGateOptions.MaximumComplexity), [limit]);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("fifteen")]
    [InlineData("15.5")]
    public async Task ValidateOptionArgumentsAsync_ComplexityLimitNotAWholeNumberOrBelowOne_ReturnsInvalid(
        string limit)
    {
        // Complexity counts decision points, so 15.5 is a typo rather than a finer setting, and a
        // limit under 1 fails the simplest method there is.
        var result = await Provider.ValidateOptionArgumentsAsync(
            Option(CoverageGateOptions.MaximumComplexity), [limit]);

        Assert.False(result.IsValid);
        Assert.Contains("whole number of at least 1", result.ErrorMessage);
    }

    [Fact]
    public async Task IsEnabledAsync_Always_ReturnsTrue()
        // The provider always registers its options; the gate itself is what honours the enable flag.
        => Assert.True(await Provider.IsEnabledAsync());
}
