namespace BmsUi.Ui;

public enum CellState
{
    Normal,
    /// <summary>Below or above an alarm threshold — outline + warning icon.</summary>
    Alarm,
    /// <summary>Missing / stale cell (0.00 V and the like).</summary>
    Invalid,
}

/// <summary>
/// Value-to-colour mapping. The fill always carries magnitude; an alarm never replaces
/// the fill, it adds an outline plus a warning icon — so colour never carries meaning on
/// its own and the grid stays readable even when a threshold is misconfigured.
/// </summary>
public static class Heatmap
{
    /// <summary>
    /// Voltage: low = red, mid = yellow, high = green (what a BMS operator expects to
    /// read). Red-green is the riskiest pair for colour blindness, which is why the value
    /// is printed on every cell and cells outside the thresholds also get a warning icon —
    /// colour alone never carries the information.
    /// </summary>
    public static readonly Color[] VoltageRamp =
    {
        FromHex(0x7F1D1D), FromHex(0x991B1B), FromHex(0xB91C1C), FromHex(0xDC2626),
        FromHex(0xE85D25), FromHex(0xF97316), FromHex(0xF59E0B), FromHex(0xEAB308),
        FromHex(0xC9C520), FromHex(0x9DC22A), FromHex(0x6FB93A), FromHex(0x45AC49),
        FromHex(0x22C55E),
    };

    // Single-hue amber ramp for temperature. A second hue is safe because it never shares
    // a screen with the voltage ramp (separate tabs); one hue with monotonic lightness is
    // inherently colour-blind safe.
    public static readonly Color[] TemperatureRamp =
    {
        FromHex(0x3A1F05), FromHex(0x4D2A07), FromHex(0x63380A), FromHex(0x7A460C),
        FromHex(0x94560E), FromHex(0xAD6610), FromHex(0xC67712), FromHex(0xDB8A1F),
        FromHex(0xE89C3C), FromHex(0xF0AE5E), FromHex(0xF5C084), FromHex(0xF8D2A8),
        FromHex(0xFBE3CB),
    };

    // Status palette — fixed, never used as a series colour
    public static readonly Color AlarmColor = FromHex(0xD03B3B);   // critical
    public static readonly Color WarningColor = FromHex(0xFAB219); // warning
    public static readonly Color InvalidColor = FromHex(0x4A4A48);

    // Dark theme surfaces / ink
    public static readonly Color Surface = FromHex(0x1A1A19);
    public static readonly Color PrimaryInk = Color.White;
    public static readonly Color MutedInk = FromHex(0x898781);
    public static readonly Color Hairline = FromHex(0x2C2C2A);
    public static readonly Color BalanceRing = FromHex(0xFAB219);

    public static Color FromHex(int rgb)
        => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>Maps a value in [low, high] onto a ramp step (clamped at both ends).</summary>
    public static Color Sequential(double value, double low, double high, Color[] ramp)
    {
        if (high <= low) return ramp[0];
        double t = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        int index = (int)Math.Round(t * (ramp.Length - 1));
        return ramp[Math.Clamp(index, 0, ramp.Length - 1)];
    }

    public static CellState VoltageState(double v, double alarmLow, double alarmHigh)
        => v < 0.5 ? CellState.Invalid
         : (v < alarmLow || v > alarmHigh) ? CellState.Alarm
         : CellState.Normal;

    public static CellState TemperatureState(double t, double alarmLow, double alarmHigh)
        => (t <= -50 || t >= 150) ? CellState.Invalid
         : (t < alarmLow || t > alarmHigh) ? CellState.Alarm
         : CellState.Normal;

    /// <summary>
    /// The fill ALWAYS shows the value; only an invalid cell falls back to grey.
    ///
    /// An alarm does NOT change the fill: the low end of the voltage ramp is already red,
    /// so a red alarm fill would be confused with a "low but normal" cell. Worse, a badly
    /// set threshold would flatten the whole grid to one colour. The alarm is shown as an
    /// outline plus a warning icon instead.
    /// </summary>
    public static Color Fill(CellState state, double value, double scaleLow, double scaleHigh,
                             Color[] ramp) => state == CellState.Invalid
        ? InvalidColor
        : Sequential(value, scaleLow, scaleHigh, ramp);

    /// <summary>
    /// Pure black, not #0B0B0B. A ramp running dark-to-light must pass through the point
    /// where white and dark ink tie on contrast, and the ceiling at that point is set by
    /// how dark the dark ink is. With #0B0B0B that floor was 4.44 (below the AA threshold
    /// of 4.5); pure black raises it to 4.58.
    /// </summary>
    public static readonly Color DarkInk = Color.Black;

    /// <summary>
    /// Text colour for a given fill: whichever of white or dark ink gives more contrast.
    ///
    /// The naive "perceived brightness + fixed threshold" approach was wrong here:
    /// saturated green (#22C55E) scores 0.535 in that formula, falls below the threshold
    /// and picked white — while real contrast is 2.3 on white versus 8.6 on dark. As a
    /// cell drifted in and out of the top ramp step its text kept flipping colour.
    /// The two options are now compared using WCAG relative luminance.
    /// </summary>
    public static Color InkOn(Color fill)
    {
        double l = RelativeLuminance(fill);
        double whiteContrast = 1.05 / (l + 0.05);
        double darkContrast = (l + 0.05) / (RelativeLuminance(DarkInk) + 0.05);
        return whiteContrast >= darkContrast ? Color.White : DarkInk;
    }

    /// <summary>WCAG 2.x relative luminance (with sRGB linearisation).</summary>
    public static double RelativeLuminance(Color c)
        => 0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);

    private static double Linearize(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
