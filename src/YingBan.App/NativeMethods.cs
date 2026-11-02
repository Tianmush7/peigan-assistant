using System.Runtime.InteropServices;

namespace YingBan.App;

internal static class NativeMethods
{
    public const int GwlExStyle = -20, WsExTransparent = 0x20, WsExLayered = 0x80000, WsExNoActivate = 0x08000000, WsExToolWindow = 0x80;
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll")] internal static extern short GetKeyState(int key);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    internal delegate IntPtr HookProcedure(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHookData { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativeMessage { public IntPtr Hwnd; public uint Message; public UIntPtr WParam; public IntPtr LParam; public uint Time; public int X, Y; public uint Private; }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int id, HookProcedure callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")] internal static extern bool PostThreadMessage(uint thread, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint = "GetMessageW")] internal static extern int GetMessage(out NativeMessage message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll", EntryPoint = "PeekMessageW")] internal static extern bool PeekMessage(out NativeMessage message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")] internal static extern IntPtr DispatchMessage(ref NativeMessage message);
    internal static uint CurrentMouseModifiers() => (GetAsyncKeyState((int)Keys.ControlKey) < 0 ? 2u : 0)
        | (GetAsyncKeyState((int)Keys.Menu) < 0 ? 1u : 0) | (GetAsyncKeyState((int)Keys.ShiftKey) < 0 ? 4u : 0)
        | (GetAsyncKeyState((int)Keys.LWin) < 0 || GetAsyncKeyState((int)Keys.RWin) < 0 ? 8u : 0);
    [DllImport("user32.dll")] internal static extern IntPtr GetLastActivePopup(IntPtr hwnd);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [StructLayout(LayoutKind.Sequential)] internal struct WindowRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out WindowRect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowEnabled(IntPtr hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    public static void SetClickThrough(IntPtr hwnd, bool enabled)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        if (enabled) style |= WsExTransparent | WsExLayered | WsExNoActivate;
        else style &= ~(WsExTransparent | WsExNoActivate);
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x27); // frame changed, no move/resize/z-order
    }
}
