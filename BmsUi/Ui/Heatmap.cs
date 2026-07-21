namespace BmsUi.Ui;

public enum CellState
{
    Normal,
    /// <summary>Alarm esiginin altinda ya da ustunde — status rengi + uyari ikonu.</summary>
    Alarm,
    /// <summary>Eksik / stale hucre (0.00 V gibi).</summary>
    Invalid,
}

/// <summary>
/// Buyukluk icin TEK hue'lu sequential ramp (koyu zeminde koyu=dusuk, acik=yuksek).
/// Alarm bir ramp adimi DEGIL, ayri bir status rengidir ve her zaman uyari ikonuyla
/// birlikte cizilir — renk tek basina anlam tasimaz.
/// </summary>
public static class Heatmap
{
    // Sequential mavi ramp (dogrulanmis referans palet). Dizi dusuk -> yuksek sirali;
    // koyu zeminde en dusuk deger zemine dogru cekilsin diye koyudan aciga.
    public static readonly Color[] VoltageRamp =
    {
        FromHex(0x0D366B), FromHex(0x104281), FromHex(0x184F95), FromHex(0x1C5CAB),
        FromHex(0x256ABF), FromHex(0x2A78D6), FromHex(0x3987E5), FromHex(0x5598E7),
        FromHex(0x6DA7EC), FromHex(0x86B6EF), FromHex(0x9EC5F4), FromHex(0xB7D3F6),
        FromHex(0xCDE2FB),
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

    /// <summary>Dolgu rengi: alarm -> status, gecersiz -> gri, aksi halde ramp adimi.</summary>
    public static Color Fill(CellState state, double value, double scaleLow, double scaleHigh,
                             Color[] ramp) => state switch
    {
        CellState.Alarm => AlarmColor,
        CellState.Invalid => InvalidColor,
        _ => Sequential(value, scaleLow, scaleHigh, ramp),
    };

    /// <summary>Dolgunun uzerine yazilacak metin rengi — acik zeminde koyu murekkep.</summary>
    public static Color InkOn(Color fill)
    {
        double luminance = (0.299 * fill.R + 0.587 * fill.G + 0.114 * fill.B) / 255.0;
        return luminance > 0.62 ? FromHex(0x0B0B0B) : Color.White;
    }
}
