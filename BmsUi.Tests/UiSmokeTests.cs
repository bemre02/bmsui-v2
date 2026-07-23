using System.Drawing;
using System.Windows.Forms;
using BmsUi;
using BmsUi.Model;
using BmsUi.Protocol;
using BmsUi.Ui;
using Xunit;

/// <summary>
/// Proves the UI really opens and paints. Layout bugs (SplitterDistance) and divide-by-zero
/// inside Paint only surface at runtime.
/// </summary>
public class UiSmokeTests
{
    /// <summary>WinForms needs an STA thread; rethrows the failure on the caller.</summary>
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI thread did not finish in time");
        if (failure is not null) throw new Xunit.Sdk.XunitException($"UI failure: {failure}");
    }

    /// <summary>
    /// Every pixel of a region, via LockBits — GetPixel over a window-sized area is slow
    /// enough to be felt, and a 3 px ring is too thin to sample every other pixel.
    /// </summary>
    private static bool ContainsColor(Bitmap bmp, Color target, Rectangle region)
    {
        region.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
        Assert.False(region.IsEmpty, "region falls outside the bitmap");

        var data = bmp.LockBits(region, System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var pixels = new int[data.Stride / 4 * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);

            int wanted = target.ToArgb() & 0xFFFFFF;
            foreach (int pixel in pixels)
                if ((pixel & 0xFFFFFF) == wanted) return true;
            return false;
        }
        finally { bmp.UnlockBits(data); }
    }

    private static bool ContainsColor(Bitmap bmp, Color target)
    {
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                var p = bmp.GetPixel(x, y);
                if (p.R == target.R && p.G == target.G && p.B == target.B) return true;
            }
        return false;
    }

    /// <summary>
    /// Is the logo found under its embedded resource name? It can disappear silently when
    /// the namespace or project name changes, so it is pinned by a test.
    /// </summary>
    [Fact]
    public void TeamLogo_IsEmbeddedAndLoads()
    {
        Assert.NotNull(Branding.TeamLogo);
        Assert.True(Branding.TeamLogo!.Width > 100, "logo smaller than expected");
    }

    /// <summary>
    /// PictureBox disposes the Image assigned to it. Hand it a single shared static image
    /// and the SECOND window draws the logo as a red X.
    /// </summary>
    [Fact]
    public void SecondWindow_StillHasUsableLogo()
    {
        RunSta(() =>
        {
            using (var first = new Form1())
            {
                first.Show();
                Application.DoEvents();
                first.Close();
            }

            using var second = new Form1();
            second.Show();
            Application.DoEvents();

            Assert.NotNull(second.LogoBox.Image);
            // Reading Width on a disposed Bitmap throws
            Assert.True(second.LogoBox.Image!.Width > 100);
            second.Close();
        });
    }

    [Fact]
    public void MainForm_OpensAndLaysOutWithoutError()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            form.Show();
            Application.DoEvents();
            Assert.True(form.IsHandleCreated);
            // Voltage / Temperature / Balance / Registers / Settings / Log
            // — no device-writing "Config" tab
            Assert.Equal(6, form.TabsControl.TabPages.Count);
            Assert.DoesNotContain(form.TabsControl.TabPages.Cast<TabPage>(),
                                  p => p.Text.Contains("Config"));
            form.Close();
        });
    }

    /// <summary>
    /// With simulation on, does the WHOLE chain run on the real UI: Start -> virtual device ->
    /// SerialLink -> parser -> PollWorker -> Invoke -> labels?
    /// </summary>
    [Fact]
    public void SimulationMode_UpdatesLiveUi()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            form.Show();
            Application.DoEvents();

            form.SimulationCheckBox.Checked = true;
            form.StartStopButton.PerformClick();
            Assert.Equal("Stop", form.StartStopButton.Text);

            // We are on the UI thread here: pump messages manually and wait for the update
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000 && form.Dashboard.CurrentSnapshot is null)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }

            var snapshot = form.Dashboard.CurrentSnapshot;
            Assert.NotNull(snapshot);
            Assert.InRange(snapshot!.PackVoltage, 300.0, 410.0);
            Assert.True(snapshot.VoltageStats.HasData);
            Assert.Contains("Simulation", form.ConnectionStatusLabel.Text);

            form.StartStopButton.PerformClick();                   // Stop
            Assert.Equal("Start", form.StartStopButton.Text);
            Assert.Null(form.Dashboard.CurrentSnapshot);            // cleared when the link drops
            form.Close();
        });
    }

    [Fact]
    public void CellGrid_PaintsAllStates()
    {
        RunSta(() =>
        {
            using var host = new Form { Size = new Size(900, 500) };
            var grid = new CellGridControl { Dock = DockStyle.Fill };
            host.Controls.Add(grid);
            host.Show();
            Application.DoEvents();

            var values = new double[96];
            for (int i = 0; i < 96; i++) values[i] = 3.85;
            values[0] = 0.00;    // invalid -> grey
            values[1] = 2.40;    // UV alarm
            values[2] = 4.30;    // OV alarm
            grid.UpdateData(values, i => i == 5);   // cell 5 is balancing

            using var bmp = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bmp, new Rectangle(0, 0, grid.Width, grid.Height));

            Assert.True(ContainsColor(bmp, Heatmap.WarningColor), "warning icon was not drawn");
            Assert.True(ContainsColor(bmp, Heatmap.InvalidColor), "invalid-cell colour was not drawn");
            Assert.True(ContainsColor(bmp, Heatmap.BalanceRing), "balance badge was not drawn");

            host.Close();
        });
    }

    [Fact]
    public void CellGrid_TemperatureMode_HandlesNegativeValues()
    {
        RunSta(() =>
        {
            using var host = new Form { Size = new Size(900, 500) };
            var grid = new CellGridControl { Dock = DockStyle.Fill, Mode = CellGridMode.Temperature };
            host.Controls.Add(grid);
            host.Show();
            Application.DoEvents();

            var temps = new double[96];
            for (int i = 0; i < 96; i++) temps[i] = -20.0 + i;   // -20 .. 75
            temps[95] = 95.0;                                    // overtemperature alarm
            grid.UpdateData(temps, _ => false);

            using var bmp = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bmp, new Rectangle(0, 0, grid.Width, grid.Height));
            Assert.True(ContainsColor(bmp, Heatmap.WarningColor), "overtemperature warning was not drawn");

            host.Close();
        });
    }

    /// <summary>
    /// The tab strip, the register table and the cell grid are all owner-drawn, and a throw
    /// inside a paint handler leaves a blank control rather than failing the build.
    ///
    /// SystemColors.Control is what the unthemed chrome was drawn in: the tab buttons, and
    /// the ring of TabPage padding no docked child can cover. Neither may come back.
    /// </summary>
    [Fact]
    public void EveryTab_PaintsWithNoSystemChromeLeft()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            form.Size = new Size(1400, 900);
            form.Show();
            Application.DoEvents();

            // Only the composed window shows it: a TabPage's own background does not appear
            // when the page or the tab control is asked to draw itself in isolation.
            foreach (TabPage page in form.TabsControl.TabPages)
            {
                form.TabsControl.SelectedTab = page;
                Application.DoEvents();

                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));

                // DrawToBitmap of a form includes the title bar, which is legitimately light
                var onScreen = form.TabsControl.RectangleToScreen(form.TabsControl.ClientRectangle);
                var region = new Rectangle(onScreen.X - form.Left, onScreen.Y - form.Top,
                                           onScreen.Width, onScreen.Height);

                Assert.False(ContainsColor(bmp, SystemColors.Control, region),
                             $"the {page.Text} tab still paints unthemed system chrome");
            }

            form.Close();
        });
    }

    /// <summary>
    /// The sweep is opt-in, so the table only fills when its tab is selected. This drives the
    /// whole path: tab selection -> worker flag -> sweep -> snapshot -> table.
    /// </summary>
    [Fact]
    public void RegistersTab_FillsWhileVisible()
    {
        RunSta(() =>
        {
            using var form = new Form1();
            form.Show();
            Application.DoEvents();

            form.TabsControl.SelectedTab = form.RegistersTab;
            form.SimulationCheckBox.Checked = true;
            form.StartStopButton.PerformClick();

            // Tag is the register index on data rows and the title on the section bands
            var rows = form.RegistersTable.Items.Cast<ListViewItem>();
            string ChargerVoltageCell() => rows
                .First(i => i.Tag is byte b && b == 5)
                .SubItems[3].Text;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 8000 && ChargerVoltageCell() == "—")
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }

            Assert.EndsWith("V", ChargerVoltageCell());
            Assert.Equal(RegisterCatalog.All.Count, rows.Count(i => i.Tag is byte));

            // One band per group, and every register sits under one
            Assert.Equal(RegisterCatalog.All.Select(d => d.Group).Distinct().Count(),
                         rows.Count(i => i.Tag is string));
            Assert.IsType<string>(form.RegistersTable.Items[0].Tag);

            form.StartStopButton.PerformClick();
            form.Close();
        });
    }

    /// <summary>
    /// The timeline renders the chronological log newest-first and paints without throwing.
    /// Drawing logic lives in owner-draw handlers, so a construction-only test would miss it.
    /// </summary>
    [Fact]
    public void EventTimeline_ShowsEventsNewestFirstAndPaints()
    {
        RunSta(() =>
        {
            // The control formats the duration in the current culture (like the rest of the
            // UI); pin it so "2.0 s" is deterministic on a Turkish machine too. This runs on
            // RunSta's throwaway thread, so it affects no other test.
            System.Globalization.CultureInfo.CurrentCulture =
                System.Globalization.CultureInfo.InvariantCulture;

            using var timeline = new EventTimeline { Width = 620, Height = 240 };
            timeline.CreateControl();

            var events = new List<PackEvent>
            {
                new(new DateTime(2026, 7, 23, 14, 0, 0), PackEventType.FaultRaised,
                    "Cell overvoltage", null, EventSeverity.Critical),
                new(new DateTime(2026, 7, 23, 14, 0, 2), PackEventType.FaultCleared,
                    "Cell overvoltage", TimeSpan.FromSeconds(2), EventSeverity.Info),
            };
            timeline.Update(events);

            Assert.Equal(2, timeline.Items.Count);
            Assert.StartsWith("14:00:02", timeline.Items[0].Text);      // newest on top
            Assert.Equal("2.0 s", timeline.Items[0].SubItems[2].Text);
            Assert.Equal("", timeline.Items[1].SubItems[2].Text);       // raised row has no duration

            using var bmp = new Bitmap(timeline.Width, timeline.Height);
            timeline.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        });
    }
}
