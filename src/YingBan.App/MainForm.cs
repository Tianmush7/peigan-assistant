using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.WinForms;
using YingBan.Core;

namespace YingBan.App;

public sealed class MainForm : Form
{
    private readonly SettingsStore store;
    private AppSettings settings;
    private readonly string[] args;
    private readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Theme.Background, Margin = Padding.Empty };
    private readonly TableLayoutPanel layout = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, Padding = Padding.Empty };
    private readonly TextBox address = new() { Dock = DockStyle.Fill, BackColor = Theme.Surface, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = "粘贴 B 站链接或 BV 号，按 Enter 打开", AccessibleName = "视频网址", Margin = new Padding(8, 9, 8, 6) };
    private readonly FlowLayoutPanel toolbar = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Margin = Padding.Empty, Padding = new Padding(8, 0, 8, 0), BackColor = Theme.Surface };
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Padding = new Padding(12, 0, 8, 0), AutoEllipsis = true, Text = "正在准备浏览器…" };
    private readonly Label clockLabel = Theme.Label("00:00 / 00:00", 124);
    private readonly ComboBox rates = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70, BackColor = Theme.Background, ForeColor = Theme.Text, Margin = new Padding(5, 7, 6, 3), AccessibleName = "播放倍速" };
    private readonly DanmakuTimeline timeline = new();
    private readonly NotifyIcon tray;
    private readonly System.Windows.Forms.Timer saveTimer = new() { Interval = 15000 };
    private readonly System.Windows.Forms.Timer topmostTimer = new() { Interval = 500 };
    private BrowserAdapter? browser;
    private HotkeyManager? hotkeys;
    private DanmakuOverlay? overlay;
    private SettingsForm? settingsDialog;
    private MediaState media = new("", "", 0, 0, true, 1, false);
    private Button play = null!, pin = null!, compactButton = null!, through = null!, danmaku = null!;
    private bool ready, compact, clickThrough, updatingRate, closing, imported, needsCompact;
    private Rectangle normalBounds;
    private FormWindowState normalWindowState;
    private Bookmark? pendingBookmark;
    private ulong? bookmarkNavigation;
    private bool bookmarkCommitted;
    private DateTime statusUntil;
    private int compactWidth = 620;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Keep a native resize border outside the video client area, without a caption.
            if (compact) parameters.Style = (parameters.Style | 0x00040000) & ~0x00C00000;
            return parameters;
        }
    }

    public MainForm(SettingsStore store, string[] args)
    {
        this.store = store; this.args = args; settings = store.Load();
        Text = "陪肝小助手 · 视频跟跑助手"; Theme.Apply(this);
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("YingBan.App.AppIcon.ico")!)
        using (var appIcon = new Icon(iconStream))
            Icon = (Icon)appIcon.Clone();
        MinimumSize = new Size(540, 320); Size = new Size(settings.Width, settings.Height);
        StartPosition = FormStartPosition.Manual;
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        var proposed = new Rectangle(settings.Left, settings.Top, settings.Width, settings.Height);
        if (settings.Left != -1 && Screen.AllScreens.Any(s => Rectangle.Intersect(s.WorkingArea, proposed).Width > 100 && Rectangle.Intersect(s.WorkingArea, proposed).Height > 100)) Bounds = proposed;
        else Location = new Point(workArea.Left + Math.Max(0, (workArea.Width - Width) / 2), workArea.Top + Math.Max(0, (workArea.Height - Height) / 2));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        BuildNavigation(); BuildToolbar();
        layout.Controls.Add(web, 0, 2); layout.Controls.Add(status, 0, 3); Controls.Add(layout);
        tray = new NotifyIcon { Icon = Icon, Text = "陪肝小助手 · Ctrl+Alt+R 恢复窗口", Visible = true };
        var menu = new ContextMenuStrip();
        menu.Items.Add("恢复窗口 / 关闭穿透", null, (_, _) => Recover());
        menu.Items.Add("播放 / 暂停", null, async (_, _) => await Command("toggle"));
        menu.Items.Add("弹幕投影 开 / 关", null, (_, _) => ToggleDanmaku());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出陪肝小助手", null, (_, _) => Close());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Recover();
        Shown += async (_, _) => await Initialize();
        LocationChanged += (_, _) => { if (!closing && settings.ScreenIndex == -1) ApplyOverlay(); };
        FormClosing += OnClosing;
        saveTimer.Tick += (_, _) => SaveState(); saveTimer.Start();
        topmostTimer.Tick += (_, _) => MaintainCompactTopmost(); topmostTimer.Start();
    }

    private void BuildNavigation()
    {
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 7, Margin = Padding.Empty, BackColor = Theme.Background, Padding = new Padding(8, 0, 8, 0) };
        foreach (int width in new[] { 78, 38, 38, 38 }) nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68)); nav.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        var home = Theme.Button("映 伴", async (_, _) => { if (browser != null) { await LeaveVideo(); await browser.ShowHomeAsync(); } }, 70, true);
        var back = Theme.Button("‹", (_, _) => { if (web.CanGoBack) web.GoBack(); }, 32);
        var forward = Theme.Button("›", (_, _) => { if (web.CanGoForward) web.GoForward(); }, 32);
        var reload = Theme.Button("↻", (_, _) => { if (ready) web.Reload(); }, 32);
        back.AccessibleName = "网页后退"; forward.AccessibleName = "网页前进"; reload.AccessibleName = "刷新网页";
        nav.Controls.Add(home, 0, 0); nav.Controls.Add(back, 1, 0); nav.Controls.Add(forward, 2, 0); nav.Controls.Add(reload, 3, 0);
        nav.Controls.Add(address, 4, 0); nav.Controls.Add(Theme.Button("打开", async (_, _) => await Navigate(address.Text), 58, true), 5, 0);
        nav.Controls.Add(Theme.Button("本地视频", async (_, _) => await OpenLocal(), 74), 6, 0);
        address.KeyDown += async (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await Navigate(address.Text); } };
        layout.Controls.Add(nav, 0, 0);
    }
    private void BuildToolbar()
    {
        play = Theme.Button("▶ 播放", async (_, _) => await Command("toggle"), 76, true);
        toolbar.Controls.Add(play);
        toolbar.Controls.Add(Theme.Button("−" + settings.StepSeconds + "秒", async (_, _) => await Command("seek", -settings.StepSeconds), 58));
        toolbar.Controls.Add(Theme.Button("+" + settings.StepSeconds + "秒", async (_, _) => await Command("seek", settings.StepSeconds), 58));
        rates.Items.AddRange(new object[] { "0.5×", "0.75×", "1×", "1.25×", "1.5×", "1.75×", "2×", "3×", "4×" }); rates.SelectedIndex = 2;
        rates.SelectedIndexChanged += async (_, _) => { if (!updatingRate && rates.SelectedItem is string s && double.TryParse(s.TrimEnd('×'), CultureInfo.InvariantCulture, out double value)) await Command("rate", value); };
        toolbar.Controls.Add(rates);
        pin = Theme.Button("置顶：开", (_, _) => { settings.AlwaysOnTop = !settings.AlwaysOnTop; ApplySettings(); }, 80);
        compactButton = Theme.Button("跟跑小窗", async (_, _) => await ToggleCompact(), 82);
        through = Theme.Button("鼠标穿透", (_, _) => ToggleThrough(), 78);
        danmaku = Theme.Button("弹幕：关", (_, _) => ToggleDanmaku(), 80);
        var config = Theme.Button("设置", (_, _) => ShowSettings(), 58);
        var more = Theme.Button("更多 ▾", (_, _) => ShowMore(), 72);
        toolbar.Controls.AddRange([pin, compactButton, through, danmaku, config, more, clockLabel]);
        layout.Controls.Add(toolbar, 0, 1);
    }
    private async Task Initialize()
    {
        int dark = 1; NativeMethods.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        hotkeys = new HotkeyManager(Handle);
        if (!hotkeys.InitializeRecovery()) ShowStatus("恢复键 Ctrl+Alt+R 被占用；暂不启用鼠标穿透。可通过托盘恢复。", 20);
        if (!hotkeys.TryApply(settings.Hotkeys, out var keyError)) ShowStatus(keyError + " 可到设置中修改。", 20);
        overlay = new DanmakuOverlay(settings); ApplyOverlay();
        ApplySettings();
        browser = new BrowserAdapter(web);
        browser.MediaChanged += OnMedia;
        browser.CommentsReceived += comments => { if (!imported) { timeline.Add(comments); ShowStatus($"已读取 {timeline.Count:N0} 条弹幕 · {(settings.DanmakuEnabled ? "投影已开启" : "点击「弹幕」开启投影")}", 5); } };
        browser.ContentChanged += () => { timeline.Clear(); overlay?.Clear(); imported = false; needsCompact = compact; };
        browser.StatusChanged += text => ShowStatus(text, 6);
        try
        {
            await browser.InitializeAsync(Path.Combine(store.DirectoryPath, "Browser"), settings.BilibiliAccelerationEnabled); ready = true;
            web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (pendingBookmark == null) return;
                if (e.Cancel) { ClearPendingBookmark(); return; }
                if (bookmarkNavigation == null)
                {
                    if (Uri.TryCreate(pendingBookmark.Url, UriKind.Absolute, out var target) && Uri.TryCreate(e.Uri, UriKind.Absolute, out var requested) && target == requested)
                        bookmarkNavigation = e.NavigationId;
                    else ClearPendingBookmark();
                }
                else if (bookmarkNavigation != e.NavigationId) ClearPendingBookmark();
            };
            web.CoreWebView2.ContentLoading += (_, e) => { if (bookmarkNavigation == e.NavigationId) bookmarkCommitted = true; };
            web.CoreWebView2.NavigationCompleted += (_, e) => { if (!e.IsSuccess && bookmarkNavigation == e.NavigationId) ClearPendingBookmark(); };
            await browser.ShowHomeAsync();
            ShowStatus(store.LoadWarning ?? "就绪 · 粘贴视频网址开始 · Ctrl+Alt+R 随时恢复窗口", 8);
            int urlArg = Array.IndexOf(args, "--url");
            if (urlArg >= 0 && args.Length > urlArg + 1) await Navigate(args[urlArg + 1]);
            int localArg = Array.IndexOf(args, "--local");
            if (localArg >= 0 && args.Length > localArg + 1) await browser.OpenLocalAsync(Path.GetFullPath(args[localArg + 1]));
        }
        catch (Exception e)
        { ShowStatus("浏览器初始化失败：" + e.Message, 3600); MessageBox.Show(this, "无法启动视频浏览器。请确认已安装 Microsoft Edge WebView2 Runtime。\n\n" + e.Message, "陪肝小助手", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private async Task LeaveVideo()
    {
        RememberPosition(); timeline.Clear(); overlay?.Clear(); ClearPendingBookmark(); imported = false;
        if (compact) await ToggleCompact();
    }
    private async Task Navigate(string input, Bookmark? restore = null)
    {
        if (!ready || browser == null) return;
        try { await LeaveVideo(); pendingBookmark = restore; await browser.NavigateAsync(input); address.Text = input; ShowStatus("正在打开视频…", 5); }
        catch (Exception e) { ClearPendingBookmark(); ShowStatus(e.Message, 10); }
    }
    private void ClearPendingBookmark() { pendingBookmark = null; bookmarkNavigation = null; bookmarkCommitted = false; }
    private async Task OpenLocal()
    {
        if (!ready || browser == null) return;
        using var dialog = new OpenFileDialog { Title = "打开本地视频", Filter = "视频文件|*.mp4;*.webm;*.m4v;*.mov;*.mkv;*.ogv|所有文件|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { await LeaveVideo(); await browser.OpenLocalAsync(dialog.FileName); address.Text = dialog.FileName; }
        catch (Exception e) { ShowStatus(e.Message, 10); }
    }
    private async Task Command(string command, double value = 0)
    {
        if (browser == null || !ready) return;
        try { await browser.CommandAsync(command, value); }
        catch (Exception e) { ShowStatus(e.Message, 8); }
    }
    private void OnMedia(MediaState next)
    {
        if (closing) return;
        bool discontinuity = next.CurrentTime < media.CurrentTime - .1 || next.CurrentTime - media.CurrentTime > Math.Max(2, media.PlaybackRate * 1.2);
        if (next.Url != media.Url && media.HasVideo) RememberPosition();
        bool dimensionsChanged = next.VideoWidth != media.VideoWidth || next.VideoHeight != media.VideoHeight;
        media = next;
        if (compact && dimensionsChanged && next.VideoWidth > 0 && next.VideoHeight > 0) FitCompact(ClientSize.Width);
        play.Text = next.Paused || !next.HasVideo ? "▶ 播放" : "Ⅱ 暂停";
        clockLabel.Text = FormatTime(next.CurrentTime) + " / " + FormatTime(next.Duration);
        if (!address.Focused && next.HasVideo) address.Text = next.Url;
        if (next.HasVideo && !string.IsNullOrWhiteSpace(next.Title)) Text = "陪肝小助手 · " + next.Title;
        updatingRate = true;
        string rateText = next.PlaybackRate.ToString("0.##", CultureInfo.InvariantCulture) + "×";
        if (rates.Items.Contains(rateText)) rates.SelectedItem = rateText;
        updatingRate = false;
        overlay?.SetMedia(next, discontinuity);
        if (next.HasVideo)
        {
            var due = timeline.Read(next.CurrentTime, discontinuity);
            if (settings.DanmakuEnabled) overlay?.Add(due);
            if (needsCompact) { needsCompact = false; _ = Command("compact", 1); }
            if (pendingBookmark != null && bookmarkCommitted && next.Duration > 0)
            {
                var bookmark = pendingBookmark; ClearPendingBookmark();
                _ = Command("position", Math.Min(bookmark.Position, Math.Max(0, next.Duration - 1)));
                ShowStatus($"已恢复到 {FormatTime(bookmark.Position)}", 5);
            }
        }
        if (DateTime.UtcNow > statusUntil)
            status.Text = next.HasVideo ? $"{(next.Paused ? "已暂停" : "正在播放")}  ·  {timeline.Count:N0} 条弹幕  ·  {(clickThrough ? "穿透开启 · Ctrl+Alt+R 恢复" : "Ctrl+Alt+Space 播放/暂停")}" : "粘贴网址或打开本地视频 · B 站高清画质由网站登录状态决定";
    }
    private static string FormatTime(double time)
    {
        if (!double.IsFinite(time) || time < 0) return "00:00";
        var t = TimeSpan.FromSeconds(Math.Min(time, 864000)); return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
    }
    private void ShowStatus(string text, int seconds = 5) { if (closing) return; status.Text = text; statusUntil = DateTime.UtcNow.AddSeconds(seconds); }
    private void ApplySettings()
    {
        bool modal = settingsDialog is { IsDisposed: false };
        TopMost = settings.AlwaysOnTop && !modal; Opacity = settings.Opacity;
        pin.Text = "置顶：" + (settings.AlwaysOnTop ? "开" : "关"); danmaku.Text = "弹幕：" + (settings.DanmakuEnabled ? "开" : "关");
        toolbar.Controls[1].Text = "−" + settings.StepSeconds + "秒"; toolbar.Controls[2].Text = "+" + settings.StepSeconds + "秒";
        ApplyOverlay();
        if (overlay != null) { if (settings.DanmakuEnabled && Visible && !modal) overlay.Show(); else overlay.Hide(); }
        if (clickThrough) NativeMethods.SetClickThrough(Handle, !modal);
    }
    private void ApplyOverlay()
    {
        if (overlay == null || closing) return;
        var screens = Screen.AllScreens;
        var screen = settings.ScreenIndex >= 0 && settings.ScreenIndex < screens.Length ? screens[settings.ScreenIndex] : Screen.FromControl(this);
        overlay.Apply(settings, screen);
    }
    private void MaintainCompactTopmost()
    {
        if (closing || !compact || !settings.AlwaysOnTop || !IsHandleCreated || !Enabled
            || WindowState == FormWindowState.Minimized || settingsDialog is { IsDisposed: false }
            || !NativeMethods.IsWindowVisible(Handle) || !NativeMethods.IsWindowEnabled(Handle)) return;
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out uint foregroundProcess);
        // Leave our menus, dialogs and browser interaction in their existing order.
        if (foregroundProcess == Environment.ProcessId) return;
        var visited = new HashSet<IntPtr>();
        for (var above = NativeMethods.GetWindow(Handle, 3); above != IntPtr.Zero && visited.Add(above); above = NativeMethods.GetWindow(above, 3))
        {
            if (!NativeMethods.IsWindowVisible(above) || !NativeMethods.GetWindowRect(above, out var rect)
                || !Bounds.IntersectsWith(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom))) continue;
            NativeMethods.GetWindowThreadProcessId(above, out uint process);
            if (process == Environment.ProcessId) continue;
            // Other topmost windows can cover an already-topmost form. Raise it without activating it.
            NativeMethods.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0213); // no size/move/activation/owner reordering
            return;
        }
    }
    private void ToggleDanmaku()
    {
        settings.DanmakuEnabled = !settings.DanmakuEnabled; ApplySettings();
        ShowStatus(settings.DanmakuEnabled ? "弹幕投影已开启 · 首次加载请保留网页弹幕开关开启" : "弹幕投影已关闭", 8);
    }
    private void ToggleThrough()
    {
        if (!clickThrough && hotkeys?.RecoveryAvailable != true) { ShowStatus("恢复热键被占用，鼠标穿透暂不可用。请释放 Ctrl+Alt+R 后重启。", 10); return; }
        clickThrough = !clickThrough; NativeMethods.SetClickThrough(Handle, clickThrough);
        through.Text = clickThrough ? "穿透：开" : "鼠标穿透";
        ShowStatus(clickThrough ? "鼠标穿透已开启 · Ctrl+Alt+T 关闭，Ctrl+Alt+R 恢复窗口" : "鼠标穿透已关闭", 10);
    }
    private void Recover()
    {
        if (clickThrough) { clickThrough = false; NativeMethods.SetClickThrough(Handle, false); through.Text = "鼠标穿透"; }
        if (FocusSettings()) { ShowStatus("设置窗口已恢复", 5); return; }
        if (compact) _ = ToggleCompact();
        Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate(); ApplySettings(); ShowStatus("窗口已恢复 · 鼠标穿透已关闭", 5);
    }
    private async Task ToggleCompact()
    {
        compact = !compact;
        SuspendLayout();
        if (compact)
        {
            normalWindowState = WindowState;
            normalBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            WindowState = FormWindowState.Normal;
            MinimumSize = Size.Empty;
            FormBorderStyle = FormBorderStyle.None;
            FitCompact(compactWidth);
        }
        else
        {
            compactWidth = ClientSize.Width;
            needsCompact = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(540, 320);
            if (!normalBounds.IsEmpty) Bounds = normalBounds;
            WindowState = normalWindowState;
        }
        layout.RowStyles[0].Height = compact ? 0 : 50;
        layout.RowStyles[1].Height = compact ? 0 : 50;
        layout.RowStyles[3].Height = compact ? 0 : 32;
        layout.GetControlFromPosition(0, 0)!.Visible = !compact;
        toolbar.Visible = status.Visible = !compact;
        compactButton.Text = compact ? "展开窗口" : "跟跑小窗";
        ResumeLayout(true);
        if (clickThrough) NativeMethods.SetClickThrough(Handle, true);
        await Command("compact", compact ? 1 : 0);
    }
    private void FitCompact(int preferredWidth)
    {
        var area = Screen.FromControl(this).WorkingArea;
        double ratio = CompactAspect;
        int frameWidth = Width - ClientSize.Width, frameHeight = Height - ClientSize.Height;
        int width = Math.Clamp(preferredWidth, 160, Math.Max(160, area.Width - frameWidth));
        int height = Math.Max(1, (int)Math.Round(width / ratio));
        if (height > area.Height - frameHeight) { height = Math.Max(1, area.Height - frameHeight); width = Math.Max(1, (int)Math.Round(height * ratio)); }
        ClientSize = new Size(width, height);
        Location = new Point(Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width)), Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }
    private async Task ToggleHide()
    {
        if (Visible) { if (settings.PauseOnHide) await Command("pause"); Hide(); overlay?.Hide(); }
        else Recover();
    }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == MouseHotkeyHook.ActionMessage)
        {
            if (hotkeys?.MouseActionFor(m.WParam.ToInt32(), m.LParam.ToInt32()) is { } mouseAction) _ = RunAction(mouseAction);
            return;
        }
        if (compact && m.Msg == 0x0214 && m.LParam != IntPtr.Zero) // WM_SIZING
        {
            var bounds = Marshal.PtrToStructure<NativeRect>(m.LParam);
            int edge = m.WParam.ToInt32();
            int frameWidth = Width - ClientSize.Width, frameHeight = Height - ClientSize.Height;
            var area = Screen.FromControl(this).WorkingArea;
            double ratio = CompactAspect;
            int maxWidth = Math.Max(1, Math.Min(area.Width - frameWidth, (int)Math.Floor((area.Height - frameHeight) * ratio)));
            int width = edge is 3 or 6 ? (int)Math.Round((bounds.Bottom - bounds.Top - frameHeight) * ratio) : bounds.Right - bounds.Left - frameWidth;
            width = Math.Clamp(width, Math.Min(160, maxWidth), maxWidth);
            int height = Math.Max(1, (int)Math.Round(width / ratio));
            if (edge is 1 or 4 or 7) bounds.Left = bounds.Right - width - frameWidth;
            else bounds.Right = bounds.Left + width + frameWidth;
            if (edge is 3 or 4 or 5) bounds.Top = bounds.Bottom - height - frameHeight;
            else bounds.Bottom = bounds.Top + height + frameHeight;
            Marshal.StructureToPtr(bounds, m.LParam, false);
            m.Result = new IntPtr(1); return;
        }
        // The video is a draggable caption region; keep double-click from maximizing it.
        if (compact && m.Msg == 0x00A3) { m.Result = IntPtr.Zero; return; }
        if (m.Msg == 0x0312 && hotkeys?.ActionFor(m.WParam.ToInt32()) is { } action) { _ = RunAction(action); return; }
        base.WndProc(ref m);
        // The middle of the top border is a grab area; preserve native corner sizing.
        if (compact && !clickThrough && m.Msg == 0x0084 && m.Result.ToInt64() == 12) // WM_NCHITTEST / HTTOP
            m.Result = new IntPtr(2); // HTCAPTION starts native window movement
    }
    private double CompactAspect => Math.Clamp(media.VideoWidth > 0 && media.VideoHeight > 0 ? (double)media.VideoWidth / media.VideoHeight : 16.0 / 9, .2, 5);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    private async Task RunAction(string action)
    {
        if (action != "recover" && settingsDialog is { Visible: true }) { FocusSettings(); return; }
        switch (action)
        {
            case "recover": Recover(); break;
            case "toggle": await Command("toggle"); break;
            case "back": await Command("seek", -settings.StepSeconds); break;
            case "forward": await Command("seek", settings.StepSeconds); break;
            case "slower": await Command("rate", Math.Max(.25, media.PlaybackRate - .25)); break;
            case "faster": await Command("rate", Math.Min(4, media.PlaybackRate + .25)); break;
            case "compact": await ToggleCompact(); break;
            case "hide": await ToggleHide(); break;
            case "clickThrough": ToggleThrough(); break;
            case "danmaku": ToggleDanmaku(); break;
            case "next" or "previous": await Command(action); break;
        }
    }
    private void ShowSettings()
    {
        if (FocusSettings()) return;
        var accelerationWasEnabled = settings.BilibiliAccelerationEnabled;
        using var form = new SettingsForm(settings, candidate =>
        {
            try { return hotkeys != null && !hotkeys.TryApply(candidate.Hotkeys, out string error) ? error : null; }
            finally { hotkeys?.SuspendActions(); }
        }) { TopMost = settings.AlwaysOnTop };
        settingsDialog = form;
        bool saved = false;
        try
        {
            hotkeys?.SuspendActions();
            // The modal owns the topmost layer while the disabled owner stays behind it.
            ApplySettings();
            if (form.ShowDialog(this) == DialogResult.OK) { settings = form.Result; saved = true; }
        }
        finally
        {
            settingsDialog = null; ApplySettings();
            if (hotkeys != null && !hotkeys.TryApply(settings.Hotkeys, out string error))
            { ShowStatus(error, 15); MessageBox.Show(this, error, "快捷键恢复失败", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        if (saved) { SaveState(); ShowStatus(accelerationWasEnabled != settings.BilibiliAccelerationEnabled ? "设置已保存；请退出并重新打开陪肝小助手，使 B站加速开关生效。" : "设置已保存", 12); }
    }
    private async Task ShowAccelerationSettings()
    {
        if (!ready || browser == null || FocusSettings()) return;
        try
        {
            if (compact) await ToggleCompact();
            if (!await browser.OpenAccelerationSettingsAsync())
                ShowStatus("请先打开 B站页面；若已关闭 B站加速，请在设置中启用并重启陪肝小助手。", 12);
        }
        catch (Exception e) { ShowStatus("加速设置暂不可用：" + e.Message, 10); }
    }
    private bool FocusSettings()
    {
        if (settingsDialog is not { IsDisposed: false } dialog) return false;
        if (dialog.Visible)
        {
            if (dialog.WindowState == FormWindowState.Minimized) dialog.WindowState = FormWindowState.Normal;
            dialog.BringToFront(); dialog.Activate();
            var popup = NativeMethods.GetLastActivePopup(dialog.Handle);
            if (popup != dialog.Handle && NativeMethods.IsWindowVisible(popup)) NativeMethods.SetForegroundWindow(popup);
        }
        return true;
    }
    private void ShowMore()
    {
        var menu = new ContextMenuStrip { BackColor = Theme.Surface, ForeColor = Theme.Text };
        menu.Items.Add(settings.DanmakuEnabled ? "关闭弹幕投影" : "开启弹幕投影", null, (_, _) => ToggleDanmaku());
        menu.Items.Add(clickThrough ? "关闭鼠标穿透" : "开启鼠标穿透", null, (_, _) => ToggleThrough());
        menu.Items.Add(settings.AlwaysOnTop ? "取消置顶" : "窗口置顶", null, (_, _) => { settings.AlwaysOnTop = !settings.AlwaysOnTop; ApplySettings(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("保存当前进度", null, (_, _) => { RememberPosition(); SaveState(); ShowStatus("已保存当前进度", 5); });
        var recent = new ToolStripMenuItem("最近视频 / 恢复进度");
        foreach (var b in settings.Bookmarks.Take(20))
        {
            var bookmark = b;
            recent.DropDownItems.Add($"{FormatTime(b.Position)}  {(b.Title.Length > 42 ? b.Title[..42] + "…" : b.Title)}", null, async (_, _) => await Navigate(bookmark.Url, bookmark));
        }
        if (recent.DropDownItems.Count == 0) recent.DropDownItems.Add("播放视频后会自动保存").Enabled = false;
        menu.Items.Add(recent); menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("导入 XML 弹幕…", null, (_, _) => ImportComments());
        menu.Items.Add("恢复网页弹幕来源", null, (_, _) => { imported = false; timeline.Clear(); overlay?.Clear(); web.Reload(); ShowStatus("已切回网页弹幕，正在重新加载", 6); });
        menu.Items.Add("测试弹幕投影", null, (_, _) => { settings.DanmakuEnabled = true; ApplySettings(); overlay?.Preview(); ShowStatus("当前显示的是测试弹幕", 8); });
        menu.Items.Add("设置…", null, (_, _) => ShowSettings());
        menu.Items.Add("B站加速设置…", null, async (_, _) => await ShowAccelerationSettings());
        menu.Items.Add("隐藏窗口", null, async (_, _) => await ToggleHide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("使用说明", null, (_, _) => MessageBox.Show(this,
            "1. 粘贴 B 站网址或 BV 号并打开。\n2. 点击「跟跑小窗」，把视频放在合适位置。\n3. 切回原神，用全局快捷键控制视频。\n4. 点击「弹幕：开」，将弹幕投影到桌面。\n\n默认快捷键\nCtrl+Alt+Space：播放/暂停\nCtrl+Alt+左右：快退/快进\nCtrl+Alt+上下：倍速\nCtrl+Alt+I：小窗\nCtrl+Alt+T：鼠标穿透\nCtrl+Alt+D：弹幕投影\nCtrl+Alt+H：显示/隐藏\nCtrl+Alt+R：恢复窗口（固定）\n\n原神推荐窗口化/无边框模式。独占全屏可能遮住投影。\nB 站画质、登录和弹幕可用性由网站决定。\n本程序不读取游戏内存，不操作游戏。", "陪肝小助手 · 使用说明"));
        // WinForms still uses the menu handle after Closed returns; dispose on the next UI turn.
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(Cursor.Position);
    }
    private void ImportComments()
    {
        if (!media.HasVideo) { ShowStatus("请先打开视频，再导入与它对应的 XML 弹幕。", 8); return; }
        using var dialog = new OpenFileDialog { Title = "导入 B 站 XML 弹幕", Filter = "XML 弹幕|*.xml" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 16 * 1024 * 1024) throw new InvalidDataException("弹幕文件不能超过 16 MiB。");
            var comments = DanmakuParser.ParseXml(File.ReadAllText(dialog.FileName));
            timeline.Clear(); timeline.Add(comments); imported = true; overlay?.Clear();
            settings.DanmakuEnabled = true; ApplySettings(); ShowStatus($"已导入 {comments.Count:N0} 条 XML 弹幕", 8);
        }
        catch (Exception e) when (e is IOException or FormatException or InvalidDataException or UnauthorizedAccessException) { ShowStatus("导入失败：" + e.Message, 10); }
    }
    private void RememberPosition()
    {
        if (!media.HasVideo || !Uri.TryCreate(media.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return;
        settings.Bookmarks.RemoveAll(b => b.Url == media.Url);
        settings.Bookmarks.Insert(0, new Bookmark { Url = media.Url, Title = media.Title, Position = media.CurrentTime, Updated = DateTime.UtcNow });
        settings.Bookmarks = settings.Bookmarks.Take(30).ToList();
    }
    private void SaveState()
    {
        if (closing) return;
        RememberPosition();
        var bounds = compact ? normalBounds : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.Left = bounds.Left; settings.Top = bounds.Top; settings.Width = bounds.Width; settings.Height = bounds.Height;
        try { store.Save(settings); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ShowStatus("设置未能保存：" + e.Message, 10); }
    }
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        SaveState(); closing = true; saveTimer.Stop(); topmostTimer.Stop(); hotkeys?.Dispose(); overlay?.Close(); overlay?.Dispose(); browser?.Dispose(); tray.Visible = false; tray.Dispose(); saveTimer.Dispose(); topmostTimer.Dispose();
    }
}
