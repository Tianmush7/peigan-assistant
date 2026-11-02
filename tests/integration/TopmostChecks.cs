using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using YingBan.App;

internal static class TopmostChecks
{
    internal static void RunPeer()
    {
        using var window = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000), Size = new Size(1000, 800), ShowInTaskbar = false };
        window.FormClosed += (_, _) => Application.ExitThread();
        SetWindowPos(window.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x53);
        Console.WriteLine(window.Handle.ToInt64());
        Application.Run();
    }
    internal static void Run(MainForm form)
    {
        // Off-screen native frames only: no browser initialization, input injection or game interaction.
        _ = form.Handle;
        var toggle = typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Task)toggle.Invoke(form, null)!).GetAwaiter().GetResult();
        typeof(MainForm).GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
        form.Location = new Point(-20000, -20000);
        SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x53);
        form.Enabled = false;
        using var peer = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--topmost-peer") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true })!;
        IntPtr competitor = IntPtr.Zero;
        try
        {
            competitor = new IntPtr(long.Parse(peer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()!));
            Cover(form, competitor);
            Assert(IsWindowVisible(form.Handle) && !Read<bool>(form, "ready"), "fixture should be visible to the OS without starting the browser");
            form.Enabled = true;
            var foreground = GetForegroundWindow();
            var original = form.Bounds;
            Pump(1600, () => IsAbove(form.Handle, competitor));
            Assert(IsAbove(form.Handle, competitor), "another topmost window still covers compact after 1.6 seconds");
            Assert(GetForegroundWindow() == foreground, "keeping compact above another window stole focus");
            Assert(form.Bounds == original, "keeping compact above another window moved or resized it");

            EnableWindow(form.Handle, false); // native modal loops do not change the managed Enabled property
            Assert(form.Enabled && !IsWindowEnabled(form.Handle), "fixture must model native modal owner disabling");
            try { StaysCovered(form, competitor, "disabled native modal owner was raised"); }
            finally { EnableWindow(form.Handle, true); }

            var settings = Read<AppSettings>(form, "settings");
            settings.AlwaysOnTop = false;
            typeof(MainForm).GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            StaysCovered(form, competitor, "turning off AlwaysOnTop still raises compact");
            settings.AlwaysOnTop = true;
            typeof(MainForm).GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);

            SetWindowPos(form.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x97); // hide without changing order
            StaysCovered(form, competitor, "hidden compact was raised");
            Assert(!IsWindowVisible(form.Handle), "maintenance showed a hidden window");
            SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x53);

            SetWindowPos(competitor, new IntPtr(-1), -25000, -25000, 0, 0, 0x11);
            StaysCovered(form, competitor, "non-overlapping window caused unnecessary reordering");
            SetWindowPos(competitor, new IntPtr(-1), -20000, -20000, 0, 0, 0x11);

            ((Task)toggle.Invoke(form, null)!).GetAwaiter().GetResult();
            form.Location = new Point(-20000, -20000);
            StaysCovered(form, competitor, "normal window unexpectedly uses compact maintenance");
            Assert(GetForegroundWindow() == foreground, "guard checks stole foreground focus");
            Console.WriteLine("PASS topmost guards: modal owner, off, hidden, non-overlapping, normal; foreground unchanged");
        }
        finally
        {
            if (competitor != IntPtr.Zero) PostMessage(competitor, 0x10, IntPtr.Zero, IntPtr.Zero);
            if (!peer.WaitForExit(3000)) peer.Kill();
        }
    }
    private static void Cover(MainForm form, IntPtr competitor)
    {
        Assert(SetWindowPos(form.Handle, competitor, 0, 0, 0, 0, 0x13), "could not arrange fixture windows");
        Assert(IsAbove(competitor, form.Handle), "fixture failed to cover compact window");
    }
    private static void StaysCovered(MainForm form, IntPtr competitor, string error)
    {
        Cover(form, competitor);
        Pump(750);
        Assert(IsAbove(competitor, form.Handle), error);
    }
    private static void Pump(int milliseconds, Func<bool>? until = null)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds && until?.Invoke() != true)
        { Application.DoEvents(); Thread.Sleep(15); }
    }
    private static T Read<T>(object target, string field) => (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static bool IsAbove(IntPtr upper, IntPtr lower)
    {
        for (var w = GetWindow(lower, 3); w != IntPtr.Zero; w = GetWindow(w, 3)) if (w == upper) return true;
        return false;
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnableWindow(IntPtr window, bool enabled);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
}
