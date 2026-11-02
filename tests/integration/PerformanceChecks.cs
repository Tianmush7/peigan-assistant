using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using YingBan.App;

internal static class PerformanceChecks
{
    internal static void Run(MainForm form)
    {
        _ = form.Handle;
        var settings = new AppSettings();
        using var overlay = new DanmakuOverlay(settings);
        typeof(MainForm).GetField("overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, overlay);
        var apply = typeof(MainForm).GetMethod("ApplySettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
        apply.Invoke(form, null);
        var topmost = typeof(MainForm).GetMethod("MaintainCompactTopmost", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var onMedia = typeof(MainForm).GetMethod("OnMedia", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var toggle = typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ((Task)toggle.Invoke(form, null)!).GetAwaiter().GetResult();
        Measure("500 window movements with projection disabled", () => { for (int i = 0; i < 500; i++) form.Location = new Point(-20000 + i % 100, -20000); });
        Measure("2400 media samples (10 minutes at 4Hz)", () => { for (int i = 0; i < 2400; i++) onMedia.Invoke(form, [new MediaState("https://fixture.invalid/", "Performance fixture", i * .25, 3600, false, 1, true, 640, 480)]); });
        SetWindowPos(form.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x53);
        Measure("1000 visible compact topmost checks", () => { for (int i = 0; i < 1000; i++) topmost.Invoke(form, null); });
        using var peer = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--topmost-peer") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true })!;
        var peerWindow = new IntPtr(long.Parse(peer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()!));
        try
        {
            Measure("500 competing-window reorder cycles", () => {
                for (int i = 0; i < 500; i++) { SetWindowPos(form.Handle, peerWindow, 0, 0, 0, 0, 0x13); topmost.Invoke(form, null); }
            });
        }
        finally { PostMessage(peerWindow, 0x10, IntPtr.Zero, IntPtr.Zero); if (!peer.WaitForExit(3000)) peer.Kill(); }
        typeof(MainForm).GetField("overlay", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(form, null);
    }
    private static void Measure(string name, Action action)
    {
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var allocations = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew(); action(); watch.Stop();
        process.Refresh();
        Console.WriteLine($"PERF {name}: elapsed={watch.Elapsed.TotalMilliseconds:F1}ms cpu={(process.TotalProcessorTime-cpu).TotalMilliseconds:F1}ms allocated={(GC.GetAllocatedBytesForCurrentThread()-allocations)/1024d:F1}KiB");
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
