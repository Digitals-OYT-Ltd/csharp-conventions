using Microsoft.Testing.Platform.CommandLine;
using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.OutputDevice;
using Microsoft.Testing.Platform.TestHost;
using NSubstitute;
using Xunit;

// Arg.Any<string[]>() in an out position is a matcher, not a value NSubstitute assigns, so the
// compiler's null analysis of it is wrong here.
#pragma warning disable CS8601

namespace Digitals.Testing.Coverage.Tests;

[Trait("Category", "Integration")]
public sealed class CoverageGateTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("digitals-gate").FullName;
    private readonly ICommandLineOptions _options = Substitute.For<ICommandLineOptions>();
    private readonly IOutputDevice _outputDevice = Substitute.For<IOutputDevice>();

    public CoverageGateTests()
    {
        _options.IsOptionSet(CoverageGateOptions.Enable).Returns(true);
        Threshold(CoverageGateOptions.MinimumLine, "100");
        Threshold(CoverageGateOptions.MinimumBranch, "100");
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Threshold(string option, string value)
        => _options.TryGetOptionArgumentList(option, out Arg.Any<string[]>())
            .Returns(call =>
            {
                call[1] = new[] { value };
                return true;
            });

    private CoverageGate Gate() => new(_options, _outputDevice);

    private async Task Report(
        CoverageGate gate,
        string fileName = "run.cobertura.xml",
        long linesCovered = 10,
        long linesValid = 10,
        long branchesCovered = 4,
        long branchesValid = 4)
    {
        var path = Path.Combine(_directory, fileName);
        await File.WriteAllTextAsync(
            path,
            $"""
            <coverage lines-covered="{linesCovered}" lines-valid="{linesValid}"
                      branches-covered="{branchesCovered}" branches-valid="{branchesValid}" />
            """,
            TestContext.Current.CancellationToken);

        await gate.ConsumeAsync(
            Substitute.For<IDataProducer>(),
            new SessionFileArtifact(new SessionUid("session"), new FileInfo(path), "coverage"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task IsEnabledAsync_GateOptionAbsent_ReturnsFalse()
    {
        _options.IsOptionSet(CoverageGateOptions.Enable).Returns(false);

        Assert.False(await Gate().IsEnabledAsync());
    }

    [Fact]
    public async Task AfterRunAsync_CoverageAtFloor_DoesNotThrow()
    {
        var gate = Gate();
        await Report(gate);

        Assert.Null(await Record.ExceptionAsync(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AfterRunAsync_LineCoverageBelowFloor_ThrowsNamingBothNumbers()
    {
        var gate = Gate();
        await Report(gate, linesCovered: 8, linesValid: 10);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("Line coverage 80.0% is below the 100.0% floor (8 of 10 covered).", error.Message);
        Assert.Contains("Add tests; do not lower the floor.", error.Message);
    }

    [Fact]
    public async Task AfterRunAsync_BranchCoverageBelowFloor_ThrowsAboutBranches()
    {
        var gate = Gate();
        await Report(gate, branchesCovered: 1, branchesValid: 4);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("Branch coverage 25.0%", error.Message);
        Assert.DoesNotContain("Line coverage", error.Message);
    }

    [Fact]
    public async Task AfterRunAsync_BothBelowFloor_ReportsBothInOneError()
    {
        var gate = Gate();
        await Report(gate, linesCovered: 5, linesValid: 10, branchesCovered: 1, branchesValid: 4);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("Line coverage 50.0%", error.Message);
        Assert.Contains("Branch coverage 25.0%", error.Message);
    }

    [Fact]
    public async Task AfterRunAsync_SeveralReports_JudgesTheirCombinedTotals()
    {
        var gate = Gate();
        Threshold(CoverageGateOptions.MinimumLine, "90");
        await Report(gate, "one.cobertura.xml", linesCovered: 99, linesValid: 100);
        await Report(gate, "two.cobertura.xml", linesCovered: 1, linesValid: 9);

        // 100 of 109 is 91.7%: over the floor, although the second report alone is far under it.
        Assert.Null(await Record.ExceptionAsync(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AfterRunAsync_NoCoverageReport_ThrowsRatherThanPassingQuietly()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Gate().AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("no Cobertura report was produced", error.Message);
    }

    [Fact]
    public async Task AfterRunAsync_TestsAlreadyFailed_LeavesTheRealFailureAlone()
        // No report was collected, which would otherwise throw. A failing run must keep its own
        // error message rather than being reported as a coverage problem.
        => Assert.Null(await Record.ExceptionAsync(
            () => Gate().AfterRunAsync(1, TestContext.Current.CancellationToken)));

    [Fact]
    public async Task ConsumeAsync_ArtifactIsNotCobertura_IgnoresIt()
    {
        var gate = Gate();
        await Report(gate, "run.coverage");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("no Cobertura report was produced", error.Message);
    }

    [Fact]
    public async Task AfterRunAsync_CoveragePasses_PrintsTheNumbersItJudged()
    {
        var gate = Gate();
        await Report(gate);

        await gate.AfterRunAsync(0, TestContext.Current.CancellationToken);

        await _outputDevice.Received(1).DisplayAsync(
            gate,
            Arg.Is<IOutputDeviceData>(data =>
                ((FormattedTextOutputDeviceData)data).Text
                    .Contains("line 100.0% (10/10), branch 100.0% (4/4) from 1 report(s)")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterRunAsync_ThresholdMissing_TreatsTheFloorAsZero()
    {
        // Reaching this means somebody enabled the gate by hand without the props. Nothing to
        // enforce, so the run stays green rather than failing on a floor nobody chose.
        _options.TryGetOptionArgumentList(Arg.Any<string>(), out Arg.Any<string[]>()).Returns(false);
        var gate = Gate();
        await Report(gate, linesCovered: 0, linesValid: 10, branchesCovered: 0, branchesValid: 4);

        Assert.Null(await Record.ExceptionAsync(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken)));
    }

    // A report carrying one method, for the CRAP check. It has no root-level counts, so the two
    // coverage floors see nothing coverable and pass, leaving the CRAP limit as the only judge.
    private async Task MethodReport(
        CoverageGate gate,
        string fileName = "methods.cobertura.xml",
        string methodName = "Parse",
        int complexity = 10,
        int linesCovered = 0,
        int linesValid = 4)
    {
        var lines = string.Join(
            "\n",
            Enumerable.Range(1, linesValid).Select(number =>
                $"""<line number="{number}" hits="{(number <= linesCovered ? 1 : 0)}" />"""));

        var path = Path.Combine(_directory, fileName);
        await File.WriteAllTextAsync(
            path,
            $"""
            <coverage>
              <packages><package name="Lib"><classes>
                <class name="Lib.Parser" filename="Parser.cs"><methods>
                  <method name="{methodName}" signature="(string)" complexity="{complexity}">
                    <lines>{lines}</lines>
                  </method>
                </methods></class>
              </classes></package></packages>
            </coverage>
            """,
            TestContext.Current.CancellationToken);

        await gate.ConsumeAsync(
            Substitute.For<IDataProducer>(),
            new SessionFileArtifact(new SessionUid("session"), new FileInfo(path), "coverage"),
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AfterRunAsync_CrapLimitAbsent_IgnoresAnAwfulMethod()
    {
        // Nothing stubs the option, so TryGetOptionArgumentList returns false: the check is off.
        var gate = Gate();
        await MethodReport(gate, complexity: 40);

        Assert.Null(await Record.ExceptionAsync(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AfterRunAsync_MethodAboveCrapLimit_ThrowsNamingIt()
    {
        Threshold(CoverageGateOptions.MaximumCrap, "30");
        var gate = Gate();
        await MethodReport(gate, complexity: 10, linesCovered: 0, linesValid: 4);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        // 10² × 1³ + 10 = 110.
        Assert.Contains("1 method(s) score above the CRAP limit of 30.0", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "Lib.Parser.Parse(string) scores 110.0 (complexity 10, 0 of 4 lines covered)",
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("Cover the branches or split the method", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterRunAsync_MethodWithinCrapLimit_Passes()
    {
        Threshold(CoverageGateOptions.MaximumCrap, "30");
        var gate = Gate();

        // 10² × 0.25³ + 10 = 11.6.
        await MethodReport(gate, complexity: 10, linesCovered: 3, linesValid: 4);

        Assert.Null(await Record.ExceptionAsync(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AfterRunAsync_CrapChecked_PrintsWhatItJudged()
    {
        Threshold(CoverageGateOptions.MaximumCrap, "30");
        var gate = Gate();
        await MethodReport(gate, complexity: 2, linesCovered: 4, linesValid: 4);

        await gate.AfterRunAsync(0, TestContext.Current.CancellationToken);

        await _outputDevice.Received(1).DisplayAsync(
            gate,
            Arg.Is<IOutputDeviceData>(data =>
                ((FormattedTextOutputDeviceData)data).Text
                    .Contains("CRAP: 0 of 1 method(s) above the 30.0 limit.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterRunAsync_CoverageAndCrapBothFail_ReportsBothInOneError()
    {
        Threshold(CoverageGateOptions.MaximumCrap, "30");
        var gate = Gate();
        await Report(gate, linesCovered: 5, linesValid: 10);
        await MethodReport(gate, complexity: 10);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("Line coverage 50.0%", error.Message, StringComparison.Ordinal);
        Assert.Contains("Add tests; do not lower the floor.", error.Message, StringComparison.Ordinal);
        Assert.Contains("above the CRAP limit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterRunAsync_ManyOffenders_NamesTheWorstFiveAndCountsTheRest()
    {
        Threshold(CoverageGateOptions.MaximumCrap, "30");
        var gate = Gate();
        for (var index = 0; index < 7; index++)
        {
            await MethodReport(gate, $"m{index}.cobertura.xml", $"Parse{index}", complexity: 10 + index);
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("7 method(s) score above", error.Message, StringComparison.Ordinal);
        Assert.Contains("Lib.Parser.Parse6(string)", error.Message, StringComparison.Ordinal);
        Assert.Contains("and 2 more", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Lib.Parser.Parse0(string)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterRunAsync_ComplexityLimitAbsent_IgnoresATangledMethod()
    {
        // Nothing stubs the option, so TryGetOptionArgumentList returns false: the check is off.
        var gate = Gate();
        await MethodReport(gate, complexity: 40, linesCovered: 4, linesValid: 4);

        Assert.Null(await Record.ExceptionAsync(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AfterRunAsync_MethodAboveComplexityLimit_ThrowsNamingIt()
    {
        Threshold(CoverageGateOptions.MaximumComplexity, "15");
        var gate = Gate();
        await MethodReport(gate, complexity: 22, linesCovered: 4, linesValid: 4);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains(
            "1 method(s) are above the cyclomatic complexity limit of 15: "
                + "Lib.Parser.Parse(string) has complexity 22.",
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("covering it does not help here", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterRunAsync_TangledButFullyCoveredMethod_PassesCrapAndFailsComplexity()
    {
        // The reason the second limit exists. A fully covered method scores exactly its complexity,
        // so 22 sits under a CRAP limit of 30 on coverage alone while still being unmaintainable.
        Threshold(CoverageGateOptions.MaximumCrap, "30");
        Threshold(CoverageGateOptions.MaximumComplexity, "15");
        var gate = Gate();
        await MethodReport(gate, complexity: 22, linesCovered: 4, linesValid: 4);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.DoesNotContain("CRAP limit", error.Message, StringComparison.Ordinal);
        Assert.Contains("cyclomatic complexity limit of 15", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterRunAsync_MethodWithinComplexityLimit_Passes()
    {
        Threshold(CoverageGateOptions.MaximumComplexity, "15");
        var gate = Gate();
        await MethodReport(gate, complexity: 15, linesCovered: 0, linesValid: 4);

        Assert.Null(await Record.ExceptionAsync(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AfterRunAsync_ComplexityChecked_PrintsWhatItJudged()
    {
        Threshold(CoverageGateOptions.MaximumComplexity, "15");
        var gate = Gate();
        await MethodReport(gate, complexity: 2, linesCovered: 4, linesValid: 4);

        await gate.AfterRunAsync(0, TestContext.Current.CancellationToken);

        await _outputDevice.Received(1).DisplayAsync(
            gate,
            Arg.Is<IOutputDeviceData>(data =>
                ((FormattedTextOutputDeviceData)data).Text
                    .Contains("Complexity: 0 of 1 method(s) above the 15 limit. "
                        + "Worst: Lib.Parser.Parse(string) has complexity 2.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AfterRunAsync_BothPerMethodLimitsExceeded_ReportsBothInOneError()
    {
        Threshold(CoverageGateOptions.MaximumCrap, "30");
        Threshold(CoverageGateOptions.MaximumComplexity, "15");
        var gate = Gate();
        await MethodReport(gate, complexity: 20, linesCovered: 0, linesValid: 4);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.AfterRunAsync(0, TestContext.Current.CancellationToken));

        Assert.Contains("above the CRAP limit of 30.0", error.Message, StringComparison.Ordinal);
        Assert.Contains("cyclomatic complexity limit of 15", error.Message, StringComparison.Ordinal);
    }
}
