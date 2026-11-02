using System.Text;
using YingBan.Core;

var tests = new (string Name, Action Run)[]
{
    ("protobuf maps real fields and Chinese", () => {
        // Hand-checked protocol fixture: numeric id 123, progress 1250 ms, mode 1,
        // font size 25, color 0xFF0000, content 中文, string id 456.
        var bytes = Convert.FromHexString("0A1B087B10E20918012019288080FC073A06E4B8ADE696876203343536");
        var item = Single(DanmakuParser.ParseProtobuf(bytes));
        Equal(new DanmakuComment("456", 1.25, 1, 0xFF0000, 25, "中文"), item);
    }),
    ("protobuf numeric id fallback and supported modes", () => {
        foreach (var mode in new[] {1, 4, 5, 6}) {
            var item = Single(DanmakuParser.ParseProtobuf(Reply(Element(7, 2500, mode, "test"))));
            Equal("7", item.Id); Equal(2.5, item.Time); Equal(mode, item.Mode);
        }
        Equal(0, DanmakuParser.ParseProtobuf(Reply(Element(8, 0, 7, "advanced"))).Count);
    }),
    ("protobuf skips unknown fields without losing comments", () => {
        byte[] unknown = [0x50, 0x01, 0x59, 1, 2, 3, 4, 5, 6, 7, 8, 0x62, 0x02, 1, 2, 0x6D, 1, 2, 3, 4];
        Equal("ok", Single(DanmakuParser.ParseProtobuf(unknown.Concat(Reply(Element(3, 0, 1, "ok"))).ToArray())).Text);
    }),
    ("protobuf rejects truncated or overflowing data", () => {
        foreach (var bytes in new byte[][] {
            [0x0A, 0x7F, 1], [0x0A, 0x02, 0x08, 0x80], [0], [0x0F],
            [0x0A, 0x02, 0x38, 1],
            [0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 2]
        }) Throws<FormatException>(() => DanmakuParser.ParseProtobuf(bytes));
    }),
    ("protobuf rejects invalid UTF8", () => {
        Throws<FormatException>(() => DanmakuParser.ParseProtobuf([0x0A, 0x07, 0x18, 1, 0x3A, 0x03, 0xFF, 0xFE, 0xFF]));
    }),
    ("parser bounds oversized input and text", () => {
        Throws<FormatException>(() => DanmakuParser.ParseProtobuf(new byte[16 * 1024 * 1024 + 1]));
        Throws<FormatException>(() => DanmakuParser.ParseXml(new string('x', 16 * 1024 * 1024 + 1)));
        Equal(512, Single(DanmakuParser.ParseProtobuf(Reply(Element(1, 0, 1, new string('界', 900))))).Text.Length);
        Equal(512, Single(DanmakuParser.ParseXml(Xml("0,1,25,16777215,0,0,u,1", new string('界', 900)))).Text.Length);
    }),
    ("xml maps metadata using invariant decimals", () => {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
            Equal(new DanmakuComment("99", 3.75, 5, 16711935, 36, "中文 & test"),
                Single(DanmakuParser.ParseXml(Xml("3.75,5,36,16711935,0,0,user,99", "中文 &amp; test"))));
        } finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }),
    ("xml rejects DTD external entities and malformed documents", () => {
        Throws<FormatException>(() => DanmakuParser.ParseXml("<!DOCTYPE i [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><i><d p='0,1,25,1,0,0,u,1'>&x;</d></i>"));
        Throws<FormatException>(() => DanmakuParser.ParseXml("<i><d>broken"));
        Throws<FormatException>(() => DanmakuParser.ParseXml("<html/>"));
    }),
    ("xml skips unsupported or invalid comments", () => {
        Equal(0, DanmakuParser.ParseXml("<i><d p='0,7,25,1,0,0,u,1'>advanced</d><d p='NaN,1,25,1,0,0,u,2'>bad</d><d p='-1,1,25,1,0,0,u,3'>bad</d><d p='0,1,25,1,0,0,u,4'></d><d p='bad'>bad</d></i>").Count);
    }),
    ("timeline deduplicates duplicate segments and emits due once", () => {
        var timeline = new DanmakuTimeline();
        var segment = new[] { Comment("later", 1), Comment("early", 0.1) };
        timeline.Add(segment); timeline.Add(segment); Equal(2, timeline.Count);
        Equal(0, timeline.Read(0).Count); Equal("early", Single(timeline.Read(0.1)).Id);
        Equal(0, timeline.Read(0.1).Count); Equal("later", Single(timeline.Read(1)).Id);
        Equal(0, timeline.Read(1).Count);
    }),
    ("timeline seeks suppress backlog and replay after backwards seek", () => {
        var timeline = new DanmakuTimeline();
        timeline.Add(new[] { Comment("start", 1), Comment("near", 9.8), Comment("future", 10.5) });
        timeline.Read(0); Equal("start", Single(timeline.Read(1)).Id);
        Equal("near", Single(timeline.Read(10, true)).Id);
        Equal("future", Single(timeline.Read(10.5)).Id);
        Equal(0, timeline.Read(0, true).Count);
        Equal("start", Single(timeline.Read(1)).Id);
    }),
    ("timeline automatically detects clock jumps", () => {
        var timeline = new DanmakuTimeline();
        timeline.Add(new[] { Comment("old", 1), Comment("near", 99.9) });
        timeline.Read(0); Equal("near", Single(timeline.Read(100)).Id);
        Equal(0, timeline.Read(0).Count); Equal("old", Single(timeline.Read(1)).Id);
    }),
    ("timeline late near-current segment emits once and stale comments stay hidden", () => {
        var timeline = new DanmakuTimeline(); timeline.Read(10);
        timeline.Add(new[] { Comment("stale", 3), Comment("near", 9.9), Comment("future", 10.2) });
        Equal("near", Single(timeline.Read(10)).Id);
        Equal(0, timeline.Read(10).Count);
        Equal("future", Single(timeline.Read(10.2)).Id);
    }),
    ("timeline limits dense frame without delayed bursts", () => {
        var timeline = new DanmakuTimeline(); timeline.Read(0);
        timeline.Add(Enumerable.Range(0, 1000).Select(i => Comment(i.ToString(), 0.1)));
        var due = timeline.Read(0.1); True(due.Count > 0 && due.Count <= 32, "Frame must be capped");
        Equal(0, timeline.Read(0.2).Count);
    }),
    ("timeline late comments expire against current playhead", () => {
        var timeline = new DanmakuTimeline(); timeline.Read(10);
        timeline.Add(new[] {Comment("expired", 9.7), Comment("on-time", 10.4)});
        Equal("on-time", Single(timeline.Read(10.8)).Id);
        Equal(0, timeline.Read(10.8).Count);
    }),
    ("timeline rejects invalid entries and clear resets playback", () => {
        var timeline = new DanmakuTimeline();
        timeline.Add(new[] { Comment("good", 0), Comment("nan", double.NaN), Comment("negative", -1), Comment("unsupported", 0) with {Mode = 7} });
        Equal(1, timeline.Count); Equal("good", Single(timeline.Read(0)).Id);
        Throws<ArgumentOutOfRangeException>(() => timeline.Read(double.PositiveInfinity));
        timeline.Clear(); Equal(0, timeline.Count); Equal(0, timeline.Read(0).Count);
        timeline.Add(new[] {Comment("good", 0)}); Equal("good", Single(timeline.Read(0)).Id);
    }),
};

var failures = 0;
foreach (var test in tests) {
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {test.Name}: {error.GetType().Name}: {error.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed.");
return failures == 0 ? 0 : 1;

static DanmakuComment Comment(string id, double time) => new(id, time, 1, 0xFFFFFF, 25, id);
static string Xml(string metadata, string text) => $"<i><d p='{metadata}'>{text}</d></i>";
static T Single<T>(IReadOnlyList<T> values) { Equal(1, values.Count); return values[0]; }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Throws<T>(Action action) where T : Exception {
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
static byte[] Varint(ulong value) {
    var bytes = new List<byte>();
    do { var next = (byte)(value & 0x7F); value >>= 7; bytes.Add(value == 0 ? next : (byte)(next | 0x80)); } while (value != 0);
    return bytes.ToArray();
}
static byte[] Element(ulong id, ulong milliseconds, int mode, string text) {
    var encoded = Encoding.UTF8.GetBytes(text);
    return new byte[] {8}.Concat(Varint(id)).Concat(new byte[] {16}).Concat(Varint(milliseconds))
        .Concat(new byte[] {24, (byte)mode, 32, 25, 40, 0xFF, 0xFF, 0xFF, 7, 58})
        .Concat(Varint((ulong)encoded.Length)).Concat(encoded).ToArray();
}
static byte[] Reply(byte[] element) => new byte[] {10}.Concat(Varint((ulong)element.Length)).Concat(element).ToArray();
