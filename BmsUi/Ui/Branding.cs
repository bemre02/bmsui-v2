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
