namespace BmsUi.Ui;

/// <summary>
/// Colour and typography tokens for the UI. Kept in one place so the grid, the panel
/// and the form all speak the same system.
/// </summary>
public static class Theme
{
    // Surfaces
    public static readonly Color Page = Heatmap.FromHex(0x121211);
    public static readonly Color Card = Heatmap.FromHex(0x1A1A19);
    public static readonly Color Input = Heatmap.FromHex(0x26262A);
    public static readonly Color Hairline = Heatmap.FromHex(0x2C2C2A);

    // Ink
    public static readonly Color Ink = Color.White;
    public static readonly Color InkSecondary = Heatmap.FromHex(0xC3C2B7);
    public static readonly Color InkMuted = Heatmap.FromHex(0x898781);

    // Status (fixed status palette — never themed)
    public static readonly Color Good = Heatmap.FromHex(0x0CA30C);
    public static readonly Color Warning = Heatmap.FromHex(0xFAB219);
    public static readonly Color Critical = Heatmap.FromHex(0xD03B3B);

    public const string FamilyName = "Segoe UI";

    public static Font Section() => new(FamilyName, 8.25f, FontStyle.Bold);
    public static Font Hero() => new(FamilyName, 25f, FontStyle.Bold);
    public static Font HeroUnit() => new(FamilyName, 12f, FontStyle.Regular);
    public static Font TileValue() => new(FamilyName, 14.5f, FontStyle.Bold);
    public static Font Caption() => new(FamilyName, 8f, FontStyle.Regular);
    public static Font RowLabel() => new(FamilyName, 9f, FontStyle.Regular);
    public static Font RowValue() => new(FamilyName, 10.5f, FontStyle.Bold);
    public static Font Badge() => new(FamilyName, 8.5f, FontStyle.Bold);
}
