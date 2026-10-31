using System.Xml.Linq;
using Xunit;

namespace Digitals.Testing.Coverage.Tests;

public sealed class MethodSummaryTests
{
    // The shape Microsoft's coverage engine and coverlet both emit: complexity on the method,
    // hits on each line.
    private static XDocument Report(
        string typeName = "Lib.Calculator",
        string methodName = "Add",
        string signature = "(int, int)",
        int complexity = 1,
        int linesCovered = 1,
        int linesValid = 1)
    {
        var lines = string.Join(
            "\n",
            Enumerable.Range(1, linesValid).Select(number =>
                $"""<line number="{number}" hits="{(number <= linesCovered ? 1 : 0)}" />"""));

        return XDocument.Parse(
            $"""
            <coverage>
              <packages><package name="Lib"><classes>
                <class name="{typeName}" filename="Calculator.cs">
                  <methods>
                    <method name="{methodName}" signature="{signature}" complexity="{complexity}">
                      <lines>
                        {lines}
                      </lines>
                    </method>
                  </methods>
                </class>
              </classes></package></packages>
            </coverage>
            """);
    }

    [Fact]
    public void CrapScore_FullyCoveredMethod_EqualsItsComplexity()
    {
        var summary = new MethodSummary();

        summary.Add(Report(complexity: 12, linesCovered: 8, linesValid: 8));

        Assert.Equal(12d, summary.WorstCrapScore()!.CrapScore);
    }

    [Fact]
    public void CrapScore_UntestedMethod_IsComplexitySquaredPlusComplexity()
    {
        var summary = new MethodSummary();

        // 5² × 1³ + 5 = 30, exactly the default limit, so this one is not an offender.
        summary.Add(Report(complexity: 5, linesCovered: 0, linesValid: 4));

        Assert.Equal(30d, summary.WorstCrapScore()!.CrapScore);
        Assert.Empty(summary.AboveCrapScore(30));
    }

    [Fact]
    public void CrapScore_HalfCoveredMethod_SitsBetweenTheTwoExtremes()
    {
        var summary = new MethodSummary();

        // 6² × 0.5³ + 6 = 10.5, against 42 untested and 6 fully covered.
        summary.Add(Report(complexity: 6, linesCovered: 2, linesValid: 4));

        Assert.Equal(10.5d, summary.WorstCrapScore()!.CrapScore);
    }

    [Fact]
    public void AboveCrapScore_LimitExceeded_NamesTheMethodAndItsNumbers()
    {
        var summary = new MethodSummary();
        summary.Add(Report(complexity: 8, linesCovered: 1, linesValid: 10));

        var offenders = summary.AboveCrapScore(30);

        var offender = Assert.Single(offenders);
        Assert.Equal("Lib.Calculator.Add(int, int)", offender.Name);
        Assert.Equal(8d, offender.Complexity);
        Assert.Equal(1, offender.LinesCovered);
        Assert.Equal(10, offender.LinesValid);
        Assert.Contains(
            "complexity 8, 1 of 10 lines covered",
            offender.DescribeCrapScore(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AboveCrapScore_SeveralOffenders_OrdersThemWorstFirst()
    {
        var summary = new MethodSummary();
        summary.Add(Report(methodName: "Mild", complexity: 7, linesCovered: 0, linesValid: 4));
        summary.Add(Report(methodName: "Awful", complexity: 20, linesCovered: 0, linesValid: 4));

        var offenders = summary.AboveCrapScore(30);

        Assert.Equal(["Lib.Calculator.Awful(int, int)", "Lib.Calculator.Mild(int, int)"],
            offenders.Select(method => method.Name));
    }

    // The whole point of the second limit: coverage cannot buy a method its way out of this one.
    [Fact]
    public void AboveComplexity_FullyCoveredMethod_IsStillAnOffender()
    {
        var summary = new MethodSummary();
        summary.Add(Report(complexity: 40, linesCovered: 9, linesValid: 9));

        Assert.Empty(summary.AboveCrapScore(45));
        var offender = Assert.Single(summary.AboveComplexity(15));
        Assert.Equal("Lib.Calculator.Add(int, int)", offender.Name);
        Assert.Equal("Lib.Calculator.Add(int, int) has complexity 40", offender.DescribeComplexity());
    }

    [Fact]
    public void AboveComplexity_MethodAtTheLimit_IsNotAnOffender()
    {
        var summary = new MethodSummary();
        summary.Add(Report(complexity: 15, linesCovered: 0, linesValid: 4));

        Assert.Empty(summary.AboveComplexity(15));
    }

    [Fact]
    public void AboveComplexity_SeveralOffenders_OrdersThemByComplexityNotByCrapScore()
    {
        var summary = new MethodSummary();

        // Covered but hairy against untested but simple: the CRAP order is the reverse of this one.
        summary.Add(Report(methodName: "Hairy", complexity: 30, linesCovered: 4, linesValid: 4));
        summary.Add(Report(methodName: "Untested", complexity: 20, linesCovered: 0, linesValid: 4));

        Assert.Equal(["Lib.Calculator.Hairy(int, int)", "Lib.Calculator.Untested(int, int)"],
            summary.AboveComplexity(15).Select(method => method.Name));
        Assert.Equal("Lib.Calculator.Untested(int, int)", summary.WorstCrapScore()!.Name);
        Assert.Equal("Lib.Calculator.Hairy(int, int)", summary.WorstComplexity()!.Name);
    }

    // The same method measured twice must merge by adding counts, the way CoverageSummary does.
    // Averaging the rates would report 50% for a method that is fully covered by the two runs
    // together.
    [Fact]
    public void Add_SameMethodInTwoReports_SumsItsLineCounts()
    {
        var summary = new MethodSummary();

        summary.Add(Report(complexity: 9, linesCovered: 4, linesValid: 4));
        summary.Add(Report(complexity: 9, linesCovered: 0, linesValid: 4));

        Assert.Equal(1, summary.MethodCount);
        var worst = summary.WorstCrapScore()!;
        Assert.Equal(4, worst.LinesCovered);
        Assert.Equal(8, worst.LinesValid);
        Assert.Equal((9 * 9 * Math.Pow(0.5, 3)) + 9, worst.CrapScore);
    }

    [Fact]
    public void Add_MethodWithNoCoverableLines_ScoresItsComplexity()
    {
        var summary = new MethodSummary();

        summary.Add(XDocument.Parse(
            """
            <coverage><packages><package><classes>
              <class name="Lib.Calculator"><methods>
                <method name="Get" signature="()" complexity="3" />
              </methods></class>
            </classes></package></packages></coverage>
            """));

        Assert.Equal(3d, summary.WorstCrapScore()!.CrapScore);
        Assert.Equal(3d, summary.WorstComplexity()!.Complexity);
    }

    [Fact]
    public void Add_ComplexityAttributeMissing_ScoresZero()
    {
        var summary = new MethodSummary();

        summary.Add(Report(complexity: 0, linesCovered: 0, linesValid: 4));

        Assert.Equal(0d, summary.WorstCrapScore()!.CrapScore);
        Assert.Empty(summary.AboveCrapScore(1));
        Assert.Empty(summary.AboveComplexity(1));
    }

    [Fact]
    public void Worst_NothingAdded_ReturnsNull()
    {
        var summary = new MethodSummary();

        Assert.Null(summary.WorstCrapScore());
        Assert.Null(summary.WorstComplexity());
        Assert.Equal(0, summary.MethodCount);
    }

    [Fact]
    public void Add_EmptyDocument_LeavesTheSummaryUntouched()
    {
        var summary = new MethodSummary();

        summary.Add(new XDocument());

        Assert.Equal(0, summary.MethodCount);
    }

    [Fact]
    public void Add_NullReport_ThrowsArgumentNullException()
    {
        var summary = new MethodSummary();

        Assert.Throws<ArgumentNullException>(() => summary.Add(null!));
    }

    [Fact]
    public void AddFile_CoberturaOnDisk_ReadsItsMethods()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cobertura.xml");
        File.WriteAllText(path, Report(complexity: 4, linesCovered: 0, linesValid: 2).ToString());
        try
        {
            var summary = new MethodSummary();

            summary.AddFile(path);

            Assert.Equal(20d, summary.WorstCrapScore()!.CrapScore);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
