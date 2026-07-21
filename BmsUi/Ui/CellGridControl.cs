using System.ComponentModel;
using System.Drawing.Drawing2D;
using BmsUi.Model;
using BmsUi.Protocol;

namespace BmsUi.Ui;

public enum CellGridMode { Voltage, Temperature }

/// <summary>
/// 6 segment x 16 hucre owner-drawn izgara. Her hucre bir batarya sembolu olarak cizilir;
/// deger sembolun icine yazilir. 96 ayri Label yerine tek Paint — 5 Hz'de titremesin diye
/// cift tamponlu.
///
/// Renk: tek hue'lu sequential ramp (buyukluk). Alarm ramp'in parcasi degildir — status
/// rengi + uyari ikonu ile gosterilir, yani renk tek basina anlam tasimaz.
/// </summary>
public sealed class CellGridControl : Control
{
    private readonly double[] _values = new double[HvProtocol.CellCount];
    private readonly bool[] _balancing = new bool[HvProtocol.CellCount];
    private DisplaySettings _settings = new();

    public CellGridControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Heatmap.Surface;
    }

    [DefaultValue(CellGridMode.Voltage)]
    public CellGridMode Mode { get; set; } = CellGridMode.Voltage;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DisplaySettings Settings
    {
        get => _settings;
        set { _settings = value; Invalidate(); }
    }

    private bool IsVoltage => Mode == CellGridMode.Voltage;
    private Color[] Ramp => IsVoltage ? Heatmap.VoltageRamp : Heatmap.TemperatureRamp;
    private double ScaleLow => IsVoltage ? _settings.VoltageScaleLow : _settings.TempScaleLow;
    private double ScaleHigh => IsVoltage ? _settings.VoltageScaleHigh : _settings.TempScaleHigh;
    private double AlarmLow => IsVoltage ? _settings.VoltageAlarmLow : _settings.TempAlarmLow;
    private double AlarmHigh => IsVoltage ? _settings.VoltageAlarmHigh : _settings.TempAlarmHigh;
    private string Unit => IsVoltage ? "V" : "°C";

    /// <summary>UI thread'inden cagrilir (Form1.Invoke icinde).</summary>
    public void UpdateData(double[] values, Func<int, bool> isBalancing)
    {
        Array.Copy(values, _values, HvProtocol.CellCount);
        for (int i = 0; i < HvProtocol.CellCount; i++) _balancing[i] = isBalancing(i);
        Invalidate();
    }

    private CellState StateOf(double value) => IsVoltage
        ? Heatmap.VoltageState(value, AlarmLow, AlarmHigh)
        : Heatmap.TemperatureState(value, AlarmLow, AlarmHigh);

    private string FormatValue(double value, CellState state)
    {
        if (state == CellState.Invalid) return "—";
        return IsVoltage ? value.ToString("F3") : value.ToString("F1");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        const float segLabelWidth = 30f;
        const float legendHeight = 34f;
        const float pad = 4f;

        int cols = HvProtocol.CellsPerSegment;
        int rows = HvProtocol.SegmentCount;

        float gridTop = pad;
        float gridHeight = Height - pad * 2 - legendHeight;
        float gridLeft = segLabelWidth;
        float gridWidth = Width - segLabelWidth - pad;
        if (gridWidth < 40 || gridHeight < 40) return;

        float cellW = gridWidth / cols;
        float cellH = gridHeight / rows;

        float tileW = cellW - 3f;
        float tileH = cellH - 3f;

        // Genislik carpani 5 karakterlik "3,550"/"-12,5" metnine gore secildi (kutunun ~%80'i)
        float valueSize = Math.Clamp(Math.Min(tileW * 0.205f, tileH * 0.38f), 7.5f, 18f);
        float indexSize = Math.Clamp(tileH * 0.155f, 6.5f, 11f);

        using var valueFont = new Font("Segoe UI", valueSize, FontStyle.Bold, GraphicsUnit.Point);
        using var indexFont = new Font("Segoe UI", indexSize, FontStyle.Regular, GraphicsUnit.Point);
        using var legendFont = new Font("Segoe UI", Math.Clamp(indexSize, 7.5f, 9.5f),
                                        FontStyle.Regular, GraphicsUnit.Point);
        using var segFont = new Font("Segoe UI", Math.Clamp(cellH * 0.20f, 9f, 14f),
                                     FontStyle.Bold, GraphicsUnit.Point);
        using var centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
        };
        using var mutedBrush = new SolidBrush(Heatmap.MutedInk);

        for (int seg = 0; seg < rows; seg++)
        {
            float rowY = gridTop + seg * cellH;
            g.DrawString($"S{seg + 1}", segFont, mutedBrush,
                         new RectangleF(0, rowY, segLabelWidth, cellH), centered);

            for (int c = 0; c < cols; c++)
            {
                int index = seg * cols + c;
                var tile = new RectangleF(gridLeft + c * cellW + 1.5f, rowY + 1.5f, tileW, tileH);
                DrawCellTile(g, tile, index, _values[index], StateOf(_values[index]),
                             _balancing[index], valueFont, indexFont, centered);
            }
        }

        DrawLegend(g, new RectangleF(gridLeft, Height - legendHeight, gridWidth, legendHeight - pad),
                   legendFont);
    }

    /// <summary>
    /// Hucre kutusu: cerceve yok — cerceve 96 kez tekrarlandiginda bilgi tasimadan
    /// dolgu alanini yiyor. Dolgu buyuklugu renkle, alttaki cubuk ise UZUNLUKLA kodlar;
    /// paket dengeliyken renkler birbirine yakin kalir ama cubuk farki hemen gosterir.
    /// </summary>
    private void DrawCellTile(Graphics g, RectangleF tile, int index, double value,
                              CellState state, bool balancing, Font valueFont, Font indexFont,
                              StringFormat centered)
    {
        if (tile.Width < 8 || tile.Height < 8) return;

        Color fill = Heatmap.Fill(state, value, ScaleLow, ScaleHigh, Ramp);
        Color ink = Heatmap.InkOn(fill);
        float radius = Math.Min(5f, Math.Min(tile.Width, tile.Height) * 0.18f);

        using (var path = RoundedRect(tile, radius))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        // Balans: sol kenarda altin serit (halka yerine — 96 hucrede halka gurultu yapiyor)
        if (balancing)
        {
            using var clip = RoundedRect(tile, radius);
            var saved = g.Save();
            g.SetClip(clip);
            using (var strip = new SolidBrush(Heatmap.BalanceRing))
                g.FillRectangle(strip, tile.X, tile.Y, Math.Max(3f, tile.Width * 0.075f),
                                tile.Height);
            g.Restore(saved);
        }

        // Indeks kutunun icinde, sol ustte — disarida dursa dikey alani bosa harciyordu
        using (var indexBrush = new SolidBrush(Color.FromArgb(165, ink)))
            g.DrawString(index.ToString(), indexFont, indexBrush,
                         tile.X + (balancing ? tile.Width * 0.10f : 3f), tile.Y + 1f);

        // Deger
        using (var inkBrush = new SolidBrush(ink))
            g.DrawString(FormatValue(value, state), valueFont, inkBrush,
                         new RectangleF(tile.X, tile.Y + tile.Height * 0.10f, tile.Width,
                                        tile.Height * 0.72f), centered);

        // Skala icindeki konum — renkten cok daha keskin ayirt eden ikinci kanal
        if (state != CellState.Invalid) DrawMagnitudeBar(g, tile, value, ink);

        // Alarm: renk tek basina birakilmaz, ustune uyari ikonu cizilir
        if (state == CellState.Alarm) DrawWarningBadge(g, tile);
    }

    private void DrawMagnitudeBar(Graphics g, RectangleF tile, double value, Color ink)
    {
        float inset = Math.Max(3f, tile.Width * 0.08f);
        float h = Math.Clamp(tile.Height * 0.075f, 2.5f, 6f);
        var track = new RectangleF(tile.X + inset, tile.Bottom - h - inset * 0.6f,
                                   tile.Width - inset * 2f, h);
        if (track.Width < 6f) return;

        double t = ScaleHigh > ScaleLow
            ? Math.Clamp((value - ScaleLow) / (ScaleHigh - ScaleLow), 0.0, 1.0)
            : 0.0;

        using (var trackBrush = new SolidBrush(Color.FromArgb(55, ink)))
        using (var path = RoundedRect(track, h / 2f))
            g.FillPath(trackBrush, path);

        var filled = new RectangleF(track.X, track.Y, Math.Max(h, (float)(track.Width * t)), h);
        using (var fillBrush = new SolidBrush(Color.FromArgb(205, ink)))
        using (var path = RoundedRect(filled, h / 2f))
            g.FillPath(fillBrush, path);
    }

    /// <summary>Uyari rozeti: govdenin sag ust kosesinde uclu + unlem.</summary>
    private static void DrawWarningBadge(Graphics g, RectangleF body)
    {
        float size = Math.Clamp(Math.Min(body.Height * 0.46f, body.Width * 0.30f), 8f, 18f);
        float x = body.Right - size - 1.5f;
        float y = body.Y + 1.5f;

        var tri = new[]
        {
            new PointF(x + size / 2f, y),
            new PointF(x + size, y + size * 0.88f),
            new PointF(x, y + size * 0.88f),
        };

        using (var shadow = new Pen(Color.FromArgb(200, 20, 20, 20), Math.Max(1.6f, size * 0.16f))
               { LineJoin = LineJoin.Round })
            g.DrawPolygon(shadow, tri);
        using (var brush = new SolidBrush(Heatmap.WarningColor))
            g.FillPolygon(brush, tri);

        // Unlem isareti
        float barW = Math.Max(1.2f, size * 0.11f);
        using var ink = new SolidBrush(Color.FromArgb(20, 20, 20));
        g.FillRectangle(ink, x + size / 2f - barW / 2f, y + size * 0.30f, barW, size * 0.34f);
        g.FillRectangle(ink, x + size / 2f - barW / 2f, y + size * 0.71f, barW, barW);
    }

    /// <summary>Alt seritte renk skalasi + alarm esikleri.</summary>
    private void DrawLegend(Graphics g, RectangleF area, Font font)
    {
        if (area.Width < 80 || area.Height < 14) return;

        float barH = Math.Min(11f, area.Height * 0.45f);
        var bar = new RectangleF(area.X, area.Y, area.Width * 0.42f, barH);

        // Ramp adimlarini bitisik dikdortgenler olarak ciz
        var ramp = Ramp;
        float stepW = bar.Width / ramp.Length;
        for (int i = 0; i < ramp.Length; i++)
            using (var b = new SolidBrush(ramp[i]))
                g.FillRectangle(b, bar.X + i * stepW, bar.Y, stepW + 0.6f, bar.Height);

        using (var hairline = new Pen(Heatmap.Hairline))
            g.DrawRectangle(hairline, bar.X, bar.Y, bar.Width, bar.Height);

        using var muted = new SolidBrush(Heatmap.MutedInk);
        using var left = new StringFormat { Alignment = StringAlignment.Near };
        using var right = new StringFormat { Alignment = StringAlignment.Far };

        string lo = IsVoltage ? $"{ScaleLow:F2}" : $"{ScaleLow:F0}";
        string hi = IsVoltage ? $"{ScaleHigh:F2} {Unit}" : $"{ScaleHigh:F0} {Unit}";
        var textRow = new RectangleF(bar.X, bar.Bottom + 1.5f, bar.Width, area.Height - barH - 2f);
        g.DrawString(lo, font, muted, textRow, left);
        g.DrawString(hi, font, muted, textRow, right);

        // Alarm esikleri — ikonla ayni status rengi
        string alarmText = IsVoltage
            ? $"Alarm: < {AlarmLow:F2} V  veya  > {AlarmHigh:F2} V"
            : $"Alarm: < {AlarmLow:F0} °C  veya  > {AlarmHigh:F0} °C";
        var alarmRect = new RectangleF(bar.Right + 16f, area.Y, area.Width - bar.Width - 16f,
                                       area.Height);
        using var alarmFmt = new StringFormat { LineAlignment = StringAlignment.Center };
        using var alarmBrush = new SolidBrush(Heatmap.WarningColor);
        var badgeBody = new RectangleF(bar.Right + 16f, area.Y + 1f, 14f, 14f);
        DrawWarningBadge(g, new RectangleF(badgeBody.X, badgeBody.Y, 16f, 16f));
        g.DrawString(alarmText, font, alarmBrush,
                     new RectangleF(alarmRect.X + 20f, alarmRect.Y, alarmRect.Width - 20f,
                                    area.Height * 0.62f), alarmFmt);

        // Balans gostergesi
        using var balanceBrush = new SolidBrush(Heatmap.BalanceRing);
        g.DrawString("Altın çerçeve = balansta", font, balanceBrush,
                     new RectangleF(alarmRect.X + 20f, area.Y + area.Height * 0.5f,
                                    alarmRect.Width - 20f, area.Height * 0.5f), alarmFmt);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1f, radius * 2f);
        if (d >= r.Width || d >= r.Height)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
