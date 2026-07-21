using BmsUi.Logging;
using BmsUi.Model;
using BmsUi.Polling;
using BmsUi.Protocol;
using BmsUi.Serial;
using BmsUi.Ui;

namespace BmsUi;

public partial class Form1 : Form
{
    private SerialLink? _link;
    private PollWorker? _worker;
    private CsvLogger? _logger;
    private string? _lastPortName;   // simulasyonda null — yeniden baglanma denenmez
    private DisplaySettings _settings = DisplaySettings.Load();

    public Form1()
    {
        InitializeComponent();
        ApplyTheme(this);
        LoadSettingsIntoInputs();
        ApplySettingsToGrids();
        RefreshPorts();
        UpdateDashboard(null);

        var icon = Branding.CreateWindowIcon();
        if (icon is not null) Icon = icon;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // Yerlesim oturduktan SONRA: kontrol kucukken atanan SplitterDistance kirpiliyor
        int desired = Math.Min(360, Math.Max(mainSplit.Panel1MinSize, mainSplit.Width / 3));
        if (mainSplit.Width > desired + mainSplit.Panel2MinSize)
            mainSplit.SplitterDistance = desired;
    }

    // ------------------------------------------------------------------ baglanti

    private void RefreshPorts()
    {
        string? previous = portCombo.SelectedItem as string;
        portCombo.Items.Clear();
        portCombo.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames());
        if (previous is not null && portCombo.Items.Contains(previous))
            portCombo.SelectedItem = previous;
        else if (portCombo.Items.Count > 0)
            portCombo.SelectedIndex = 0;
    }

    private void refreshButton_Click(object? sender, EventArgs e) => RefreshPorts();

    /// <summary>Simülasyon işaretliyken COM seçimi anlamsızdır.</summary>
    private void simulationCheck_CheckedChanged(object? sender, EventArgs e)
    {
        if (_link is not null) return;   // bağlıyken kilitli zaten
        portCombo.Enabled = refreshButton.Enabled = !simulationCheck.Checked;
    }

    private void startButton_Click(object? sender, EventArgs e)
    {
        if (_link is not null) { reconnectTimer.Stop(); Disconnect(); return; }

        bool simulated = simulationCheck.Checked;
        ISerialTransport transport;
        string label;

        if (simulated)
        {
            // Uygulama içi sanal cihaz: SerialLink'ten yukarısı gerçek kartla aynı kod yolu
            transport = new SimulatedTransport();
            label = "Simülasyon";
        }
        else
        {
            if (portCombo.SelectedItem is not string portName)
            {
                MessageBox.Show("Önce bir COM portu seçin " +
                                "(ya da 'Simülasyon' kutusunu işaretleyin).", "Uyarı");
                return;
            }
            transport = new SerialPortTransport(portName);
            label = portName;
        }

        var link = new SerialLink(transport);
        try
        {
            link.Open();
        }
        catch (Exception ex)
        {
            link.Dispose();
            SetStatus($"{label} açılamadı", Theme.Critical);
            if (!reconnectTimer.Enabled)
                MessageBox.Show($"{label} açılamadı: {ex.Message}\n\n" +
                                "Port takılı mı ve başka bir uygulama tarafından kullanılıyor mu?",
                                "Hata");
            return;
        }

        // Bağlı cihazın gerçekten HV BMS olduğunu ping ile doğrula
        if (!link.Ping())
        {
            string reason = link.LastError ?? "cevap yok";
            link.Dispose();
            SetStatus($"{label}: cihaz cevap vermiyor", Theme.Critical);
            if (!reconnectTimer.Enabled)
                MessageBox.Show($"{label} açıldı ama cihaz ping'e cevap vermedi ({reason}).\n\n" +
                                "Doğru port mu? Kart çalışıyor mu?", "Cihaz bulunamadı");
            return;
        }

        _link = link;
        _lastPortName = simulated ? null : label;
        reconnectTimer.Stop();

        _worker = new PollWorker(link);
        _worker.SnapshotReady += OnSnapshotReady;
        _worker.ConnectionLost += OnConnectionLost;
        _worker.Start();

        startButton.Text = "Durdur";
        portCombo.Enabled = refreshButton.Enabled = simulationCheck.Enabled = false;
        SetStatus(simulated ? "Simülasyon çalışıyor" : $"Bağlı: {label}", Theme.Good);
    }

    private void Disconnect()
    {
        _worker?.Stop();
        _worker?.Dispose();
        _worker = null;

        _link?.Dispose();
        _link = null;

        logEnabledCheck.Checked = false;

        startButton.Text = "Başlat";
        simulationCheck.Enabled = true;
        portCombo.Enabled = refreshButton.Enabled = !simulationCheck.Checked;
        if (statusLabel.ForeColor != Theme.Critical) SetStatus("Bağlı değil", Theme.InkMuted);
        UpdateDashboard(null);
    }

    private void SetStatus(string text, Color color)
    {
        statusLabel.Text = text;
        statusLabel.ForeColor = color;
    }

    private void OnConnectionLost(string reason)
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() =>
        {
            SetStatus($"Bağlantı kayıp: {reason}", Theme.Critical);
            Disconnect();
            if (autoReconnectCheck.Checked && _lastPortName is not null) reconnectTimer.Start();
        });
    }

    private void reconnectTimer_Tick(object? sender, EventArgs e)
    {
        if (_link is not null) { reconnectTimer.Stop(); return; }
        if (_lastPortName is null ||
            !System.IO.Ports.SerialPort.GetPortNames().Contains(_lastPortName)) return;

        SetStatus($"{_lastPortName} için yeniden deneniyor...", Theme.Warning);
        RefreshPorts();
        portCombo.SelectedItem = _lastPortName;
        startButton_Click(this, EventArgs.Empty);
        if (_link is not null) reconnectTimer.Stop();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        reconnectTimer.Stop();
        Disconnect();
        _logger?.Dispose();
        base.OnFormClosing(e);
    }

    // ------------------------------------------------------------------ UI güncelleme

    private void OnSnapshotReady(BmsSnapshot snapshot)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                try { ApplySnapshot(snapshot); }
                finally { _worker?.NotifyUiIdle(); }   // worker'a "hazırım" de
            });
        }
        catch (ObjectDisposedException) { /* form kapanıyor */ }
        catch (InvalidOperationException) { /* handle yok edildi */ }
    }

    private void UpdateDashboard(BmsSnapshot? snapshot)
        => dashboard.UpdateData(
            snapshot,
            new LinkHealth(_link?.CrcErrorCount ?? 0, _link?.TimeoutCount ?? 0,
                           _link?.IdMismatchCount ?? 0),
            _link is not null);

    private void ApplySnapshot(BmsSnapshot s)
    {
        voltageGrid.UpdateData(s.CellVoltages, s.IsBalancing);
        temperatureGrid.UpdateData(s.CellTemps, s.IsBalancing);
        balanceGrid.UpdateData(s.CellVoltages, s.IsBalancing);

        UpdateDashboard(s);

        balanceSummary.Text =
            $"Balanstaki hücre: {s.BalancingCount()}/96     " +
            $"İzin verilen dengesizlik: {s.Registers[Reg.AllowedDisbalance]} mV     " +
            $"(veri yaşı {(DateTime.Now - s.BalanceAt).TotalMilliseconds:F0} ms)\n" +
            "Not: balans aç/kapa firmware'de ayrı bir global; UI'dan kontrol edilemez.";

        _logger?.Log(s);
    }

    // ------------------------------------------------------------------ görünüm ayarları

    private void LoadSettingsIntoInputs()
    {
        vAlarmLowInput.Value = (decimal)_settings.VoltageAlarmLow;
        vAlarmHighInput.Value = (decimal)_settings.VoltageAlarmHigh;
        vScaleLowInput.Value = (decimal)_settings.VoltageScaleLow;
        vScaleHighInput.Value = (decimal)_settings.VoltageScaleHigh;
        tAlarmLowInput.Value = (decimal)_settings.TempAlarmLow;
        tAlarmHighInput.Value = (decimal)_settings.TempAlarmHigh;
        tScaleLowInput.Value = (decimal)_settings.TempScaleLow;
        tScaleHighInput.Value = (decimal)_settings.TempScaleHigh;
    }

    private void ApplySettingsToGrids()
    {
        voltageGrid.Settings = _settings;
        temperatureGrid.Settings = _settings;
        balanceGrid.Settings = _settings;
    }

    private void applySettingsButton_Click(object? sender, EventArgs e)
    {
        _settings = new DisplaySettings
        {
            VoltageAlarmLow = (double)vAlarmLowInput.Value,
            VoltageAlarmHigh = (double)vAlarmHighInput.Value,
            VoltageScaleLow = (double)vScaleLowInput.Value,
            VoltageScaleHigh = (double)vScaleHighInput.Value,
            TempAlarmLow = (double)tAlarmLowInput.Value,
            TempAlarmHigh = (double)tAlarmHighInput.Value,
            TempScaleLow = (double)tScaleLowInput.Value,
            TempScaleHigh = (double)tScaleHighInput.Value,
        }.Normalized();

        LoadSettingsIntoInputs();      // normalizasyon değiştirdiyse kutulara yansısın
        ApplySettingsToGrids();

        try
        {
            _settings.Save();
            settingsStatusLabel.Text = "Kaydedildi — sonraki açılışta da geçerli.";
            settingsStatusLabel.ForeColor = Theme.Good;
        }
        catch (Exception ex)
        {
            settingsStatusLabel.Text = $"Uygulandı ama diske yazılamadı: {ex.Message}";
            settingsStatusLabel.ForeColor = Theme.Warning;
        }
    }

    private void resetSettingsButton_Click(object? sender, EventArgs e)
    {
        _settings = new DisplaySettings();
        LoadSettingsIntoInputs();
        ApplySettingsToGrids();
        settingsStatusLabel.Text = "Firmware eşiklerine dönüldü (2.50 / 4.23 V, 80 °C).";
        settingsStatusLabel.ForeColor = Theme.InkSecondary;
    }

    // ------------------------------------------------------------------ CSV log

    private void chooseFileButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV dosyası (*.csv)|*.csv",
            FileName = $"bmsui_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            OverwritePrompt = false,   // mevcut dosyaya ekleme yapılır
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) logPathBox.Text = dialog.FileName;
    }

    private void logEnabledCheck_CheckedChanged(object? sender, EventArgs e)
    {
        if (logEnabledCheck.Checked)
        {
            if (string.IsNullOrWhiteSpace(logPathBox.Text))
            {
                MessageBox.Show("Önce bir dosya seçin.", "Uyarı");
                logEnabledCheck.Checked = false;
                return;
            }
            try
            {
                var interval = TimeSpan.FromSeconds(1.0 / (double)logRateInput.Value);
                _logger = new CsvLogger(logPathBox.Text, interval);
                logStatusLabel.Text = "Kayıt sürüyor...";
                logStatusLabel.ForeColor = Theme.Good;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dosya açılamadı: {ex.Message}", "Hata");
                logEnabledCheck.Checked = false;
            }
        }
        else
        {
            logStatusLabel.Text = _logger is null ? "" : $"Kayıt durdu ({_logger.RowCount} satır)";
            logStatusLabel.ForeColor = Theme.InkMuted;
            _logger?.Dispose();
            _logger = null;
        }
    }

    // ------------------------------------------------------------------ test erişimi

    // Testler UI veri yolunu gerçek kontroller üzerinden sürebilsin diye dar erişim.
    internal CheckBox SimulationCheckBox => simulationCheck;
    internal Button StartStopButton => startButton;
    internal Label ConnectionStatusLabel => statusLabel;
    internal TabControl TabsControl => tabs;
    internal DashboardPanel Dashboard => dashboard;
    internal PictureBox LogoBox => logoBox;

    // ------------------------------------------------------------------ tema

    private void ApplyTheme(Control root)
    {
        switch (root)
        {
            case Form:
                root.BackColor = Theme.Page;
                root.ForeColor = Theme.Ink;
                break;
            case Button button:
                button.BackColor = Theme.Input;
                button.ForeColor = Theme.Ink;
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 90);
                break;
            case ComboBox or TextBox or NumericUpDown:
                root.BackColor = Theme.Input;
                root.ForeColor = Theme.Ink;
                break;
            case GroupBox or Panel or TabPage or FlowLayoutPanel or TableLayoutPanel or PictureBox:
                root.BackColor = Theme.Card;
                root.ForeColor = Theme.InkSecondary;
                break;
            case DashboardPanel or CellGridControl:
                return;                      // kendi çizimini yapar, temaya dokunma
            default:
                root.ForeColor = Theme.InkSecondary;
                break;
        }

        foreach (Control child in root.Controls) ApplyTheme(child);
    }
}
