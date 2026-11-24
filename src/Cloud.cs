using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;

namespace Syntra;

// Syntra Cloud (Supabase): links this PC to your account so the website can control it
// from anywhere, and lets admins see who uses Syntra. Off until the two values below are filled in.
static class CloudConfig
{
    public const string Url = "https://bdjzdrfgwrsrtwkukhco.supabase.co";
    public const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImJkanpkcmZnd3JzcnR3a3VraGNvIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTU1NTczMDcsImV4cCI6MjExMTEzMzMwN30.AvVCOc5GMRW2NNvCzLZOehZ5TFWfeFupsNOFlnhGb_c";
    public static bool On => Url.Length > 0 && AnonKey.Length > 0;
}

static class Cloud
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    static string? access;
    static DateTime accessUntil;

    public static string EmailOf(string username) => username.Trim().ToLowerInvariant() + "@syntra.user";

    static HttpRequestMessage Req(HttpMethod m, string path, object? body = null, string? bearer = null)
    {
        var r = new HttpRequestMessage(m, CloudConfig.Url.TrimEnd('/') + path);
        r.Headers.Add("apikey", CloudConfig.AnonKey);
        r.Headers.Add("Authorization", "Bearer " + (bearer ?? CloudConfig.AnonKey));
        if (body is JsonNode jn) r.Content = new StringContent(jn.ToJsonString(), Encoding.UTF8, "application/json");
        else if (body != null) r.Content = JsonContent.Create(body);
        return r;
    }

    static async Task<string> Err(HttpResponseMessage resp)
    {
        try
        {
            var j = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            var m = (string?)j?["msg"] ?? (string?)j?["error_description"] ?? (string?)j?["message"] ?? $"Error {(int)resp.StatusCode}";
            if (m.Contains("already registered", StringComparison.OrdinalIgnoreCase)) return "That username is taken.";
            if (m.Contains("Invalid login", StringComparison.OrdinalIgnoreCase)) return "Wrong username or password.";
            return m;
        }
        catch { return $"Error {(int)resp.StatusCode}"; }
    }

    // Returns (userId, refreshToken, error)
    static async Task<(string?, string?, string?)> Session(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode) return (null, null, await Err(resp));
        var j = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        var at = (string?)j?["access_token"];
        var rt = (string?)j?["refresh_token"];
        var id = (string?)j?["user"]?["id"];
        if (at == null || rt == null || id == null) return (null, null, "Sign-up needs \"Confirm email\" turned off in Supabase.");
        access = at;
        accessUntil = DateTime.UtcNow.AddSeconds(((int?)j?["expires_in"] ?? 3600) - 120);
        return (id, rt, null);
    }

    public static async Task<(string? id, string? refresh, string? error)> SignUp(string username, string name, string password)
    {
        try
        {
            using var r = await http.SendAsync(Req(HttpMethod.Post, "/auth/v1/signup",
                new { email = EmailOf(username), password, data = new { username = username.Trim(), name } }));
            return await Session(r);
        }
        catch (Exception ex) { return (null, null, "Couldn't reach Syntra Cloud: " + ex.Message); }
    }

    public static async Task<(string? id, string? refresh, string? error)> SignIn(string username, string password)
    {
        try
        {
            using var r = await http.SendAsync(Req(HttpMethod.Post, "/auth/v1/token?grant_type=password",
                new { email = EmailOf(username), password }));
            return await Session(r);
        }
        catch (Exception ex) { return (null, null, "Couldn't reach Syntra Cloud: " + ex.Message); }
    }

    static async Task<bool> EnsureAccess(Account acc)
    {
        if (access != null && DateTime.UtcNow < accessUntil) return true;
        if (acc.CloudRefresh.Length == 0) return false;
        using var r = await http.SendAsync(Req(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", new { refresh_token = acc.CloudRefresh }));
        var (_, rt, err) = await Session(r);
        if (err != null || rt == null) return false;
        acc.CloudRefresh = rt;
        acc.Save();
        return true;
    }

    // Makes sure this PC has a row in "devices". Returns the row (settings, updated_at) or null.
    public static async Task<JsonObject?> GetDevice(Account acc)
    {
        if (!CloudConfig.On || acc.CloudUserId.Length == 0 || !await EnsureAccess(acc)) return null;
        if (acc.DeviceId.Length > 0)
        {
            using var g = await http.SendAsync(Req(HttpMethod.Get, $"/rest/v1/devices?id=eq.{acc.DeviceId}&select=id,settings,updated_at", bearer: access));
            if (g.IsSuccessStatusCode && JsonNode.Parse(await g.Content.ReadAsStringAsync()) is JsonArray a && a.Count > 0) return a[0] as JsonObject;
        }
        var body = new JsonObject
        {
            ["owner"] = acc.CloudUserId, ["name"] = Environment.MachineName, ["version"] = Updater.Current.ToString(),
            ["settings"] = new JsonObject(),
        };
        var req = Req(HttpMethod.Post, "/rest/v1/devices", body, access);
        req.Headers.Add("Prefer", "return=representation");
        using var p = await http.SendAsync(req);
        if (!p.IsSuccessStatusCode) return null;
        if (JsonNode.Parse(await p.Content.ReadAsStringAsync()) is JsonArray arr && arr.Count > 0 && arr[0] is JsonObject row)
        {
            acc.DeviceId = (string?)row["id"] ?? "";
            acc.Save();
            return row;
        }
        return null;
    }

    public static async Task Push(Account acc, JsonObject? settings)
    {
        if (acc.DeviceId.Length == 0 || !await EnsureAccess(acc)) return;
        var body = new JsonObject { ["last_seen"] = DateTime.UtcNow.ToString("o"), ["version"] = Updater.Current.ToString(), ["name"] = Environment.MachineName };
        if (settings != null) { body["settings"] = settings; body["updated_at"] = DateTime.UtcNow.ToString("o"); }
        using var r = await http.SendAsync(Req(HttpMethod.Patch, $"/rest/v1/devices?id=eq.{acc.DeviceId}", body, access));
    }
}
