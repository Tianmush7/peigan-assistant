namespace YingBan.Core;

public sealed class DanmakuTimeline
{
    private const double LateWindow = 0.5;
    private const int MaximumPerFrame = 32;
    private readonly object _gate = new();
    private readonly List<Entry> _entries = [];
    private readonly HashSet<string> _identities = new(StringComparer.Ordinal);
    private double? _lastTime;

    public int Count { get { lock (_gate) return _entries.Count; } }

    public void Add(IEnumerable<DanmakuComment> comments)
    {
        ArgumentNullException.ThrowIfNull(comments);
        lock (_gate)
        {
            var inspected = 0;
            foreach (var raw in comments)
            {
                if (_entries.Count >= CommentLimits.MaximumComments || ++inspected > CommentLimits.MaximumComments) break;
                var comment = CommentLimits.Normalize(raw);
                if (comment is null) continue;
                var identity = comment.Id.Length > 0 ? "id:" + comment.Id :
                    FormattableString.Invariant($"content:{comment.Time:R}:{comment.Mode}:{comment.Color}:{comment.FontSize}:{comment.Text}");
                if (_identities.Add(identity)) _entries.Add(new Entry(comment));
            }
            _entries.Sort((left, right) => left.Comment.Time.CompareTo(right.Comment.Time));
        }
    }

    /// <summary>
    /// Returns comments due in this playback passage. Call with discontinuity on an explicit seek.
    /// New or resumed timelines show only comments within 0.5 seconds of the playhead.
    /// Dense frames drop overflow rather than queueing it for subsequent frames.
    /// </summary>
    public IReadOnlyList<DanmakuComment> Read(double time, bool discontinuity = false)
    {
        if (!double.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        lock (_gate)
        {
            var jumped = discontinuity || _lastTime is null || time < _lastTime || time - _lastTime > 1;
            if (discontinuity || (_lastTime is not null && time < _lastTime))
                foreach (var entry in _entries) entry.Emitted = false;
            var lower = jumped ? time - LateWindow : _lastTime!.Value - LateWindow;
            var results = new List<DanmakuComment>();
            for (var index = LowerBound(Math.Max(0, lower)); index < _entries.Count; index++)
            {
                var entry = _entries[index];
                if (entry.Comment.Time > time) break;
                if (entry.Emitted) continue;
                entry.Emitted = true;
                // Older arrivals may be caught by the previous read's window, but must
                // still be near the current playhead to avoid delayed bursts.
                if (!jumped && entry.Comment.Time <= _lastTime && time - entry.Comment.Time > LateWindow) continue;
                if (results.Count < MaximumPerFrame) results.Add(entry.Comment);
            }
            _lastTime = time;
            return results;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _identities.Clear();
            _lastTime = null;
        }
    }

    private int LowerBound(double time)
    {
        var left = 0;
        var right = _entries.Count;
        while (left < right)
        {
            var middle = left + (right - left) / 2;
            if (_entries[middle].Comment.Time < time) left = middle + 1;
            else right = middle;
        }
        return left;
    }

    private sealed class Entry(DanmakuComment comment)
    {
        internal DanmakuComment Comment { get; } = comment;
        internal bool Emitted { get; set; }
    }
}
