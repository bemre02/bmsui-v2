using BmsUi.Ui;

namespace BmsUi;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    // Baglanti cubugu
    private Panel connectionPanel;
    private Label appTitleLabel;
    private Label portLabel;
    private ComboBox portCombo;
    private Button refreshButton;
    private Button startButton;
    private Label statusLabel;
    private CheckBox simulationCheck;
    private CheckBox autoReconnectCheck;
    private PictureBox logoBox;
    private System.Windows.Forms.Timer reconnectTimer;

    // Ana yerlesim
    private SplitContainer mainSplit;
    private DashboardPanel dashboard;
    private TabControl tabs;

    // Sekmeler
    private TabPage voltageTab, temperatureTab, balanceTab, settingsTab, logTab;
    private CellGridControl voltageGrid, temperatureGrid, balanceGrid;
    private Label tempNoteLabel, balanceSummary;

    // Ayarlar sekmesi (yalnizca gorunum — cihaza yazilmaz)
    private NumericUpDown vAlarmLowInput, vAlarmHighInput, vScaleLowInput, vScaleHighInput;
    private NumericUpDown tAlarmLowInput, tAlarmHighInput, tScaleLowInput, tScaleHighInput;
    private Button applySettingsButton, resetSettingsButton;
    private Label settingsStatusLabel;

    // Log sekmesi
    private Button chooseFileButton;
    private TextBox logPathBox;
    private NumericUpDown logRateInput;
    private CheckBox logEnabledCheck;
    private Label logStatusLabel;

    private static NumericUpDown NumInput(decimal min, decimal max, int decimals, decimal step)
        => new()
        {
            Minimum = min,
            Maximum = max,
            DecimalPlaces = decimals,
            Increment = step,
            Dock = DockStyle.Fill,
            TextAlign = HorizontalAlignment.Right,
        };

    /// <summary>"etiket : sayi girisi" satirlarindan olusan bir kutu kurar.</summary>
    private static GroupBox BuildNumericGroup(string title,
                                              params (string Caption, NumericUpDown Input)[] rows)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows.Length,
            Padding = new Padding(8, 4, 8, 4),
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58f));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));

        for (int i = 0; i < rows.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
            table.Controls.Add(new Label
            {
                Text = rows[i].Caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
            }, 0, i);
            table.Controls.Add(rows[i].Input, 1, i);
        }

        var box = new GroupBox { Text = title, Dock = DockStyle.Fill };
        box.Controls.Add(table);
        return box;
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        // ---------------- baglanti cubugu ----------------
        connectionPanel = new Panel { Dock = DockStyle.Top, Height = 52 };

        appTitleLabel = new Label
        {
            Text = "BMS UI",
            AutoSize = true,
            Location = new Point(14, 14),
            Font = new Font(Theme.FamilyName, 14f, FontStyle.Bold),
        };

        portLabel = new Label { Text = "Port:", AutoSize = true, Location = new Point(112, 19) };
        portCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(152, 15),
            Width = 175,
        };
        refreshButton = new Button { Text = "Yenile", Location = new Point(337, 14), Width = 80 };
        refreshButton.Click += refreshButton_Click;
        startButton = new Button { Text = "Başlat", Location = new Point(425, 14), Width = 90 };
        startButton.Click += startButton_Click;
        statusLabel = new Label
        {
            Text = "Bağlı değil",
            AutoSize = true,
            Location = new Point(529, 19),
        };
        simulationCheck = new CheckBox
        {
            Text = "Simülasyon (kart gerekmez)",
            AutoSize = true,
            Location = new Point(700, 18),
        };
        simulationCheck.CheckedChanged += simulationCheck_CheckedChanged;
        autoReconnectCheck = new CheckBox
        {
            Text = "Otomatik yeniden bağlan",
            AutoSize = true,
            Location = new Point(890, 18),
            Checked = true,
        };
        reconnectTimer = new System.Windows.Forms.Timer(components) { Interval = 3000 };
        reconnectTimer.Tick += reconnectTimer_Tick;

        logoBox = new PictureBox
        {
            Dock = DockStyle.Right,
            Width = 140,
            SizeMode = PictureBoxSizeMode.Zoom,
            Margin = new Padding(0),
            Image = Branding.CreateLogo(),
        };

        connectionPanel.Controls.Add(logoBox);
        connectionPanel.Controls.Add(autoReconnectCheck);
        connectionPanel.Controls.Add(simulationCheck);
        connectionPanel.Controls.Add(statusLabel);
        connectionPanel.Controls.Add(startButton);
        connectionPanel.Controls.Add(refreshButton);
        connectionPanel.Controls.Add(portCombo);
        connectionPanel.Controls.Add(portLabel);
        connectionPanel.Controls.Add(appTitleLabel);

        // ---------------- sol pano ----------------
        dashboard = new DashboardPanel { Dock = DockStyle.Fill };

        // ---------------- sekmeler ----------------
        voltageGrid = new CellGridControl { Dock = DockStyle.Fill, Mode = CellGridMode.Voltage };
        voltageTab = new TabPage("Voltaj");
        voltageTab.Controls.Add(voltageGrid);

        temperatureGrid = new CellGridControl
        {
            Dock = DockStyle.Fill,
            Mode = CellGridMode.Temperature,
        };
        // Firmware GUI_DATAS.Cell_Temps[94] = Cell_Temps[20] yapiyor (main.cpp:971) ve USB'nin
        // 0x2A cevabi bu diziden okunuyor -> hucre 94 kendi sensorunu degil 20'yi gosterir.
        tempNoteLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            Text = "Not: hücre 94 kendi sensörünü değil hücre 20'nin sıcaklığını gösterir " +
                   "(firmware remap'i, main.cpp:971).",
        };
        temperatureTab = new TabPage("Sıcaklık");
        temperatureTab.Controls.Add(temperatureGrid);
        temperatureTab.Controls.Add(tempNoteLabel);

        balanceGrid = new CellGridControl { Dock = DockStyle.Fill, Mode = CellGridMode.Voltage };
        balanceSummary = new Label { Dock = DockStyle.Bottom, Height = 42, Text = "—" };
        balanceTab = new TabPage("Balans");
        balanceTab.Controls.Add(balanceGrid);
        balanceTab.Controls.Add(balanceSummary);

        // ---------------- ayarlar sekmesi (yalnizca gorunum) ----------------
        vAlarmLowInput = NumInput(0.00m, 5.00m, 2, 0.05m);
        vAlarmHighInput = NumInput(0.00m, 5.00m, 2, 0.05m);
        vScaleLowInput = NumInput(0.00m, 5.00m, 2, 0.05m);
        vScaleHighInput = NumInput(0.00m, 5.00m, 2, 0.05m);
        tAlarmLowInput = NumInput(-50m, 150m, 1, 1m);
        tAlarmHighInput = NumInput(-50m, 150m, 1, 1m);
        tScaleLowInput = NumInput(-50m, 150m, 1, 1m);
        tScaleHighInput = NumInput(-50m, 150m, 1, 1m);

        var voltageSettings = BuildNumericGroup("Voltaj (V)",
            ("Alarm alt eşiği", vAlarmLowInput),
            ("Alarm üst eşiği", vAlarmHighInput),
            ("Renk skalası alt ucu", vScaleLowInput),
            ("Renk skalası üst ucu", vScaleHighInput));

        var tempSettings = BuildNumericGroup("Sıcaklık (°C)",
            ("Alarm alt eşiği", tAlarmLowInput),
            ("Alarm üst eşiği", tAlarmHighInput),
            ("Renk skalası alt ucu", tScaleLowInput),
            ("Renk skalası üst ucu", tScaleHighInput));

        var settingsTable = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 190,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10, 10, 10, 4),
        };
        settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        settingsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        settingsTable.Controls.Add(voltageSettings, 0, 0);
        settingsTable.Controls.Add(tempSettings, 1, 0);

        applySettingsButton = new Button { Text = "Uygula ve kaydet", Width = 160, Height = 30 };
        applySettingsButton.Click += applySettingsButton_Click;
        resetSettingsButton = new Button { Text = "Varsayılana dön", Width = 160, Height = 30 };
        resetSettingsButton.Click += resetSettingsButton_Click;

        var settingsButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Padding = new Padding(10, 4, 10, 4),
        };
        settingsButtons.Controls.Add(applySettingsButton);
        settingsButtons.Controls.Add(resetSettingsButton);

        settingsStatusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Padding = new Padding(12, 4, 4, 0),
            Text = "",
        };

        var settingsNote = new Label
        {
            Dock = DockStyle.Top,
            Height = 92,
            Padding = new Padding(12, 6, 12, 0),
            Text = "Bu ayarlar YALNIZCA arayüzün görünümünü etkiler — BMS'e hiçbir şey " +
                   "yazılmaz, cihazın kendi fault eşikleri değişmez.\n\n" +
                   "Alarm eşikleri: dışına çıkan hücre kırmızı gösterilir ve üzerine uyarı " +
                   "ikonu çizilir. Renk skalası: heatmap'in iki ucu; aralık daraldıkça " +
                   "hücreler arası fark daha belirgin görünür.\n" +
                   "Varsayılanlar firmware eşikleriyle aynı: 2.50 / 4.23 V ve 80 °C " +
                   "(main.h:194-200).",
        };

        settingsTab = new TabPage("Ayarlar");
        settingsTab.Controls.Add(settingsNote);
        settingsTab.Controls.Add(settingsStatusLabel);
        settingsTab.Controls.Add(settingsButtons);
        settingsTab.Controls.Add(settingsTable);

        // ---------------- log sekmesi ----------------
        chooseFileButton = new Button { Text = "Dosya seç...", Location = new Point(20, 20), Width = 120 };
        chooseFileButton.Click += chooseFileButton_Click;
        logPathBox = new TextBox { ReadOnly = true, Location = new Point(150, 21), Width = 460 };
        logRateInput = new NumericUpDown
        {
            Minimum = 0.1m,
            Maximum = 10m,
            Increment = 0.5m,
            DecimalPlaces = 1,
            Value = 1m,
            Location = new Point(150, 60),
            Width = 90,
        };
        logEnabledCheck = new CheckBox
        {
            Text = "Kaydı başlat",
            AutoSize = true,
            Location = new Point(260, 61),
        };
        logEnabledCheck.CheckedChanged += logEnabledCheck_CheckedChanged;
        logStatusLabel = new Label
        {
            Text = "",
            AutoSize = false,
            Width = 560,
            Height = 24,
            Location = new Point(20, 100),
        };

        logTab = new TabPage("Log");
        logTab.Controls.Add(new Label
        {
            Text = "Kayıt hızı (Hz):",
            AutoSize = true,
            Location = new Point(20, 62),
        });
        logTab.Controls.Add(chooseFileButton);
        logTab.Controls.Add(logPathBox);
        logTab.Controls.Add(logRateInput);
        logTab.Controls.Add(logEnabledCheck);
        logTab.Controls.Add(logStatusLabel);

        tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.AddRange(new[]
        {
            voltageTab, temperatureTab, balanceTab, settingsTab, logTab,
        });

        // ---------------- ana bolme ----------------
        // NOT: SplitterDistance BURADA atanmaz — kontrol henuz boyutlanmadigi icin
        // sessizce kirpilir. Form1.OnLoad icinde, yerlesim oturduktan sonra atanir.
        mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 6,
            Panel1MinSize = 300,
        };
        mainSplit.Panel1.Controls.Add(dashboard);
        mainSplit.Panel2.Controls.Add(tabs);

        // ---------------- form ----------------
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1120, 720);
        Controls.Add(mainSplit);
        Controls.Add(connectionPanel);
        Text = "BMS UI";
    }

    #endregion
}
