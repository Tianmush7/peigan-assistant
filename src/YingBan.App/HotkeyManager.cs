namespace YingBan.App;

public readonly record struct KeyCombination(uint Modifiers, uint VirtualKey)
{
    public bool IsMouse => VirtualKey is (uint)Keys.XButton1 or (uint)Keys.XButton2;
}

public sealed class HotkeyManager(IntPtr handle) : IDisposable
{
    public const int RecoveryId = 0x4B00;
    private readonly Dictionary<int, string> actions = [];
    private readonly HashSet<int> keyboardIds = [];
    private MouseHotkeyHook? mouseHook;
    private Dictionary<string, KeyCombination> current = [];
    public bool RecoveryAvailable { get; private set; }
    public string? ActionFor(int id) => id == RecoveryId ? "recover" : actions.GetValueOrDefault(id);
    public string? MouseActionFor(int id, int generation) => mouseHook?.IsCurrent(generation) == true ? actions.GetValueOrDefault(id) : null;
    public static KeyCombination Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("快捷键缺少按键");
        uint modifiers = 0; uint key = 0;
        foreach (var token in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var m = token.ToLowerInvariant() switch { "ctrl" or "control" => 2u, "alt" => 1u, "shift" => 4u, _ => 0u };
            if (m != 0) { if ((modifiers & m) != 0) throw new FormatException("重复的修饰键"); modifiers |= m; continue; }
            var name = token.ToLowerInvariant() switch { "mouse4" or "鼠标侧键1" => "XButton1", "mouse5" or "鼠标侧键2" => "XButton2", _ => token };
            if (key != 0 || !Enum.TryParse<Keys>(name, true, out var parsed) || parsed == Keys.None || (int)parsed > 255 || parsed is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.LButton or Keys.RButton or Keys.MButton)
                throw new FormatException($"无效快捷键：{text}");
            key = (uint)parsed;
        }
        if (key == 0) throw new FormatException($"快捷键缺少按键：{text}");
        return new(modifiers, key);
    }
    public bool InitializeRecovery() => RecoveryAvailable = NativeMethods.RegisterHotKey(handle, RecoveryId, 3 | 0x4000, (uint)Keys.R);
    public bool TryApply(Dictionary<string, string> candidate, out string error)
    {
        error = "";
        var parsed = new List<(string Action, KeyCombination Key)>();
        try
        {
            var seen = new HashSet<KeyCombination> { new(3, (uint)Keys.R) };
            foreach (var pair in candidate)
            {
                if (!AppSettings.DefaultHotkeys().ContainsKey(pair.Key)) continue;
                var key = Parse(pair.Value);
                if (!seen.Add(key)) throw new FormatException($"快捷键重复或占用了恢复键：{pair.Value}");
                parsed.Add((pair.Key, key));
            }
        }
        catch (FormatException e) { error = e.Message; return false; }
        var previous = current;
        UnregisterActions();
        var registered = new Dictionary<string, KeyCombination>();
        int id = RecoveryId + 1;
        foreach (var item in parsed)
        {
            if (!TryRegister(id, item.Key, out var mouseError))
            {
                error = mouseError ?? $"{candidate[item.Action]} 已被其他程序占用。";
                UnregisterActions();
                var restored = new Dictionary<string, KeyCombination>();
                var failed = new List<string>();
                id = RecoveryId + 1;
                foreach (var old in previous)
                {
                    var key = old.Value;
                    if (TryRegister(id, key, out _))
                    { actions[id] = old.Key; restored[old.Key] = key; }
                    else failed.Add(old.Key);
                    id++;
                }
                current = restored;
                error += failed.Count == 0 ? "原快捷键已恢复。" : $"部分原快捷键恢复失败：{string.Join("、", failed)}。请在设置中重新配置；托盘仍可恢复窗口。";
                return false;
            }
            actions[id++] = item.Action;
            registered[item.Action] = item.Key;
        }
        current = registered; return true;
    }
    // Leave the fixed recovery registration intact while an editor receives key presses.
    public void SuspendActions() => UnregisterActions();
    private bool TryRegister(int id, KeyCombination key, out string? error)
    {
        error = null;
        if (key.IsMouse)
        {
            mouseHook ??= new MouseHotkeyHook(handle);
            bool result = mouseHook.TryAdd(key, id, out string message);
            if (!result) error = message;
            return result;
        }
        if (!NativeMethods.RegisterHotKey(handle, id, key.Modifiers | 0x4000, key.VirtualKey)) return false;
        keyboardIds.Add(id); return true;
    }
    private void UnregisterActions()
    {
        foreach (var id in keyboardIds) NativeMethods.UnregisterHotKey(handle, id);
        keyboardIds.Clear(); actions.Clear(); mouseHook?.Clear();
    }
    public void Dispose() { UnregisterActions(); mouseHook?.Dispose(); if (RecoveryAvailable) NativeMethods.UnregisterHotKey(handle, RecoveryId); }
}
