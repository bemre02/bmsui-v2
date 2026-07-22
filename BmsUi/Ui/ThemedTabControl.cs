namespace BmsUi.Ui;

/// <summary>
/// A TabControl that draws its own strip.
///
/// The native strip is the one part of the window the theme cannot reach: its buttons and the
/// border around the page area come from the system, so they stayed light against everything
/// else. Nothing here changes behaviour — the native control still owns layout, hit testing
/// and selection, and the tab rectangles are read back from it so a click always lands on the
/// tab it appears to.
///
/// The selected tab is filled with the page colour so it reads as connected to the panel below
/// it, and the hairline that separates the strip from the page breaks under it.
/// </summary>
public sealed class ThemedTabControl : TabControl
{
    private const int TabWidth = 112;
    private const int TabHeight = 32;

    private int _hovered = -1;

    public ThemedTabControl()
    {
        // The strip is non-client-ish territory: without UserPaint the system draws it before
        // OnPaint ever runs, and painting over it afterwards flickers.
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(TabWidth, TabHeight);
        Font = new Font(Theme.FamilyName, 9f);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        using (var page = new SolidBrush(Theme.Page)) g.FillRectangle(page, ClientRectangle);
        using (var card = new SolidBrush(Theme.Card)) g.FillRectangle(card, DisplayRectangle);

        if (TabCount == 0) return;

        int baseline = GetTabRect(0).Bottom;
        using var hairline = new Pen(Theme.Hairline);
        g.DrawLine(hairline, ClientRectangle.Left, baseline, ClientRectangle.Right, baseline);

        for (int i = 0; i < TabCount; i++)
        {
            var box = TabBox(i, baseline);
            bool selected = i == SelectedIndex;

            using (var fill = new SolidBrush(selected ? Theme.Card
                                             : i == _hovered ? Theme.Input : Theme.Page))
                g.FillRectangle(fill, box);

            // The page and its tab are one surface, so the separator breaks under the tab
            if (selected)
                g.DrawLine(hairline, box.Left, box.Bottom, box.Right - 1, box.Bottom);

            TextRenderer.DrawText(
                g, TabPages[i].Text, Font, box,
                selected ? Theme.Ink : i == _hovered ? Theme.InkSecondary : Theme.InkMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// The rectangles come from the native control so drawing and hit testing cannot drift
    /// apart. Windows inflates the selected tab's rectangle, which has to be undone or the
    /// strip visibly shifts as the selection moves.
    /// </summary>
    private Rectangle TabBox(int index, int baseline)
    {
        var r = GetTabRect(index);
        if (index == SelectedIndex) r.Inflate(-2, 0);
        return new Rectangle(r.Left, baseline - TabHeight, r.Width, TabHeight);
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        int hovered = -1;
        for (int i = 0; i < TabCount; i++)
            if (GetTabRect(i).Contains(e.Location)) { hovered = i; break; }

        if (hovered == _hovered) return;
        _hovered = hovered;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hovered == -1) return;
        _hovered = -1;
        Invalidate();
    }
}
