using System.Reflection;
using System.Runtime.InteropServices;
using YingBan.App;

internal static class InteractionChecks
{
    // Exercise product controls and native messages in this test process only.
    internal static void Recording(Form owner)
    {
        int saves = 0;
        using var dialog = new SettingsForm(new AppSettings(), _ => { saves++; return null; });
        dialog.Show(owner);
        Descendants(dialog).OfType<TabControl>().Single().SelectedIndex = 1;
        var controls = Descendants(dialog).Where(c => c.AccessibleName?.EndsWith("快捷键") == true && c.AccessibleName != "恢复默认快捷键").ToArray();
        Assert(controls.Length == 11 && controls.All(c => c is Button), "shortcut fields still require typed text instead of recording");
        var record = (Button)controls.Single(c => c.AccessibleName == "播放 / 暂停快捷键");
        record.PerformClick();
        Assert(Press(record, Keys.Control | Keys.ControlKey), "modifier escaped capture");
        Assert(Press(record, Keys.Control | Keys.Shift | Keys.F8), "combination escaped capture");
        Assert(record.Text == "Ctrl+Shift+F8", "pressed combination was not recorded");
        record.PerformClick();
        Assert(Press(record, Keys.Escape), "Escape did not cancel capture");
        Assert(Press(record, Keys.Escape), "holding Escape leaked into closing the settings dialog");
        SendMessage(record.Handle, 0x101, new IntPtr((int)Keys.Escape), IntPtr.Zero);
        Assert(dialog.Visible && record.Text == "Ctrl+Shift+F8", "cancel lost shortcut or closed settings");
        record.PerformClick();
        Assert(Press(record, Keys.Enter), "Enter was not captured");
        Assert(dialog.Visible && saves == 0 && HotkeyManager.Parse(record.Text).VirtualKey == (uint)Keys.Enter, "Enter saved the dialog instead of recording");
        Assert(Press(record, Keys.Enter), "holding Enter leaked into default button handling");
        Assert(dialog.Visible && saves == 0, "holding the recorded Enter key accidentally saved settings");
        SendMessage(record.Handle, 0x101, new IntPtr((int)Keys.Enter), IntPtr.Zero);
        record.PerformClick();
        Assert(Press(record, Keys.Space) && Press(record, Keys.Space), "Space repeat escaped capture");
        SendMessage(record.Handle, 0x101, new IntPtr((int)Keys.Space), IntPtr.Zero);
        Assert(record.Text == "Space", "Space key-up restarted recording");
        record.PerformClick(); Press(record, Keys.Alt | Keys.Left);
        Assert(record.Text == "Alt+Left", "arrow key was not recorded");
        record.PerformClick();
        Press(record, Keys.LWin); Press(record, Keys.F24);
        Assert(HotkeyManager.Parse(((HotkeyRecordButton)record).Shortcut).VirtualKey == (uint)Keys.Left, "unsupported Win modifier silently dropped");
        SendMessage(record.Handle, 0x101, new IntPtr((int)Keys.LWin), IntPtr.Zero);
        record.PerformClick();
        Descendants(dialog).OfType<Button>().Single(c => c.Text == "保存设置").PerformClick();
        Assert(saves == 1 && dialog.Result.Hotkeys["toggle"] == "Alt+Left", "save stored pending prompt instead of last shortcut");
    }
    internal static void CompactResize(MainForm form)
    {
        long style = GetWindowLongPtr(form.Handle, -16).ToInt64();
        Assert((style & 0x40000) != 0, "compact window has no native resize frame");
        Assert((style & 0xC00000) == 0, "compact resize brought back the title bar");
        var bounds = form.Bounds;
        int border = form.PointToScreen(Point.Empty).X - bounds.Left;
        Assert(border > 0, "resize border has no hit area");
        var hits = new[] {
            (new Point(bounds.Left + border / 2, bounds.Top + bounds.Height / 2), 10),
            (new Point(bounds.Right - border / 2 - 1, bounds.Top + bounds.Height / 2), 11),
            (new Point(bounds.Left + bounds.Width / 2, bounds.Top + border / 2), 2),
            (new Point(bounds.Left + bounds.Width / 2, bounds.Bottom - border / 2 - 1), 15),
            (new Point(bounds.Left + border / 2, bounds.Top + border / 2), 13),
            (new Point(bounds.Right - border / 2 - 1, bounds.Top + border / 2), 14),
            (new Point(bounds.Left + border / 2, bounds.Bottom - border / 2 - 1), 16),
            (new Point(bounds.Right - border / 2 - 1, bounds.Bottom - border / 2 - 1), 17)
        };
        foreach (var (point, expected) in hits)
        {
            var packed = new IntPtr(unchecked((int)((uint)(ushort)point.X | ((uint)(ushort)point.Y << 16))));
            int actual = SendMessage(form.Handle, 0x84, IntPtr.Zero, packed).ToInt32();
            Assert(actual == expected, $"native frame hit mismatch: expected {expected}, actual {actual}; top must move, other edges/corners resize");
        }
        int frameWidth = form.Width - form.ClientSize.Width, frameHeight = form.Height - form.ClientSize.Height;
        double ratio = (double)form.ClientSize.Width / form.ClientSize.Height;
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf<NativeRect>());
        try
        {
            foreach (int edge in Enumerable.Range(1, 8))
            {
                var proposed = new NativeRect { Left = 100, Top = 100, Right = 850, Bottom = 680 };
                Marshal.StructureToPtr(proposed, memory, false);
                SendMessage(form.Handle, 0x214, new IntPtr(edge), memory);
                var sized = Marshal.PtrToStructure<NativeRect>(memory);
                Assert(Math.Abs((double)(sized.Right - sized.Left - frameWidth) / (sized.Bottom - sized.Top - frameHeight) - ratio) < .015, "resize lost video aspect on edge " + edge);
                Assert((edge is 1 or 4 or 7 ? sized.Right == proposed.Right : sized.Left == proposed.Left) && (edge is 3 or 4 or 5 ? sized.Bottom == proposed.Bottom : sized.Top == proposed.Top), "resize moved opposite anchor");
                form.Bounds = Rectangle.FromLTRB(sized.Left, sized.Top, sized.Right, sized.Bottom);
                Assert(form.ClientSize.Width > 620, "sizing did not change the compact window");
            }
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    internal static bool Available(string shortcut)
    {
        using var probe = new Form(); var key = HotkeyManager.Parse(shortcut);
        bool result = RegisterHotKey(probe.Handle, 1, key.Modifiers | 0x4000, key.VirtualKey);
        if (result) UnregisterHotKey(probe.Handle, 1);
        return result;
    }
    internal static bool Press(Control control, Keys key)
    {
        object[] args = [Message.Create(control.Handle, 0x100, new IntPtr((int)(key & Keys.KeyCode)), IntPtr.Zero), key];
        return (bool)control.GetType().GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, args)!;
    }
    private static IEnumerable<Control> Descendants(Control control) { foreach (Control child in control.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
}
