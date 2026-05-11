using FastPortDashboard.Core.Charts;

namespace FastPortDashboardTests.Charts;

// Design Ref: §8.2 (dashboard-chart-graphicsview-migration) — ComputeRange 단위 테스트.
[TestClass]
public class LineChartMathTests
{
    [TestMethod]
    public void ComputeRange_Empty_ReturnsZeroOne()
    {
        var (min, max) = LineChartMath.ComputeRange(Array.Empty<double>());
        Assert.AreEqual(0d, min);
        Assert.AreEqual(1d, max);
    }

    [TestMethod]
    public void ComputeRange_SingleValue_PadsRange()
    {
        var (min, max) = LineChartMath.ComputeRange(new double[] { 5d });
        Assert.IsTrue(max > min, $"max ({max}) > min ({min}) for single-value input");
        Assert.AreEqual(5d, (min + max) / 2d, 1e-9, "center should be the single value");
    }

    [TestMethod]
    public void ComputeRange_NormalSequence_ReturnsMinMax()
    {
        var (min, max) = LineChartMath.ComputeRange(new double[] { 3, 1, 4, 1, 5, 9, 2, 6 });
        Assert.AreEqual(1d, min);
        Assert.AreEqual(9d, max);
    }

    [TestMethod]
    public void ComputeRange_AllIdentical_PadsRange()
    {
        var (min, max) = LineChartMath.ComputeRange(new double[] { 7d, 7d, 7d });
        Assert.IsTrue(max - min > 0d, "range should be > 0 even when all values identical");
    }

    [TestMethod]
    public void ComputeStepX_SingleValue_ReturnsZero()
    {
        Assert.AreEqual(0f, LineChartMath.ComputeStepX(1, 100f));
    }

    [TestMethod]
    public void ComputeStepX_EmptyOrZeroWidth_ReturnsZero()
    {
        Assert.AreEqual(0f, LineChartMath.ComputeStepX(0, 100f));
        Assert.AreEqual(0f, LineChartMath.ComputeStepX(5, 0f));
    }

    [TestMethod]
    public void ComputeStepX_NormalSequence_DividesWidth()
    {
        Assert.AreEqual(25f, LineChartMath.ComputeStepX(5, 100f), 1e-6f);
    }

    // Design Ref: §8.2 (dashboard-chart-multi-rtt-overlay-v2) — ComputeRangeMulti.

    [TestMethod]
    public void ComputeRangeMulti_Empty_ReturnsZeroOne()
    {
        var (min, max) = LineChartMath.ComputeRangeMulti(Array.Empty<IReadOnlyList<double>>());
        Assert.AreEqual(0d, min);
        Assert.AreEqual(1d, max);
    }

    [TestMethod]
    public void ComputeRangeMulti_AllSeriesEmpty_ReturnsZeroOne()
    {
        var seriesList = new IReadOnlyList<double>[]
        {
            Array.Empty<double>(),
            Array.Empty<double>(),
        };
        var (min, max) = LineChartMath.ComputeRangeMulti(seriesList);
        Assert.AreEqual(0d, min);
        Assert.AreEqual(1d, max);
    }

    [TestMethod]
    public void ComputeRangeMulti_NormalSeries_ReturnsUnionMinMax()
    {
        var seriesList = new IReadOnlyList<double>[]
        {
            new double[] { 1d, 2d, 3d },
            new double[] { 10d, 20d },
        };
        var (min, max) = LineChartMath.ComputeRangeMulti(seriesList);
        Assert.AreEqual(1d, min);
        Assert.AreEqual(20d, max);
    }

    [TestMethod]
    public void ComputeRangeMulti_AllIdentical_PadsRange()
    {
        var seriesList = new IReadOnlyList<double>[]
        {
            new double[] { 5d, 5d, 5d },
            new double[] { 5d, 5d },
        };
        var (min, max) = LineChartMath.ComputeRangeMulti(seriesList);
        Assert.IsTrue(max - min > 0d, "range should be > 0 when all values identical across series");
    }

    [TestMethod]
    public void ComputeRangeMulti_PartialEmptySeries_IgnoresEmpty()
    {
        var seriesList = new IReadOnlyList<double>[]
        {
            new double[] { 1d, 2d },
            Array.Empty<double>(),
            new double[] { 9d, 8d },
        };
        var (min, max) = LineChartMath.ComputeRangeMulti(seriesList);
        Assert.AreEqual(1d, min);
        Assert.AreEqual(9d, max);
    }
}
