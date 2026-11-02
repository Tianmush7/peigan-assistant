using System.Diagnostics;
using System.Drawing.Drawing2D;
using YingBan.Core;

namespace YingBan.App;

public sealed class DanmakuOverlay : Form
{
    private sealed record VisibleComment(DanmakuComment Comment, double Start, float Width, int Lane, int Height, double Duration);
    private readonly List<VisibleComment> active = [];
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    private readonly Stopwatch sinceMedia = Stopwatch.StartNew();
    private AppSettings settings;
    private double mediaTime, rate = 1;
    private bool paused = true;
    private double? previewUntil;
    public int ActiveCount => active.Count;
    public DanmakuOverlay(AppSettings settings)
    {
        this.settings = settings;
        FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
        BackColor = Color.Fuchsia; TransparencyKey = Color.Fuchsia; DoubleBuffered = true;
        Text = "陪肝小助手 · 弹幕投影"; StartPosition = FormStartPosition.Manual;
        timer.Tick += (_, _) =>
        {
            bool changed = active.RemoveAll(c => Playhead - c.Start > c.Duration) > 0;
            if (previewUntil is { } until && Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency > until)
            { previewUntil = null; active.Clear(); paused = true; changed = true; }
            if (changed || active.Count > 0 || !paused) Invalidate();
        };
        timer.Start();
    }
    protected override bool ShowWithoutActivation => true;
    protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow | NativeMethods.WsExLayered; return p; } }
    public void Apply(AppSettings value, Screen screen)
    {
        settings = value; Opacity = value.CommentOpacity;
        Bounds = new Rectangle(screen.Bounds.Left, screen.Bounds.Top + screen.Bounds.Height * value.RegionTop / 100, screen.Bounds.Width, screen.Bounds.Height * value.RegionHeight / 100);
        active.Clear(); Invalidate();
    }
    private double Playhead => mediaTime + (paused ? 0 : Math.Min(sinceMedia.Elapsed.TotalSeconds, previewUntil != null ? 30 : 1) * rate);
    public void SetMedia(MediaState state, bool discontinuity)
    {
        if (previewUntil != null) return;
        if (discontinuity || !state.HasVideo) Clear();
        mediaTime = state.CurrentTime; paused = state.Paused || !state.HasVideo; rate = state.PlaybackRate; sinceMedia.Restart();
        Invalidate();
    }
    public void Clear() { active.Clear(); Invalidate(); }
    public void Add(IEnumerable<DanmakuComment> comments)
    {
        if (!Visible) return;
        using var graphics = CreateGraphics();
        using var font = new Font("Microsoft YaHei UI", settings.FontSize * DeviceDpi / 96f, FontStyle.Bold, GraphicsUnit.Pixel);
        int rowHeight = (int)Math.Ceiling(font.GetHeight(graphics)) + 12;
        int rows = Math.Max(1, Height / rowHeight);
        double now = Playhead;
        foreach (var comment in comments.Take(32))
        {
            if (active.Count >= 80 || now - comment.Time > settings.CommentSeconds) break;
            var textWidth = graphics.MeasureString(comment.Text, font).Width + 16;
            int lane = -1;
            IEnumerable<int> candidates = comment.Mode == 4 ? Enumerable.Range(0, rows).Reverse() : Enumerable.Range(0, rows);
            foreach (int row in candidates)
            {
                bool free = true;
                foreach (var old in active.Where(c => c.Lane == row && now - c.Start < c.Duration))
                {
                    if (comment.Mode is 4 or 5 || old.Comment.Mode != comment.Mode || old.Comment.Mode is 4 or 5) { free = false; break; }
                    double elapsed = now - old.Start;
                    double oldSpeed = (Width + old.Width) / old.Duration;
                    double newSpeed = (Width + textWidth) / settings.CommentSeconds;
                    double gap = oldSpeed * elapsed - old.Width - 30;
                    if (gap < 0 || (newSpeed > oldSpeed && gap / (newSpeed - oldSpeed) < old.Duration - elapsed)) { free = false; break; }
                }
                if (free) { lane = row; break; }
            }
            if (lane < 0) continue;
            active.Add(new(comment, now, textWidth, lane, rowHeight, settings.CommentSeconds));
        }
        Invalidate();
    }
    public void Preview()
    {
        Clear(); mediaTime = 0; paused = false; rate = 1; sinceMedia.Restart(); previewUntil = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency + settings.CommentSeconds;
        Add([new("test1", 0, 1, 0xFFFFFF, settings.FontSize, "陪肝小助手 · 弹幕投影测试"), new("test2", 0, 5, 0x59DEB9, settings.FontSize, "测试弹幕：鼠标可以直接穿过"), new("test3", 0, 4, 0xF0C987, settings.FontSize, "测试弹幕：播放真实视频后与进度同步")]);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float size = settings.FontSize * DeviceDpi / 96f;
        using var font = new Font("Microsoft YaHei UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
        double now = Playhead;
        foreach (var c in active)
        {
            double age = Math.Max(0, now - c.Start); if (age > c.Duration) continue;
            float x = c.Comment.Mode switch { 4 or 5 => (Width - c.Width) / 2, 6 => (float)(-c.Width + (Width + c.Width) * age / c.Duration), _ => (float)(Width - (Width + c.Width) * age / c.Duration) };
            float y = c.Lane * c.Height + 4;
            using var path = new GraphicsPath();
            path.AddString(c.Comment.Text, font.FontFamily, (int)FontStyle.Bold, size, new PointF(x, y), StringFormat.GenericTypographic);
            using var outline = new Pen(Color.FromArgb(230, 10, 15, 22), 3) { LineJoin = LineJoin.Round };
            using var fill = new SolidBrush(Color.FromArgb(255, (c.Comment.Color >> 16) & 255, (c.Comment.Color >> 8) & 255, c.Comment.Color & 255));
            e.Graphics.DrawPath(outline, path); e.Graphics.FillPath(fill, path);
        }
    }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
