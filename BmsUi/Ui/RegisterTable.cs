using System.Diagnostics;
using System.Runtime.InteropServices;
using BmsUi.Model;
using BmsUi.Protocol;

namespace BmsUi.Ui;

/// <summary>
/// Read-only table of every readable MAINBUFFER register.
///
/// A ListView rather than an owner-drawn control: 47 rows do not fit the tab, and ListView
/// brings scrolling for free. It is used purely as a scrolling frame, though — every pixel is
/// drawn here, because the native Details view does not follow the dark theme the rest of the
/// application uses.
///
/// The catalog's groups are rendered as section bands, and a register whose value changed in
/// the last second carries an accent bar on its left edge. That bar is what separates a
/// register the firmware actively writes from one stuck at its init value.
/// </summary>
public sealed class RegisterTable : ListView
{
    private static readonly TimeSpan HighlightFor = TimeSpan.FromSeconds(1);

    /// <summary>
    /// The table is fed at the UI update rate (up to 10 Hz) but the sweep behind it runs at
    /// 1 Hz, so most rows cannot change that often. Repainting slower costs nothing.
    /// </summary>
    private const int MinRenderIntervalMs = 250;

    private const int RowHeight = 26;
    private const int CellPad = 9;
    private const int AccentWidth = 3;

    private static readonly Color RowAlt = Heatmap.FromHex(0x1F1F1E);
    private static readonly Color SectionBand = Heatmap.FromHex(0x141413);

    private readonly ushort[] _previous = new ushort[HvProtocol.MaxRegisterIndexExclusive];
    private readonly bool[] _seen = new bool[HvProtocol.MaxRegisterIndexExclusive];
    private readonly long[] _changedAt = new long[HvProtocol.MaxRegisterIndexExclusive];
    private readonly bool[] _fresh = new bool[HvProtocol.MaxRegisterIndexExclusive];
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    // ListView gives every row the same height, taken from the tallest of the font and the
    // image list. A 1 px wide image is the only way to ask for more breathing room.
    private readonly ImageList _rowSpacer = new() { ImageSize = new Size(1, RowHeight) };

    private readonly Font _headerFont = new(Theme.FamilyName, 8f, FontStyle.Bold);
    private readonly Font _sectionFont = new(Theme.FamilyName, 8f, FontStyle.Bold);
    private readonly Font _indexFont = new(Theme.FamilyName, 8.25f);
    private readonly Font _nameFont = new(Theme.FamilyName, 9f);
    private readonly Font _rawFont = new("Consolas", 9f);          // digits must line up
    private readonly Font _valueFont = new(Theme.FamilyName, 9.5f, FontStyle.Bold);

    private long _lastRender = long.MinValue;

    public RegisterTable()
    {
        // ListView is not double buffered by default and repainting 47 rows at the UI
        // update rate flickers visibly. The property is protected, which is one more reason
        // this is a subclass rather than a plain ListView.
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

        Columns.Add("idx", 54, HorizontalAlignment.Right);
        Columns.Add("Register", 250);
        Columns.Add("Raw", 130, HorizontalAlignment.Right);
        Columns.Add("Value", 130, HorizontalAlignment.Right);
        Columns.Add("Note", 300);

        BuildRows();

        // Display-only: a selection highlight would suggest the rows are actionable
        ItemSelectionChanged += (_, _) => SelectedIndices.Clear();
    }

    private void BuildRows()
    {
        RegisterGroup? current = null;
        foreach (var d in RegisterCatalog.All)
        {
            if (current != d.Group)
            {
                current = d.Group;
                var band = new ListViewItem("") { Tag = SectionTitle(d.Group) };
                for (int i = 0; i < 4; i++) band.SubItems.Add("");
                Items.Add(band);
            }

            var item = new ListViewItem(d.Index.ToString()) { Tag = d.Index };
            item.SubItems.Add(d.IsKnown ? d.Name : "—");
            item.SubItems.Add("—");
            item.SubItems.Add("—");
            item.SubItems.Add("");
            Items.Add(item);
        }
    }

    private static string SectionTitle(RegisterGroup group) => group switch
    {
        RegisterGroup.Status => "STATUS",
        RegisterGroup.Charger => "CHARGER",
        RegisterGroup.Pack => "PACK",
        RegisterGroup.Config => "THRESHOLDS & CONFIG",
        _ => "UNNAMED  ·  no symbol for these indices in main.h",
    };

    public void UpdateData(BmsSnapshot? snapshot)
    {
        long now = _clock.ElapsedMilliseconds;
        bool clearing = snapshot is null;
        if (!clearing && now - _lastRender < MinRenderIntervalMs) return;
        _lastRender = now;

        bool accentsMoved = false;
        BeginUpdate();
        try
        {
            foreach (ListViewItem item in Items)
            {
                if (item.Tag is not byte index) continue;    // section band

                bool valid = !clearing && snapshot!.RegisterValid[index];
                if (!valid)
                {
                    SetText(item, 2, "—");
                    SetText(item, 3, "—");
                    SetText(item, 4, clearing ? "" : "no answer");
                    accentsMoved |= SetFresh(index, false);
                    continue;
                }

                ushort raw = snapshot!.Registers[index];
                if (!_seen[index] || _previous[index] != raw)
                {
                    if (_seen[index]) _changedAt[index] = now;
                    _previous[index] = raw;
                    _seen[index] = true;
                }

                SetText(item, 2, RegisterCatalog.FormatRaw(index, raw));
                SetText(item, 3, RegisterCatalog.FormatValue(index, raw));
                SetText(item, 4, RegisterCatalog.FormatNote(index, raw));

                accentsMoved |= SetFresh(index, _changedAt[index] != 0 &&
                                                now - _changedAt[index] < HighlightFor.TotalMilliseconds);
            }
        }
        finally
        {
            EndUpdate();
        }

        // An accent bar that turned off changed no text, so nothing would repaint it away.
        if (accentsMoved) Invalidate();
    }

    // Assigning the same text still invalidates the item, so most of the 47 rows would be
    // repainted every pass even though only a handful change.
    private static void SetText(ListViewItem item, int column, string text)
    {
        if (item.SubItems[column].Text != text) item.SubItems[column].Text = text;
    }

    private bool SetFresh(byte index, bool fresh)
    {
        if (_fresh[index] == fresh) return false;
        _fresh[index] = fresh;
        return true;
    }

    // ---------------- drawing ----------------

    // DllImport rather than LibraryImport: the source generator emits unsafe code, and one
    // theme call is not worth turning AllowUnsafeBlocks on for the whole project.
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string appName, IntPtr idList);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        // The scroll bar belongs to the system, not to OnDrawItem, so it stays light grey
        // against the dark table. Opting the window into the dark explorer theme is the only
        // way to reach it. The theme name is undocumented and simply does nothing on Windows
        // versions that do not know it, which is why the result is not asserted anywhere.
        SetWindowTheme(Handle, "DarkMode_Explorer", IntPtr.Zero);
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using var back = new SolidBrush(Theme.Page);
        e.Graphics.FillRectangle(back, e.Bounds);
        using var hairline = new Pen(Theme.Hairline);
        e.Graphics.DrawLine(hairline, e.Bounds.Left, e.Bounds.Bottom - 1,
                            e.Bounds.Right, e.Bounds.Bottom - 1);

        DrawCell(e.Graphics, e.Header!.Text.ToUpperInvariant(), e.Bounds,
                 _headerFont, Theme.InkMuted, e.Header.TextAlign);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        if (e.Item!.Tag is string title)
        {
            using var band = new SolidBrush(SectionBand);
            e.Graphics.FillRectangle(band, e.Bounds);
            if (e.ItemIndex > 0)
            {
                using var hairline = new Pen(Theme.Hairline);
                e.Graphics.DrawLine(hairline, e.Bounds.Left, e.Bounds.Top,
                                    e.Bounds.Right, e.Bounds.Top);
            }
            DrawCell(e.Graphics, title, e.Bounds, _sectionFont, Theme.InkMuted,
                     HorizontalAlignment.Left);
            return;
        }

        using var fill = new SolidBrush(e.ItemIndex % 2 == 0 ? Theme.Card : RowAlt);
        e.Graphics.FillRectangle(fill, e.Bounds);

        if (e.Item.Tag is byte index && _fresh[index])
        {
            using var accent = new SolidBrush(Theme.Warning);
            e.Graphics.FillRectangle(accent, e.Bounds.Left, e.Bounds.Top,
                                     AccentWidth, e.Bounds.Height);
        }
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        if (e.Item!.Tag is not byte index) return;      // section band, drawn whole

        bool known = RegisterCatalog.Describe(index).IsKnown;
        bool answered = e.Item.SubItems[3].Text != "—";

        (Font font, Color ink) = e.ColumnIndex switch
        {
            0 => (_indexFont, Theme.InkMuted),
            1 => (_nameFont, known ? Theme.InkSecondary : Theme.InkMuted),
            2 => (_rawFont, Theme.InkMuted),
            // An unnamed register's value carries no meaning on its own, so it stays recessed.
            // If the firmware does start writing one, the accent bar is what says so.
            3 => (_valueFont, answered && known ? Theme.Ink : Theme.InkMuted),
            _ => (_nameFont, NoteInk(index, e.SubItem!.Text)),
        };

        DrawCell(e.Graphics, e.SubItem!.Text, e.Bounds, font, ink,
                 Columns[e.ColumnIndex].TextAlign);
    }

    /// <summary>A fault list is the one note that has to catch the eye.</summary>
    private static Color NoteInk(byte index, string note) => note.Length == 0
        ? Theme.InkMuted
        : index == Reg.Faults && note != "none"
            ? Theme.Critical
            : Theme.InkSecondary;

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

    // The last column absorbs the leftover width, so the table never ends in dead space.
    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        if (Columns.Count == 0) return;

        int used = 0;
        for (int i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
        Columns[^1].Width = Math.Max(200, ClientSize.Width - used);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SmallImageList = null;
            _rowSpacer.Dispose();
            _headerFont.Dispose();
            _sectionFont.Dispose();
            _indexFont.Dispose();
            _nameFont.Dispose();
            _rawFont.Dispose();
            _valueFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
