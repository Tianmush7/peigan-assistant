namespace YingBan.App;

/// <summary>Records a keyboard key or a mouse side button with optional modifiers.</summary>
public sealed class HotkeyRecordButton : Button
{
    private string shortcut = "";
    private bool recording;
    private Keys pendingRelease;
    private int pendingMouseRelease;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Shortcut
    {
        get => shortcut;
        set { shortcut = value; FinishRecording(); }
    }

    public HotkeyRecordButton()
    {
        Width = 235; Height = 35;
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderColor = Theme.Muted;
        BackColor = Theme.Surface; ForeColor = Theme.Text;
        TextAlign = ContentAlignment.MiddleLeft; Padding = new Padding(8, 0, 0, 0);
        AccessibleDescription = "点击后按键盘组合，或将鼠标停在框内按侧键；Esc 取消本次录入。";
        UseMnemonic = false;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        pendingRelease = Keys.None;
        pendingMouseRelease = 0;
        recording = true;
        Text = "请按快捷键…";
        ForeColor = Theme.Accent; FlatAppearance.BorderColor = Theme.Accent;
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key == pendingRelease && key != Keys.None) return true;
        if (!recording) return base.ProcessCmdKey(ref message, keyData);
        if (key == Keys.Escape) { FinishRecording(); pendingRelease = key; return true; }
        if (key is Keys.LWin or Keys.RWin || NativeMethods.GetKeyState((int)Keys.LWin) < 0 || NativeMethods.GetKeyState((int)Keys.RWin) < 0)
        {
            FinishRecording(); Text = "不支持 Win，请点击重录"; ForeColor = Theme.Accent;
            return true;
        }
        if (key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.Menu or Keys.LMenu or Keys.RMenu or Keys.None)
            return true;
        string value = ((keyData & Keys.Control) != 0 ? "Ctrl+" : "")
            + ((keyData & Keys.Alt) != 0 ? "Alt+" : "")
            + ((keyData & Keys.Shift) != 0 ? "Shift+" : "") + KeyName(key);
        try { HotkeyManager.Parse(value); }
        catch (FormatException) { return true; }
        Shortcut = value;
        pendingRelease = key;
        return true;
    }

    // Avoid enum aliases such as Return/Prior/Next in user-visible combinations.
    private static string KeyName(Keys key) => key switch
    { Keys.Enter => "Enter", Keys.PageUp => "PageUp", Keys.PageDown => "PageDown", Keys.CapsLock => "CapsLock", _ => key.ToString() };
    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x020B or 0x020C or 0x020D)
        {
            int side = (int)((message.WParam.ToInt64() >> 16) & 0xffff);
            if (side is 1 or 2)
            {
                if (side == pendingMouseRelease)
                {
                    if (message.Msg == 0x020C) pendingMouseRelease = 0;
                    message.Result = new IntPtr(1); return;
                }
                if (recording && message.Msg is 0x020B or 0x020D)
                {
                    if (NativeMethods.GetKeyState((int)Keys.LWin) < 0 || NativeMethods.GetKeyState((int)Keys.RWin) < 0)
                    { FinishRecording(); Text = "不支持 Win，请点击重录"; }
                    else
                    {
                        long flags = message.WParam.ToInt64();
                        Shortcut = ((flags & 8) != 0 ? "Ctrl+" : "") + (NativeMethods.GetKeyState((int)Keys.Menu) < 0 ? "Alt+" : "")
                            + ((flags & 4) != 0 ? "Shift+" : "") + (side == 1 ? "Mouse4" : "Mouse5");
                    }
                    pendingMouseRelease = side;
                    message.Result = new IntPtr(1); return;
                }
            }
        }
        if (message.Msg is 0x0101 or 0x0105 && (Keys)message.WParam.ToInt32() == pendingRelease && pendingRelease != Keys.None)
        { pendingRelease = Keys.None; message.Result = IntPtr.Zero; return; }
        base.WndProc(ref message);
    }
    protected override void OnLeave(EventArgs e) { pendingRelease = Keys.None; pendingMouseRelease = 0; FinishRecording(); base.OnLeave(e); }
    private void FinishRecording()
    {
        recording = false; Text = shortcut.Replace("Mouse4", "鼠标侧键1").Replace("Mouse5", "鼠标侧键2");
        ForeColor = Theme.Text; FlatAppearance.BorderColor = Theme.Muted;
    }
}
