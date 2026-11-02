using System.Runtime.InteropServices;

namespace YingBan.App;

/// <summary>Only bound side buttons are consumed. Work is posted back to the UI thread.</summary>
internal sealed class MouseHotkeyHook(IntPtr owner) : IDisposable
{
    internal const int ActionMessage = 0x8042;
    private sealed record Bindings(int Generation, Dictionary<KeyCombination, int> Actions);
    private volatile Bindings bindings = new(0, []);
    private readonly HashSet<uint> consumed = [];
    private readonly ManualResetEventSlim ready = new();
    private Thread? thread;
    private uint threadId;
    private IntPtr hook;
    private NativeMethods.HookProcedure? callback;
    private int startupError;
    private bool disposed;

    internal bool TryAdd(KeyCombination key, int id, out string error)
    {
        error = "";
        if (thread == null)
        {
            thread = new Thread(MessageLoop) { IsBackground = true, Name = "YingBan mouse shortcuts" };
            thread.Start(); ready.Wait();
        }
        if (hook == IntPtr.Zero) { error = $"鼠标侧键监听启动失败（错误码 {startupError}）。"; return false; }
        bindings = new(bindings.Generation + 1, new(bindings.Actions) { [key] = id });
        return true;
    }
    internal bool IsCurrent(int generation) => generation == bindings.Generation;
    internal void Clear() => bindings = new(bindings.Generation + 1, []);

    // State is updated by the hook thread; bindings are immutable snapshots from the UI.
    internal bool ProcessButton(uint message, uint side, uint modifiers, out int id, out int generation)
    {
        id = 0; generation = 0;
        if (side is not (1 or 2)) return false;
        if (message == 0x20C) return consumed.Remove(side);
        if (message != 0x20B) return false;
        if (consumed.Contains(side)) return true;
        var snapshot = bindings;
        var key = new KeyCombination(modifiers, side == 1 ? (uint)Keys.XButton1 : (uint)Keys.XButton2);
        if (!snapshot.Actions.TryGetValue(key, out id)) return false;
        consumed.Add(side); generation = snapshot.Generation;
        return true;
    }
    private IntPtr HookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt64() is 0x20B or 0x20C)
        {
            var mouse = Marshal.PtrToStructure<NativeMethods.MouseHookData>(data);
            if (ProcessButton((uint)message.ToInt64(), mouse.MouseData >> 16, NativeMethods.CurrentMouseModifiers(), out int id, out int generation))
            {
                if (id != 0) NativeMethods.PostMessage(owner, ActionMessage, new IntPtr(id), new IntPtr(generation));
                return new IntPtr(1);
            }
        }
        return NativeMethods.CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    private void MessageLoop()
    {
        try
        {
            threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            callback = HookCallback; // rooted for the entire native hook lifetime
            hook = NativeMethods.SetWindowsHookEx(14, callback, NativeMethods.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) startupError = Marshal.GetLastWin32Error();
            ready.Set();
            if (hook == IntPtr.Zero) return;
            // Dedicated loop keeps browser/UI work out of the low-level hook timeout.
            while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            { NativeMethods.TranslateMessage(ref message); NativeMethods.DispatchMessage(ref message); }
        }
        finally
        {
            if (hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero; ready.Set();
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; Clear();
        if (thread is { IsAlive: true }) { NativeMethods.PostThreadMessage(threadId, 0x12, IntPtr.Zero, IntPtr.Zero); thread.Join(); }
        ready.Dispose();
    }
}
