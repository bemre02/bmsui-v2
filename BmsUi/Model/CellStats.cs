namespace BmsUi.Model;

public readonly record struct CellStat(
    double Min, int MinIndex, double Max, int MaxIndex, double Avg, int ValidCount)
{
    public static readonly CellStat None = new(0, -1, 0, -1, 0, 0);
    public bool HasData => ValidCount > 0;
}

public static class CellStats
{
    /// <summary>Voltage: cells below 0.5 V count as missing/stale and are excluded.</summary>
    public static CellStat Voltage(double[] values) => Compute(values, static v => v >= 0.5);

    /// <summary>Temperature: values outside -50..150 C are treated as sensor faults.</summary>
    public static CellStat Temperature(double[] values)
        => Compute(values, static t => t > -50 && t < 150);

    private static CellStat Compute(double[] values, Func<double, bool> isValid)
    {
        double min = double.MaxValue, max = double.MinValue, sum = 0;
        int minIdx = -1, maxIdx = -1, count = 0;

        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            if (!isValid(v)) continue;
            if (v < min) { min = v; minIdx = i; }
            if (v > max) { max = v; maxIdx = i; }
            sum += v;
            count++;
        }
        return count == 0 ? CellStat.None
                          : new CellStat(min, minIdx, max, maxIdx, sum / count, count);
    }
}
