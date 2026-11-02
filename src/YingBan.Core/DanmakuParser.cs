using System.Globalization;
using System.Text;
using System.Xml;

namespace YingBan.Core;

public static class DanmakuParser
{
    private const int MaximumInputLength = 16 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<DanmakuComment> ParseProtobuf(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length > MaximumInputLength) throw new FormatException("Danmaku response exceeds 16 MiB.");
        var reader = new ProtoReader(data);
        var comments = new List<DanmakuComment>();
        var elements = 0;
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            if (field != 1) { reader.Skip(wire); continue; }
            RequireWire(wire, 2);
            if (++elements > CommentLimits.MaximumComments) throw new FormatException("Too many danmaku elements.");
            var comment = ParseElement(reader.ReadBytes());
            if (comment is not null) comments.Add(comment);
        }
        return comments;
    }

    public static IReadOnlyList<DanmakuComment> ParseXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        if (xml.Length > MaximumInputLength) throw new FormatException("Danmaku XML exceeds its size limit.");
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumInputLength,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };
        try
        {
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.Name != "i")
                throw new FormatException("Expected Bilibili XML root i.");
            var comments = new List<DanmakuComment>();
            var elements = 0;
            reader.Read();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && reader.Name == "d" && reader.Depth == 1)
                {
                    if (++elements > CommentLimits.MaximumComments) throw new FormatException("Too many danmaku elements.");
                    var metadata = reader.GetAttribute("p");
                    var text = reader.ReadElementContentAsString();
                    var comment = ParseXmlElement(metadata, text);
                    if (comment is not null) comments.Add(comment);
                    continue;
                }
                reader.Read();
            }
            return comments;
        }
        catch (XmlException error) { throw new FormatException("Invalid or unsafe danmaku XML.", error); }
    }

    private static DanmakuComment? ParseXmlElement(string? metadata, string text)
    {
        if (metadata is null || metadata.Length > 1024) return null;
        var fields = metadata.Split(',');
        if (fields.Length < 8 ||
            !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var time) ||
            !int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var mode) ||
            !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var fontSize) ||
            !int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var color)) return null;
        return CommentLimits.Normalize(new DanmakuComment(fields[7], time, mode, color, fontSize, text));
    }

    private static DanmakuComment? ParseElement(ReadOnlySpan<byte> data)
    {
        var reader = new ProtoReader(data);
        ulong id = 0, progress = 0, mode = 0, color = 0, fontSize = 25;
        var text = "";
        var idString = "";
        while (!reader.End)
        {
            var (field, wire) = reader.ReadTag();
            switch (field)
            {
                case 1: RequireWire(wire, 0); id = reader.ReadVarint(); break;
                case 2: RequireWire(wire, 0); progress = reader.ReadVarint(); break;
                case 3: RequireWire(wire, 0); mode = reader.ReadVarint(); break;
                case 4: RequireWire(wire, 0); fontSize = reader.ReadVarint(); break;
                case 5: RequireWire(wire, 0); color = reader.ReadVarint(); break;
                case 7: RequireWire(wire, 2); text = Decode(reader.ReadBytes()); break;
                case 12:
                    RequireWire(wire, 2);
                    var bytes = reader.ReadBytes();
                    if (bytes.Length > CommentLimits.MaximumIdLength * 4) throw new FormatException("Danmaku id is too long.");
                    idString = Decode(bytes);
                    break;
                default: reader.Skip(wire); break;
            }
        }
        if (id > long.MaxValue || progress > 864_000_000 || mode > int.MaxValue ||
            color > 0xFFFFFF || fontSize > 128) return null;
        return CommentLimits.Normalize(new DanmakuComment(
            string.IsNullOrEmpty(idString) ? (id == 0 ? "" : id.ToString(CultureInfo.InvariantCulture)) : idString,
            progress / 1000d, (int)mode, (int)color, (int)fontSize, text));
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        try { return StrictUtf8.GetString(bytes); }
        catch (DecoderFallbackException error) { throw new FormatException("Danmaku contains invalid UTF-8.", error); }
    }

    private static void RequireWire(int actual, int expected)
    {
        if (actual != expected) throw new FormatException("Unexpected protobuf wire type.");
    }

    private ref struct ProtoReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _position;
        public readonly bool End => _position == _data.Length;

        public (int Field, int Wire) ReadTag()
        {
            var tag = ReadVarint();
            if ((tag >> 3) is 0 or > 536_870_911) throw new FormatException("Invalid protobuf tag.");
            return ((int)(tag >> 3), (int)(tag & 7));
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            for (var index = 0; index < 10; index++)
            {
                if (_position >= _data.Length) throw new FormatException("Truncated protobuf varint.");
                var next = _data[_position++];
                if (index == 9 && next > 1) throw new FormatException("Overflowing protobuf varint.");
                result |= (ulong)(next & 0x7F) << (index * 7);
                if ((next & 0x80) == 0) return result;
            }
            throw new FormatException("Overflowing protobuf varint.");
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            var length = ReadVarint();
            if (length > (ulong)(_data.Length - _position)) throw new FormatException("Truncated protobuf field.");
            var bytes = _data.Slice(_position, (int)length);
            _position += (int)length;
            return bytes;
        }

        public void Skip(int wire)
        {
            switch (wire)
            {
                case 0: ReadVarint(); return;
                case 1: SkipFixed(8); return;
                case 2: ReadBytes(); return;
                case 5: SkipFixed(4); return;
                default: throw new FormatException("Unsupported protobuf wire type.");
            }
        }

        private void SkipFixed(int length)
        {
            if (length > _data.Length - _position) throw new FormatException("Truncated fixed protobuf field.");
            _position += length;
        }
    }
}
