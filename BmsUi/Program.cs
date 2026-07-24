namespace BmsUi;

static class Program
{
    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main()
    {
        // To customize application configuration such as set high DPI settings or default font,
        // see https://aka.ms/applicationconfiguration.
        ApplicationConfiguration.Initialize();

        // The splash lives on its own thread, so building Form1 below (~426 ms, measured) happens
        // WHILE it is on screen rather than after — it costs no startup time. It is deliberately
        // treated as optional: Show() returns null on any failure and Close() swallows its own,
        // so the window always opens either way.
        var splash = Ui.SplashScreen.Show();

        var form = new Form1();
        form.Shown += (_, _) => splash?.Close();
        Application.Run(form);
    }
}
