using System.Diagnostics;
using System.Reflection;

namespace Syntra;

// Checks GitHub every minute. If a newer Syntra.exe is out, it downloads it next to the
// current one, swaps them (Windows lets a running .exe be renamed) and restarts.
static class Updater
{
    public const string Repo = "xomaim/Syntra";
    static readonly HttpClient probe = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    static readonly HttpClient dl = new() { Timeout = TimeSpan.FromMinutes(10) };
    static bool busy;
    public static string LastStatus = "Not checked yet";

    static Updater()
    {
        probe.DefaultRequestHeaders.UserAgent.ParseAdd("Syntra-Updater");
        dl.DefaultRequestHeaders.UserAgent.ParseAdd("Syntra-Updater");
    }

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static void CleanupOld()
    {
        try { var old = Environment.ProcessPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
    }

    // Returns the new tag when an update has been installed and a restart is needed.
    public static async Task<string?> CheckAsync()
    {
        if (busy) return null;
        busy = true;
        string? tmp = null;
        try
        {
            using var resp = await probe.GetAsync($"https://github.com/{Repo}/releases/latest");
            var loc = resp.Headers.Location?.ToString();
            if (loc == null || !loc.Contains("/tag/")) { LastStatus = "No release found"; return null; }
            var tag = Uri.UnescapeDataString(loc[(loc.LastIndexOf('/') + 1)..]);
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) { LastStatus = "Up to date"; return null; }
            var latest = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            if (latest <= Current) { LastStatus = $"Up to date (checked {DateTime.Now:HH:mm})"; return null; }

            var exe = Environment.ProcessPath!;
            tmp = exe + ".download";
            LastStatus = $"Downloading {tag}…";
            using (var s = await dl.GetStreamAsync($"https://github.com/{Repo}/releases/download/{tag}/Syntra.exe"))
            using (var f = File.Create(tmp))
                await s.CopyToAsync(f);
            if (new FileInfo(tmp).Length < 1_000_000) throw new IOException("Download too small");

            var old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(tmp, exe); }
            catch { File.Move(old, exe); throw; }
            tmp = null;
            LastStatus = $"Updated to {tag}";
            return tag;
        }
        catch (Exception ex)
        {
            LastStatus = "Update check failed: " + ex.Message;
            return null;
        }
        finally
        {
            if (tmp != null) try { File.Delete(tmp); } catch { }
            busy = false;
        }
    }

    public static void Restart(bool tray)
    {
        try { Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Arguments = tray ? "--updated --tray" : "--updated" }); }
        catch { return; }
        Program.ExitApp();
    }
}
