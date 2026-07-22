using System.Drawing;
using System.Windows.Forms;
using BmsUi;
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
            // Voltage / Temperature / Balance / Settings / Log — no device-writing "Config" tab
            Assert.Equal(5, form.TabsControl.TabPages.Count);
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
}
