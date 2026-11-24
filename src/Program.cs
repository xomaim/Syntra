using System.Security.Principal;

namespace Syntra;

static class Program
{
    public static AppSettings Settings = new();
    public static bool Exiting;
    public static bool IsAdmin;
    static MainForm? form;
    public static readonly int ShowMsg = RegisterWindowMessage("Syntra.ShowWindow");

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern int RegisterWindowMessage(string name);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

    static void Crash(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.AppendAllText(Path.Combine(AppSettings.Dir, "crash.log"), $"[{DateTime.Now}] v{Updater.Current}\n{ex}\n\n");
        }
        catch { }
        try { ColorEngine.Reset(); } catch { }
        MessageBox.Show("Syntra hit a problem and has to close:\n\n" + ex.Message +
            "\n\nDetails were saved to %AppData%\\Syntra\\crash.log — send that file over so it can be fixed.", "Syntra");
    }

    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\Syntra.SingleInstance", out bool first);
        bool repair = args.Contains("--repair"), updated = args.Contains("--updated");
        if (!first)
        {
            if (!repair && !updated)
            {
                // Already running (it lives in the tray) — bring that window up instead of doing nothing.
                PostMessage(new IntPtr(0xFFFF), ShowMsg, IntPtr.Zero, IntPtr.Zero);
                return;
            }
            try { mutex.WaitOne(15000); } catch (AbandonedMutexException) { }
        }
        Updater.CleanupOld();

        try { using var id = WindowsIdentity.GetCurrent(); IsAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch { }

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Crash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => { if (e.ExceptionObject is Exception ex) Crash(ex); };
        try { Run(args); }
        catch (Exception ex) { Crash(ex); }
    }

    static void Run(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Settings = AppSettings.Load();
        if (Settings.AppTheme != null) Theme.Use(Settings.AppTheme);

        // First run: accept the terms, then sign up / log in.
        if (!Settings.TermsAccepted)
        {
            using var terms = new TermsForm();
            if (terms.ShowDialog() != DialogResult.OK) return;
            Settings.TermsAccepted = true;
            Settings.Save();
        }
        if (!Account.Load().LoggedIn)
        {
            using var acc = new AccountForm();
            if (acc.ShowDialog() != DialogResult.OK) return;
        }

        ColorEngine.Init();

        // Always give the screen back, even after a crash.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { ColorEngine.Reset(); } catch { } };

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
