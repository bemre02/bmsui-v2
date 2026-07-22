using System.Diagnostics;
using BmsUi.Model;
using BmsUi.Protocol;

namespace BmsUi.Ui;

/// <summary>
/// Read-only table of every readable MAINBUFFER register.
///
/// A ListView rather than an owner-drawn control: 47 rows do not fit the tab, and ListView
/// brings scrolling for free. Owner-drawing would mean writing scroll handling by hand for no
/// visual gain on what is a plain data grid.
///
/// A row is highlighted for a moment when its value changes, which is what separates a
/// register the firmware actively writes from one stuck at its init value.
/// </summary>
public sealed class RegisterTable : ListView
{
    private static readonly TimeSpan HighlightFor = TimeSpan.FromSeconds(1);

    private readonly ushort[] _previous = new ushort[HvProtocol.MaxRegisterIndexExclusive];
    private readonly bool[] _seen = new bool[HvProtocol.MaxRegisterIndexExclusive];
    private readonly long[] _changedAt = new long[HvProtocol.MaxRegisterIndexExclusive];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private long _lastRender = long.MinValue;

    /// <summary>
    /// The table is fed at the UI update rate (up to 10 Hz) but the sweep behind it runs at
    /// 1 Hz, so most rows cannot change that often. Repainting slower costs nothing and cuts
    /// the redraw work by more than half.
    /// </summary>
    private const int MinRenderIntervalMs = 250;

    public RegisterTable()
    {
        // ListView is not double buffered by default and repainting 47 rows at the UI
        // update rate flickers visibly. The property is protected, which is one more reason
        // this is a subclass rather than a plain ListView.
        DoubleBuffered = true;

        View = View.Details;
        FullRowSelect = true;
        MultiSelect = false;
        HideSelection = true;
        BorderStyle = BorderStyle.None;
        GridLines = false;
        TabStop = false;
        BackColor = Theme.Card;
        ForeColor = Theme.InkSecondary;
        Font = new Font(Theme.FamilyName, 9f);

        Columns.Add("idx", 46, HorizontalAlignment.Right);
        Columns.Add("Name", 220);
        Columns.Add("Raw", 120, HorizontalAlignment.Right);
        Columns.Add("Value", 120, HorizontalAlignment.Right);
        Columns.Add("Note", 320);

        foreach (var d in RegisterCatalog.All)
        {
            var item = new ListViewItem(d.Index.ToString()) { Tag = d.Index };
            item.SubItems.Add(d.IsKnown ? d.Name : "—");
            item.SubItems.Add("—");
            item.SubItems.Add("—");
            item.SubItems.Add("");
            Items.Add(item);
        }

        // Display-only: a selection highlight would suggest the rows are actionable
        ItemSelectionChanged += (_, _) => SelectedIndices.Clear();
    }

    public void UpdateData(BmsSnapshot? snapshot)
    {
        long now = _clock.ElapsedMilliseconds;
        bool clearing = snapshot is null;
        if (!clearing && now - _lastRender < MinRenderIntervalMs) return;
        _lastRender = now;

        BeginUpdate();
        try
        {
            foreach (ListViewItem item in Items)
            {
                byte index = (byte)item.Tag!;
                bool valid = snapshot is not null && snapshot.RegisterValid[index];

                if (!valid)
                {
                    SetText(item, 2, "—");
                    SetText(item, 3, "—");
                    SetText(item, 4, clearing ? "" : "no answer");
                    SetColor(item, Theme.InkMuted);
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

                bool fresh = _changedAt[index] != 0 &&
                             now - _changedAt[index] < HighlightFor.TotalMilliseconds;
                SetColor(item, fresh ? Theme.Warning : Theme.Ink);
            }
        }
        finally
        {
            EndUpdate();
        }
    }

    // Assigning the same text still invalidates the item, so most of the 47 rows would be
    // repainted every pass even though only a handful actually change.
    private static void SetText(ListViewItem item, int column, string text)
    {
        if (item.SubItems[column].Text != text) item.SubItems[column].Text = text;
    }

    private static void SetColor(ListViewItem item, Color color)
    {
        if (item.ForeColor != color) item.ForeColor = color;
    }
}
