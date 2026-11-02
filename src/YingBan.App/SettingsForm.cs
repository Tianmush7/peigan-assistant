namespace YingBan.App;

public sealed class SettingsForm : Form
{
    public AppSettings Result { get; }
    private readonly Dictionary<string, HotkeyRecordButton> keyBoxes = [];
    private readonly Dictionary<string, NumericUpDown> numbers = [];
    private readonly CheckBox pauseOnHide;
    private readonly CheckBox bilibiliAcceleration;
    private readonly ComboBox screen;
    private readonly Func<AppSettings, string?> validate;
    public static readonly Dictionary<string, string> ActionLabels = new()
    { ["toggle"] = "播放 / 暂停", ["back"] = "快退", ["forward"] = "快进", ["slower"] = "降低倍速", ["faster"] = "提高倍速", ["compact"] = "跟跑小窗", ["hide"] = "显示 / 隐藏", ["clickThrough"] = "鼠标穿透", ["danmaku"] = "弹幕投影", ["previous"] = "上一集", ["next"] = "下一集" };
    public SettingsForm(AppSettings settings, Func<AppSettings, string?> validate)
    {
        Result = settings.Clone(); this.validate = validate;
        Text = "陪肝小助手设置"; Theme.Apply(this); Size = new Size(670, 740); MinimumSize = new Size(620, 650);
        StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false; MaximizeBox = false;
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(20, 10) };
        var projection = new TabPage("弹幕与播放") { BackColor = Theme.Background, Padding = new Padding(20) };
        var keys = new TabPage("全局快捷键") { BackColor = Theme.Background, Padding = new Padding(20) };
        tabs.TabPages.AddRange([projection, keys]);
        var grid = Grid(); projection.Controls.Add(grid);
        AddNumber(grid, "快进 / 快退秒数", "step", settings.StepSeconds, 1, 120);
        AddNumber(grid, "视频窗口透明度 %", "opacity", (decimal)(settings.Opacity * 100), 25, 100);
        AddNumber(grid, "弹幕字号", "font", settings.FontSize, 16, 64);
        AddNumber(grid, "弹幕持续秒数", "seconds", (decimal)settings.CommentSeconds, 3, 20);
        AddNumber(grid, "弹幕不透明度 %", "commentOpacity", (decimal)(settings.CommentOpacity * 100), 20, 100);
        AddNumber(grid, "投影距离屏幕顶部 %", "top", settings.RegionTop, 0, 80);
        AddNumber(grid, "投影区域高度 %", "height", settings.RegionHeight, 10, 100);
        screen = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270, BackColor = Theme.Surface, ForeColor = Theme.Text };
        screen.Items.Add("自动：跟随主窗口所在显示器");
        for (int i = 0; i < Screen.AllScreens.Length; i++) { var s = Screen.AllScreens[i]; screen.Items.Add($"显示器 {i + 1}  ·  {s.Bounds.Width} × {s.Bounds.Height}"); }
        screen.SelectedIndex = Math.Clamp(settings.ScreenIndex + 1, 0, screen.Items.Count - 1);
        AddRow(grid, "投影显示器", screen);
        pauseOnHide = new CheckBox { Text = "隐藏窗口时暂停视频", Checked = settings.PauseOnHide, AutoSize = true, ForeColor = Theme.Text, Margin = new Padding(0, 14, 0, 10) };
        AddRow(grid, "", pauseOnHide);
        bilibiliAcceleration = new CheckBox { Text = "启用线程撕裂者（重启后生效）", Checked = settings.BilibiliAccelerationEnabled, AutoSize = true, ForeColor = Theme.Text, AccessibleName = "B站视频加速" };
        AddRow(grid, "B站视频加速", bilibiliAcceleration);
        var note = new Label { Text = "投影窗始终鼠标穿透，不抢游戏焦点。\n推荐原神使用窗口化或无边框模式。\nB 站弹幕需页面成功加载；也可导入 XML 弹幕。", AutoSize = true, ForeColor = Theme.Muted, MaximumSize = new Size(500, 0), Margin = new Padding(0, 14, 0, 0) };
        grid.Controls.Add(note); grid.SetColumnSpan(note, 2);
        var keyGrid = Grid(); keys.Controls.Add(keyGrid);
        foreach (var pair in ActionLabels)
        {
            var box = new HotkeyRecordButton { Shortcut = settings.Hotkeys.GetValueOrDefault(pair.Key, ""), AccessibleName = pair.Value + "快捷键" };
            keyBoxes.Add(pair.Key, box); AddRow(keyGrid, pair.Value, box);
        }
        var recovery = new Label { Text = "点击按键框，按键盘组合或鼠标侧键。\n侧键在框内录入；Esc 取消；保存后生效。\n固定恢复键：Ctrl+Alt+R，不可修改。\n设置期间暂停普通热键；重复组合不保存。", AutoSize = true, ForeColor = Theme.Accent, Margin = new Padding(0, 12, 0, 10) };
        keyGrid.Controls.Add(recovery); keyGrid.SetColumnSpan(recovery, 2);
        var defaults = Theme.Button("恢复默认快捷键", (_, _) => { foreach (var pair in AppSettings.DefaultHotkeys()) keyBoxes[pair.Key].Shortcut = pair.Value; }, 165);
        keyGrid.Controls.Add(defaults); keyGrid.SetColumnSpan(defaults, 2);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 62, Padding = new Padding(14, 8, 12, 8), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Surface };
        var save = Theme.Button("保存设置", Save, 100, true);
        var cancel = Theme.Button("取消", (_, _) => Close(), 80);
        footer.Controls.AddRange([save, cancel]); Controls.Add(tabs); Controls.Add(footer);
        AcceptButton = save; CancelButton = cancel;
    }
    private static TableLayoutPanel Grid() => new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(6), ColumnStyles = { new ColumnStyle(SizeType.Absolute, 220), new ColumnStyle(SizeType.Percent, 100) } };
    private static void AddRow(TableLayoutPanel grid, string label, Control value)
    {
        int row = grid.RowCount++; grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        grid.Controls.Add(Theme.Label(label, 210), 0, row); value.Margin = new Padding(0, 3, 0, 3); grid.Controls.Add(value, 1, row);
    }
    private void AddNumber(TableLayoutPanel grid, string text, string name, decimal value, int min, int max)
    {
        var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 110, BackColor = Theme.Surface, ForeColor = Theme.Text, AccessibleName = text };
        numbers.Add(name, n); AddRow(grid, text, n);
    }
    private void Save(object? sender, EventArgs args)
    {
        Result.StepSeconds = (int)numbers["step"].Value; Result.Opacity = (double)numbers["opacity"].Value / 100;
        Result.FontSize = (int)numbers["font"].Value; Result.CommentSeconds = (double)numbers["seconds"].Value;
        Result.CommentOpacity = (double)numbers["commentOpacity"].Value / 100;
        Result.RegionTop = (int)numbers["top"].Value; Result.RegionHeight = (int)numbers["height"].Value;
        if (Result.RegionTop + Result.RegionHeight > 100) { MessageBox.Show(this, "投影顶部与高度之和不能超过 100%。", "检查投影区域"); return; }
        Result.ScreenIndex = screen.SelectedIndex - 1; Result.PauseOnHide = pauseOnHide.Checked;
        Result.BilibiliAccelerationEnabled = bilibiliAcceleration.Checked;
        Result.Hotkeys = keyBoxes.ToDictionary(p => p.Key, p => p.Value.Shortcut);
        var error = validate(Result);
        if (error != null) { MessageBox.Show(this, error, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Result.Normalize(); DialogResult = DialogResult.OK; Close();
    }
}
