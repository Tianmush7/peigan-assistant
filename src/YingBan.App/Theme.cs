namespace YingBan.App;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(16, 21, 30), Surface = Color.FromArgb(25, 33, 45), Border = Color.FromArgb(45, 58, 73), Text = Color.FromArgb(225, 234, 242), Muted = Color.FromArgb(149, 169, 186), Accent = Color.FromArgb(89, 222, 185);
    public static Button Button(string text, EventHandler? click = null, int width = 80, bool accent = false)
    {
        var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(width, 36), Margin = new Padding(3, 4, 3, 4), FlatStyle = FlatStyle.Flat, BackColor = accent ? Accent : Surface, ForeColor = accent ? Background : Text, Font = new Font("Microsoft YaHei UI", 9), Cursor = Cursors.Hand, TabStop = true, AccessibleName = text };
        b.FlatAppearance.BorderSize = 0; b.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(118, 238, 207) : Border;
        if (click != null) b.Click += click; return b;
    }
    public static Label Label(string text, int width = 140) => new() { Text = text, ForeColor = Muted, Width = width, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Microsoft YaHei UI", 9) };
    public static void Apply(Control control)
    {
        control.BackColor = Background; control.ForeColor = Text; control.Font = new Font("Microsoft YaHei UI", 9);
    }
}
