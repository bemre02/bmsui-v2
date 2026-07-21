using System.Reflection;

namespace BmsUi.Ui;

/// <summary>Takim logosu — derlemeye gomulu, dosya kaybolsa da uygulama acilir.</summary>
public static class Branding
{
    private const string ResourceName = "BmsUi.Resources.team_logo.png";

    /// <summary>Kaynak görsel — DOĞRUDAN bir kontrole atanmaz (aşağıya bakın).</summary>
    public static Image? TeamLogo { get; } = LoadLogo();

    /// <summary>
    /// Her çağrıda YENİ bir kopya döndürür. PictureBox kendisine atanan Image'i Dispose
    /// ederken yok ediyor; paylaşılan tek statik görsel ikinci bir pencerede ölü kalıyor
    /// (kırmızı X). Kontrollere hep kopya verilir.
    /// </summary>
    public static Image? CreateLogo() => TeamLogo is null ? null : (Image)TeamLogo.Clone();

    private static Image? LoadLogo()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null) return null;
            return Image.FromStream(stream);
        }
        catch
        {
            return null;   // logo yoksa UI yine calisir
        }
    }

    /// <summary>Pencere/gorev cubugu ikonu (logodan uretilir).</summary>
    public static Icon? CreateWindowIcon()
    {
        if (TeamLogo is null) return null;
        try
        {
            using var square = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(square))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                float scale = Math.Min(64f / TeamLogo.Width, 64f / TeamLogo.Height);
                float w = TeamLogo.Width * scale, h = TeamLogo.Height * scale;
                g.DrawImage(TeamLogo, (64 - w) / 2f, (64 - h) / 2f, w, h);
            }
            IntPtr handle = square.GetHicon();
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        catch
        {
            return null;
        }
    }
}
