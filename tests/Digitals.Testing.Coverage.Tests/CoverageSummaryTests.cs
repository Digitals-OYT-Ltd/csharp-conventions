using System.Xml.Linq;
using Xunit;

namespace Digitals.Testing.Coverage.Tests;

public sealed class CoverageSummaryTests
{
    private static XDocument Report(
        long linesCovered = 0,
        long linesValid = 0,
        long branchesCovered = 0,
        long branchesValid = 0)
        => XDocument.Parse(
            $"""
            <coverage lines-covered="{linesCovered}" lines-valid="{linesValid}"
                      branches-covered="{branchesCovered}" branches-valid="{branchesValid}" />
            """);

    [Fact]
    public void Add_SingleReport_ReportsItsCounts()
    {
        var summary = new CoverageSummary();

        summary.Add(Report(linesCovered: 9, linesValid: 10, branchesCovered: 3, branchesValid: 4));

        Assert.Equal(9, summary.LinesCovered);
        Assert.Equal(10, summary.LinesValid);
        Assert.Equal(90d, summary.LinePercentage);
        Assert.Equal(75d, summary.BranchPercentage);
    }

    [Fact]
    public void Add_TwoReports_SumsCountsRatherThanAveragingRates()
    {
        var summary = new CoverageSummary();

        // Averaging the two rates would give 75%. Weighted by size the answer is 92%, and the
        // large report is the one that should dominate.
        summary.Add(Report(linesCovered: 99, linesValid: 100));
        summary.Add(Report(linesCovered: 1, linesValid: 9));

        Assert.Equal(100, summary.LinesCovered);
        Assert.Equal(109, summary.LinesValid);
        Assert.Equal(91.7d, Math.Round(summary.LinePercentage, 1));
    }

    [Fact]
    public void LinePercentage_NothingCoverable_ReportsFullCoverage()
    {
        var summary = new CoverageSummary();

        summary.Add(Report());

        Assert.Equal(100d, summary.LinePercentage);
        Assert.Equal(100d, summary.BranchPercentage);
    }

    [Fact]
    public void Add_AttributesMissing_TreatsThemAsZero()
    {
        var summary = new CoverageSummary();

        summary.Add(XDocument.Parse("""<coverage line-rate="0.9" />"""));

        Assert.Equal(0, summary.LinesValid);
        Assert.Equal(100d, summary.LinePercentage);
    }

    [Fact]
    public void Add_AttributeNotANumber_TreatsItAsZero()
    {
        var summary = new CoverageSummary();

        summary.Add(XDocument.Parse("""<coverage lines-covered="NaN" lines-valid="10" />"""));

        Assert.Equal(0, summary.LinesCovered);
        Assert.Equal(10, summary.LinesValid);
    }

    [Fact]
    public void Add_EmptyDocument_LeavesTotalsUntouched()
    {
        var summary = new CoverageSummary();
        summary.Add(Report(linesCovered: 5, linesValid: 5));

        summary.Add(new XDocument());

        Assert.Equal(5, summary.LinesCovered);
    }

    [Fact]
    public void Add_NullReport_ThrowsArgumentNullException()
    {
        var summary = new CoverageSummary();

        Assert.Throws<ArgumentNullException>(() => summary.Add(null!));
    }

    [Fact]
    public void AddFile_CoberturaOnDisk_ReadsItsCounts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cobertura.xml");
        File.WriteAllText(path, Report(linesCovered: 4, linesValid: 8).ToString());
        try
        {
            var summary = new CoverageSummary();

            summary.AddFile(path);

            Assert.Equal(50d, summary.LinePercentage);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
