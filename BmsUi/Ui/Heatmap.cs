namespace BmsUi.Ui;

public enum CellState
{
    Normal,
    /// <summary>Alarm eşiğinin altında ya da üstünde — kalın kontur + uyarı ikonu.</summary>
    Alarm,
    /// <summary>Eksik / stale hucre (0.00 V gibi).</summary>
    Invalid,
}

/// <summary>
/// Değer → renk eşlemesi. Dolgu her zaman büyüklüğü taşır; alarm dolguyu değiştirmez,
/// kalın kontur + uyarı ikonuyla gösterilir — böylece renk tek başına anlam taşımaz ve
/// eşik yanlış ayarlansa bile ızgara okunur kalır.
/// </summary>
public static class Heatmap
{
    /// <summary>
    /// Voltaj: düşük = kırmızı, orta = sarı, yüksek = yeşil (BMS operatörünün beklediği
    /// okuma). Kırmızı-yeşil, renk körlüğü için en riskli çifttir; bu yüzden değer her
    /// hücrede yazılı, alt çubuk aynı bilgiyi UZUNLUKLA veriyor ve eşik dışı hücreler
    /// ayrıca uyarı ikonu alıyor — renk tek başına hiçbir şey taşımıyor.
    /// </summary>
    public static readonly Color[] VoltageRamp =
    {
        FromHex(0x7F1D1D), FromHex(0x991B1B), FromHex(0xB91C1C), FromHex(0xDC2626),
        FromHex(0xE85D25), FromHex(0xF97316), FromHex(0xF59E0B), FromHex(0xEAB308),
        FromHex(0xC9C520), FromHex(0x9DC22A), FromHex(0x6FB93A), FromHex(0x45AC49),
        FromHex(0x22C55E),
    };

    // Sicaklik icin tek hue'lu amber ramp — voltajla ayni ekranda gorunmedigi icin
    // (ayri sekmeler) ikinci bir hue guvenli; tek hue + monoton aciklik CVD-guvenli.
    public static readonly Color[] TemperatureRamp =
    {
        FromHex(0x3A1F05), FromHex(0x4D2A07), FromHex(0x63380A), FromHex(0x7A460C),
        FromHex(0x94560E), FromHex(0xAD6610), FromHex(0xC67712), FromHex(0xDB8A1F),
        FromHex(0xE89C3C), FromHex(0xF0AE5E), FromHex(0xF5C084), FromHex(0xF8D2A8),
        FromHex(0xFBE3CB),
    };

    // Status paleti — sabit, hicbir zaman seri rengi olarak kullanilmaz
    public static readonly Color AlarmColor = FromHex(0xD03B3B);   // critical
    public static readonly Color WarningColor = FromHex(0xFAB219); // warning
    public static readonly Color InvalidColor = FromHex(0x4A4A48);

    // Koyu tema yuzeyleri / murekkep
    public static readonly Color Surface = FromHex(0x1A1A19);
    public static readonly Color PrimaryInk = Color.White;
    public static readonly Color MutedInk = FromHex(0x898781);
    public static readonly Color Hairline = FromHex(0x2C2C2A);
    public static readonly Color BalanceRing = FromHex(0xFAB219);

    public static Color FromHex(int rgb)
        => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>Degeri [low, high] araliginda ramp adimina esler (aralik disi uclara kirpilir).</summary>
    public static Color Sequential(double value, double low, double high, Color[] ramp)
    {
        if (high <= low) return ramp[0];
        double t = Math.Clamp((value - low) / (high - low), 0.0, 1.0);
        int index = (int)Math.Round(t * (ramp.Length - 1));
        return ramp[Math.Clamp(index, 0, ramp.Length - 1)];
    }

    public static CellState VoltageState(double v, double alarmLow, double alarmHigh)
        => v < 0.5 ? CellState.Invalid
         : (v < alarmLow || v > alarmHigh) ? CellState.Alarm
         : CellState.Normal;

    public static CellState TemperatureState(double t, double alarmLow, double alarmHigh)
        => (t <= -50 || t >= 150) ? CellState.Invalid
         : (t < alarmLow || t > alarmHigh) ? CellState.Alarm
         : CellState.Normal;

    /// <summary>
    /// Dolgu rengi HER ZAMAN degeri gosterir; yalnizca gecersiz hucre griye duser.
    ///
    /// Alarm dolguyu DEGISTIRMEZ: voltaj ramp'inin dusuk ucu zaten kirmizi oldugu icin
    /// kirmizi bir alarm dolgusu "dusuk ama normal" hucreyle karisirdi. Ustelik esik
    /// yanlis ayarlaninca tum izgara tek renge duserdi. Alarm bunun yerine kalin kontur
    /// + uyari ikonu ile gosterilir.
    /// </summary>
    public static Color Fill(CellState state, double value, double scaleLow, double scaleHigh,
                             Color[] ramp) => state == CellState.Invalid
        ? InvalidColor
        : Sequential(value, scaleLow, scaleHigh, ramp);

    /// <summary>
    /// Saf siyah — #0B0B0B degil. Bir ramp koyudan aciga gecerken mutlaka beyaz ve koyu
    /// murekkebin esitlendigi noktadan geciyor; oradaki kontrast tavanini murekkebin
    /// koyulugu belirliyor. #0B0B0B ile bu taban 4.44'te kaliyordu (AA esigi 4.5'in
    /// altinda), saf siyahla 4.58'e cikiyor.
    /// </summary>
    public static readonly Color DarkInk = Color.Black;

    /// <summary>
    /// Dolgunun üzerine yazılacak metin rengi: beyaz mı koyu mu daha yüksek kontrast
    /// veriyorsa o.
    ///
    /// Basit "algısal parlaklık + sabit eşik" yaklaşımı burada yanlış sonuç veriyordu:
    /// doygun yeşil (#22C55E) o formülde 0.535 çıkıp eşiğin altına düşüyor ve beyaz yazı
    /// seçiliyordu, oysa gerçek kontrast beyazda 2.3, koyuda 8.6. Hücre değeri rampın en
    /// üst adımına girip çıktıkça yazı rengi bir beyaz bir koyu oluyordu.
    /// Artık WCAG bağıl parlaklığı üzerinden iki seçenek karşılaştırılıyor.
    /// </summary>
    public static Color InkOn(Color fill)
    {
        double l = RelativeLuminance(fill);
        double whiteContrast = 1.05 / (l + 0.05);
        double darkContrast = (l + 0.05) / (RelativeLuminance(DarkInk) + 0.05);
        return whiteContrast >= darkContrast ? Color.White : DarkInk;
    }

    /// <summary>WCAG 2.x bağıl parlaklık (sRGB doğrusallaştırmasıyla).</summary>
    public static double RelativeLuminance(Color c)
        => 0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);

    private static double Linearize(byte channel)
    {
        double v = channel / 255.0;
        return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }
}
