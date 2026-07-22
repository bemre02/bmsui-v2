using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using BmsUi.Model;
using BmsUi.Protocol;
using BmsUi.Ui;
using Xunit;

/// <summary>
/// Renders the grid to a bitmap at realistic sizes. Catches drawing errors and leaves a
/// PNG behind (in the temp folder) for visual inspection.
/// </summary>
public class GridRenderTests
{
    public static string PreviewPath(string name)
        => Path.Combine(Path.GetTempPath(), $"bmsui_preview_{name}.png");

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
        if (failure is not null) throw new Xunit.Sdk.XunitException($"Drawing failure: {failure}");
    }

    private static void Render(CellGridMode mode, double[] values, string previewName)
    {
        RunSta(() =>
        {
            using var host = new Form { Size = new Size(960, 620) };
            var grid = new CellGridControl
            {
                Dock = DockStyle.Fill,
                Mode = mode,
                Settings = new DisplaySettings(),
            };
            host.Controls.Add(grid);
            host.Show();
            Application.DoEvents();

            grid.UpdateData(values, i => i is 3 or 19 or 77);

            using var bmp = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bmp, new Rectangle(0, 0, grid.Width, grid.Height));
            bmp.Save(PreviewPath(previewName), ImageFormat.Png);
            host.Close();
        });
    }

    [Fact]
    public void VoltageGrid_RendersAllStates()
    {
        var v = new double[HvProtocol.CellCount];
        for (int i = 0; i < v.Length; i++) v[i] = 3.55 + i * 0.006;   // 3.55 -> 4.12
        v[5] = 0.00;    // invalid
        v[11] = 2.31;   // low alarm
        v[40] = 4.31;   // high alarm
        Render(CellGridMode.Voltage, v, "voltage");
        Assert.True(File.Exists(PreviewPath("voltage")));
    }

    [Fact]
    public void TemperatureGrid_RendersNegativeAndAlarm()
    {
        var t = new double[HvProtocol.CellCount];
        for (int i = 0; i < t.Length; i++) t[i] = 18.0 + i * 0.42;    // 18 -> 58
        t[2] = -12.5;   // negative (low alarm)
        t[63] = 91.0;   // high alarm
        Render(CellGridMode.Temperature, t, "temperature");
        Assert.True(File.Exists(PreviewPath("temperature")));
    }

    /// <summary>Renders the whole window with simulated data and leaves a PNG behind.</summary>
    [Fact]
    public void FullWindow_RendersWithLiveSimulationData()
    {
        RunSta(() =>
        {
            using var form = new BmsUi.Form1();
            form.Size = new Size(1400, 860);
            form.Show();
            Application.DoEvents();

            form.SimulationCheckBox.Checked = true;
            form.StartStopButton.PerformClick();

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 6000 && form.Dashboard.CurrentSnapshot is null)
            {
                Application.DoEvents();
                Thread.Sleep(20);
            }
            // Let a few more rounds run so the cell arrays fill up too
            sw.Restart();
            while (sw.ElapsedMilliseconds < 900) { Application.DoEvents(); Thread.Sleep(20); }

            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(PreviewPath("window"), ImageFormat.Png);
            }

            // Select by reference, not index: a new tab shifted the indices once already
            foreach (var (tab, name) in new[]
                     {
                         (form.RegistersTab, "registers"),
                         (form.TabsControl.TabPages.Cast<TabPage>().First(t => t.Text == "Settings"), "settings"),
                     })
            {
                form.TabsControl.SelectedTab = tab;
                // Pump while waiting: a bare Thread.Sleep starves the UI thread, so the
                // BeginInvoke callbacks never run and the view keeps a pre-switch snapshot
                var wait = System.Diagnostics.Stopwatch.StartNew();
                while (wait.ElapsedMilliseconds < 1500)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(PreviewPath(name), ImageFormat.Png);
            }

            form.StartStopButton.PerformClick();
            form.Close();
        });

        Assert.True(File.Exists(PreviewPath("window")));
        Assert.True(File.Exists(PreviewPath("settings")));
        Assert.True(File.Exists(PreviewPath("registers")));
    }

    [Fact]
    public void Grid_SurvivesTinySize()
    {
        RunSta(() =>
        {
            using var host = new Form { Size = new Size(240, 160) };
            var grid = new CellGridControl { Dock = DockStyle.Fill };
            host.Controls.Add(grid);
            host.Show();
            Application.DoEvents();
            grid.UpdateData(new double[HvProtocol.CellCount], _ => false);

            using var bmp = new Bitmap(Math.Max(1, grid.Width), Math.Max(1, grid.Height));
            grid.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            host.Close();
        });
    }
}
