using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using YingBan.App;

internal static partial class BrowserSmoke
{
    private static int _failures;
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        using var form = new BackgroundTestForm { Width = 860, Height = 640, Opacity = 0, ShowInTaskbar = false };
        using var view = new WebView2 { Dock = DockStyle.Fill };
        form.Controls.Add(view);
        form.Shown += async (_, _) =>
        {
            try
            {
                if (args.Length >= 2 && args[0] == "--url") await ObserveExternal(view, args[1], args.Length >= 3 && int.TryParse(args[2], out var seconds) ? Math.Clamp(seconds, 10, 90) : 45);
                else if (args.Contains("--btr-only")) await RunAcceleration(view, !args.Contains("--btr-disabled"));
                else await Run(view);
            }
            catch (Exception ex) { Console.WriteLine("FAIL harness: " + ex); _failures++; }
            finally { form.Close(); }
        };
        Application.Run(form);
        Console.WriteLine($"Browser harness failures: {_failures}");
        Environment.ExitCode = _failures == 0 ? 0 : 1;
    }

    static async Task ObserveExternal(WebView2 view, string url, int seconds)
    {
        using var adapter = new BrowserAdapter(view);
        await adapter.InitializeAsync(Path.Combine(AppContext.BaseDirectory, "external-profile"));
        var core = view.CoreWebView2;
        var count = 0; var batches = 0; var statuses = new List<string>(); var resources = new List<string>();
        MediaState? state = null;
        string cid = "";
        adapter.MediaChanged += x => state = x;
        adapter.CommentsReceived += comments => { count += comments.Count; batches++; Console.WriteLine($"Observed comment batch: {comments.Count}"); };
        adapter.StatusChanged += text => { statuses.Add(text); Console.WriteLine(text); };
        core.WebResourceResponseReceived += (sender, e) =>
        {
            if (!BrowserAdapter.TryCommentResource(e.Request.Uri, out var resourceCid, out _)) return;
            var uri = new Uri(e.Request.Uri);
            var part = System.Text.RegularExpressions.Regex.Match(uri.Query, "(?:[?&])segment_index=([0-9]+)").Groups[1].Value;
            resources.Add($"{e.Response.StatusCode} {uri.GetLeftPart(UriPartial.Path)} cid={resourceCid} segment={part}");
            Console.WriteLine("Observed resource: " + resources[^1]);
        };
        core.WebMessageReceived += (_, e) =>
        {
            try { using var doc = JsonDocument.Parse(e.WebMessageAsJson); if (doc.RootElement.TryGetProperty("cid", out var value)) cid = value.GetString() ?? ""; } catch { }
        };
        await adapter.NavigateAsync(url);
        var elapsed = Stopwatch.StartNew();
        var requestedPlay = false; var compact = false; var second = false; var third = false;
        while (elapsed.Elapsed.TotalSeconds < seconds)
        {
            if (!requestedPlay && elapsed.Elapsed.TotalSeconds >= 8 && state is { HasVideo: true })
            {
                requestedPlay = true; await adapter.CommandAsync("play");
                Console.WriteLine("Playback requested through shell adapter.");
            }
            if (!second && elapsed.Elapsed.TotalSeconds >= 11 && state is { HasVideo: true }) { second = true; await adapter.CommandAsync("position", 430); }
            if (!third && elapsed.Elapsed.TotalSeconds >= 18 && state is { HasVideo: true }) { third = true; await adapter.CommandAsync("position", 760); }
            if (!compact && elapsed.Elapsed.TotalSeconds >= seconds - 6)
            {
                using (var capture = File.Create(Path.Combine(AppContext.BaseDirectory, "external-page.png"))) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
                if (state is { HasVideo: true }) await adapter.CommandAsync("compact", 1);
                compact = true;
            }
            await Task.Delay(500);
        }
        using (var capture = File.Create(Path.Combine(AppContext.BaseDirectory, "external-compact.png"))) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, capture);
        var acceleration = await core.ExecuteScriptAsync("(()=>{const d=window.__biliThreadRipperDebug,s=d?.getStats();return s?JSON.stringify({version:d.version,state:s.playerState,requests:s.acceleratedRequests,bytes:s.acceleratedBytes,subrequests:s.parallelSubrequests,bufferedAhead:s.bufferedAhead,error:s.lastError}):'BTR not loaded';})()");
        Console.WriteLine("BTR runtime: " + acceleration);
        var details = await core.ExecuteScriptAsync("JSON.stringify({title:document.title,text:document.body?.innerText?.slice(0,1500),videoCount:document.querySelectorAll('video').length,segments:performance.getEntriesByType('resource').filter(x=>x.name.includes('seg.so')).map(x=>({encoded:x.encodedBodySize,decoded:x.decodedBodySize,transfer:x.transferSize})),video:document.querySelector('video')?{ready:document.querySelector('video').readyState,error:document.querySelector('video').error?.code,currentSrc:document.querySelector('video').currentSrc}:null})");
        using var detailDocument = JsonDocument.Parse(JsonSerializer.Deserialize<string>(details) ?? "{}");
        var report = new { Url = url, Runtime = core.Environment.BrowserVersionString, Seconds = seconds, State = state, Cid = cid, Comments = count, Batches = batches, Resources = resources, Status = statuses, Details = detailDocument.RootElement.Clone() };
        await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "external-report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    static async Task Run(WebView2 view)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        Directory.CreateDirectory(fixtures);
        MakeWave(Path.Combine(fixtures, "fixture.wav"));
        using var adapter = new BrowserAdapter(view);
        var profile = Path.Combine(Path.GetDirectoryName(typeof(BrowserSmoke).Assembly.Location)!, "test-profile");
        await adapter.InitializeAsync(profile);
        Console.WriteLine("WebView2 runtime: " + view.CoreWebView2.Environment.BrowserVersionString);
        var core = view.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("fixture.test", fixtures, CoreWebView2HostResourceAccessKind.DenyCors);
        MediaState? state = null;
        var changes = 0;
        adapter.MediaChanged += x => state = x;
        adapter.ContentChanged += () => changes++;

        await Check("comment resource recognition supports legacy, WBI and strict host matching", () =>
        {
            Assert(BrowserAdapter.TryCommentResource("https://api.bilibili.com/x/v2/dm/wbi/web/seg.so?oid=123&type=1&segment_index=1", out var cid, out var xml) && cid == "123" && !xml, "WBI segment not recognized");
            Assert(BrowserAdapter.TryCommentResource("https://comment.bilibili.com/123.xml", out cid, out xml) && cid == "123" && xml, "legacy XML");
            Assert(!BrowserAdapter.TryCommentResource("https://api.bilibili.com.evil.test/x/v2/dm/web/seg.so?oid=123", out _, out _), "spoofed host");
            Assert(!BrowserAdapter.TryCommentResource("https://api.bilibili.com/x/v2/dm/web/seg.so?oid=abc", out _, out _), "invalid cid");
            return Task.CompletedTask;
        });
        await Check("local ranges clamp ends, support suffix and reject malformed or unsatisfiable ranges", () =>
        {
            foreach (var (header, expectedStart, expectedEnd) in new[] { ("bytes=0-3", 0L, 3L), ("bytes=60-", 60L, 99L), ("bytes=-10", 90L, 99L), ("bytes=80-500", 80L, 99L), ("bytes=-500", 0L, 99L) })
                Assert(BrowserAdapter.TryRange(header, 100, out var start, out var end) && start == expectedStart && end == expectedEnd, header);
            foreach (var invalid in new[] { "bytes=100-", "bytes=5-3", "bytes=-0", "bytes=0-3,5-8", "other=0-3", "bytes=NaN-", "bytes=999999999999999999999-" })
                Assert(!BrowserAdapter.TryRange(invalid, 100, out _, out _), invalid);
            Assert(!BrowserAdapter.TryRange("bytes=0-", 0, out _, out _), "empty file");
            return Task.CompletedTask;
        });

        await Check("navigation rejects arbitrary protocols, credentials and native asset URLs", async () =>
        {
            foreach (var invalid in new[] { "javascript:alert(1)", "file:///C:/Windows/win.ini", "https://user:pass@example.com/", "https://app.yingban.local/home.html", "C:/video.mp4" })
            {
                var rejected = false; try { await adapter.NavigateAsync(invalid); } catch (ArgumentException) { rejected = true; }
                Assert(rejected, invalid + " was not rejected");
            }
        });
        await adapter.NavigateAsync("https://fixture.test/media.html");
        await WaitUntil(async () => await Number(core, "document.querySelector('video')?.readyState||0") >= 2);
        await Check("real media telemetry reports duration and paused state", async () =>
        {
            await WaitUntil(() => Task.FromResult(state is { HasVideo: true, Duration: > 19, Paused: true }));
            Assert(state!.Title == "真实媒体测试", "title");
        });
        await Check("native position and relative seek change real HTMLMediaElement", async () =>
        {
            await adapter.CommandAsync("position", 5);
            Assert(Math.Abs(await Number(core, "document.querySelector('video').currentTime") - 5) < .05, "position did not seek");
            await adapter.CommandAsync("seek", 3);
            Assert(Math.Abs(await Number(core, "document.querySelector('video').currentTime") - 8) < .05, "relative seek did not seek");
            await adapter.CommandAsync("rate", 1.5);
            Assert(await Number(core, "document.querySelector('video').playbackRate") == 1.5, "rate");
        });
        await Check("native play and pause work on decoded local media", async () =>
        {
            await adapter.CommandAsync("play");
            await WaitUntil(async () => await Boolean(core, "!document.querySelector('video').paused"));
            await Task.Delay(180);
            await adapter.CommandAsync("pause");
            Assert(await Boolean(core, "document.querySelector('video').paused"), "pause");
        });
        await Check("play rejection receives native command failure status", async () =>
        {
            var failed = false;
            adapter.StatusChanged += text => failed |= text.Contains("操作暂不可用");
            await core.ExecuteScriptAsync("document.querySelector('video').play=()=>Promise.reject(new Error('fixture autoplay denied'))");
            await adapter.CommandAsync("play");
            Assert(failed, "rejected play did not report command failure");
            await core.ExecuteScriptAsync("delete document.querySelector('video').play");
        });
        await Check("compact keeps real overlay visible, excludes siblings and restores layout", async () =>
        {
            await core.ExecuteScriptAsync("document.querySelector('#player').style.minWidth='1200px'");
            await adapter.CommandAsync("compact", 1);
            Assert(await Boolean(core, "document.querySelector('#player').getBoundingClientRect().width<=window.innerWidth+1"), "compact retains site minimum width and overflows viewport");
            Assert(await Boolean(core, "getComputedStyle(document.querySelector('#outside')).display==='none'"), "sibling remains visible");
            Assert(await Boolean(core, "getComputedStyle(document.querySelector('#overlay')).display!=='none' && document.querySelector('#overlay').getBoundingClientRect().width>0"), "overlay hidden");
            Assert(await Boolean(core, "document.fullscreenElement===null"), "native fullscreen entered");
            await adapter.CommandAsync("compact", 0);
            Assert(await Boolean(core, "getComputedStyle(document.querySelector('#outside')).display!=='none'"), "sibling not restored");
        });
        await Check("compact removes Bilibili sending bar and fills nested video wrappers", async () =>
        {
            await core.ExecuteScriptAsync("{const player=document.querySelector('#player');player.className='bpx-player-container';const area=document.createElement('div');area.className='bpx-player-video-area';area.style.cssText='height:calc(100% - 64px);width:75%;position:relative';const wrap=document.createElement('div');wrap.className='bpx-player-video-wrap';wrap.style.cssText='width:80%;height:80%;padding:8px';wrap.append(document.querySelector('video'));area.append(wrap,document.querySelector('#overlay'));player.append(area);const bar=document.createElement('div');bar.className='bpx-player-sending-area';bar.id='sending';bar.style.cssText='height:64px;background:white';bar.textContent='17人正在看 / 发送弹幕';player.append(bar);}");
            await adapter.CommandAsync("compact", 1);
            Assert(await Boolean(core, "document.querySelector('#sending').getBoundingClientRect().height===0"), "Bilibili sending bar is still visible");
            Assert(await Boolean(core, "(()=>{const b=document.querySelector('video').getBoundingClientRect();return Math.abs(b.x)<1&&Math.abs(b.y)<1&&Math.abs(b.width-innerWidth)<1&&Math.abs(b.height-innerHeight)<1})()"), "video does not fill viewport");
            Assert(await Boolean(core, "getComputedStyle(document.querySelector('#overlay')).display!=='none'"), "on-video captions hidden");
            Assert(await Boolean(core, "getComputedStyle(document.querySelector('video')).objectFit==='contain'"), "video stretched or cropped");
            await adapter.CommandAsync("compact", 0);
            Assert(await Boolean(core, "document.querySelector('#sending').getBoundingClientRect().height===64"), "sending bar not restored outside compact mode");
        });
        await Check("SPA video replacement becomes current command target", async () =>
        {
            await core.ExecuteScriptAsync("{const old=document.querySelector('video');const next=old.cloneNode();next.id='replacement';old.replaceWith(next);}");
            await WaitUntil(async () => await Number(core, "document.querySelector('video').readyState") >= 2);
            await adapter.CommandAsync("position", 12);
            Assert(Math.Abs(await Number(core, "document.querySelector('#replacement').currentTime") - 12) < .05, "replacement not controlled");
        });
        await Check("external message cannot trigger native navigation and malformed telemetry is ignored", async () =>
        {
            var previous = state;
            await core.ExecuteScriptAsync("window.chrome.webview.postMessage({kind:'navigate',url:'https://example.com/'});window.chrome.webview.postMessage({kind:'media',url:location.href,time:-1,duration:10,rate:1,paused:false,hasVideo:true,cid:'123',title:'bad',src:''});");
            await Task.Delay(90);
            Assert(core.Source == "https://fixture.test/media.html", "native navigation");
            Assert(state?.Title != "bad", "malformed telemetry accepted");
        });
        await Check("frame telemetry is ignored", async () =>
        {
            await core.ExecuteScriptAsync("{const f=document.createElement('iframe');f.srcdoc='<script>setTimeout(()=>window.chrome.webview.postMessage({kind:\"media\",url:\"https://fixture.test/media.html\",title:\"frame attack\",time:1,duration:10,rate:1,paused:true,hasVideo:true,cid:\"\",src:\"\"}),80)</script>';document.body.append(f);}");
            await Task.Delay(160);
            Assert(state?.Title != "frame attack", "frame accepted");
        });
        await Check("OpenLocal serves selected media and rejects sibling fetch", async () =>
        {
            File.WriteAllText(Path.Combine(fixtures, "private.txt"), "private sibling fixture");
            await adapter.OpenLocalAsync(Path.Combine(fixtures, "fixture.wav"));
            await WaitUntil(async () => core.Source.Contains("local-player.html") && await Number(core, "document.querySelector('video')?.readyState||0") >= 2);
            Assert(await Number(core, "document.querySelector('video').duration") > 19, "local file not decoded");
            await core.ExecuteScriptAsync("{const src=new URL(document.querySelector('video').src);src.pathname='/private.txt';fetch(src).then(r=>document.body.dataset.sibling=String(r.status)).catch(()=>document.body.dataset.sibling='403')}");
            await WaitUntil(async () => await Boolean(core, "document.body.dataset.sibling==='403'"));
        });
        await Check("selected local file supports actual range bytes, unsatisfiable range and seeking", async () =>
        {
            await core.ExecuteScriptAsync("{const src=document.querySelector('video').src;document.body.dataset.range='';fetch(src,{headers:{Range:'bytes=0-3'}}).then(async r=>document.body.dataset.range=r.status+':'+r.headers.get('Content-Range')+':'+String.fromCharCode(...new Uint8Array(await r.arrayBuffer()))).catch(e=>document.body.dataset.range='error');}");
            await WaitUntil(async () => await Boolean(core, "Boolean(document.body.dataset.range)"));
            Assert(await Boolean(core, "document.body.dataset.range==='206:bytes 0-3/320044:RIFF'"), "selected byte range incorrect: " + await core.ExecuteScriptAsync("document.body.dataset.range"));
            await core.ExecuteScriptAsync("fetch(document.querySelector('video').src,{headers:{Range:'bytes=999999999-'}}).then(r=>document.body.dataset.rangeError=String(r.status))");
            await WaitUntil(async () => await Boolean(core, "document.body.dataset.rangeError==='416'"));
            await adapter.CommandAsync("position", 12);
            Assert(Math.Abs(await Number(core, "document.querySelector('video').currentTime") - 12) < .05, "selected local file did not seek");
            await adapter.CommandAsync("play"); await WaitUntil(async () => await Boolean(core, "!document.querySelector('video').paused")); await adapter.CommandAsync("pause");
        });
        await Check("non-CORS video embedding cannot load unselected sibling media", async () =>
        {
            MakeWave(Path.Combine(fixtures, "secret.wav"));
            await core.ExecuteScriptAsync("{const video=document.querySelector('video');window.fixtureSelected=video.src;const sibling=new URL(video.src);sibling.pathname='/secret.wav';video.src=sibling.href;document.body.dataset.siblingMedia='';video.onloadedmetadata=()=>document.body.dataset.siblingMedia='loaded';video.onerror=()=>document.body.dataset.siblingMedia='blocked';}");
            await WaitUntil(async () => await Boolean(core, "Boolean(document.body.dataset.siblingMedia)"));
            Assert(await Boolean(core, "document.body.dataset.siblingMedia==='blocked'"), "unselected sibling media was exposed");
        });
        await Check("observed Bilibili protobuf response reaches core parser", async () =>
        {
            core.SetVirtualHostNameToFolderMapping("www.bilibili.com", fixtures, CoreWebView2HostResourceAccessKind.DenyCors);
            core.AddWebResourceRequestedFilter("https://api.bilibili.com/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, e) =>
            {
                if (e.Request.Uri.StartsWith("https://api.bilibili.com/"))
                {
                    // The nested element is 19 bytes: id, progress, mode, size, color and 3-byte text.
                    byte[] bytes = [0x0a, 0x13, 0x08, 0x01, 0x10, 0xe8, 0x07, 0x18, 0x01, 0x20, 0x19, 0x28, 0xff, 0xff, 0xff, 0x07, 0x3a, 0x03, 0xe5, 0xa5, 0xbd];
                    if (e.Request.Uri.Contains("oid=456")) { bytes[^3] = 0xe6; bytes[^2] = 0x96; bytes[^1] = 0xb0; }
                    e.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", "Content-Type: application/octet-stream\r\nAccess-Control-Allow-Origin: *");
                }
            };
            var received = false;
            adapter.CommentsReceived += comments => received |= comments.Any(x => x.Text == "好" && x.Time == 1);
            await adapter.NavigateAsync("https://www.bilibili.com/comments.html");
            await WaitUntil(() => Task.FromResult(received));
        });
        await Check("cid change isolates stale segments from prior SPA content", async () =>
        {
            var old = 0; var current = 0;
            adapter.CommentsReceived += comments => { old += comments.Count(x => x.Text == "好"); current += comments.Count(x => x.Text == "新"); };
            var previousChanges = changes;
            await core.ExecuteScriptAsync("window.__INITIAL_STATE__.cid=456");
            await WaitUntil(() => Task.FromResult(changes > previousChanges));
            await core.ExecuteScriptAsync("fetch('https://api.bilibili.com/x/v2/dm/wbi/web/seg.so?oid=123&segment_index=2');fetch('https://api.bilibili.com/x/v2/dm/wbi/web/seg.so?oid=456&segment_index=1');");
            await WaitUntil(() => Task.FromResult(current == 1));
            await Task.Delay(150); Assert(old == 0, "stale cid comments leaked");
        });
        await adapter.ShowHomeAsync();
        await WaitUntil(async () => await Boolean(core, "document.querySelector('h1')!==null"));
        using var screenshot = File.Create(Path.Combine(AppContext.BaseDirectory, "home.png"));
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, screenshot);
        Console.WriteLine("Home screenshot: " + Path.Combine(AppContext.BaseDirectory, "home.png"));
    }

    static async Task Check(string name, Func<Task> action)
    {
        try { await action(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static async Task WaitUntil(Func<Task<bool>> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!await condition()) { if (timer.Elapsed.TotalSeconds > 12) throw new TimeoutException("Condition did not become true within 12 s."); await Task.Delay(60); }
    }
    static async Task<bool> Boolean(CoreWebView2 core, string expression) => await core.ExecuteScriptAsync(expression) == "true";
    static async Task<double> Number(CoreWebView2 core, string expression) => double.TryParse(await core.ExecuteScriptAsync(expression), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
    static void MakeWave(string path)
    {
        const int samples = 20 * 8000;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        for (var i = 0; i < samples; i++) writer.Write((short)0);
    }
}

internal sealed class BackgroundTestForm : Form
{
    protected override bool ShowWithoutActivation => true;
}
