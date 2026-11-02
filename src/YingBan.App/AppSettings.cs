using System.Text.Json;

namespace YingBan.App;

public sealed class Bookmark
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public double Position { get; set; }
    public DateTime Updated { get; set; } = DateTime.UtcNow;
}

public sealed class AppSettings
{
    public int Left { get; set; } = -1;
    public int Top { get; set; } = -1;
    public int Width { get; set; } = 1100;
    public int Height { get; set; } = 760;
    public double Opacity { get; set; } = 1;
    public bool AlwaysOnTop { get; set; } = true;
    public int StepSeconds { get; set; } = 5;
    public bool DanmakuEnabled { get; set; } = false;
    public int FontSize { get; set; } = 26;
    public double CommentSeconds { get; set; } = 8;
    public double CommentOpacity { get; set; } = .9;
    public int RegionTop { get; set; } = 4;
    public int RegionHeight { get; set; } = 30;
    public int ScreenIndex { get; set; } = -1;
    public bool PauseOnHide { get; set; } = true;
    public bool BilibiliAccelerationEnabled { get; set; } = true;
    public List<Bookmark> Bookmarks { get; set; } = [];
    public Dictionary<string, string> Hotkeys { get; set; } = DefaultHotkeys();

    public static Dictionary<string, string> DefaultHotkeys() => new()
    {
        ["toggle"] = "Ctrl+Alt+Space", ["back"] = "Ctrl+Alt+Left", ["forward"] = "Ctrl+Alt+Right",
        ["slower"] = "Ctrl+Alt+Down", ["faster"] = "Ctrl+Alt+Up", ["compact"] = "Ctrl+Alt+I",
        ["hide"] = "Ctrl+Alt+H", ["clickThrough"] = "Ctrl+Alt+T", ["danmaku"] = "Ctrl+Alt+D",
        ["previous"] = "Ctrl+Alt+PageUp", ["next"] = "Ctrl+Alt+PageDown"
    };

    public AppSettings Clone() => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this))!;

    public void Normalize()
    {
        Width = Math.Clamp(Width, 500, 3840); Height = Math.Clamp(Height, 320, 2160);
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, .25, 1) : 1;
        StepSeconds = Math.Clamp(StepSeconds, 1, 120); FontSize = Math.Clamp(FontSize, 16, 64);
        CommentSeconds = double.IsFinite(CommentSeconds) ? Math.Clamp(CommentSeconds, 3, 20) : 8;
        CommentOpacity = double.IsFinite(CommentOpacity) ? Math.Clamp(CommentOpacity, .2, 1) : .9;
        RegionTop = Math.Clamp(RegionTop, 0, 80); RegionHeight = Math.Clamp(RegionHeight, 10, 100 - RegionTop);
        ScreenIndex = Math.Clamp(ScreenIndex, -1, 32);
        Hotkeys ??= [];
        Hotkeys = Hotkeys.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var pair in DefaultHotkeys())
            if (!Hotkeys.TryGetValue(pair.Key, out var value) || string.IsNullOrWhiteSpace(value)) Hotkeys[pair.Key] = pair.Value;
        Bookmarks = (Bookmarks ?? []).Where(b => b != null && b.Url is { Length: > 0 and < 8192 })
            .Where(b => double.IsFinite(b.Position) && b.Position >= 0).OrderByDescending(b => b.Updated).Take(30).ToList();
        foreach (var bookmark in Bookmarks) bookmark.Title ??= "";
    }
}

public sealed class SettingsStore(string directory)
{
    public string DirectoryPath { get; } = directory;
    public string FilePath => Path.Combine(DirectoryPath, "settings.json");
    public string? LoadWarning { get; private set; }
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            if (new FileInfo(FilePath).Length > 1024 * 1024) throw new InvalidDataException("设置文件过大");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new();
            settings.Normalize(); return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        { LoadWarning = "设置文件无法读取，已恢复默认值。原文件保留。"; return new(); }
    }
    public void Save(AppSettings settings)
    {
        settings.Normalize(); Directory.CreateDirectory(DirectoryPath);
        if (LoadWarning != null && File.Exists(FilePath))
        { File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false); LoadWarning = null; }
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
    }
}
