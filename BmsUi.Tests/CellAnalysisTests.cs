using BmsUi.Model;
using BmsUi.Protocol;
using Xunit;

public class CellAnalysisTests
{
    private static readonly Func<double, bool> Valid = v => v >= 0.5;

    private static double[] Uniform(double value)
    {
        var a = new double[HvProtocol.CellCount];
        Array.Fill(a, value);
        return a;
    }

    [Fact]
    public void Mean_IsAverageOfValidCells()
    {
        var v = Uniform(3.90);
        v[0] = 4.00;
        v[1] = 3.80;
        var a = CellAnalysis.Compute(v, Valid);
        Assert.Equal(3.90, a.Mean, 6);
        Assert.Equal(96, a.ValidCount);
    }

    [Fact]
    public void InvalidCells_AreExcludedFromMean()
    {
        var v = Uniform(3.90);
        v[5] = 0.0;                       // missing / stale cell
        var a = CellAnalysis.Compute(v, Valid);
        Assert.Equal(3.90, a.Mean, 6);    // 0.0 must not drag the mean down
        Assert.Equal(95, a.ValidCount);
        Assert.Equal(CellMark.None, a.Marks[5]);
    }

    [Fact]
    public void StdDev_IsZeroWhenAllCellsEqual()
        => Assert.Equal(0.0, CellAnalysis.Compute(Uniform(3.90), Valid).StdDev, 9);

    [Fact]
    public void StdDev_MatchesPopulationFormula()
    {
        // Half at 3.90, half at 3.80 -> mean 3.85, population sigma = 0.05
        var v = new double[HvProtocol.CellCount];
        for (int i = 0; i < v.Length; i++) v[i] = i < 48 ? 3.90 : 3.80;
        var a = CellAnalysis.Compute(v, Valid);
        Assert.Equal(3.85, a.Mean, 6);
        Assert.Equal(0.05, a.StdDev, 6);
    }

    [Fact]
    public void AboveBelowMean_MarksEveryValidCell()
    {
        var v = Uniform(3.90);
        v[0] = 4.05;
        v[1] = 3.75;
        var a = CellAnalysis.Compute(v, Valid);

        Assert.True(a.Marks[0].HasFlag(CellMark.AboveMean));
        Assert.True(a.Marks[1].HasFlag(CellMark.BelowMean));
        Assert.False(a.Marks[0].HasFlag(CellMark.BelowMean));
    }

    [Fact]
    public void SegmentSigma_IsComputedPerSegment_NotGlobally()
    {
        // Segment 0 is spread out, segment 1 is completely flat
        var v = Uniform(3.90);
        for (int c = 0; c < 16; c++) v[c] = 3.80 + c * 0.01;

        var a = CellAnalysis.Compute(v, Valid);
        Assert.True(a.SegmentStdDev[0] > 0.03);
        Assert.Equal(0.0, a.SegmentStdDev[1], 9);
        Assert.Equal(3.90, a.SegmentMean[1], 6);
    }

    [Fact]
    public void SegmentOutlier_MarksOnlyCellsBeyondOneSigma()
    {
        // Segment 0: 15 cells at 3.90, one at 4.20 -> only that one is an outlier
        var v = Uniform(3.90);
        v[7] = 4.20;
        var a = CellAnalysis.Compute(v, Valid);

        Assert.True(a.Marks[7].HasFlag(CellMark.AboveSegmentSigma));
        for (int c = 0; c < 16; c++)
            if (c != 7)
                Assert.False(a.Marks[c].HasFlag(CellMark.AboveSegmentSigma));
    }

    [Fact]
    public void SegmentOutlier_AndGlobalMean_CanPointOppositeWays()
    {
        // Segment 0 sits low overall; its highest cell is "σ+" within the segment yet still
        // BELOW the pack mean -> the two marks point in opposite directions
        var v = Uniform(4.00);
        for (int c = 0; c < 16; c++) v[c] = 3.50;
        v[3] = 3.60;

        var a = CellAnalysis.Compute(v, Valid);
        Assert.True(a.Marks[3].HasFlag(CellMark.AboveSegmentSigma));
        Assert.True(a.Marks[3].HasFlag(CellMark.BelowMean));
    }

    [Fact]
    public void UniformPack_ProducesNoSigmaMarks()
    {
        var a = CellAnalysis.Compute(Uniform(3.90), Valid);
        Assert.All(a.Marks, m =>
        {
            Assert.False(m.HasFlag(CellMark.AboveSegmentSigma));
            Assert.False(m.HasFlag(CellMark.BelowSegmentSigma));
        });
    }

    [Fact]
    public void AllInvalid_ReturnsEmptyWithoutThrowing()
    {
        var a = CellAnalysis.Compute(new double[HvProtocol.CellCount], Valid);
        Assert.False(a.HasData);
        Assert.Equal(0, a.Mean);
    }
}
