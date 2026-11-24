using System.Diagnostics;
using Microsoft.Win32;

namespace Syntra;

// Per-user install, like game launchers: copies Syntra.exe to %LocalAppData%\Programs\Syntra,
// adds Desktop + Start menu shortcuts and an "Apps" entry. No admin needed.
// After this, the downloaded file can be deleted — the shortcut runs the installed copy.
static class Installer
{
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Syntra");
    public static string Exe => Path.Combine(Dir, "Syntra.exe");
    static string DesktopLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Syntra.lnk");
    static string StartLnk => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Syntra.lnk");
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Syntra";

    public static bool IsInstalledCopy =>
        string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase);

    static Version? VersionOf(string path)
    {
        try
        {
            var v = FileVersionInfo.GetVersionInfo(path);
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);
        }
        catch { return null; }
    }

    // Called on start when running from somewhere else (e.g. Downloads).
    // Returns true if the installed copy was started and this one should quit.
    public static bool InstallAndLaunch()
    {
        if (IsInstalledCopy) { EnsureShortcuts(); return false; }
        try
        {
            Directory.CreateDirectory(Dir);
            var have = File.Exists(Exe) ? VersionOf(Exe) : null;
            if (have == null || have < Updater.Current)
            {
                if (File.Exists(Exe))
                {
                    var old = Exe + ".old";
                    try { if (File.Exists(old)) File.Delete(old); } catch { }
                    File.Move(Exe, old);
                }
                File.Copy(Environment.ProcessPath!, Exe, true);
            }
            EnsureShortcuts();
            Register();
            if (Startup.IsOn()) Startup.Set(true, Exe);
            Process.Start(new ProcessStartInfo(Exe) { UseShellExecute = true, Arguments = "--installed", WorkingDirectory = Dir });
            return true;
        }
        catch
        {
            return false; // couldn't install (very rare) — just run from where it is
        }
    }

    public static void EnsureShortcuts()
    {
        MakeShortcut(DesktopLnk);
        MakeShortcut(StartLnk);
    }

    static void MakeShortcut(string lnk)
    {
        try
        {
            var t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) return;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic s = shell.CreateShortcut(lnk);
            s.TargetPath = Exe;
            s.WorkingDirectory = Dir;
            s.IconLocation = Exe + ",0";
            s.Description = "Syntra — colour, resolution, crosshair";
            s.Save();
        }
        catch { }
    }

    static void Register()
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
            k.SetValue("DisplayName", "Syntra");
            k.SetValue("DisplayIcon", Exe + ",0");
            k.SetValue("DisplayVersion", Updater.Current.ToString());
            k.SetValue("Publisher", "Syntra");
            k.SetValue("InstallLocation", Dir);
            k.SetValue("UninstallString", $"\"{Exe}\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord);
            k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("EstimatedSize", (int)(new FileInfo(Exe).Length / 1024), RegistryValueKind.DWord);
        }
        catch { }
    }

    public static bool Uninstall(Action? stopRunningCopy = null)
    {
        if (MessageBox.Show("Remove Syntra from this PC?\n\nYour screen goes back to normal, and the shortcuts and app are removed.",
                "Uninstall Syntra", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;
        bool wipe = MessageBox.Show("Also delete your settings, looks and account?", "Uninstall Syntra",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        stopRunningCopy?.Invoke();
        try { ColorEngine.Reset(); } catch { }
        try { File.Delete(DesktopLnk); } catch { }
        try { File.Delete(StartLnk); } catch { }
        try { Startup.Set(false); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        var cmd = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{Dir}\"";
        if (wipe) cmd += $" & rmdir /s /q \"{AppSettings.Dir}\"";
        try { Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false }); } catch { }
        MessageBox.Show("Syntra has been removed.", "Syntra");
        return true;
    }
}
