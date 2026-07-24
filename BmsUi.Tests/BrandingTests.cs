using System.Drawing;
using BmsUi.Ui;
using Xunit;

/// <summary>
/// The team logo is far from square (620x161). Any layout that fills a fixed area squashes it
/// visibly, so the fit maths — shared by the window icon and the splash screen — is pinned here.
/// </summary>
public class BrandingTests
{
    private static readonly Size Logo = new(620, 161);

    [Fact]
    public void ScaleToFit_KeepsTheAspectRatio()
    {
        var fitted = Branding.ScaleToFit(Logo, new Size(300, 300));

        double source = (double)Logo.Width / Logo.Height;
        double result = (double)fitted.Width / fitted.Height;
        Assert.Equal(source, result, 2);      // 2 decimals absorbs the integer rounding
    }

    [Fact]
    public void ScaleToFit_IsLimitedByTheTighterAxis()
    {
        // A wide logo in a square box is limited by width...
        var inSquare = Branding.ScaleToFit(Logo, new Size(300, 300));
        Assert.Equal(300, inSquare.Width);
        Assert.True(inSquare.Height < 300);

        // ...and by height in a wide strip
        var inStrip = Branding.ScaleToFit(Logo, new Size(1000, 50));
        Assert.Equal(50, inStrip.Height);
        Assert.True(inStrip.Width < 1000);
    }

    [Fact]
    public void ScaleToFit_NeverExceedsTheBox()
    {
        var box = new Size(300, 92);           // the splash's logo box
        var fitted = Branding.ScaleToFit(Logo, box);

        Assert.True(fitted.Width <= box.Width, $"{fitted.Width} > {box.Width}");
        Assert.True(fitted.Height <= box.Height, $"{fitted.Height} > {box.Height}");
    }

    [Fact]
    public void ScaleToFit_EnlargesASourceSmallerThanTheBox()
    {
        Assert.Equal(new Size(100, 50), Branding.ScaleToFit(new Size(10, 5), new Size(100, 100)));
    }

    [Fact]
    public void ScaleToFit_DegenerateInputsAreEmpty_NotACrash()
    {
        // The logo resource can be missing entirely; the splash still has to reach its Paint
        Assert.Equal(Size.Empty, Branding.ScaleToFit(Size.Empty, new Size(100, 100)));
        Assert.Equal(Size.Empty, Branding.ScaleToFit(Logo, new Size(0, 50)));
        Assert.Equal(Size.Empty, Branding.ScaleToFit(Logo, new Size(100, -1)));
    }

    [Fact]
    public void EmbeddedLogo_IsMuchWiderThanTall()
    {
        // Pins the assumption the splash layout is built on
        Assert.NotNull(Branding.TeamLogo);
        Assert.True(Branding.TeamLogo!.Width > Branding.TeamLogo.Height * 2,
                    $"expected a wide logo, got {Branding.TeamLogo.Width}x{Branding.TeamLogo.Height}");
    }
}
