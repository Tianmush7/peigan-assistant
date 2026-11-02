using System.Reflection;
using System.Runtime.InteropServices;
using YingBan.App;

internal static class MouseShortcutChecks
{
    internal static void RecordAndSave(Form owner)
    {
        using var dialog = new SettingsForm(new AppSettings(), _ => null);
        dialog.Show(owner);
        Descendants(dialog).OfType<TabControl>().Single().SelectedIndex = 1;
        var record = Descendants(dialog).OfType<HotkeyRecordButton>().First();
        record.PerformClick();
        SendMessage(record.Handle, 0x20B, new IntPtr(1 << 16), IntPtr.Zero);
        Assert(record.Shortcut == "Mouse4", "first mouse side button was not recorded");
        SendMessage(record.Handle, 0x20C, new IntPtr(1 << 16), IntPtr.Zero);
        Assert(record.Text.Contains("侧键"), "mouse shortcut label not understandable");
        record.PerformClick();
        SendMessage(record.Handle, 0x20B, new IntPtr((2 << 16) | 8 | 4), IntPtr.Zero);
        Assert(record.Shortcut == "Ctrl+Shift+Mouse5", "second mouse side button lost modifiers");
        SendMessage(record.Handle, 0x20C, new IntPtr(2 << 16), IntPtr.Zero);
        record.PerformClick(); InteractionChecks.Press(record, Keys.Escape);
        Assert(record.Shortcut == "Ctrl+Shift+Mouse5", "Escape lost recorded side button");
        Descendants(dialog).OfType<Button>().Single(b => b.Text == "保存设置").PerformClick();
        var directory = Path.Combine(AppContext.BaseDirectory, "mouse-settings-" + Guid.NewGuid().ToString("N"));
        var store = new SettingsStore(directory); store.Save(dialog.Result);
        Assert(store.Load().Hotkeys["toggle"] == "Ctrl+Shift+Mouse5", "side button settings did not persist");
    }

    internal static void GlobalRouting()
    {
        using var sink = new HotkeySink();
        using var manager = new HotkeyManager(sink.Handle);
        Assert(manager.TryApply(new() { ["toggle"] = "Mouse4", ["back"] = "Ctrl+Mouse5" }, out var error), error);
        var hook = Read(manager, "mouseHook");
        Assert((IntPtr)Read(hook, "hook") != IntPtr.Zero, "native mouse hook was not installed");
        Assert(HookMessage(hook, 0x20B, 1) == 1, "bound side button should be consumed");
        Application.DoEvents();
        Assert(sink.Actions.Count == 1 && manager.MouseActionFor(sink.Actions[0].Id, sink.Actions[0].Generation) == "toggle", "hook did not dispatch the assigned global action");
        Assert(HookMessage(hook, 0x20B, 1) == 1, "held side button should stay consumed");
        Application.DoEvents(); Assert(sink.Actions.Count == 1, "held side button repeated the action");
        manager.SuspendActions();
        Assert(HookMessage(hook, 0x20C, 1) == 1, "suspension lost the paired button release");
        HookMessage(hook, 0x20B, 1); HookMessage(hook, 0x20C, 1);
        Application.DoEvents(); Assert(sink.Actions.Count == 1, "suspended side button still triggered");
        Assert(manager.TryApply(new() { ["toggle"] = "Mouse4", ["back"] = "Ctrl+Mouse5" }, out error), error);
        Assert(!manager.TryApply(new() { ["toggle"] = "Mouse4", ["back"] = "XButton1" }, out error), "equivalent side buttons bypassed duplicate check");
        HookMessage(hook, 0x20B, 1); HookMessage(hook, 0x20C, 1);
        Application.DoEvents(); Assert(sink.Actions.Count == 2, "duplicate error lost previous bindings");
        manager.Dispose();
        Assert((IntPtr)Read(hook, "hook") == IntPtr.Zero, "native mouse hook leaked after dispose");
    }

    internal static async Task ControlsVideo(MainForm form)
    {
        var manager = (HotkeyManager)Read(form, "hotkeys");
        var settings = (AppSettings)Read(form, "settings");
        Assert(manager.TryApply(new() { ["toggle"] = "Mouse4", ["back"] = "Mouse5" }, out var error), error);
        var hook = Read(manager, "mouseHook");
        var before = ((MediaState)Read(form, "media")).Paused;
        try
        {
            form.Hide();
            HookMessage(hook, 0x20B, 1); HookMessage(hook, 0x20C, 1);
            for (int attempt = 0; attempt < 100 && ((MediaState)Read(form, "media")).Paused == before; attempt++) await Task.Delay(50);
            Assert(((MediaState)Read(form, "media")).Paused != before, "mouse shortcut did not control decoded video while main window was hidden");
            HookMessage(hook, 0x20B, 1); HookMessage(hook, 0x20C, 1);
            for (int attempt = 0; attempt < 100 && ((MediaState)Read(form, "media")).Paused != before; attempt++) await Task.Delay(50);
            Assert(((MediaState)Read(form, "media")).Paused == before, "second mouse shortcut failed to restore pause state");
        }
        finally { form.Show(); Assert(manager.TryApply(settings.Hotkeys, out error), error); }
    }

    private static int HookMessage(object hook, int message, int side)
    {
        var data = new MouseData { Mouse = (uint)side << 16 };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<MouseData>());
        try
        {
            Marshal.StructureToPtr(data, memory, false);
            return ((IntPtr)hook.GetType().GetMethod("HookCallback", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(hook, [0, new IntPtr(message), memory])!).ToInt32();
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static object Read(object value, string field) => value.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    private static IEnumerable<Control> Descendants(Control control) { foreach (Control c in control.Controls) { yield return c; foreach (var nested in Descendants(c)) yield return nested; } }
    private static void Assert(bool ok, string message) { if (!ok) throw new Exception(message); }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Mouse, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private sealed class HotkeySink : Form
    {
        public readonly List<(int Id, int Generation)> Actions = [];
        protected override void WndProc(ref Message m) { if (m.Msg == 0x8042) { Actions.Add((m.WParam.ToInt32(), m.LParam.ToInt32())); return; } base.WndProc(ref m); }
    }
}
