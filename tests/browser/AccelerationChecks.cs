using System.Text;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using YingBan.App;

internal static partial class BrowserSmoke
{
    static async Task RunAcceleration(WebView2 view, bool enabled)
    {
        var profile = Path.Combine(AppContext.BaseDirectory, "test-profile-btr-" + Guid.NewGuid().ToString("N"));
        using var adapter = new BrowserAdapter(view);
        await adapter.InitializeAsync(profile, enabled);
        var core = view.CoreWebView2;
        var wave = Path.Combine(profile, "fixture.wav");
        MakeWave(wave);
        var html = Encoding.UTF8.GetBytes($"""
            <!doctype html><html><head><title>BTR integration fixture</title></head>
            <body><div class="bpx-player-container" style="width:640px;height:360px">
            <video muted controls style="width:640px;height:360px" src="data:audio/wav;base64,{Convert.ToBase64String(File.ReadAllBytes(wave))}"></video>
            </div><script>window.btrAtPageStart=!!window.__biliThreadRipperDebug;window.fixtureReady=true;</script></body></html>
            """);
        // All resources remain inside the fixture. No signed URLs or user profile are used.
        var streams = new List<MemoryStream>();
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) =>
        {
            var body = new MemoryStream(html);
            streams.Add(body);
            e.Response = core.Environment.CreateWebResourceResponse(body, 200, "OK", "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
        };
        try
        {
            await adapter.NavigateAsync("https://www.bilibili.com/video/BV19zYj6xEfv/");
            await WaitUntil(async () => await Boolean(core, "window.fixtureReady===true"));
            if (!enabled)
            {
                await Check("disabled integration leaves Bilibili untouched", async () =>
                {
                    Assert(await Boolean(core, "!document.documentElement.hasAttribute('data-btr-userscript') && !window.__BTR_SETTINGS_PANEL__"), "BTR still injected");
                    Assert(!await adapter.OpenAccelerationSettingsAsync(), "disabled settings opened");
                });
                return;
            }
            await Check("BTR starts before Bilibili page scripts", async () =>
            {
                Assert(await Boolean(core, "window.btrAtPageStart && document.documentElement.hasAttribute('data-btr-userscript') && typeof window.__biliThreadRipperDebug==='object'"), "BTR is absent from the embedded Bilibili page");
                Assert(await Boolean(core, "typeof window.chrome.webview.postMessage==='function'"), "native bridge was overwritten");
                await WaitUntil(async () => await Boolean(core, "__biliThreadRipperDebug.getSettings().autoConcurrency===true"));
                Assert(await Boolean(core, "__biliThreadRipperDebug.getSettings().enabled && __biliThreadRipperDebug.getSettings().mode==='mainland'"), "unexpected defaults");
            });
            await Check("native menu opens BTR panel and repeated open keeps it visible", async () =>
            {
                Assert(await adapter.OpenAccelerationSettingsAsync(), "settings panel did not open");
                Assert(await adapter.OpenAccelerationSettingsAsync(), "second open closed panel");
                Assert(await Boolean(core, "!!document.querySelector('#__bilibili_thread_ripper_settings__')?.shadowRoot?.querySelector('#enabled')"), "upstream panel missing");
            });
            await Check("BTR settings persist across document navigation", async () =>
            {
                await core.ExecuteScriptAsync("document.querySelector('#__bilibili_thread_ripper_settings__').shadowRoot.querySelector('#theme').click()");
                await WaitUntil(async () => await Boolean(core, "JSON.parse(localStorage.getItem('BTR_Userscript.sync')||'{}').theme==='light'"));
                await adapter.NavigateAsync("https://www.bilibili.com/video/BV19zYj6xEfv/?reload=1");
                await WaitUntil(async () => core.Source.EndsWith("?reload=1") && await Boolean(core, "window.fixtureReady && window.__biliThreadRipperDebug?.getSettings().theme==='light'"));
            });
            await Check("BTR coexists with media commands and compact mode", async () =>
            {
                await WaitUntil(async () => await Boolean(core, "document.querySelector('video')?.readyState>=2"));
                await adapter.CommandAsync("play");
                await WaitUntil(async () => await Boolean(core, "!document.querySelector('video').paused && document.querySelector('video').currentTime>0"));
                await adapter.CommandAsync("pause");
                Assert(await Boolean(core, "document.querySelector('video').paused"), "pause failed");
                await adapter.CommandAsync("seek", 5);
                Assert(await Number(core, "document.querySelector('video').currentTime") >= 5, "seek failed");
                await adapter.CommandAsync("seek", -3);
                Assert(await Number(core, "document.querySelector('video').currentTime") < 4, "rewind failed");
                await adapter.CommandAsync("rate", 1.5);
                Assert(await Number(core, "document.querySelector('video').playbackRate") == 1.5, "rate failed");
                await adapter.CommandAsync("compact", 1);
                Assert(await Boolean(core, "document.querySelector('video').hasAttribute('data-yb-video') && !!document.querySelector('[data-yb-player]')"), "compact failed");
                await adapter.CommandAsync("compact", 0);
                Assert(await adapter.OpenAccelerationSettingsAsync(), "panel did not reopen after compact");
            });
            foreach (var url in new[] { "https://fixture.test/", "https://www.bilibili.com.evil.test/", "http://www.bilibili.com/" })
            {
                await Check("BTR excludes " + url, async () =>
                {
                    await adapter.NavigateAsync(url);
                    await WaitUntil(async () => core.Source == url && await Boolean(core, "window.fixtureReady===true"));
                    Assert(await Boolean(core, "!document.documentElement.hasAttribute('data-btr-userscript') && !window.__BTR_SETTINGS_PANEL__"), "out-of-scope injection");
                    Assert(!await adapter.OpenAccelerationSettingsAsync(), "out-of-scope panel");
                });
            }
        }
        finally { foreach (var stream in streams) stream.Dispose(); }
    }
}
