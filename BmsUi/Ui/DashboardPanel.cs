using System.Drawing.Drawing2D;
using BmsUi.Model;
using BmsUi.Protocol;

namespace BmsUi.Ui;

/// <summary>Baglanti sayaclari — panelin alt seridinde gosterilir.</summary>
public readonly record struct LinkHealth(int CrcErrors, int Timeouts, int IdMismatches);

/// <summary>
/// Sol pano: paket ozeti, hucre istatistikleri, cikislar, aktif hatalar ve baglanti
/// sagligi. Onlarca Label/GroupBox yerine TEK owner-drawn kontrol — tipografi hiyerarsisi
/// ve hizalama boylece tutarli kaliyor.
///
/// Hata listesi YALNIZCA aktif olanlari gosterir; 15 satirlik pasif liste ekranin
/// yarisini kaplayip gercek bir hatayi goze batmaz hale getiriyordu.
/// </summary>
public sealed class DashboardPanel : Control
{
    private BmsSnapshot? _snapshot;
    private LinkHealth _health;
    private bool _connected;

    public DashboardPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Page;
    }

    internal BmsSnapshot? CurrentSnapshot => _snapshot;

    public void UpdateData(BmsSnapshot? snapshot, LinkHealth health, bool connected)
    {
        _snapshot = snapshot;
        _health = health;
        _connected = connected;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(Theme.Page);

        const float pad = 12f;
        float width = Width - pad * 2;
        if (width < 120) return;

        using var section = Theme.Section();
        using var hero = Theme.Hero();
        using var heroUnit = Theme.HeroUnit();
        using var tileValue = Theme.TileValue();
        using var caption = Theme.Caption();
        using var rowLabel = Theme.RowLabel();
        using var rowValue = Theme.RowValue();
        using var badge = Theme.Badge();

        float y = pad;
        y = DrawPackSection(g, pad, y, width, section, hero, heroUnit, tileValue, caption);
        y = DrawCellSection(g, pad, y, width, section, rowLabel, rowValue, caption);
        y = DrawOutputSection(g, pad, y, width, section, badge);
        y = DrawFaultSection(g, pad, y, width, section, rowLabel, badge);
        DrawLinkSection(g, pad, y, width, section, caption);
    }

    // ------------------------------------------------------------------ bolumler

    private float DrawPackSection(Graphics g, float x, float y, float w, Font section,
                                  Font hero, Font heroUnit, Font tileValue, Font caption)
    {
        y = DrawSectionHeader(g, x, y, w, "PAKET", section);

        // Hero: paket voltaji
        string v = _snapshot is null ? "—" : _snapshot.PackVoltage.ToString("F2");
        using (var ink = new SolidBrush(Theme.Ink))
            g.DrawString(v, hero, ink, x - 3f, y - 6f);
        SizeF heroSize = g.MeasureString(v, hero);
        using (var muted = new SolidBrush(Theme.InkSecondary))
            g.DrawString("V", heroUnit, muted, x - 3f + heroSize.Width - 6f, y + 16f);
        using (var muted = new SolidBrush(Theme.InkMuted))
            g.DrawString("paket voltajı", caption, muted, x, y + 40f);
        y += 62f;

        // 2x2 stat kutulari
        float gap = 8f;
        float tileW = (w - gap) / 2f;
        const float tileH = 52f;

        string current = _snapshot is null ? "—" : $"{_snapshot.PackCurrent:F1}";
        string power = _snapshot is null ? "—" : $"{_snapshot.PowerKw:F2}";
        string soc = _snapshot is null ? "—" : $"{_snapshot.SocPercent:F1}";
        string slave = _snapshot is null ? "—" : $"{_snapshot.MaxSlaveTemp:F1}";

        DrawTile(g, new RectangleF(x, y, tileW, tileH), current, "A", "akım", tileValue, caption);
        DrawTile(g, new RectangleF(x + tileW + gap, y, tileW, tileH), power, "kW", "güç",
                 tileValue, caption);
        y += tileH + gap;
        DrawTile(g, new RectangleF(x, y, tileW, tileH), soc, "%", "SoC", tileValue, caption);
        DrawTile(g, new RectangleF(x + tileW + gap, y, tileW, tileH), slave, "°C",
                 "maks slave sıc.", tileValue, caption);

        return y + tileH + 16f;
    }

    private float DrawCellSection(Graphics g, float x, float y, float w, Font section,
                                  Font label, Font value, Font caption)
    {
        y = DrawSectionHeader(g, x, y, w, "HÜCRELER", section);

        var volt = _snapshot?.VoltageStats ?? CellStat.None;
        var temp = _snapshot?.TempStats ?? CellStat.None;

        y = DrawStatRow(g, x, y, w, "Min voltaj", volt.HasData ? $"{volt.Min:F3} V" : "—",
                        volt.HasData ? $"#{volt.MinIndex}" : null, label, value, caption);
        y = DrawStatRow(g, x, y, w, "Maks voltaj", volt.HasData ? $"{volt.Max:F3} V" : "—",
                        volt.HasData ? $"#{volt.MaxIndex}" : null, label, value, caption);
        y = DrawStatRow(g, x, y, w, "Ortalama", volt.HasData ? $"{volt.Avg:F3} V" : "—",
                        volt.HasData ? $"{volt.ValidCount} hücre" : null, label, value, caption);
        // Dengesizlik: BMS'te en cok bakilan tek sayi
        y = DrawStatRow(g, x, y, w, "Fark (maks−min)",
                        volt.HasData ? $"{(volt.Max - volt.Min) * 1000:F0} mV" : "—", null,
                        label, value, caption);

        var analysis = _snapshot?.VoltageAnalysis;
        y = DrawStatRow(g, x, y, w, "Std sapma",
                        analysis is { HasData: true } ? $"{analysis.StdDev * 1000:F1} mV" : "—",
                        null, label, value, caption);

        y += 4f;
        y = DrawStatRow(g, x, y, w, "Min sıcaklık", temp.HasData ? $"{temp.Min:F1} °C" : "—",
                        temp.HasData ? $"#{temp.MinIndex}" : null, label, value, caption);
        y = DrawStatRow(g, x, y, w, "Maks sıcaklık", temp.HasData ? $"{temp.Max:F1} °C" : "—",
                        temp.HasData ? $"#{temp.MaxIndex}" : null, label, value, caption);
        y = DrawStatRow(g, x, y, w, "Ortalama", temp.HasData ? $"{temp.Avg:F1} °C" : "—", null,
                        label, value, caption);

        return y + 12f;
    }

    private float DrawOutputSection(Graphics g, float x, float y, float w, Font section,
                                    Font badge)
    {
        y = DrawSectionHeader(g, x, y, w, "ÇIKIŞLAR", section);

        float pillW = (w - 16f) / 3f;
        DrawPill(g, new RectangleF(x, y, pillW, 30f), "AIR",
                 _snapshot?.AirClosed ?? false, Theme.Good, badge);
        DrawPill(g, new RectangleF(x + pillW + 8f, y, pillW, 30f), "PRE",
                 _snapshot?.PreActive ?? false, Theme.Warning, badge);
        DrawPill(g, new RectangleF(x + (pillW + 8f) * 2, y, pillW, 30f), "ERR",
                 _snapshot?.ErrActive ?? false, Theme.Critical, badge);

        return y + 30f + 16f;
    }

    private float DrawFaultSection(Graphics g, float x, float y, float w, Font section,
                                   Font label, Font badge)
    {
        ushort mask = _snapshot?.Faults ?? 0;
        var active = FaultBits.Decode(mask);

        y = DrawSectionHeader(g, x, y, w, "HATALAR", section);

        if (_snapshot is null)
        {
            using var muted = new SolidBrush(Theme.InkMuted);
            g.DrawString("veri yok", label, muted, x, y);
            return y + 26f;
        }

        if (active.Count == 0)
        {
            using (var ok = new SolidBrush(Theme.Good))
            {
                g.FillEllipse(ok, x, y + 3f, 10f, 10f);
                g.DrawString("Aktif hata yok", label, ok, x + 16f, y);
            }
            return y + 26f;
        }

        // Kalan yukseklige sigacak kadar goster, gerisini say
        float available = Height - y - 62f;
        int maxRows = Math.Max(1, (int)(available / 20f));
        int shown = Math.Min(active.Count, maxRows);

        using (var critical = new SolidBrush(Theme.Critical))
        {
            g.DrawString($"{active.Count} aktif hata", badge, critical, x, y);
            y += 20f;

            for (int i = 0; i < shown; i++)
            {
                var rect = new RectangleF(x, y, w, 19f);
                using (var chip = new SolidBrush(Color.FromArgb(58, 24, 24)))
                using (var path = RoundedRect(rect, 4f))
                    g.FillPath(chip, path);
                g.FillRectangle(critical, x, y, 3f, 19f);
                g.DrawString(active[i], label, critical, x + 9f, y + 2f);
                y += 21f;
            }

            if (shown < active.Count)
                g.DrawString($"+{active.Count - shown} daha", label, critical, x + 9f, y);
        }

        return y + 14f;
    }

    private void DrawLinkSection(Graphics g, float x, float y, float w, Font section,
                                 Font caption)
    {
        // Alt seride sabitle — ustteki bolumler buyudukce kaymasin
        float bottom = Height - 46f;
        y = Math.Max(y, bottom);
        if (y + 40f > Height) y = Height - 40f;

        using (var hairline = new Pen(Theme.Hairline))
            g.DrawLine(hairline, x, y - 8f, x + w, y - 8f);

        string state = _connected ? "bağlı" : "bağlı değil";
        double ageMs = _snapshot is null ? -1 : (DateTime.Now - _snapshot.RegistersAt).TotalMilliseconds;
        string age = ageMs < 0 ? "—" : $"{ageMs:F0} ms";

        using var muted = new SolidBrush(Theme.InkMuted);
        g.DrawString($"CRC {_health.CrcErrors}   ·   zaman aşımı {_health.Timeouts}   ·   " +
                     $"kimlik {_health.IdMismatches}", caption, muted, x, y);

        var ageColor = ageMs > 1000 ? Theme.Warning : Theme.InkMuted;
        using var ageBrush = new SolidBrush(ageColor);
        g.DrawString($"veri yaşı {age}   ·   {state}", caption, ageBrush, x, y + 16f);
    }

    // ------------------------------------------------------------------ parcalar

    private static float DrawSectionHeader(Graphics g, float x, float y, float w, string text,
                                           Font font)
    {
        using var muted = new SolidBrush(Theme.InkMuted);
        g.DrawString(text, font, muted, x, y);
        SizeF size = g.MeasureString(text, font);
        using var hairline = new Pen(Theme.Hairline);
        g.DrawLine(hairline, x + size.Width + 6f, y + size.Height / 2f, x + w, y + size.Height / 2f);
        return y + size.Height + 8f;
    }

    private static void DrawTile(Graphics g, RectangleF rect, string value, string unit,
                                 string caption, Font valueFont, Font captionFont)
    {
        using (var card = new SolidBrush(Theme.Card))
        using (var path = RoundedRect(rect, 6f))
            g.FillPath(card, path);

        using var ink = new SolidBrush(Theme.Ink);
        g.DrawString(value, valueFont, ink, rect.X + 9f, rect.Y + 5f);
        SizeF size = g.MeasureString(value, valueFont);

        using var muted = new SolidBrush(Theme.InkSecondary);
        g.DrawString(unit, captionFont, muted, rect.X + 7f + size.Width, rect.Y + 12f);

        using var dim = new SolidBrush(Theme.InkMuted);
        g.DrawString(caption, captionFont, dim, rect.X + 9f, rect.Y + 31f);
    }

    private static float DrawStatRow(Graphics g, float x, float y, float w, string label,
                                     string value, string? badge, Font labelFont, Font valueFont,
                                     Font badgeFont)
    {
        using var labelBrush = new SolidBrush(Theme.InkSecondary);
        g.DrawString(label, labelFont, labelBrush, x, y + 3f);

        float right = x + w;
        if (badge is not null)
        {
            SizeF badgeSize = g.MeasureString(badge, badgeFont);
            using var dim = new SolidBrush(Theme.InkMuted);
            g.DrawString(badge, badgeFont, dim, right - badgeSize.Width, y + 4f);
            right -= badgeSize.Width + 8f;
        }

        SizeF valueSize = g.MeasureString(value, valueFont);
        using var ink = new SolidBrush(Theme.Ink);
        g.DrawString(value, valueFont, ink, right - valueSize.Width, y);

        return y + 22f;
    }

    private static void DrawPill(Graphics g, RectangleF rect, string text, bool active,
                                 Color activeColor, Font font)
    {
        Color fill = active ? activeColor : Theme.Card;
        using (var brush = new SolidBrush(fill))
        using (var path = RoundedRect(rect, rect.Height / 2f))
            g.FillPath(brush, path);

        if (!active)
            using (var pen = new Pen(Theme.Hairline))
            using (var path = RoundedRect(rect, rect.Height / 2f))
                g.DrawPath(pen, path);

        float d = 9f;
        var dot = new RectangleF(rect.X + 10f, rect.Y + (rect.Height - d) / 2f, d, d);
        using (var dotBrush = new SolidBrush(active
                   ? Color.FromArgb(230, 255, 255, 255)
                   : Theme.InkMuted))
            g.FillEllipse(dotBrush, dot);

        using var textBrush = new SolidBrush(active ? Heatmap.InkOn(fill) : Theme.InkMuted);
        using var fmt = new StringFormat { LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, textBrush,
                     new RectangleF(dot.Right + 6f, rect.Y, rect.Width - 30f, rect.Height), fmt);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Max(1f, radius * 2f);
        if (d >= r.Width || d >= r.Height) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
