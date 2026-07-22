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
    private string? _lastPortName;   // null in simulation — no reconnect is attempted
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
        // AFTER layout settles: a SplitterDistance set while the control is tiny gets clamped
        int desired = Math.Min(360, Math.Max(mainSplit.Panel1MinSize, mainSplit.Width / 3));
        if (mainSplit.Width > desired + mainSplit.Panel2MinSize)
            mainSplit.SplitterDistance = desired;
    }

    // ------------------------------------------------------------------ connection

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVNODES_CHANGED = 0x0007;

    /// <summary>
    /// Windows broadcasts this to every top-level window when a USB device is plugged or
    /// unplugged. Without an automatic refresh a board plugged in after the app started
    /// never appears — expecting the user to press "Refresh" invites mistakes.
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WM_DEVICECHANGE && m.WParam.ToInt32() == DBT_DEVNODES_CHANGED &&
            _link is null && !portCombo.DroppedDown)
            RefreshPorts();
    }

    private string? SelectedPortName() => (portCombo.SelectedItem as PortInfo)?.Name;

    private bool SelectPort(string? name)
    {
        if (name is null) return false;
        for (int i = 0; i < portCombo.Items.Count; i++)
            if (portCombo.Items[i] is PortInfo info &&
                string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                portCombo.SelectedIndex = i;
                return true;
            }
        return false;
    }

    private void RefreshPorts()
    {
        string? previous = SelectedPortName();
        var ports = SerialPortCatalog.List();

        // Leave it alone when nothing changed, so the selection is not reset for nothing
        if (portCombo.Items.Count == ports.Count)
        {
            bool same = true;
            for (int i = 0; i < ports.Count && same; i++)
                same = portCombo.Items[i] is PortInfo existing && existing == ports[i];
            if (same) return;
        }

        portCombo.BeginUpdate();
        portCombo.Items.Clear();
        foreach (var port in ports) portCombo.Items.Add(port);
        portCombo.EndUpdate();

        if (!SelectPort(previous) && portCombo.Items.Count > 0) portCombo.SelectedIndex = 0;
    }

    private void refreshButton_Click(object? sender, EventArgs e) => RefreshPorts();

    /// <summary>The COM selection is meaningless while simulation is ticked.</summary>
    private void simulationCheck_CheckedChanged(object? sender, EventArgs e)
    {
        if (_link is not null) return;   // already locked while connected
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
            // In-app virtual device: everything above SerialLink is the real-board path
            transport = new SimulatedTransport();
            label = "Simulation";
        }
        else
        {
            if (SelectedPortName() is not string portName)
            {
                MessageBox.Show("Select a COM port first " +
                                "(or tick the 'Simulation' box).", "Warning");
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
            SetStatus($"Could not open {label}", Theme.Critical);
            if (!reconnectTimer.Enabled)
                MessageBox.Show($"Could not open {label}: {ex.Message}\n\n" +
                                "Is the port plugged in, and is another application using it?",
                                "Error");
            return;
        }

        // Verify with a ping that the attached device really is the HV BMS
        if (!link.Ping())
        {
            string reason = link.LastError ?? "no response";
            link.Dispose();
            SetStatus($"{label}: device not responding", Theme.Critical);
            if (!reconnectTimer.Enabled)
                MessageBox.Show($"{label} opened but the device did not answer the ping " +
                                $"({reason}).\n\nIs this the right port? Is the board running?",
                                "Device not found");
            return;
        }

        _link = link;
        _lastPortName = simulated ? null : label;
        reconnectTimer.Stop();

        _worker = new PollWorker(link);
        _worker.SnapshotReady += OnSnapshotReady;
        _worker.ConnectionLost += OnConnectionLost;
        _worker.Start();
        _worker.IncludeAllRegisters = tabs.SelectedTab == registersTab;

        startButton.Text = "Stop";
        portCombo.Enabled = refreshButton.Enabled = simulationCheck.Enabled = false;
        SetStatus(simulated ? "Simulation running" : $"Connected: {label}", Theme.Good);
    }

    private void Disconnect()
    {
        _worker?.Stop();
        _worker?.Dispose();
        _worker = null;

        _link?.Dispose();
        _link = null;

        logEnabledCheck.Checked = false;

        startButton.Text = "Start";
        simulationCheck.Enabled = true;
        portCombo.Enabled = refreshButton.Enabled = !simulationCheck.Checked;
        if (statusLabel.ForeColor != Theme.Critical) SetStatus("Not connected", Theme.InkMuted);
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
            SetStatus($"Link lost: {reason}", Theme.Critical);
            Disconnect();
            if (autoReconnectCheck.Checked && _lastPortName is not null) reconnectTimer.Start();
        });
    }

    private void reconnectTimer_Tick(object? sender, EventArgs e)
    {
        if (_link is not null) { reconnectTimer.Stop(); return; }
        if (_lastPortName is null ||
            !System.IO.Ports.SerialPort.GetPortNames().Contains(_lastPortName)) return;

        SetStatus($"Retrying {_lastPortName}...", Theme.Warning);
        RefreshPorts();
        if (!SelectPort(_lastPortName)) return;
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

    // ------------------------------------------------------------------ UI update

    private void OnSnapshotReady(BmsSnapshot snapshot)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() =>
            {
                try { ApplySnapshot(snapshot); }
                finally { _worker?.NotifyUiIdle(); }   // tell the worker we are ready again
            });
        }
        catch (ObjectDisposedException) { /* the form is closing */ }
        catch (InvalidOperationException) { /* the handle is gone */ }
    }

    /// <summary>
    /// The register sweep is 33 extra transactions per second, so it only runs while the
    /// Registers tab is actually on screen.
    /// </summary>
    private void tabs_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_worker is not null) _worker.IncludeAllRegisters = tabs.SelectedTab == registersTab;
    }

    private void UpdateDashboard(BmsSnapshot? snapshot)
    {
        dashboard.UpdateData(
            snapshot,
            new LinkHealth(_link?.CrcErrorCount ?? 0, _link?.TimeoutCount ?? 0,
                           _link?.IdMismatchCount ?? 0),
            _link is not null);

        // Clear the register table on disconnect; stale values would look live
        if (snapshot is null) registersTable.UpdateData(null);
    }

    private void ApplySnapshot(BmsSnapshot s)
    {
        voltageGrid.UpdateData(s.CellVoltages, s.IsBalancing);
        temperatureGrid.UpdateData(s.CellTemps, s.IsBalancing);
        balanceGrid.UpdateData(s.CellVoltages, s.IsBalancing);
        registersTable.UpdateData(s);

        UpdateDashboard(s);

        balanceSummary.Text =
            $"Balancing cells: {s.BalancingCount()}/96     " +
            $"Allowed disbalance: {s.Registers[Reg.AllowedDisbalance]} mV     " +
            $"(data age {(DateTime.Now - s.BalanceAt).TotalMilliseconds:F0} ms)\n" +
            "Note: balance enable is a separate global in the firmware and cannot be " +
            "controlled from the UI.";

        _logger?.Log(s);
    }

    // ------------------------------------------------------------------ display settings

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

        LoadSettingsIntoInputs();      // reflect anything normalisation changed
        ApplySettingsToGrids();

        try
        {
            _settings.Save();
            settingsStatusLabel.Text = "Saved — also applies on the next start.";
            settingsStatusLabel.ForeColor = Theme.Good;
        }
        catch (Exception ex)
        {
            settingsStatusLabel.Text = $"Applied but could not be written to disk: {ex.Message}";
            settingsStatusLabel.ForeColor = Theme.Warning;
        }
    }

    private void resetSettingsButton_Click(object? sender, EventArgs e)
    {
        _settings = new DisplaySettings();
        LoadSettingsIntoInputs();
        ApplySettingsToGrids();
        settingsStatusLabel.Text = "Restored the firmware thresholds (2.50 / 4.23 V, 80 °C).";
        settingsStatusLabel.ForeColor = Theme.InkSecondary;
    }

    // ------------------------------------------------------------------ CSV log

    private void chooseFileButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"bmsui_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            OverwritePrompt = false,   // we append to an existing file
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) logPathBox.Text = dialog.FileName;
    }

    private void logEnabledCheck_CheckedChanged(object? sender, EventArgs e)
    {
        if (logEnabledCheck.Checked)
        {
            if (string.IsNullOrWhiteSpace(logPathBox.Text))
            {
                MessageBox.Show("Choose a file first.", "Warning");
                logEnabledCheck.Checked = false;
                return;
            }
            try
            {
                var interval = TimeSpan.FromSeconds(1.0 / (double)logRateInput.Value);
                _logger = new CsvLogger(logPathBox.Text, interval);
                logStatusLabel.Text = "Recording...";
                logStatusLabel.ForeColor = Theme.Good;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open the file: {ex.Message}", "Error");
                logEnabledCheck.Checked = false;
            }
        }
        else
        {
            logStatusLabel.Text = _logger is null ? "" : $"Recording stopped ({_logger.RowCount} rows)";
            logStatusLabel.ForeColor = Theme.InkMuted;
            _logger?.Dispose();
            _logger = null;
        }
    }

    // ------------------------------------------------------------------ test access

    // A narrow surface so tests can drive the UI data path through the real controls.
    internal CheckBox SimulationCheckBox => simulationCheck;
    internal Button StartStopButton => startButton;
    internal Label ConnectionStatusLabel => statusLabel;
    internal TabControl TabsControl => tabs;
    internal DashboardPanel Dashboard => dashboard;
    internal RegisterTable RegistersTable => registersTable;
    internal TabPage RegistersTab => registersTab;
    internal PictureBox LogoBox => logoBox;

    // ------------------------------------------------------------------ theme

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
                return;                      // draws itself; leave the theme alone
            default:
                root.ForeColor = Theme.InkSecondary;
                break;
        }

        foreach (Control child in root.Controls) ApplyTheme(child);
    }
}
