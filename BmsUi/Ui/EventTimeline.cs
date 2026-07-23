using System.Runtime.InteropServices;
using BmsUi.Model;

namespace BmsUi.Ui;

/// <summary>
/// Read-only, newest-first list of pack events. Like RegisterTable it is a ListView used only
/// for its scrolling; every pixel is painted here so it follows the dark theme. The log is
/// stored chronologically and reversed for display.
/// </summary>
public sealed class EventTimeline : ListView
{
    private const int RowHeight = 24;
    private const int CellPad = 9;
    private static readonly Color RowAlt = Heatmap.FromHex(0x1F1F1E);

    private readonly ImageList _rowSpacer = new() { ImageSize = new Size(1, RowHeight) };
    private readonly Font _timeFont = new("Consolas", 9f);
    private readonly Font _labelFont = new(Theme.FamilyName, 9.5f, FontStyle.Bold);
    private readonly Font _headerFont = new(Theme.FamilyName, 8f, FontStyle.Bold);

    public EventTimeline()
    {
        DoubleBuffered = true;
        View = View.Details;
        OwnerDraw = true;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = true;
        BorderStyle = BorderStyle.None;
        GridLines = false;
        TabStop = false;
        BackColor = Theme.Card;
        ForeColor = Theme.InkSecondary;
        Font = new Font(Theme.FamilyName, 9f);
        SmallImageList = _rowSpacer;

        Columns.Add("Time", 120, HorizontalAlignment.Left);
        Columns.Add("Event", 330, HorizontalAlignment.Left);
        Columns.Add("Duration", 110, HorizontalAlignment.Right);

        ItemSelectionChanged += (_, _) => SelectedIndices.Clear();
    }

    /// <summary>Rebuilds the rows newest-first from the chronological event list.</summary>
    public void UpdateData(IReadOnlyList<PackEvent> events)
    {
        BeginUpdate();
        try
        {
            Items.Clear();
            for (int i = events.Count - 1; i >= 0; i--)
            {
                var e = events[i];
                var item = new ListViewItem(e.At.ToString("HH:mm:ss.fff")) { Tag = e };
                item.SubItems.Add($"{Icon(e.Type)}  {e.Label}");
                item.SubItems.Add(e.Duration is { } d ? $"{d.TotalSeconds:F1} s" : "");
                Items.Add(item);
            }
        }
        finally { EndUpdate(); }
    }

    private static string Icon(PackEventType type) => type switch
    {
        PackEventType.FaultRaised => "▲",
        PackEventType.FaultCleared => "▼",
        _ => "●",
    };

    // ---------------- theme + drawing (mirrors RegisterTable) ----------------

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string appName, IntPtr idList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The scroll bar is system-drawn; opt the window into the dark explorer theme to
        // reach it. Undocumented name, a no-op on versions that do not know it.
        SetWindowTheme(Handle, "DarkMode_Explorer", IntPtr.Zero);
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var back = new SolidBrush(Theme.Page);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var hairline = new Pen(Theme.Hairline);
        e.Graphics.DrawLine(hairline, e.Bounds.Left, e.Bounds.Bottom - 1,
                            e.Bounds.Right, e.Bounds.Bottom - 1);
        DrawCell(e.Graphics, e.Header!.Text.ToUpperInvariant(), e.Bounds, _headerFont,
                 Theme.InkMuted, e.Header.TextAlign);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        using var fill = new SolidBrush(e.ItemIndex % 2 == 0 ? Theme.Card : RowAlt);
        e.Graphics.FillRectangle(fill, e.Bounds);
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        var ev = (PackEvent)e.Item!.Tag!;
        (Font font, Color ink) = e.ColumnIndex switch
        {
            0 => (_timeFont, Theme.InkMuted),
            1 => (_labelFont, ev.Severity == EventSeverity.Critical ? Theme.Critical : Theme.InkSecondary),
            _ => (Font, Theme.InkSecondary),
        };
        DrawCell(e.Graphics, e.SubItem!.Text, e.Bounds, font, ink, Columns[e.ColumnIndex].TextAlign);
    }

    private static void DrawCell(Graphics g, string text, Rectangle bounds, Font font,
                                 Color ink, HorizontalAlignment align)
    {
        if (text.Length == 0) return;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix | align switch
        {
            HorizontalAlignment.Right => TextFormatFlags.Right,
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left,
        };
        TextRenderer.DrawText(g, text, font, Rectangle.Inflate(bounds, -CellPad, 0), ink, flags);
    }

    // The Event column absorbs the leftover width so the table never ends in dead space.
    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (Columns.Count < 3) return;
        Columns[1].Width = Math.Max(200, ClientSize.Width - Columns[0].Width - Columns[2].Width);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SmallImageList = null;
            _rowSpacer.Dispose();
            _timeFont.Dispose();
            _labelFont.Dispose();
            _headerFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
