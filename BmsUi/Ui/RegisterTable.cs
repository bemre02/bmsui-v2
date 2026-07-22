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

    public RegisterTable()
    {
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
        BeginUpdate();
        try
        {
            long now = _clock.ElapsedMilliseconds;
            foreach (ListViewItem item in Items)
            {
                byte index = (byte)item.Tag!;
                bool valid = snapshot is not null && snapshot.RegisterValid[index];

                if (!valid)
                {
                    item.SubItems[2].Text = "—";
                    item.SubItems[3].Text = "—";
                    item.SubItems[4].Text = snapshot is null ? "" : "no answer";
                    item.ForeColor = Theme.InkMuted;
                    continue;
                }

                ushort raw = snapshot!.Registers[index];
                if (!_seen[index] || _previous[index] != raw)
                {
                    if (_seen[index]) _changedAt[index] = now;
                    _previous[index] = raw;
                    _seen[index] = true;
                }

                item.SubItems[2].Text = RegisterCatalog.FormatRaw(index, raw);
                item.SubItems[3].Text = RegisterCatalog.FormatValue(index, raw);
                item.SubItems[4].Text = RegisterCatalog.FormatNote(index, raw);

                bool fresh = _changedAt[index] != 0 &&
                             now - _changedAt[index] < HighlightFor.TotalMilliseconds;
                item.ForeColor = fresh ? Theme.Warning : Theme.Ink;
            }
        }
        finally
        {
            EndUpdate();
        }
    }
}
