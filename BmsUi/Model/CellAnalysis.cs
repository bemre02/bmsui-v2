using BmsUi.Protocol;

namespace BmsUi.Model;

/// <summary>Statistical position of a cell — more than one flag can apply at once.</summary>
[Flags]
public enum CellMark
{
    None = 0,
    /// <summary>Above the overall mean of all 96 cells.</summary>
    AboveMean = 1 << 0,
    /// <summary>Below the overall mean of all 96 cells.</summary>
    BelowMean = 1 << 1,
    /// <summary>More than +1 sigma away from its own segment mean.</summary>
    AboveSegmentSigma = 1 << 2,
    /// <summary>More than -1 sigma away from its own segment mean.</summary>
    BelowSegmentSigma = 1 << 3,
}

/// <summary>
/// Overall mean of the 96 cells plus the standard deviation within each segment.
///
/// Segment sigma is computed over ALL 16 cells of that segment (the whole population, not
/// a sample) — hence divided by N, not N-1.
///
/// Deviation within a segment catches a cell drifting away from its neighbours, while the
/// overall mean gives its position relative to the whole pack. If a segment sits entirely
/// below the pack mean those two can point in opposite directions, which is why they are
/// tracked as separate flags.
/// </summary>
public sealed class CellAnalysis
{
    private CellAnalysis(double mean, double stdDev, double[] segmentMean,
                         double[] segmentStdDev, CellMark[] marks, int validCount)
    {
        Mean = mean;
        StdDev = stdDev;
        SegmentMean = segmentMean;
        SegmentStdDev = segmentStdDev;
        Marks = marks;
        ValidCount = validCount;
    }

    public double Mean { get; }
    public double StdDev { get; }
    public double[] SegmentMean { get; }
    public double[] SegmentStdDev { get; }
    public CellMark[] Marks { get; }
    public int ValidCount { get; }

    public bool HasData => ValidCount > 0;

    public static readonly CellAnalysis Empty = new(
        0, 0,
        new double[HvProtocol.SegmentCount], new double[HvProtocol.SegmentCount],
        new CellMark[HvProtocol.CellCount], 0);

    public static CellAnalysis Compute(double[] values, Func<double, bool> isValid)
    {
        int cells = HvProtocol.CellCount;
        int segments = HvProtocol.SegmentCount;
        int perSegment = HvProtocol.CellsPerSegment;

        var segmentMean = new double[segments];
        var segmentSigma = new double[segments];
        var marks = new CellMark[cells];

        double sum = 0;
        int validCount = 0;
        for (int i = 0; i < cells; i++)
        {
            if (!isValid(values[i])) continue;
            sum += values[i];
            validCount++;
        }
        if (validCount == 0) return Empty;

        double mean = sum / validCount;

        double variance = 0;
        for (int i = 0; i < cells; i++)
        {
            if (!isValid(values[i])) continue;
            double d = values[i] - mean;
            variance += d * d;
        }
        double stdDev = Math.Sqrt(variance / validCount);

        for (int seg = 0; seg < segments; seg++)
        {
            double segSum = 0;
            int segCount = 0;
            for (int c = 0; c < perSegment; c++)
            {
                double v = values[seg * perSegment + c];
                if (!isValid(v)) continue;
                segSum += v;
                segCount++;
            }
            if (segCount == 0) continue;

            segmentMean[seg] = segSum / segCount;

            double segVar = 0;
            for (int c = 0; c < perSegment; c++)
            {
                double v = values[seg * perSegment + c];
                if (!isValid(v)) continue;
                double d = v - segmentMean[seg];
                segVar += d * d;
            }
            segmentSigma[seg] = Math.Sqrt(segVar / segCount);
        }

        for (int i = 0; i < cells; i++)
        {
            double v = values[i];
            if (!isValid(v)) continue;

            var mark = CellMark.None;
            if (v > mean) mark |= CellMark.AboveMean;
            else if (v < mean) mark |= CellMark.BelowMean;

            int seg = i / perSegment;
            double sigma = segmentSigma[seg];
            if (sigma > 0)
            {
                double delta = v - segmentMean[seg];
                if (delta > sigma) mark |= CellMark.AboveSegmentSigma;
                else if (delta < -sigma) mark |= CellMark.BelowSegmentSigma;
            }
            marks[i] = mark;
        }

        return new CellAnalysis(mean, stdDev, segmentMean, segmentSigma, marks, validCount);
    }
}
