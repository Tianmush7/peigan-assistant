using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using YingBan.Core;

namespace YingBan.App;

public record MediaState(string Url, string Title, double CurrentTime, double Duration, bool Paused, double PlaybackRate, bool HasVideo, int VideoWidth = 0, int VideoHeight = 0);

/// <summary>Observes only resources already requested by the browser. It exposes no native objects to pages.</summary>
public sealed class BrowserAdapter : IDisposable
{
    private const string AssetHost = "app.yingban.local";
    private const int MaxResponseBytes = 8 * 1024 * 1024;
    private readonly WebView2 _view;
    private readonly List<(string Cid, byte[] Data, bool Xml)> _pending = [];
    private readonly HashSet<string> _received = [];
    private readonly Dictionary<string, TaskCompletionSource<bool>> _commands = [];
    private string _cid = "", _identity = "", _source = "", _localHost = "", _localUrl = "", _localFile = "", _scriptId = "";
    private readonly List<FileRangeStream> _localStreams = [];
    private int _generation, _pendingBytes, _inflight;
    private bool _disposed, _initialized;
    private string _accelerationScriptId = "";
    private long _lastMessage;

    public event Action<MediaState>? MediaChanged;
    public event Action<IReadOnlyList<DanmakuComment>>? CommentsReceived;
    public event Action? ContentChanged;
    public event Action<string>? StatusChanged;

    public BrowserAdapter(WebView2 view) => _view = view ?? throw new ArgumentNullException(nameof(view));

    public async Task InitializeAsync(string profileDirectory, bool bilibiliAccelerationEnabled = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) return;
        Directory.CreateDirectory(profileDirectory);
        var environment = await CoreWebView2Environment.CreateAsync(null, profileDirectory);
        await _view.EnsureCoreWebView2Async(environment);
        var core = _view.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.IsWebMessageEnabled = true;
        core.Settings.IsNonClientRegionSupportEnabled = true;
        core.SetVirtualHostNameToFolderMapping(AssetHost, Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.Deny);
        core.NavigationStarting += NavigationStarting;
        core.NavigationCompleted += NavigationCompleted;
        core.SourceChanged += SourceChanged;
        core.WebMessageReceived += WebMessageReceived;
        core.WebResourceResponseReceived += ResponseReceived;
        core.WebResourceRequested += LocalResourceRequested;
        core.NewWindowRequested += NewWindowRequested;
        core.AddWebResourceRequestedFilter("https://*.yingban.local/*", CoreWebView2WebResourceContext.All);
        if (bilibiliAccelerationEnabled)
        {
            var accelerator = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "vendor", "btr", "bilibili-thread-ripper.user.js"));
            // WebView2 does not enforce userscript metadata. Restrict execution explicitly.
            // The pinned upstream runs in the page and needs no native host access.
            _accelerationScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "(()=>{if(location.protocol!=='https:' || !(location.hostname==='bilibili.com' || location.hostname.endsWith('.bilibili.com')) || window.top!==window)return;\n" + accelerator + "\n})();");
        }
        var bridgeScript = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "bridge.js"));
        var compactCss = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "compact.css"));
        _scriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync($"({bridgeScript})({JsonSerializer.Serialize(compactCss)});");
        _initialized = true;
        await ShowHomeAsync();
    }

    public async Task<bool> OpenAccelerationSettingsAsync()
    {
        EnsureReady();
        if (_accelerationScriptId.Length == 0) return false;
        // Check in the same execution too, in case navigation changes the document.
        return await _view.CoreWebView2.ExecuteScriptAsync("(()=>{if(location.protocol!=='https:' || !(location.hostname==='bilibili.com' || location.hostname.endsWith('.bilibili.com')) || !window.__BTR_SETTINGS_PANEL__)return false;window.__BTR_SETTINGS_PANEL__.open();return window.__BTR_SETTINGS_PANEL__.isOpen();})()") == "true";
    }

    public Task NavigateAsync(string input)
    {
        EnsureReady();
        var text = input.Trim();
        if (Regex.IsMatch(text, "^BV[0-9A-Za-z]{10}$", RegexOptions.CultureInvariant)) text = "https://www.bilibili.com/video/" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !IsWeb(uri) || uri.UserInfo.Length != 0 || text.Length > 4096 || uri.Host.EndsWith(".yingban.local", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请输入 http / https 网址或完整 BV 号。", nameof(input));
        ClearLocalMapping();
        _view.CoreWebView2.Navigate(uri.AbsoluteUri);
        return Task.CompletedTask;
    }

    public async Task CommandAsync(string command, double value = 0)
    {
        EnsureReady();
        if (!double.IsFinite(value) || command is not ("toggle" or "pause" or "play" or "seek" or "position" or "rate" or "compact" or "next" or "previous"))
            throw new ArgumentException("不支持的播放命令。");
        var id = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands.Add(id, completion);
        try
        {
            // ExecuteScriptAsync does not await JavaScript promises. A bounded acknowledgment
            // reports completion only; it never permits a page to initiate a native operation.
            await _view.CoreWebView2.ExecuteScriptAsync($"(()=>{{const report=ok=>window.chrome.webview.postMessage({{kind:'command-result',id:{JsonSerializer.Serialize(id)},ok:Boolean(ok)}});Promise.resolve(window.__yingbanBridge?.command({JsonSerializer.Serialize(command)},{JsonSerializer.Serialize(value)})??false).then(report).catch(()=>report(false));}})()");
            if (!await completion.Task.WaitAsync(TimeSpan.FromSeconds(5))) Publish(StatusChanged, "当前页面没有可用的播放器或该操作暂不可用。");
        }
        catch (TimeoutException) { Publish(StatusChanged, "播放器没有响应。页面加载后可重试。"); }
        finally { _commands.Remove(id); }
    }

    public Task OpenLocalAsync(string file)
    {
        EnsureReady();
        var full = Path.GetFullPath(file);
        if (!File.Exists(full)) throw new FileNotFoundException("找不到本地视频。", full);
        if (!new[] { ".mp4", ".m4v", ".webm", ".mov", ".mkv", ".avi", ".wmv", ".wav", ".mp3", ".ogg" }.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("请选择视频或音频文件；实际格式支持由 WebView2 决定。", nameof(file));
        ClearLocalMapping();
        _localHost = "media-" + Guid.NewGuid().ToString("N") + ".yingban.local";
        _localUrl = "https://" + _localHost + "/" + Uri.EscapeDataString(Path.GetFileName(full));
        _localFile = full;
        _view.CoreWebView2.Navigate($"https://{AssetHost}/local-player.html?media={Uri.EscapeDataString(_localUrl)}&title={Uri.EscapeDataString(Path.GetFileName(full))}");
        return Task.CompletedTask;
    }

    public Task ShowHomeAsync()
    {
        EnsureReady();
        ClearLocalMapping();
        _view.CoreWebView2.Navigate($"https://{AssetHost}/home.html");
        return Task.CompletedTask;
    }

    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !IsWeb(uri) || uri.UserInfo.Length != 0 ||
            (uri.Host.EndsWith(".yingban.local", StringComparison.OrdinalIgnoreCase) && (uri.Host != AssetHost || uri.AbsolutePath is not ("/home.html" or "/local-player.html"))))
        { e.Cancel = true; Publish(StatusChanged, "已阻止不支持的跳转。请通过工具栏打开网址或本地文件。"); return; }
        ResetContent();
        Publish(StatusChanged, "正在加载页面…");
    }

    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) Publish(StatusChanged, $"页面加载失败：{e.WebErrorStatus}。可重新打开网址。");
        else Publish(StatusChanged, "页面已加载；等待视频和页面弹幕。");
    }

    private void SourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var source = _view.CoreWebView2.Source;
        if (_source != source) { _source = source; ResetContent(); }
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Host != AssetHost) ClearLocalMapping();
    }

    private void NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && IsWeb(uri) && uri.UserInfo.Length == 0 && !uri.Host.EndsWith(".yingban.local", StringComparison.OrdinalIgnoreCase))
            _view.CoreWebView2.Navigate(uri.AbsoluteUri);
        else Publish(StatusChanged, "已阻止不支持的新窗口链接。");
    }

    private void LocalResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || !uri.Host.StartsWith("media-", StringComparison.OrdinalIgnoreCase) || !uri.Host.EndsWith(".yingban.local", StringComparison.OrdinalIgnoreCase)) return;
        var core = _view.CoreWebView2;
        if (e.Request.Uri != _localUrl || _localFile.Length == 0 || !Uri.TryCreate(core.Source, UriKind.Absolute, out var source) || source.Host != AssetHost || source.AbsolutePath != "/local-player.html")
        { e.Response = core.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Length: 0"); return; }
        if (e.Request.Method is not ("GET" or "HEAD"))
        { e.Response = core.Environment.CreateWebResourceResponse(null, 405, "Method Not Allowed", "Allow: GET, HEAD\r\nContent-Length: 0"); return; }
        try
        {
            // Only the explicitly selected file is opened. No directory mapping exposes sibling files.
            var file = new FileStream(_localFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var length = file.Length;
            var partial = e.Request.Headers.Contains("Range");
            var start = 0L; var end = length - 1;
            if (partial && !TryRange(e.Request.Headers.GetHeader("Range"), length, out start, out end))
            { file.Dispose(); e.Response = core.Environment.CreateWebResourceResponse(null, 416, "Range Not Satisfiable", $"Content-Range: bytes */{length}\r\nContent-Length: 0\r\nAccess-Control-Allow-Origin: https://{AssetHost}\r\nAccess-Control-Expose-Headers: Content-Range"); return; }
            var count = Math.Max(0, end - start + 1);
            var headers = $"Content-Type: {MediaType(Path.GetExtension(_localFile))}\r\nContent-Length: {count}\r\nAccept-Ranges: bytes\r\nAccess-Control-Allow-Origin: https://{AssetHost}\r\nAccess-Control-Expose-Headers: Content-Range, Accept-Ranges\r\nCache-Control: no-store";
            if (partial) headers += $"\r\nContent-Range: bytes {start}-{end}/{length}";
            Stream? body = null;
            if (e.Request.Method == "HEAD") file.Dispose();
            else
            {
                for (var i = _localStreams.Count - 1; i >= 0; i--) if (_localStreams[i].Finished) { _localStreams[i].Dispose(); _localStreams.RemoveAt(i); }
                var range = new FileRangeStream(file, start, count); _localStreams.Add(range); body = range;
            }
            e.Response = core.Environment.CreateWebResourceResponse(body, partial ? 206 : 200, partial ? "Partial Content" : "OK", headers);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { e.Response = core.Environment.CreateWebResourceResponse(null, 404, "Not Found", "Content-Length: 0"); Publish(StatusChanged, "本地文件无法读取，可能已移动或无访问权限。"); }
    }

    internal static bool TryRange(string header, long length, out long start, out long end)
    {
        start = 0; end = length - 1;
        if (length <= 0 || !header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || header.Contains(',')) return false;
        var parts = header[6..].Split('-', 2);
        if (parts.Length != 2) return false;
        if (parts[0].Length == 0)
        {
            if (!long.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var suffix) || suffix <= 0) return false;
            start = Math.Max(0, length - suffix); return true;
        }
        if (!long.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out start) || start >= length) return false;
        if (parts[1].Length == 0) return true;
        if (!long.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out end) || end < start) return false;
        end = Math.Min(end, length - 1); return true;
    }
    private static string MediaType(string extension) => extension.ToLowerInvariant() switch
    { ".mp4" or ".m4v" => "video/mp4", ".webm" => "video/webm", ".mov" => "video/quicktime", ".mkv" => "video/x-matroska", ".avi" => "video/x-msvideo", ".wmv" => "video/x-ms-wmv", ".wav" => "audio/wav", ".mp3" => "audio/mpeg", ".ogg" => "audio/ogg", _ => "application/octet-stream" };

    private sealed class FileRangeStream(FileStream file, long offset, long count) : Stream
    {
        private readonly object _lock = new();
        private long _position;
        private bool _closed;
        public bool Finished { get { lock (_lock) return _closed || _position >= count; } }
        public override bool CanRead => !_closed;
        public override bool CanSeek => !_closed;
        public override bool CanWrite => false;
        public override long Length => count;
        public override long Position { get { lock (_lock) return _position; } set => Seek(value, SeekOrigin.Begin); }
        public override int Read(byte[] buffer, int bufferOffset, int size)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                if (_position >= count) return 0;
                file.Position = offset + _position;
                var read = file.Read(buffer, bufferOffset, (int)Math.Min(size, count - _position));
                _position += read; return read;
            }
        }
        public override long Seek(long value, SeekOrigin origin)
        {
            lock (_lock)
            {
                ObjectDisposedException.ThrowIf(_closed, this);
                var position = checked(origin switch { SeekOrigin.Begin => value, SeekOrigin.Current => _position + value, SeekOrigin.End => count + value, _ => throw new ArgumentException(nameof(origin)) });
                if (position < 0) throw new IOException("Negative stream position.");
                return _position = position;
            }
        }
        protected override void Dispose(bool disposing) { lock (_lock) { if (!_closed && disposing) file.Dispose(); _closed = true; } base.Dispose(disposing); }
        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private void WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_disposed || e.Source != _view.CoreWebView2.Source) return;
        try
        {
            var json = e.WebMessageAsJson;
            if (json.Length > 16384) return;
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            var kind = Text(root, "kind", 16);
            if (kind == "command-result")
            {
                var id = Text(root, "id", 32);
                if (_commands.TryGetValue(id, out var command)) command.TrySetResult(root.GetProperty("ok").GetBoolean());
                return;
            }
            var now = Environment.TickCount64;
            if (now - _lastMessage < 40 || kind != "media" || Text(root, "url", 4096) != e.Source) return;
            var time = Number(root, "time", 0, 604800);
            var duration = Number(root, "duration", 0, 604800);
            var rate = Number(root, "rate", 0.25, 4);
            var title = Text(root, "title", 512);
            var paused = root.GetProperty("paused").GetBoolean();
            var hasVideo = root.GetProperty("hasVideo").GetBoolean();
            var videoWidth = root.TryGetProperty("videoWidth", out _) ? (int)Number(root, "videoWidth", 0, 16384) : 0;
            var videoHeight = root.TryGetProperty("videoHeight", out _) ? (int)Number(root, "videoHeight", 0, 16384) : 0;
            var cid = Text(root, "cid", 20);
            if (cid.Length != 0 && !Regex.IsMatch(cid, "^[0-9]{1,20}$", RegexOptions.CultureInvariant)) return;
            if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var page) || !IsBilibili(page.Host)) cid = "";
            var src = Text(root, "src", 4096);
            var identity = e.Source + "\n" + cid + "\n" + (cid.Length == 0 ? src : "");
            _lastMessage = now;
            if (hasVideo && identity != _identity)
            {
                var firstCid = _cid.Length == 0 && cid.Length > 0;
                if (!firstCid && _identity.Length > 0) { _generation++; _received.Clear(); _pending.Clear(); _pendingBytes = 0; }
                _identity = identity; _cid = cid; Publish(ContentChanged);
                if (firstCid) FlushPending();
            }
            Publish(MediaChanged, new MediaState(e.Source, title, time, duration, paused, rate, hasVideo, videoWidth, videoHeight));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException) { /* Untrusted page messages are ignored. */ }
    }

    private async void ResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (_disposed || _inflight >= 8 || !Uri.TryCreate(_view.CoreWebView2.Source, UriKind.Absolute, out var page) || !IsBilibili(page.Host) ||
            !TryCommentResource(e.Request.Uri, out var cid, out var xml) || e.Response.StatusCode != 200 || (_cid.Length > 0 && cid != _cid)) return;
        var generation = _generation;
        var key = e.Request.Uri;
        if (_received.Contains(key)) return;
        _inflight++;
        try
        {
            if (e.Response.Headers.Contains("Content-Length") && long.TryParse(e.Response.Headers.GetHeader("Content-Length"), out var length) && length > MaxResponseBytes) return;
            using var stream = await e.Response.GetContentAsync();
            if (stream is null) return;
            using var buffer = new MemoryStream();
            var block = new byte[32768];
            int count;
            while ((count = await stream.ReadAsync(block)) != 0)
            {
                if (buffer.Length + count > MaxResponseBytes) { Publish(StatusChanged, "页面弹幕响应过大，已忽略。"); return; }
                buffer.Write(block, 0, count);
            }
            if (_disposed || generation != _generation || (_cid.Length > 0 && cid != _cid)) return;
            var data = buffer.ToArray();
            if (_received.Count > 2048) _received.Clear();
            _received.Add(key);
            if (_cid.Length == 0)
            {
                if (_pending.Count < 12 && _pendingBytes + data.Length <= MaxResponseBytes) { _pending.Add((cid, data, xml)); _pendingBytes += data.Length; }
            }
            else Decode(data, xml);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        { if (!_disposed && generation == _generation) Publish(StatusChanged, "当前弹幕响应无法读取；视频仍可播放。"); }
        finally { _inflight--; }
    }

    private void Decode(byte[] bytes, bool xml)
    {
        try
        {
            var comments = xml ? DanmakuParser.ParseXml(Encoding.UTF8.GetString(bytes)) : DanmakuParser.ParseProtobuf(bytes);
            if (comments.Count > 0) { Publish(CommentsReceived, comments); Publish(StatusChanged, $"已接收页面弹幕：{comments.Count} 条。"); }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidDataException or System.Xml.XmlException or OverflowException)
        { Publish(StatusChanged, "页面弹幕格式无法解析；视频仍可播放。"); }
    }

    private void FlushPending()
    {
        var pending = _pending.Where(x => x.Cid == _cid).ToArray(); _pending.Clear(); _pendingBytes = 0;
        foreach (var item in pending) Decode(item.Data, item.Xml);
    }

    internal static bool TryCommentResource(string url, out string cid, out bool xml)
    {
        cid = ""; xml = false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !IsWeb(uri)) return false;
        if (uri.Host == "api.bilibili.com" && uri.AbsolutePath is "/x/v2/dm/web/seg.so" or "/x/v2/dm/wbi/web/seg.so" or "/x/v1/dm/list.so")
        {
            foreach (var field in uri.Query.TrimStart('?').Split('&'))
            {
                var pair = field.Split('=', 2);
                if (pair.Length == 2 && pair[0] == "oid") cid = Uri.UnescapeDataString(pair[1]);
            }
            xml = uri.AbsolutePath.EndsWith("list.so", StringComparison.Ordinal);
        }
        else if (uri.Host == "comment.bilibili.com" && uri.AbsolutePath.EndsWith(".xml", StringComparison.Ordinal))
        { cid = Path.GetFileNameWithoutExtension(uri.AbsolutePath); xml = true; }
        return Regex.IsMatch(cid, "^[0-9]{1,20}$", RegexOptions.CultureInvariant);
    }

    private static string Text(JsonElement root, string name, int max)
    {
        var value = root.GetProperty(name).GetString() ?? throw new FormatException();
        if (value.Length > max) throw new FormatException();
        return value;
    }
    private static double Number(JsonElement root, string name, double min, double max)
    {
        var value = root.GetProperty(name).GetDouble();
        if (!double.IsFinite(value) || value < min || value > max) throw new FormatException();
        return value;
    }
    private static bool IsWeb(Uri uri) => uri.Scheme is "http" or "https";
    private static bool IsBilibili(string host) => host == "bilibili.com" || host.EndsWith(".bilibili.com", StringComparison.OrdinalIgnoreCase);
    private void EnsureReady() { ObjectDisposedException.ThrowIf(_disposed, this); if (!_initialized) throw new InvalidOperationException("浏览器尚未初始化。"); }
    private void ResetContent() { _generation++; _cid = ""; _identity = ""; _pending.Clear(); _received.Clear(); _pendingBytes = 0; _lastMessage = 0; foreach (var command in _commands.Values) command.TrySetResult(false); Publish(ContentChanged); }
    private void ClearLocalMapping() { _localHost = ""; _localUrl = ""; _localFile = ""; foreach (var stream in _localStreams) stream.Dispose(); _localStreams.Clear(); }
    private static void Publish<T>(Action<T>? subscribers, T value) { if (subscribers is null) return; foreach (Action<T> subscriber in subscribers.GetInvocationList()) { try { subscriber(value); } catch { /* Subscriber failure must not break the browser event loop. */ } } }
    private static void Publish(Action? subscribers) { if (subscribers is null) return; foreach (Action subscriber in subscribers.GetInvocationList()) { try { subscriber(); } catch { } } }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _generation++; _pending.Clear(); _received.Clear(); foreach (var command in _commands.Values) command.TrySetResult(false); _commands.Clear();
        if (_view.CoreWebView2 is not { } core) return;
        core.NavigationStarting -= NavigationStarting; core.NavigationCompleted -= NavigationCompleted; core.SourceChanged -= SourceChanged;
        core.WebMessageReceived -= WebMessageReceived; core.WebResourceResponseReceived -= ResponseReceived; core.WebResourceRequested -= LocalResourceRequested; core.NewWindowRequested -= NewWindowRequested;
        if (_scriptId.Length > 0) core.RemoveScriptToExecuteOnDocumentCreated(_scriptId);
        if (_accelerationScriptId.Length > 0) core.RemoveScriptToExecuteOnDocumentCreated(_accelerationScriptId);
        ClearLocalMapping();
    }
}
