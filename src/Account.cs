using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Syntra;

// Email sign-up with a 6-digit code, using Supabase Auth (it sends the email).
// Fill these two in with your Supabase project's URL and "anon public" key.
// The anon key is designed to be public, so it is safe inside the .exe.
static class AccountConfig
{
    public const string SupabaseUrl = "";
    public const string AnonKey = "";
    public static bool Enabled => SupabaseUrl.Length > 0 && AnonKey.Length > 0;
}

public class Account
{
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public DateTime Since { get; set; }
    public bool SignedIn => Email.Length > 0 && RefreshToken.Length > 0;

    static string FilePath => Path.Combine(AppSettings.Dir, "account.json");

    public static Account Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Account>(File.ReadAllText(FilePath)) ?? new(); } catch { }
        return new();
    }

    public void Save()
    {
        try { Directory.CreateDirectory(AppSettings.Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); } catch { }
    }

    public static void SignOut() { try { File.Delete(FilePath); } catch { } }
}

static class Auth
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(25) };

    static HttpRequestMessage Req(string path, object body)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, AccountConfig.SupabaseUrl.TrimEnd('/') + path) { Content = JsonContent.Create(body) };
        r.Headers.Add("apikey", AccountConfig.AnonKey);
        r.Headers.Add("Authorization", "Bearer " + AccountConfig.AnonKey);
        return r;
    }

    static async Task<string> Error(HttpResponseMessage resp)
    {
        try
        {
            var j = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            return (string?)j?["msg"] ?? (string?)j?["error_description"] ?? (string?)j?["message"] ?? $"Error {(int)resp.StatusCode}";
        }
        catch { return $"Error {(int)resp.StatusCode}"; }
    }

    // Sends the code. Returns null when sent, otherwise an error message.
    public static async Task<string?> SendCode(string email, string username)
    {
        try
        {
            using var resp = await http.SendAsync(Req("/auth/v1/otp", new { email, create_user = true, data = new { username } }));
            return resp.IsSuccessStatusCode ? null : await Error(resp);
        }
        catch (Exception ex) { return "Couldn't reach the server: " + ex.Message; }
    }

    // Checks the code. Returns (refresh token, null) on success or (null, error).
    public static async Task<(string? token, string? error)> Verify(string email, string code)
    {
        try
        {
            using var resp = await http.SendAsync(Req("/auth/v1/verify", new { type = "email", email, token = code }));
            if (!resp.IsSuccessStatusCode) return (null, await Error(resp));
            var j = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            return ((string?)j?["refresh_token"] ?? "ok", null);
        }
        catch (Exception ex) { return (null, "Couldn't reach the server: " + ex.Message); }
    }
}

sealed class AccountForm : Form
{
    readonly FlowLayoutPanel body = new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
        Padding = new Padding(36, 30, 36, 20),
    };
    readonly Label error = Ui.Lbl("", Theme.Body, Theme.Warn, 340);
    string email = "", username = "";

    public AccountForm()
    {
        Text = "Syntra";
        Size = new Size(440, 560);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        body.BackColor = Theme.Bg;
        Font = Theme.Body;
        Ui.DarkTitle(this);
        Controls.Add(body);
        StepEmail();
    }

    void Reset(string title, string sub)
    {
        body.Controls.Clear();
        var logo = new LogoTile { Margin = new Padding(2, 0, 0, 18) };
        body.Controls.Add(logo);
        body.Controls.Add(Ui.Lbl(title, Theme.H1, width: 350));
        body.Controls.Add(Ui.Lbl(sub, Theme.Body, Theme.Muted, 350));
        body.Controls.Add(new Label { Height = 10, Width = 10 });
        error.Text = "";
        error.ForeColor = Theme.Warn;
    }

    void StepEmail()
    {
        Reset("Create your account", "Pick a username and enter your email. We'll send you a 6-digit code.");
        var user = new DarkInput("Username", 350);
        var mail = new DarkInput("Email", 350);
        user.Box.Text = username; mail.Box.Text = email;
        body.Controls.Add(user);
        body.Controls.Add(mail);
        var send = Ui.Btn("Send code", () => { }, true);
        send.AutoSize = false; send.Size = new Size(350, 42);
        send.Click += async (_, _) =>
        {
            username = user.Box.Text.Trim(); email = mail.Box.Text.Trim();
            if (username.Length < 2) { error.Text = "Pick a username (2+ characters)."; return; }
            if (!email.Contains('@') || !email.Contains('.')) { error.Text = "That email doesn't look right."; return; }
            send.Enabled = false; send.Text = "Sending…"; error.Text = "";
            var err = await Auth.SendCode(email, username);
            send.Enabled = true; send.Text = "Send code";
            if (err != null) { error.Text = err; return; }
            StepCode();
        };
        body.Controls.Add(send);
        body.Controls.Add(error);
        body.Controls.Add(Ui.Lbl("Already have an account? Enter the same email — the code signs you in.", Theme.Body, Theme.Muted, 350));
        AcceptButton = send;
    }

    void StepCode()
    {
        Reset("Check your email", $"We sent a code to {email}. It can take a minute — check spam too.");
        var code = new DarkInput("6-digit code", 350);
        code.Box.MaxLength = 10;
        code.Box.Font = new Font("Segoe UI Semibold", 16f);
        code.Size = new Size(350, 52);
        body.Controls.Add(code);
        var verify = Ui.Btn("Verify", () => { }, true);
        verify.AutoSize = false; verify.Size = new Size(350, 42);
        verify.Click += async (_, _) =>
        {
            var c = new string(code.Box.Text.Where(char.IsDigit).ToArray());
            if (c.Length < 6) { error.Text = "Enter the code from the email."; return; }
            verify.Enabled = false; verify.Text = "Checking…"; error.Text = "";
            var (token, err) = await Auth.Verify(email, c);
            verify.Enabled = true; verify.Text = "Verify";
            if (token == null) { error.Text = err ?? "That code didn't work."; return; }
            new Account { Email = email, Username = username, RefreshToken = token, Since = DateTime.UtcNow }.Save();
            DialogResult = DialogResult.OK;
            Close();
        };
        body.Controls.Add(verify);
        body.Controls.Add(error);
        var row = new FlowLayoutPanel { AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, 6, 0, 0) };
        var resend = Ui.Btn("Resend code", async () =>
        {
            var err = await Auth.SendCode(email, username);
            error.ForeColor = err == null ? Theme.Good : Theme.Warn;
            error.Text = err ?? "Sent again.";
        });
        row.Controls.Add(resend);
        row.Controls.Add(Ui.Btn("Change email", StepEmail));
        body.Controls.Add(row);
        AcceptButton = verify;
        code.Box.Focus();
    }
}
