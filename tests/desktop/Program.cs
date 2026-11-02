using YingBan.App;
using YingBan.Core;
using System.Reflection;
using System.Text.Json;

internal static class Program
{
[STAThread]
private static int Main()
{

int failures = 0;
void Check(string name, Action action) { try { action(); Console.WriteLine($"PASS {name}"); } catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e.Message}"); } }
void Assert(bool ok) { if (!ok) throw new Exception("Expectation not met"); }
Check("Bilibili acceleration defaults on and disabled preference survives save", () => {
    Assert(JsonSerializer.Deserialize<AppSettings>("{}")!.BilibiliAccelerationEnabled);
    var directory = Path.Combine(AppContext.BaseDirectory, "test-profile-acceleration-" + Guid.NewGuid().ToString("N"));
    var store = new SettingsStore(directory);
    var settings = new AppSettings { BilibiliAccelerationEnabled = false };
    store.Save(settings);
    Assert(!store.Load().BilibiliAccelerationEnabled && !settings.Clone().BilibiliAccelerationEnabled);
});
Check("hotkey parses modifiers and arrow", () => { var k = HotkeyManager.Parse("Ctrl+Alt+Left"); Assert(k.Modifiers == 3 && k.VirtualKey == 0x25); });
Check("hotkey rejects duplicate key tokens", () => { try { HotkeyManager.Parse("Ctrl+A+B"); } catch (FormatException) { return; } throw new Exception("accepted invalid shortcut"); });
Check("hotkey rejects modifier only", () => { try { HotkeyManager.Parse("Ctrl+Alt"); } catch (FormatException) { return; } throw new Exception("accepted missing key"); });
Check("side button aliases normalize to the same binding", () => {
    Assert(HotkeyManager.Parse("Mouse4") == new KeyCombination(0, 5));
    Assert(HotkeyManager.Parse("Ctrl+Alt+Shift+Mouse5") == new KeyCombination(7, 6));
    Assert(HotkeyManager.Parse("鼠标侧键1") == HotkeyManager.Parse("XButton1"));
});
Check("side button matching preserves modifiers and paired release", () => {
    using var target = new Form();
    using var hook = new MouseHotkeyHook(target.Handle);
    Assert(hook.TryAdd(new(3, 5), 101, out _));
    Assert(hook.TryAdd(new(4, 6), 102, out _));
    foreach (uint modifiers in new uint[] { 0, 1, 2, 7, 11 })
        Assert(!hook.ProcessButton(0x20B, 1, modifiers, out _, out _));
    Assert(hook.ProcessButton(0x20B, 1, 3, out int id, out int generation) && id == 101 && hook.IsCurrent(generation));
    Assert(hook.ProcessButton(0x20B, 1, 0, out id, out _) && id == 0);
    hook.Clear();
    Assert(!hook.IsCurrent(generation));
    Assert(hook.ProcessButton(0x20C, 1, 0, out id, out _) && id == 0);
    Assert(!hook.ProcessButton(0x20C, 1, 0, out _, out _));
    Assert(!hook.ProcessButton(0x20B, 1, 3, out _, out _));
    Assert(hook.TryAdd(new(4, 6), 102, out _));
    Assert(hook.ProcessButton(0x20B, 2, 4, out id, out _) && id == 102);
    Assert(hook.ProcessButton(0x20C, 2, 0, out _, out _));
    Assert(!hook.ProcessButton(0x201, 1, 3, out _, out _) && !hook.ProcessButton(0x20B, 3, 0, out _, out _));
});
Check("mixed keyboard conflict restores side bindings and rejects stale dispatch", () => {
    using var target = new Form();
    using var manager = new HotkeyManager(target.Handle);
    Assert(manager.TryApply(new() { ["toggle"] = "Mouse4", ["back"] = "Ctrl+Shift+F11" }, out _));
    var hook = (MouseHotkeyHook)typeof(HotkeyManager).GetField("mouseHook", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
    Assert(hook.ProcessButton(0x20B, 1, 0, out int id, out int generation));
    Assert(manager.MouseActionFor(id, generation) == "toggle");
    Assert(hook.ProcessButton(0x20C, 1, 0, out _, out _));
    using var occupied = new OccupiedKey("F10");
    Assert(!manager.TryApply(new() { ["toggle"] = "Mouse5", ["back"] = "F10" }, out _));
    Assert(manager.MouseActionFor(id, generation) == null);
    Assert(hook.ProcessButton(0x20B, 1, 0, out id, out generation) && manager.MouseActionFor(id, generation) == "toggle");
    Assert(hook.ProcessButton(0x20C, 1, 0, out _, out _));
    Assert(!hook.ProcessButton(0x20B, 2, 0, out _, out _));
    Assert(!CanRegisterElsewhere("Ctrl+Shift+F11"));
});
Check("settings clamps values and repairs missing hotkeys", () => { var s = new AppSettings { Opacity = -5, FontSize = 999, RegionHeight = 0, Hotkeys = [] }; s.Normalize(); Assert(s.Opacity >= .25 && s.FontSize <= 64 && s.RegionHeight >= 10 && s.Hotkeys.ContainsKey("toggle")); });
var folder = Path.Combine(Path.GetTempPath(), "YingBanTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
Check("settings round trip preserves playback position", () => { var store = new SettingsStore(folder); var s = new AppSettings(); s.Bookmarks.Add(new Bookmark { Url = "https://www.bilibili.com/video/BV19zYj6xEfv/", Title = "中文标题", Position = 121.5 }); store.Save(s); var read = store.Load(); Assert(read.Bookmarks[0].Title == "中文标题" && read.Bookmarks[0].Position == 121.5); });
Check("corrupt settings recover without deleting source", () => { var store = new SettingsStore(folder); File.WriteAllText(store.FilePath, "broken json"); var s = store.Load(); Assert(s.StepSeconds > 0 && File.ReadAllText(store.FilePath) == "broken json"); });
Check("settings tolerate JSON null bookmark strings and dictionary values", () => {
    var settings = JsonSerializer.Deserialize<AppSettings>("""
        {"Bookmarks":[null,{"Url":null,"Title":"bad"},{"Url":"https://example.com","Title":null,"Position":5}],"Hotkeys":{"toggle":null,"future":null}}
        """)!;
    settings.Normalize();
    Assert(settings.Bookmarks.Count == 1 && settings.Bookmarks[0].Title == "");
    Assert(settings.Hotkeys["toggle"] == "Ctrl+Alt+Space" && settings.Hotkeys.Values.All(v => v != null));
});
Check("unknown hotkey settings cannot poison conflict rollback", () => {
    using var occupied = new OccupiedKey("F10");
    using var manager = new HotkeyManager(IntPtr.Zero);
    Assert(manager.TryApply(new() { ["toggle"] = "Ctrl+Shift+F11", ["future"] = "not a key" }, out _));
    Assert(!manager.TryApply(new() { ["toggle"] = "F10" }, out var error));
    Assert(error.Contains("F10") && Enumerable.Range(HotkeyManager.RecoveryId + 1, 20).Any(id => manager.ActionFor(id) == "toggle"));
    Assert(!CanRegisterElsewhere("Ctrl+Shift+F11"));
});
Check("hotkey rollback reports keys unavailable during restoration", () => {
    using var occupied = new OccupiedKey("F10");
    using var manager = new HotkeyManager(IntPtr.Zero);
    Assert(manager.TryApply(new() { ["toggle"] = "Ctrl+Shift+F11" }, out _));
    var registeredId = Enumerable.Range(HotkeyManager.RecoveryId + 1, 20).Single(id => manager.ActionFor(id) == "toggle");
    // Place the OS in the same state as another process taking the old key after
    // it is released. This avoids relying on thread scheduling to reproduce a race.
    Assert(NativeMethods.UnregisterHotKey(IntPtr.Zero, registeredId));
    using var lost = new OccupiedKey("Ctrl+Shift+F11");
    Assert(!manager.TryApply(new() { ["toggle"] = "F10" }, out var error));
    Assert(error.Contains("恢复失败") && error.Contains("toggle"));
    Assert(!Enumerable.Range(HotkeyManager.RecoveryId + 1, 20).Any(id => manager.ActionFor(id) == "toggle"));
});
Check("paused overlay expiration repaints after last comment is removed", () => {
    using var overlay = NewOverlay();
    overlay.Add([new("old", 0, 1, 0xFFFFFF, 26, "过期弹幕")]);
    Assert(overlay.ActiveCount == 1);
    overlay.SetMedia(State(20), false);
    int invalidations = 0; overlay.Invalidated += (_, _) => invalidations++;
    Tick(overlay);
    Assert(overlay.ActiveCount == 0 && invalidations > 0);
});
Check("overlay preview expiration repaints its cleared content", () => {
    using var overlay = NewOverlay(); overlay.Preview(); Assert(overlay.ActiveCount > 0);
    typeof(DanmakuOverlay).GetField("previewUntil", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(overlay, 0d);
    int invalidations = 0; overlay.Invalidated += (_, _) => invalidations++;
    Tick(overlay); Assert(overlay.ActiveCount == 0 && invalidations > 0);
});
Check("late overlay entrant starts behind existing moving comment", () => {
    using var overlay = NewOverlay();
    overlay.Add([new("first", 0, 1, 0xFFFFFF, 26, "旧弹幕")]);
    var (oldWidth, _, duration) = LastGeometry(overlay);
    var oldSpeed = (overlay.Width + oldWidth) / duration;
    // This gives a 10 px visible gap beyond the allocator's required 30 px margin.
    double now = (oldWidth + 40) / oldSpeed;
    overlay.SetMedia(State(now), false);
    overlay.Add([new("late", now - .5, 1, 0xFFFFFF, 26, "新弹幕")]);
    Assert(overlay.ActiveCount == 2);
    var (newWidth, newStart, newDuration) = LastGeometry(overlay);
    double oldRight = overlay.Width - oldSpeed * now + oldWidth;
    double newLeft = overlay.Width - (overlay.Width + newWidth) / newDuration * (now - newStart);
    Assert(newLeft >= oldRight + 30);
});
Console.WriteLine($"Desktop logic: {failures} failure(s)");
return failures == 0 ? 0 : 1;
}

private static MediaState State(double time) => new("test", "test", time, 100, true, 1, true);
private static DanmakuOverlay NewOverlay()
{
    var overlay = new DanmakuOverlay(new AppSettings()) { Bounds = new Rectangle(-3000, -3000, 800, 42) };
    overlay.Show(); overlay.SetMedia(State(0), false); return overlay;
}
private static void Tick(DanmakuOverlay overlay)
{
    var timer = (System.Windows.Forms.Timer)typeof(DanmakuOverlay).GetField("timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
    typeof(System.Windows.Forms.Timer).GetMethod("OnTick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(timer, [EventArgs.Empty]);
}
private static (float Width, double Start, double Duration) LastGeometry(DanmakuOverlay overlay)
{
    var entries = (System.Collections.IList)typeof(DanmakuOverlay).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(overlay)!;
    var entry = entries[entries.Count - 1]!;
    return ((float)entry.GetType().GetProperty("Width")!.GetValue(entry)!,
        (double)entry.GetType().GetProperty("Start")!.GetValue(entry)!,
        (double)entry.GetType().GetProperty("Duration")!.GetValue(entry)!);
}
private static bool CanRegisterElsewhere(string text)
{
    bool success = false;
    var thread = new Thread(() => {
        var key = HotkeyManager.Parse(text);
        success = NativeMethods.RegisterHotKey(IntPtr.Zero, 777, key.Modifiers | 0x4000, key.VirtualKey);
        if (success) NativeMethods.UnregisterHotKey(IntPtr.Zero, 777);
    });
    thread.Start(); thread.Join(); return success;
}
private sealed class OccupiedKey : IDisposable
{
    private readonly ManualResetEventSlim ready = new();
    private readonly ManualResetEventSlim release = new();
    private readonly Thread thread;
    public OccupiedKey(string text)
    {
        bool registered = false;
        thread = new Thread(() => {
            var key = HotkeyManager.Parse(text);
            registered = NativeMethods.RegisterHotKey(IntPtr.Zero, 778, key.Modifiers | 0x4000, key.VirtualKey);
            ready.Set(); release.Wait();
            if (registered) NativeMethods.UnregisterHotKey(IntPtr.Zero, 778);
        });
        thread.Start(); ready.Wait();
        if (!registered) { Dispose(); throw new Exception($"Test fixture could not occupy {text}"); }
    }
    public void Dispose() { release.Set(); thread.Join(); ready.Dispose(); release.Dispose(); }
}
}
