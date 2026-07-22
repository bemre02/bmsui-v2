using System.Drawing;
using System.Windows.Forms;
using BmsUi;
using BmsUi.Ui;
using Xunit;

/// <summary>
/// UI'nin gercekten acilip cizildigini kanitlar. Yerlesim hatalari (SplitterDistance),
/// Paint icindeki sifira bolme gibi sorunlar yalnizca calisma aninda ortaya cikar.
/// </summary>
public class UiSmokeTests
{
    /// <summary>WinForms STA thread ister; istisnayi cagirana tasir.</summary>
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
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "UI thread'i zamaninda bitmedi");
        if (failure is not null) throw new Xunit.Sdk.XunitException($"UI hatasi: {failure}");
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
    /// Logo gomulu kaynak adiyla bulunuyor mu? Namespace/proje adi degisince sessizce
    /// kaybolabilir — bu yuzden testle sabitlendi.
    /// </summary>
    [Fact]
    public void TeamLogo_IsEmbeddedAndLoads()
    {
        Assert.NotNull(Branding.TeamLogo);
        Assert.True(Branding.TeamLogo!.Width > 100, "logo beklenenden kucuk");
    }

    /// <summary>
    /// PictureBox kendisine atanan Image'i Dispose ederken yok ediyor. Paylasilan tek
    /// statik gorsel verilirse IKINCI pencere logoyu kirmizi X olarak cizer.
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
            // Dispose edilmis Bitmap'te Width erisimi istisna atar
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
            // Voltaj / Sicaklik / Balans / Ayarlar / Log — cihaza yazan "Config" sekmesi yok
            Assert.Equal(5, form.TabsControl.TabPages.Count);
            Assert.DoesNotContain(form.TabsControl.TabPages.Cast<TabPage>(),
                                  p => p.Text.Contains("Config"));
            form.Close();
        });
    }

    /// <summary>
    /// Simulasyon modu acilinca Start -> sanal cihaz -> SerialLink -> parser -> PollWorker ->
    /// Invoke -> etiketler zincirinin TAMAMI gercek UI uzerinde calisiyor mu?
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
            Assert.Equal("Durdur", form.StartStopButton.Text);

            // UI thread'i burasi: mesaj pompasini elle dondurup guncellemeyi bekle
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
            Assert.Contains("Simülasyon", form.ConnectionStatusLabel.Text);

            form.StartStopButton.PerformClick();                   // Durdur
            Assert.Equal("Başlat", form.StartStopButton.Text);
            Assert.Null(form.Dashboard.CurrentSnapshot);            // baglanti kesilince temizlenir
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
            values[0] = 0.00;    // gecersiz  -> gri
            values[1] = 2.40;    // UV alarmi -> kirmizi
            values[2] = 4.30;    // OV alarmi -> kirmizi
            grid.UpdateData(values, i => i == 5);   // hucre 5 balansta

            using var bmp = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bmp, new Rectangle(0, 0, grid.Width, grid.Height));

            Assert.True(ContainsColor(bmp, Heatmap.WarningColor), "Uyari ikonu cizilmedi");
            Assert.True(ContainsColor(bmp, Heatmap.InvalidColor), "Gecersiz hucre rengi cizilmedi");
            Assert.True(ContainsColor(bmp, Heatmap.BalanceRing), "Balans cercevesi cizilmedi");

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
            temps[95] = 95.0;                                    // asiri sicaklik alarmi
            grid.UpdateData(temps, _ => false);

            using var bmp = new Bitmap(grid.Width, grid.Height);
            grid.DrawToBitmap(bmp, new Rectangle(0, 0, grid.Width, grid.Height));
            Assert.True(ContainsColor(bmp, Heatmap.WarningColor), "Asiri sicaklik uyarisi cizilmedi");

            host.Close();
        });
    }
}
