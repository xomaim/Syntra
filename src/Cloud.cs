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

// Accounts and PCs live in Syntra's own tables; the app only calls these database functions.
static class Cloud
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static async Task<JsonNode?> Rpc(string fn, JsonObject args)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, CloudConfig.Url.TrimEnd('/') + "/rest/v1/rpc/" + fn)
        { Content = new StringContent(args.ToJsonString(), Encoding.UTF8, "application/json") };
        r.Headers.Add("apikey", CloudConfig.AnonKey);
        r.Headers.Add("Authorization", "Bearer " + CloudConfig.AnonKey);
        using var resp = await http.SendAsync(r);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string msg;
            try { msg = (string?)JsonNode.Parse(text)?["message"] ?? $"Error {(int)resp.StatusCode}"; } catch { msg = $"Error {(int)resp.StatusCode}"; }
            throw new CloudError(msg);
        }
        return JsonNode.Parse(text);
    }

    sealed class CloudError : Exception { public CloudError(string m) : base(m) { } }

    static async Task<(string? id, string? token, string? error)> Session(string fn, JsonObject args)
    {
        try
        {
            var j = await Rpc(fn, args);
            return ((string?)j?["user_id"], (string?)j?["token"], null);
        }
        catch (CloudError e) { return (null, null, e.Message); }
        catch (Exception ex) { return (null, null, "Couldn't reach Syntra Cloud: " + ex.Message); }
    }

    public static Task<(string? id, string? token, string? error)> SignUp(string username, string name, string password) =>
        Session("syntra_signup", new JsonObject { ["p_username"] = username.Trim(), ["p_name"] = name, ["p_password"] = password });

    public static Task<(string? id, string? token, string? error)> SignIn(string username, string password) =>
        Session("syntra_login", new JsonObject { ["p_username"] = username.Trim(), ["p_password"] = password });

    // Says "this PC is online", sends its state if given, and returns the latest settings (maybe changed on the website).
    public static async Task<JsonObject?> Sync(Account acc, JsonObject? state)
    {
        if (!CloudConfig.On || acc.CloudRefresh.Length == 0) return null;
        var args = new JsonObject
        {
            ["p_token"] = acc.CloudRefresh,
            ["p_device"] = acc.DeviceId.Length > 0 ? acc.DeviceId : null,
            ["p_name"] = Environment.MachineName,
            ["p_version"] = Updater.Current.ToString(),
            ["p_state"] = state,
        };
        try
        {
            var j = await Rpc("syntra_device_sync", args) as JsonObject;
            var id = (string?)j?["device_id"];
            if (id != null && id != acc.DeviceId) { acc.DeviceId = id; acc.Save(); }
            return j?["settings"] as JsonObject;
        }
        catch (CloudError e) when (e.Message.Contains("Signed out"))
        {
            acc.CloudRefresh = ""; acc.CloudUserId = ""; acc.Save();
            return null;
        }
        catch { return null; }
    }
}
