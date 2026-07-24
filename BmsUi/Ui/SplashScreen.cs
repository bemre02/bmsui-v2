using System.Diagnostics;
using System.Reflection;

namespace BmsUi.Ui;

/// <summary>
/// Startup splash, shown while the main window is being built.
///
/// It runs on its OWN STA thread with its own message pump, which is the whole design decision
/// here. Startup was measured before writing this: ~426 ms inside the Form1 constructor plus
/// ~258 ms to first paint, all of it on the main UI thread. A splash owned by that thread would
/// be frozen for exactly the period it is meant to be alive. On its own thread it keeps
/// repainting, and because the two run concurrently the splash adds no startup time at all.
///
/// Nothing here may prevent the application from opening: <see cref="Show"/> returns null
/// instead of throwing, and every other entry point swallows its own failures. The caller can
/// treat the whole feature as optional.
/// </summary>
public sealed class SplashScreen : IDisposable
{
    /// <summary>
    /// Floor on how long the splash stays up. Startup measures ~700 ms on this machine, so this
    /// normally costs nothing — it exists so a faster machine cannot flash the splash for a
    /// fraction of a second, which reads as a glitch rather than a welcome.
    /// </summary>
    private static readonly TimeSpan MinimumVisible = TimeSpan.FromMilliseconds(600);

    private readonly SplashForm _form;
    private readonly Stopwatch _visibleFor = Stopwatch.StartNew();
    private bool _closeRequested;

    private SplashScreen(SplashForm form) => _form = form;

    /// <summary>
    /// Starts the splash on a background STA thread and returns once it is on screen.
    /// Returns null if anything goes wrong — startup then simply continues without it.
    /// </summary>
    public static SplashScreen? Show()
    {
        try
        {
            SplashForm? shown = null;
            using var ready = new ManualResetEventSlim();

            // IsBackground: the splash thread must never be what keeps the process alive.
            var thread = new Thread(() =>
            {
                try
                {
                    var form = new SplashForm();
                    form.Shown += (_, _) => { shown = form; ready.Set(); };
                    Application.Run(form);
                }
                catch
                {
                    // A splash that cannot paint is not worth an exception on the way in
                }
                finally
                {
                    ready.Set();
                }
            })
            { IsBackground = true, Name = "SplashScreen" };

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            // Generous relative to the ~30 ms this takes; the wait is bounded so a wedged
            // splash thread cannot hold up the application.
            ready.Wait(TimeSpan.FromSeconds(2));

            return shown is null ? null : new SplashScreen(shown);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Closes the splash, honouring <see cref="MinimumVisible"/>. Returns immediately: the wait
    /// happens on the splash thread, because blocking the caller here would freeze the main
    /// window at the exact moment it appears.
    /// </summary>
    public void Close()
    {
        if (_closeRequested) return;
        _closeRequested = true;

        try
        {
            if (_form.IsDisposed) return;

            _form.BeginInvoke(() =>
            {
                var remaining = MinimumVisible - _visibleFor.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    _form.Close();
                    return;
                }

                var timer = new System.Windows.Forms.Timer
                {
                    Interval = Math.Max(1, (int)remaining.TotalMilliseconds),
                };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    _form.Close();
                };
                timer.Start();
            });
        }
        catch
        {
            // The splash thread may already be gone (its form closed itself); nothing to do
        }
    }

    public void Dispose() => Close();

    /// <summary>
    /// The window itself: borderless, centred, drawn from the same <see cref="Theme"/> tokens as
    /// the main window so the hand-off does not look like two different applications.
    /// </summary>
    private sealed class SplashForm : Form
    {
        private const int LogoBoxHeight = 78;
        private const int LogoSideMargin = 80;

        private readonly Image? _logo = Branding.CreateLogo();
        private readonly Font _titleFont = new(Theme.FamilyName, 19f, FontStyle.Bold);
        private readonly Font _captionFont = Theme.Caption();
        private readonly System.Windows.Forms.Timer _safetyNet;
        private readonly string _version = ReadVersion();

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(420, 230);
            BackColor = Theme.Card;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);

            // If the main window never signals — a crash mid-startup, a callback that never
            // fires — an always-on-top splash would sit over the desktop for the whole session.
            // Closing regardless is what keeps the failure mode "no splash" instead of
            // "an unclosable window".
            _safetyNet = new System.Windows.Forms.Timer { Interval = 15000 };
            _safetyNet.Tick += (_, _) => Close();
            _safetyNet.Start();
        }

        /// <summary>
        /// The main window is about to take the focus; a splash that grabs it first sends the
        /// real window behind whatever else the user has open.
        /// </summary>
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;   // also keeps it out of Alt+Tab
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            // A hairline stops the card bleeding into a dark desktop, the same edge the panels use
            using (var border = new Pen(Theme.Hairline))
                g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            int y = 34;
            if (_logo is not null)
            {
                var fitted = Branding.ScaleToFit(
                    _logo.Size, new Size(ClientSize.Width - LogoSideMargin * 2, LogoBoxHeight));
                if (!fitted.IsEmpty)
                {
                    g.DrawImage(_logo, (ClientSize.Width - fitted.Width) / 2, y,
                                fitted.Width, fitted.Height);
                    y += fitted.Height;
                }
            }

            // Name and version read as one block; the status line is pushed to the bottom edge so
            // the two muted lines are not mistaken for a single wrapped caption.
            y = DrawCentred(g, "BMS UI", _titleFont, Theme.Ink, y + 26);
            if (_version.Length > 0)
                DrawCentred(g, _version, _captionFont, Theme.InkMuted, y + 8);

            DrawCentred(g, "Starting…", _captionFont, Theme.InkMuted, ClientSize.Height - 30);
        }

        /// <summary>Draws one centred line and returns the y just below it.</summary>
        private int DrawCentred(Graphics g, string text, Font font, Color ink, int y)
        {
            var size = TextRenderer.MeasureText(g, text, font);
            TextRenderer.DrawText(g, text, font,
                                  new Point((ClientSize.Width - size.Width) / 2, y), ink);
            return y + size.Height;
        }

        private static string ReadVersion()
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v is null ? "" : $"v{v.Major}.{v.Minor}.{v.Build}";
            }
            catch
            {
                return "";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _safetyNet.Dispose();
                _logo?.Dispose();      // our own copy — Branding.CreateLogo hands out one per call
                _titleFont.Dispose();
                _captionFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
