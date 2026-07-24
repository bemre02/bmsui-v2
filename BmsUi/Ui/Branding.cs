using System.Reflection;

namespace BmsUi.Ui;

/// <summary>Team logo — embedded in the assembly, so a missing file never blocks startup.</summary>
public static class Branding
{
    private const string ResourceName = "BmsUi.Resources.team_logo.png";

    /// <summary>Source image — never assigned to a control DIRECTLY (see below).</summary>
    public static Image? TeamLogo { get; } = LoadLogo();

    /// <summary>
    /// Returns a NEW copy on every call. PictureBox disposes the Image assigned to it, so
    /// a single shared static image would already be dead in a second window (red X).
    /// Controls always get a copy.
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

    /// <summary>
    /// Largest size that fits inside <paramref name="box"/> without distorting the source.
    ///
    /// The team logo is far from square — 620x161, close to 4:1 — so any layout that fills a
    /// fixed area squashes it visibly. Both the window icon and the splash screen size it
    /// through here. Returns <see cref="Size.Empty"/> for a degenerate input rather than
    /// throwing: the logo may be missing entirely and the caller still has to draw something.
    /// </summary>
    public static Size ScaleToFit(Size source, Size box)
    {
        if (source.Width <= 0 || source.Height <= 0 || box.Width <= 0 || box.Height <= 0)
            return Size.Empty;

        double scale = Math.Min((double)box.Width / source.Width, (double)box.Height / source.Height);
        return new Size(Math.Max(1, (int)Math.Round(source.Width * scale)),
                        Math.Max(1, (int)Math.Round(source.Height * scale)));
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
                var fitted = ScaleToFit(TeamLogo.Size, new Size(64, 64));
                g.DrawImage(TeamLogo, (64 - fitted.Width) / 2f, (64 - fitted.Height) / 2f,
                            fitted.Width, fitted.Height);
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
