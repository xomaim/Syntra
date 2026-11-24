using System.Text.Json;

namespace Syntra;

public class Look
{
    public string Name { get; set; } = "Custom";
    public string Note { get; set; } = "";
    public double Saturation { get; set; } = 100;   // 0..300, software
    public double Vibrance { get; set; } = 50;      // 0..100, 50 = normal. Above 50 uses NVIDIA driver if present
    public double Brightness { get; set; } = 100;   // 50..150
    public double Contrast { get; set; } = 100;     // 50..150
    public double Gamma { get; set; } = 1.0;        // 0.5..2.0
    public double Temperature { get; set; } = 0;    // -100..100 (warm +)
    public double Highlights { get; set; } = 0;     // -100..100
    public double Shadows { get; set; } = 0;        // -100..100
    public double Blacks { get; set; } = 0;         // -100..100
    public double Whites { get; set; } = 0;         // -100..100

    public Look Clone() => (Look)MemberwiseClone();
    public bool IsNeutral() =>
        Saturation == 100 && Vibrance == 50 && Brightness == 100 && Contrast == 100 && Gamma == 1.0 &&
        Temperature == 0 && Highlights == 0 && Shadows == 0 && Blacks == 0 && Whites == 0;

    public static List<Look> BuiltIn() => new()
    {
        new() { Name = "Balanced", Note = "Game as it ships." },
        new() { Name = "Clean colours", Note = "Natural colours, a little brighter.", Saturation = 160, Vibrance = 70, Temperature = -13, Brightness = 104 },
        new() { Name = "Vivid", Note = "Punchy colours for most games.", Saturation = 221, Vibrance = 80 },
        new() { Name = "Forest", Note = "Greens pop, players stand out.", Saturation = 230, Vibrance = 75, Contrast = 106 },
        new() { Name = "Desert", Note = "Cuts sand glare.", Saturation = 225, Temperature = -20, Highlights = -20 },
        new() { Name = "Snow", Note = "Tames white-out maps.", Saturation = 215, Highlights = -35, Whites = -15, Temperature = -10 },
        new() { Name = "Night", Note = "Lifts shadows a little.", Saturation = 180, Vibrance = 80, Shadows = 30, Gamma = 1.15 },
        new() { Name = "Competitive", Note = "Clear, cool, high contrast.", Saturation = 200, Vibrance = 85, Contrast = 112, Temperature = -15 },
        new() { Name = "Warm", Note = "Easy on the eyes.", Saturation = 120, Temperature = 40 },
        new() { Name = "Black Holo", Note = "Strong colours for holo sights.", Saturation = 300, Vibrance = 100, Temperature = -60 },
        new() { Name = "Rust PvP", Note = "Made for PvP in Rust.", Saturation = 242, Vibrance = 75, Temperature = -32 },
        new() { Name = "Greyscale", Note = "No colour at all.", Saturation = 0, Vibrance = 50 },
    };
}

public class CrosshairSettings
{
    public bool On { get; set; }
    public string Style { get; set; } = "Cross";
    public int Size { get; set; } = 10;
    public int Thickness { get; set; } = 2;
    public int Gap { get; set; } = 3;
    public int Argb { get; set; } = unchecked((int)0xFF00FF66);
    public bool Outline { get; set; } = true;
    public bool CenterDot { get; set; }
}

public class AppSettings
{
    public Look Current { get; set; } = new();
    public List<Look> Saved { get; set; } = new();
    public Dictionary<string, Look> GameProfiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Enabled { get; set; } = true;
    public double LowBlue { get; set; } = 0;
    public CrosshairSettings Crosshair { get; set; } = new();
    public bool HueOn { get; set; }
    public double HueDegrees { get; set; } = 0;
    public double HueSat { get; set; } = 100;
    public string HueTarget { get; set; } = "";       // "" = whole screen, else a program name
    public ThemePreset? AppTheme { get; set; }
    public string BackgroundPath { get; set; } = "";
    public int BackgroundDim { get; set; } = 45;
    public int WindowOpacity { get; set; } = 100;
    public bool AutoUpdate { get; set; } = true;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Syntra");
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opts);
                if (s != null)
                {
                    s.GameProfiles = new(s.GameProfiles, StringComparer.OrdinalIgnoreCase);
                    return s;
                }
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch { }
    }

    public static string ToJson(object o) => JsonSerializer.Serialize(o, Opts);
    public static T? FromJson<T>(string s) => JsonSerializer.Deserialize<T>(s, Opts);
}
