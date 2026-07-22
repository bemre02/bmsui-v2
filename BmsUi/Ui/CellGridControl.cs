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
    private CellAnalysis _analysis = CellAnalysis.Empty;
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
        _analysis = CellAnalysis.Compute(_values, IsValidValue);
        Invalidate();
    }

    internal CellAnalysis Analysis => _analysis;

    private bool IsValidValue(double v) => IsVoltage ? v >= 0.5 : v > -50 && v < 150;

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
        const float legendHeight = 40f;
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
        // Indeks ve segment etiketleri kalin: soluk gri okunmuyordu
        using var indexFont = new Font("Segoe UI", indexSize, FontStyle.Bold, GraphicsUnit.Point);
        using var markFont = new Font("Segoe UI", Math.Clamp(indexSize * 1.3f, 8.5f, 13.5f),
                                      FontStyle.Bold, GraphicsUnit.Point);
        using var legendFont = new Font("Segoe UI", Math.Clamp(indexSize, 7.5f, 9.5f),
                                        FontStyle.Regular, GraphicsUnit.Point);
        using var segFont = new Font("Segoe UI", Math.Clamp(cellH * 0.24f, 10f, 16f),
                                     FontStyle.Bold, GraphicsUnit.Point);
        using var centered = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.None,
        };
        using var segBrush = new SolidBrush(Heatmap.PrimaryInk);

        for (int seg = 0; seg < rows; seg++)
        {
            float rowY = gridTop + seg * cellH;
            g.DrawString($"S{seg + 1}", segFont, segBrush,
                         new RectangleF(0, rowY, segLabelWidth, cellH), centered);

            for (int c = 0; c < cols; c++)
            {
                int index = seg * cols + c;
                var tile = new RectangleF(gridLeft + c * cellW + 1.5f, rowY + 1.5f, tileW, tileH);
                DrawCellTile(g, tile, index, _values[index], StateOf(_values[index]),
                             _balancing[index], _analysis.Marks[index],
                             valueFont, indexFont, markFont, centered);
            }
        }

        DrawLegend(g, new RectangleF(gridLeft, Height - legendHeight, gridWidth, legendHeight - pad),
                   legendFont);
    }

    /// <summary>
    /// Hücre bir DİKEY PİL silueti olarak çizilir: gövde + üstte kutup.
    ///
    /// Gövde ve kutup TEK bir path olarak kurulur; ayrı ayrı çizilseydi alarm konturu
    /// ikisinin birleştiği iç kenarları da çizer ve siluet bozulurdu.
    ///
    /// İçerik gövdeye yerleşir: indeks (sol üst), genel ortalama oku (sağ üst),
    /// balans "B" (sol alt), segment sigma işareti (sağ alt).
    /// </summary>
    private void DrawCellTile(Graphics g, RectangleF tile, int index, double value,
                              CellState state, bool balancing, CellMark mark, Font valueFont,
                              Font indexFont, Font markFont, StringFormat centered)
    {
        if (tile.Width < 8 || tile.Height < 8) return;

        Color fill = Heatmap.Fill(state, value, ScaleLow, ScaleHigh, Ramp);
        Color ink = Heatmap.InkOn(fill);

        using (var path = BatteryPath(tile, out _))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        BatteryPath(tile, out var body).Dispose();

        // Indeks govdenin sol ustunde
        using (var indexBrush = new SolidBrush(Color.FromArgb(235, ink)))
            g.DrawString(index.ToString(), indexFont, indexBrush, body.X + 3f, body.Y + 1f);

        // Deger — alt kosedeki rozetlere yer birakacak sekilde ortalanir
        using (var inkBrush = new SolidBrush(ink))
            g.DrawString(FormatValue(value, state), valueFont, inkBrush,
                         new RectangleF(body.X, body.Y + body.Height * 0.10f, body.Width,
                                        body.Height * 0.68f), centered);

        if (balancing) DrawBalanceBadge(g, body, markFont, ink);

        if (state != CellState.Invalid)
            DrawStatMarks(g, body, mark, ink, markFont, state == CellState.Alarm);

        // Alarm: dolgu degeri gostermeye devam eder; alarm kontur + ikonla gelir.
        // Kontur ⚠ ikonuyla AYNI amber renkte; altindaki koyu kilif sayesinde amber
        // dolgu uzerinde de sinir gorunur kalir. Kontur pil siluetini takip eder.
        if (state == CellState.Alarm)
        {
            float w = Math.Max(2f, tile.Height * 0.042f);
            using (var casing = new Pen(Color.FromArgb(190, 25, 25, 25), w + 1.8f))
            using (var path = BatteryPath(tile, out _))
                g.DrawPath(casing, path);
            using (var pen = new Pen(Heatmap.WarningColor, w))
            using (var path = BatteryPath(tile, out _))
                g.DrawPath(pen, path);
            DrawWarningBadge(g, body);
        }
    }

    /// <summary>
    /// Dikey pil silueti: yuvarlatilmis govde + ust kenarin ortasinda kutup.
    /// Kose yaylari arasindaki duz kenarlar ACIK cizgilerle veriliyor; AddArc'in
    /// kendi bagladigi cizgiler capraz olur ve kutup ucgene doner.
    /// </summary>
    private static GraphicsPath BatteryPath(RectangleF cell, out RectangleF body)
    {
        float nubH = Math.Clamp(cell.Height * 0.075f, 3f, 10f);
        body = new RectangleF(cell.X, cell.Y + nubH, cell.Width, cell.Height - nubH);

        var path = new GraphicsPath();
        if (body.Width < 16f || body.Height < 16f)
        {
            path.AddRectangle(body);
            return path;
        }

        float r = Math.Min(5f, Math.Min(body.Width, body.Height) * 0.18f);
        float d = r * 2f;
        float nubW = Math.Clamp(body.Width * 0.32f, 10f, 36f);
        float nubLeft = body.X + (body.Width - nubW) / 2f;
        float nubRight = nubLeft + nubW;
        float nr = Math.Min(nubW * 0.28f, nubH * 0.7f);
        float nd = nr * 2f;

        path.AddArc(body.X, body.Y, d, d, 180, 90);                 // govde sol ust
        path.AddLine(body.X + r, body.Y, nubLeft, body.Y);          // govde ustu
        path.AddLine(nubLeft, body.Y, nubLeft, cell.Y + nr);        // kutup sol kenari
        path.AddArc(nubLeft, cell.Y, nd, nd, 180, 90);              // kutup sol ust
        path.AddLine(nubLeft + nr, cell.Y, nubRight - nr, cell.Y);  // kutup ustu
        path.AddArc(nubRight - nd, cell.Y, nd, nd, 270, 90);        // kutup sag ust
        path.AddLine(nubRight, cell.Y + nr, nubRight, body.Y);      // kutup sag kenari
        path.AddLine(nubRight, body.Y, body.Right - r, body.Y);     // govde ustu
        path.AddArc(body.Right - d, body.Y, d, d, 270, 90);         // govde sag ust
        path.AddArc(body.Right - d, body.Bottom - d, d, d, 0, 90);  // govde sag alt
        path.AddArc(body.X, body.Bottom - d, d, d, 90, 90);         // govde sol alt
        path.CloseFigure();
        return path;
    }

    /// <summary>
    /// Balans rozeti: sol alt kosede altin zemin uzerinde "B".
    /// Kontur sart: altin renk, ramp'in sari-turuncu ortasinda dolguyla neredeyse
    /// ayni tona dusup rozeti gorunmez yapiyor.
    /// </summary>
    private static void DrawBalanceBadge(Graphics g, RectangleF tile, Font font, Color ink)
    {
        float size = Math.Clamp(Math.Min(tile.Height * 0.32f, tile.Width * 0.28f), 12f, 22f);
        var badge = new RectangleF(tile.X + 3f, tile.Bottom - size - 3f, size, size);

        using (var brush = new SolidBrush(Heatmap.BalanceRing))
        using (var outline = new Pen(ink, Math.Max(1.3f, size * 0.09f)))
        using (var path = RoundedRect(badge, size * 0.28f))
        {
            g.FillPath(brush, path);
            g.DrawPath(outline, path);
        }

        using var textBrush = new SolidBrush(Heatmap.InkOn(Heatmap.BalanceRing));
        using var fmt = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString("B", font, textBrush, badge, fmt);
    }

    /// <summary>
    /// Iki bagimsiz istatistik isareti:
    ///   ▲/▼ (sag ust)  — 96 hucrenin GENEL ortalamasinin ustunde/altinda
    ///   σ+ / σ− (sag alt) — kendi SEGMENTinin ortalamasindan 1σ'dan fazla sapmis
    /// Ayri tutulmalari sart: bir segment tumuyle paket ortalamasinin altindaysa, o
    /// segmentin en yuksek hucresi "σ+" ama yine de "▼" olabilir.
    /// </summary>
    private static void DrawStatMarks(Graphics g, RectangleF tile, CellMark mark, Color ink,
                                      Font markFont, bool alarmPresent)
    {
        if (mark == CellMark.None || tile.Width < 26f) return;

        // Genel ortalamaya gore yon — her hucrede var, o yuzden soluk.
        // Alarm ikonu da sag ust kosede duruyor; varsa ok onun soluna kayar.
        string? arrow = mark.HasFlag(CellMark.AboveMean) ? "▲"
                      : mark.HasFlag(CellMark.BelowMean) ? "▼" : null;
        if (arrow is not null)
        {
            float badgeWidth = alarmPresent ? WarningBadgeSize(tile) + 3f : 0f;
            using var brush = new SolidBrush(Color.FromArgb(150, ink));
            var size = g.MeasureString(arrow, markFont);
            g.DrawString(arrow, markFont, brush,
                         tile.Right - size.Width - 1f - badgeWidth, tile.Y + 1f);
        }

        // Segment ici aykirilik — az sayida hucrede cikar, o yuzden belirgin
        string? sigma = mark.HasFlag(CellMark.AboveSegmentSigma) ? "σ+"
                      : mark.HasFlag(CellMark.BelowSegmentSigma) ? "σ−" : null;
        if (sigma is not null)
        {
            using var brush = new SolidBrush(Color.FromArgb(245, ink));
            var size = g.MeasureString(sigma, markFont);
            g.DrawString(sigma, markFont, brush, tile.Right - size.Width - 1f,
                         tile.Bottom - size.Height - tile.Height * 0.13f);
        }
    }

    private static float WarningBadgeSize(RectangleF body)
        => Math.Clamp(Math.Min(body.Height * 0.46f, body.Width * 0.30f), 8f, 18f);

    /// <summary>Uyari rozeti: govdenin sag ust kosesinde uclu + unlem.</summary>
    private static void DrawWarningBadge(Graphics g, RectangleF body)
    {
        float size = WarningBadgeSize(body);
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

    /// <summary>Alt şerit: renk skalası, alarm eşikleri ve işaret açıklamaları.</summary>
    private void DrawLegend(Graphics g, RectangleF area, Font font)
    {
        if (area.Width < 80 || area.Height < 14) return;

        float barH = Math.Min(11f, area.Height * 0.38f);
        var bar = new RectangleF(area.X, area.Y, Math.Min(area.Width * 0.30f, 260f), barH);

        var ramp = Ramp;
        float stepW = bar.Width / ramp.Length;
        for (int i = 0; i < ramp.Length; i++)
            using (var b = new SolidBrush(ramp[i]))
                g.FillRectangle(b, bar.X + i * stepW, bar.Y, stepW + 0.6f, bar.Height);

        using (var hairline = new Pen(Heatmap.Hairline))
            g.DrawRectangle(hairline, bar.X, bar.Y, bar.Width, bar.Height);

        using var muted = new SolidBrush(Heatmap.MutedInk);
        using var near = new StringFormat { Alignment = StringAlignment.Near };
        using var far = new StringFormat { Alignment = StringAlignment.Far };

        string lo = IsVoltage ? $"{ScaleLow:F2}" : $"{ScaleLow:F0}";
        string hi = IsVoltage ? $"{ScaleHigh:F2} {Unit}" : $"{ScaleHigh:F0} {Unit}";
        var scaleText = new RectangleF(bar.X, bar.Bottom + 1.5f, bar.Width,
                                       area.Height - barH - 2f);
        g.DrawString(lo, font, muted, scaleText, near);
        g.DrawString(hi, font, muted, scaleText, far);

        float colW = Math.Max(150f, (area.Width - bar.Width - 24f) / 2f);
        float rowH = area.Height * 0.5f;

        // 1. sutun: alarm ve balans
        float x1 = bar.Right + 24f;
        DrawWarningBadge(g, new RectangleF(x1, area.Y + 1f, 15f, 15f));
        string alarmText = IsVoltage
            ? $"eşik dışı: < {AlarmLow:F2} / > {AlarmHigh:F2} V"
            : $"eşik dışı: < {AlarmLow:F0} / > {AlarmHigh:F0} °C";
        using (var warn = new SolidBrush(Heatmap.WarningColor))
            g.DrawString(alarmText, font, warn, x1 + 19f, area.Y);
        using (var gold = new SolidBrush(Heatmap.BalanceRing))
        using (var badgePath = RoundedRect(new RectangleF(x1 + 1f, area.Y + rowH + 1f, 13f, 13f), 4f))
        {
            g.FillPath(gold, badgePath);
            using (var badgeInk = new SolidBrush(Heatmap.InkOn(Heatmap.BalanceRing)))
            using (var fmt = new StringFormat
                   { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString("B", font, badgeInk,
                             new RectangleF(x1 + 1f, area.Y + rowH + 1f, 13f, 13f), fmt);
            g.DrawString("balansta", font, gold, x1 + 19f, area.Y + rowH);
        }

        // 2. sutun: istatistik isaretleri + o anki degerler
        float x2 = x1 + colW;
        if (x2 + 60f > area.Right) return;

        string meanText = _analysis.HasData
            ? (IsVoltage ? $"ort {_analysis.Mean:F3} V" : $"ort {_analysis.Mean:F1} °C")
            : "ort —";
        // Paket geneli sigma; hucre isaretleri SEGMENT sigmasina gore verildigi icin
        // etiket "paket" diyerek karisikligi onluyor
        string sigmaText = _analysis.HasData
            ? (IsVoltage ? $"paket σ {_analysis.StdDev * 1000:F1} mV"
                         : $"paket σ {_analysis.StdDev:F2} °C")
            : "paket σ —";

        // Degerler aciklamalarin ARDINA yazilir: ayri sutun dar pencerede sigmiyordu
        using var ink = new SolidBrush(Heatmap.PrimaryInk);
        g.DrawString($"▲▼ genel ort. üstü / altı  ·  {meanText}", font, muted, x2, area.Y);
        g.DrawString($"σ+ σ−  segment ±1σ dışı  ·  {sigmaText}", font, ink, x2, area.Y + rowH);
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
