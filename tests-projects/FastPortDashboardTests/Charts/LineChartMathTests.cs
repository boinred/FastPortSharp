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
}
