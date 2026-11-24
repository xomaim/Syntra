using System.Security.Principal;

namespace Syntra;

static class Program
{
    public static AppSettings Settings = new();
    public static bool Exiting;
    public static bool IsAdmin;
    static MainForm? form;

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\Syntra.SingleInstance", out bool first);
        bool repair = args.Contains("--repair");
        if (!first && !repair) return; // already running — it's in the tray

        try { using var id = WindowsIdentity.GetCurrent(); IsAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch { }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Settings = AppSettings.Load();
        ColorEngine.Init();

        // Always give the screen back, even after a crash.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { ColorEngine.Reset(); } catch { } };
        Application.ThreadException += (_, e) => MessageBox.Show(e.Exception.Message, "Syntra");

        form = new MainForm();
        if (args.Contains("--tray")) { form.WindowState = FormWindowState.Minimized; form.ShowInTaskbar = false; form.Load += (_, _) => { form.Hide(); form.ShowInTaskbar = true; form.WindowState = FormWindowState.Normal; }; }
        Application.Run(form);
        ColorEngine.Shutdown();
    }

    public static void ExitApp()
    {
        Exiting = true;
        form?.Cleanup();
        Settings.Save();
        Application.Exit();
    }
}
