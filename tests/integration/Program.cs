using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Web.WebView2.WinForms;
using YingBan.App;
using YingBan.Core;

internal static class Integration
{
    private static int failures;
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--acceleration-settings-only"))
        {
            Check("Bilibili acceleration checkbox saves through the settings form", () =>
            {
                using var dialog = new SettingsForm(new AppSettings(), _ => null);
                dialog.CreateControl();
                var option = Descendants(dialog).OfType<CheckBox>().Single(c => c.AccessibleName == "B站视频加速");
                Assert(option.Checked && option.Text.Contains("重启后生效"), "default or restart hint missing");
                option.Checked = false;
                typeof(SettingsForm).GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [null, EventArgs.Empty]);
                Assert(dialog.DialogResult == DialogResult.OK && !dialog.Result.BilibiliAccelerationEnabled, "form did not save disabled option");
                return Task.CompletedTask;
            }).GetAwaiter().GetResult();
            Environment.ExitCode = failures == 0 ? 0 : 1;
            return;
        }
        if (args.Contains("--topmost-peer")) { TopmostChecks.RunPeer(); return; }
        var directory = Path.Combine(AppContext.BaseDirectory, "test-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var mediaFile = Path.Combine(directory, "fixture.wav");
        MakeWave(mediaFile);
        var testStore = new SettingsStore(directory);
        if (args.Contains("--modal-only"))
        {
            var testSettings = new AppSettings();
            int keyIndex = 13;
            testSettings.Hotkeys = testSettings.Hotkeys.Keys.ToDictionary(action => action, _ => "Ctrl+Alt+Shift+F" + keyIndex++);
            testStore.Save(testSettings);
        }
        using var form = new MainForm(testStore, ["--local", mediaFile]);
        if (args.Contains("--perf-only"))
        {
            PerformanceChecks.Run(form);
            form.Close();
            return;
        }
        if (args.Contains("--topmost-only"))
        {
            Check("compact window recovers its z-order without taking foreground focus", () => { TopmostChecks.Run(form); return Task.CompletedTask; }).GetAwaiter().GetResult();
            Console.WriteLine($"Topmost failures: {failures}"); Environment.ExitCode = failures == 0 ? 0 : 1;
            form.Close();
            return;
        }
        if (args.Contains("--frame-only"))
        {
            // Create only an invisible native frame: no browser, global hooks, or input injection.
            _ = form.Handle;
            var original = form.Bounds;
            var toggle = typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.Instance | BindingFlags.NonPublic)!;
            ((Task)toggle.Invoke(form, null)!).GetAwaiter().GetResult();
            Check("compact top edge moves while remaining edges and corners resize", () => { InteractionChecks.CompactResize(form); return Task.CompletedTask; }).GetAwaiter().GetResult();
            ((Task)toggle.Invoke(form, null)!).GetAwaiter().GetResult();
            Check("normal window frame and bounds restore", () => { Assert(form.Bounds == original && form.FormBorderStyle == FormBorderStyle.Sizable, "normal frame changed"); return Task.CompletedTask; }).GetAwaiter().GetResult();
            Console.WriteLine($"Hidden frame failures: {failures}"); Environment.ExitCode = failures == 0 ? 0 : 1;
            form.Close();
            return;
        }
        form.Shown += async (_, _) =>
        {
            try
            {
                await Wait(() => Read<MediaState>(form, "media") is { HasVideo: true, Duration: > 19 });
                if (args.Contains("--drag-inspect"))
                {
                    var dragWeb = Read<WebView2>(form, "web");
                    if (args.Contains("--bili"))
                    {
                        await Read<BrowserAdapter>(form, "browser").NavigateAsync("https://www.bilibili.com/video/BV19zYj6xEfv/");
                        await Wait(() => Read<MediaState>(form, "media") is { VideoWidth: > 0 } state && state.Url.Contains("bilibili.com"));
                    }
                    else
                    {
                        await dragWeb.CoreWebView2.ExecuteScriptAsync("{const c=document.createElement('canvas');c.width=640;c.height=360;const x=c.getContext('2d');x.fillStyle='#146a83';x.fillRect(0,0,640,360);x.fillStyle='white';x.font='30px sans-serif';x.fillText('Drag test / 拖动画面',125,175);const v=document.querySelector('video');v.srcObject=c.captureStream(10);v.play();}");
                        await Wait(() => Read<MediaState>(form, "media").VideoWidth == 640);
                    }
                    await (Task)typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!;
                    form.Location = new Point(400, 400);
                    using var trace = new DragTrace(form.Handle);
                    form.LocationChanged += (_, _) => Console.WriteLine("DRAG position=" + form.Location);
                    Console.WriteLine("DRAG ready bounds=" + form.Bounds + "; clickThrough=" + Read<bool>(form, "clickThrough"));
                    Console.WriteLine(await dragWeb.CoreWebView2.ExecuteScriptAsync("JSON.stringify([...document.querySelectorAll('video,[data-yb-player]')].map(e=>({tag:e.tagName,drag:getComputedStyle(e).getPropertyValue('app-region')})))"));
                    Console.WriteLine(await dragWeb.CoreWebView2.ExecuteScriptAsync("JSON.stringify([.1,.5,.9].map(x=>document.elementsFromPoint(innerWidth*x,innerHeight*.5).map(e=>({tag:e.tagName,cls:e.className,drag:getComputedStyle(e).getPropertyValue('app-region')}))))"));
                    await Task.Delay(180000);
                    return;
                }
                if (args.Contains("--modal-only")) { await CheckSettingsModal(form); return; }
                await Check("mouse side buttons record, cancel and persist", () => { MouseShortcutChecks.RecordAndSave(form); return Task.CompletedTask; });
                await Check("mouse side buttons route globally, suspend and release the hook", () => { MouseShortcutChecks.GlobalRouting(); return Task.CompletedTask; });
                await Check("mouse side button dispatch controls decoded video with the window hidden", () => MouseShortcutChecks.ControlsVideo(form));
                await Check("shortcut controls record keys and cancel without typing or saving early", () => { InteractionChecks.Recording(form); return Task.CompletedTask; });
                var web = Read<WebView2>(form, "web");
                var adapter = Read<BrowserAdapter>(form, "browser");
                Console.WriteLine($"Layout: bounds={form.Bounds}, dpi={form.DeviceDpi}, autoscale={form.AutoScaleMode}, scale={form.AutoScaleDimensions}");
                foreach (var b in Descendants(form).OfType<Button>().Take(20)) Console.WriteLine($"Control: {b.Text} actual={b.Size} preferred={b.PreferredSize}");
                await Check("toolbar captions fit at the current display DPI", () =>
                {
                    foreach (var b in Descendants(form).OfType<Button>()) Assert(b.Width >= b.PreferredSize.Width && b.Height >= b.PreferredSize.Height, "clipped button: " + b.Text);
                    return Task.CompletedTask;
                });
                await Check("shell playback button controls actual decoded media", async () =>
                {
                    Read<Button>(form, "play").PerformClick();
                    await Wait(() => !Read<MediaState>(form, "media").Paused);
                    Read<Button>(form, "play").PerformClick();
                    await Wait(() => Read<MediaState>(form, "media").Paused);
                });
                await Check("compact window restores original layout and video remains controllable", async () =>
                {
                    var original = form.Bounds;
                    Read<Button>(form, "compactButton").PerformClick();
                    await Wait(() => Read<bool>(form, "compact"));
                    Assert(form.Width <= 650, "compact width");
                    Assert(form.FormBorderStyle == FormBorderStyle.None, "compact still has a native title bar");
                    Assert(!Read<Control>(form, "toolbar").Visible && !Read<Control>(form, "status").Visible, "compact still has toolbar or status bar");
                    Assert(web.Size == form.ClientSize && web.PointToScreen(Point.Empty) == form.PointToScreen(Point.Empty), "compact video has surrounding margins");
                    Assert(Math.Abs((double)form.ClientSize.Width / form.ClientSize.Height - 16.0 / 9) < .01, "compact default ratio");
                    await Check("compact top edge moves and other edges resize with a fixed video aspect", () => { InteractionChecks.CompactResize(form); return Task.CompletedTask; });
                    await adapter.CommandAsync("position", 4);
                    await Wait(() => Read<MediaState>(form, "media").CurrentTime >= 3.9);
                    await (Task)typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!;
                    await Wait(() => !Read<bool>(form, "compact"));
                    Assert(form.Bounds == original, "bounds not restored");
                    Assert(form.FormBorderStyle == FormBorderStyle.Sizable && Read<Control>(form, "toolbar").Visible, "normal chrome not restored");
                });
                await Check("recovery disables native click through", () =>
                {
                    Assert(Read<HotkeyManager>(form, "hotkeys").RecoveryAvailable, "recovery unavailable");
                    Read<Button>(form, "through").PerformClick();
                    Assert(Read<bool>(form, "clickThrough"), "click through not enabled");
                    typeof(MainForm).GetMethod("Recover", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                    Assert(!Read<bool>(form, "clickThrough") && form.Visible, "recovery failed");
                    return Task.CompletedTask;
                });
                await Check("native overlay receives timed comments and pauses with media", async () =>
                {
                    Read<Button>(form, "danmaku").PerformClick();
                    var overlay = Read<DanmakuOverlay>(form, "overlay");
                    Assert(overlay.Visible, "overlay hidden");
                    await adapter.CommandAsync("position", 1);
                    await Wait(() => Math.Abs(Read<MediaState>(form, "media").CurrentTime - 1) < .1);
                    var timeline = Read<DanmakuTimeline>(form, "timeline");
                    timeline.Add([new("integration1", 1.25, 5, 0x59DEB9, 26, "集成测试弹幕")]);
                    await adapter.CommandAsync("play");
                    await Wait(() => overlay.ActiveCount > 0);
                    await adapter.CommandAsync("pause");
                    await Wait(() => Read<MediaState>(form, "media").Paused);
                    var count = overlay.ActiveCount;
                    await Task.Delay(300);
                    Assert(count > 0 && overlay.ActiveCount == count, "paused overlay lost comments");
                    await adapter.CommandAsync("position", 12);
                    await Wait(() => overlay.ActiveCount == 0);
                });
                await Check("settings dialog constructs every shortcut and projection control", () =>
                {
                    using var dialog = new SettingsForm(new AppSettings(), _ => null);
                    dialog.CreateControl();
                    Assert(Descendants(dialog).OfType<HotkeyRecordButton>().Count() == 11, "shortcut editor missing");
                    Assert(Descendants(dialog).OfType<NumericUpDown>().Count() == 7, "projection editor missing");
                    return Task.CompletedTask;
                });
                await CheckSettingsModal(form);
                await Check("compact follows real 4:3 and 16:9 frame sizes and recovery restores full chrome", async () =>
                {
                    await web.CoreWebView2.ExecuteScriptAsync("{window.frameFixture=document.createElement('canvas');frameFixture.width=640;frameFixture.height=480;const c=frameFixture.getContext('2d');c.fillStyle='#59deb9';c.fillRect(0,0,640,480);const v=document.querySelector('video');v.srcObject=frameFixture.captureStream(10);v.play();}");
                    await Wait(() => Read<MediaState>(form, "media") is { VideoWidth: 640, VideoHeight: 480 });
                    var original = form.Bounds;
                    await (Task)typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!;
                    Assert(Math.Abs((double)form.ClientSize.Width / form.ClientSize.Height - 4.0 / 3) < .01, "4:3 frame has extra letterboxing");
                    Assert(web.CoreWebView2.Settings.IsNonClientRegionSupportEnabled, "native video dragging disabled");
                    var dragStyle = await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({standard:CSS.supports('app-region','drag'),webkit:CSS.supports('-webkit-app-region','drag'),value:getComputedStyle(document.querySelector('[data-yb-player]')).getPropertyValue('app-region'),prefixed:getComputedStyle(document.querySelector('[data-yb-player]')).getPropertyValue('-webkit-app-region')})");
                    Assert(await web.CoreWebView2.ExecuteScriptAsync("getComputedStyle(document.querySelector('[data-yb-player]')).getPropertyValue('-webkit-app-region')") == "\"drag\"", "video is not a draggable native region: " + dragStyle);
                    using var probe = new Form();
                    bool stoleRecovery = RegisterHotKey(probe.Handle, 1, 3 | 0x4000, (uint)Keys.R);
                    if (stoleRecovery) UnregisterHotKey(probe.Handle, 1);
                    Assert(!stoleRecovery, "recovery registration lost after removing native frame");
                    await web.CoreWebView2.ExecuteScriptAsync("{frameFixture.width=1280;frameFixture.height=720;const c=frameFixture.getContext('2d');c.fillStyle='#59deb9';c.fillRect(0,0,1280,720);document.querySelector('video').srcObject.getVideoTracks()[0].requestFrame();}");
                    await Wait(() => Read<MediaState>(form, "media") is { VideoWidth: 1280, VideoHeight: 720 });
                    Assert(Math.Abs((double)form.ClientSize.Width / form.ClientSize.Height - 16.0 / 9) < .01, "resolution change did not resize compact window");
                    typeof(MainForm).GetMethod("Recover", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                    await Wait(() => !Read<bool>(form, "compact"));
                    Assert(form.Bounds == original && form.FormBorderStyle == FormBorderStyle.Sizable, "recovery did not restore full window");
                    Assert(Read<Control>(form, "toolbar").Visible && Read<Control>(form, "status").Visible, "recovery left controls hidden");
                    await web.CoreWebView2.ExecuteScriptAsync("document.querySelector('video').srcObject.getTracks().forEach(t=>t.stop());document.querySelector('video').srcObject=null");
                });
                await Check("compact transitions preserve native click-through flags", async () =>
                {
                    Read<Button>(form, "through").PerformClick();
                    try
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            await (Task)typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!;
                            var flags = GetWindowLongPtr(form.Handle, -20).ToInt64();
                            Assert((flags & 0x08000020) == 0x08000020, "compact transition cleared native click-through");
                        }
                    }
                    finally { typeof(MainForm).GetMethod("Recover", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null); }
                });
                await Check("delayed bookmark navigation ignores the previous video's telemetry", async () =>
                {
                    var core = web.CoreWebView2;
                    const string first = "https://bookmark.yingban.test/first";
                    const string second = "https://bookmark.yingban.test/second";
                    var html = "<!doctype html><title>Bookmark fixture</title><video controls src='data:audio/wav;base64," + Convert.ToBase64String(File.ReadAllBytes(mediaFile)) + "'></video>";
                    var delayed = false;
                    core.AddWebResourceRequestedFilter("https://bookmark.yingban.test/*", Microsoft.Web.WebView2.Core.CoreWebView2WebResourceContext.All);
                    core.WebResourceRequested += async (_, request) =>
                    {
                        if (!request.Request.Uri.StartsWith("https://bookmark.yingban.test/")) return;
                        using var deferral = request.GetDeferral();
                        if (request.Request.Uri == second) { delayed = true; await Task.Delay(1800); }
                        request.Response = core.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html; charset=utf-8");
                    };
                    await adapter.NavigateAsync(first);
                    await Wait(() => Read<MediaState>(form, "media") is { HasVideo: true, Duration: > 19 } state && state.Url == first);
                    await adapter.CommandAsync("play");
                    var navigate = typeof(MainForm).GetMethod("Navigate", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    await (Task)navigate.Invoke(form, [second, new Bookmark { Url = second, Title = "Saved target", Position = 12 }])!;
                    await Wait(() => delayed);
                    await Task.Delay(700);
                    Assert(Read<Bookmark?>(form, "pendingBookmark") != null, "previous document consumed pending bookmark");
                    Assert(Read<MediaState>(form, "media").CurrentTime < 5, "previous document received the target seek");
                    await Wait(() => Read<MediaState>(form, "media") is { HasVideo: true, CurrentTime: >= 11.9 } state && state.Url == second);
                    Assert(Read<Bookmark?>(form, "pendingBookmark") == null, "target bookmark was not consumed");
                    await (Task)navigate.Invoke(form, ["file:///invalid", new Bookmark { Url = "file:///invalid", Position = 3 }])!;
                    Assert(Read<Bookmark?>(form, "pendingBookmark") == null, "failed navigation retained a bookmark");
                });
                await Check("More menu closes repeatedly without disposing inside the native close operation", async () =>
                {
                    var more = Descendants(form).OfType<Button>().Single(b => b.Text.StartsWith("更多"));
                    foreach (var reason in new[] { ToolStripDropDownCloseReason.AppClicked, ToolStripDropDownCloseReason.Keyboard, ToolStripDropDownCloseReason.ItemClicked })
                    {
                        for (var repeat = 0; repeat < 3; repeat++)
                        {
                            more.PerformClick();
                            var menu = FindVisibleMenu();
                            Assert(menu != null, "More menu did not open");
                            if (reason == ToolStripDropDownCloseReason.ItemClicked)
                            {
                                var enabled = Read<AppSettings>(form, "settings").DanmakuEnabled;
                                menu!.Items[0].PerformClick();
                                Assert(Read<AppSettings>(form, "settings").DanmakuEnabled != enabled, "menu action did not run");
                            }
                            menu!.Close(reason);
                            Assert(!menu.Visible, "More menu remained visible after close");
                            await Wait(() => menu.IsDisposed);
                        }
                    }
                    Assert(!form.IsDisposed && form.Visible, "main window did not survive menu closes");
                });
                if (args.Contains("--inspect"))
                {
                    await adapter.NavigateAsync("https://www.bilibili.com/video/BV19zYj6xEfv/");
                    await Wait(() => Read<MediaState>(form, "media") is { VideoWidth: > 0, VideoHeight: > 0 });
                    await (Task)typeof(MainForm).GetMethod("ToggleCompact", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null)!;
                    await adapter.CommandAsync("play");
                    await Task.Delay(2000);
                    using (var capture = File.Create(Path.Combine(AppContext.BaseDirectory, "compact-live.png"))) await web.CoreWebView2.CapturePreviewAsync(Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, capture);
                    Console.WriteLine("INSPECT compact native client=" + form.ClientSize + "; media=" + Read<MediaState>(form, "media"));
                    Console.WriteLine(await web.CoreWebView2.ExecuteScriptAsync("JSON.stringify({viewport:[innerWidth,innerHeight],video:(()=>{const r=document.querySelector('video').getBoundingClientRect();return [r.x,r.y,r.width,r.height]})(),sending:[...document.querySelectorAll('.bpx-player-sending-area')].map(x=>x.getBoundingClientRect().height),title:document.title})"));
                    Console.WriteLine("INSPECT compact window ready; waiting 20 seconds for read-only screen checks.");
                    await Task.Delay(20000);
                }
            }
            catch (Exception e) { failures++; Console.WriteLine("FAIL harness: " + e); }
            finally { form.Close(); }
        };
        Application.Run(form);
        Console.WriteLine($"Native integration failures: {failures}"); Environment.ExitCode = failures == 0 ? 0 : 1;
    }
    private static IEnumerable<Control> Descendants(Control c) { foreach (Control child in c.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private sealed class DragTrace : NativeWindow, IDisposable
    {
        internal DragTrace(IntPtr handle) => AssignHandle(handle);
        protected override void WndProc(ref Message m)
        {
            if (m.Msg is 0xA1 or 0x112 or 0x216 or 0x231 or 0x232) Console.WriteLine($"DRAG native {m.Msg:X} {m.WParam:X}");
            base.WndProc(ref m);
        }
        public void Dispose() => ReleaseHandle();
    }
    // Enumerate only this test UI thread to inspect the product's own transient menu.
    private delegate bool EnumThreadWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowCallback callback, IntPtr parameter);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    private static bool IsAbove(IntPtr upper, IntPtr lower)
    {
        for (var window = GetWindow(lower, 3); window != IntPtr.Zero; window = GetWindow(window, 3)) if (window == upper) return true;
        return false;
    }
    private static async Task CheckSettingsModal(MainForm form)
    {
        foreach (var (topmost, save) in new[] { (true, false), (false, true) })
        {
            await Check($"settings modal topmost={topmost}, {(save ? "save" : "Escape cancel")} and recovery preserve interaction", () =>
            {
                var before = Read<AppSettings>(form, "settings");
                before.AlwaysOnTop = topmost;
                before.DanmakuEnabled = true;
                typeof(MainForm).GetMethod("ApplySettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                int previousStep = before.StepSeconds;
                Exception? failure = null;
                bool inspected = false;
                using var observer = new System.Windows.Forms.Timer { Interval = 120 };
                observer.Tick += (_, _) =>
                {
                    var dialog = Application.OpenForms.OfType<SettingsForm>().FirstOrDefault();
                    if (dialog == null || !dialog.Visible) return;
                    observer.Stop(); inspected = true;
                    try
                    {
                        Assert(!IsWindowEnabled(form.Handle) && IsWindowEnabled(dialog.Handle), "dialog is not the interactive modal");
                        Assert(!form.TopMost && ((GetWindowLongPtr(dialog.Handle, -20).ToInt64() & 8) != 0) == topmost, "modal topmost layer incorrect");
                        Assert(!Read<DanmakuOverlay>(form, "overlay").Visible, "projection covers modal editor");
                        Assert(InteractionChecks.Available(before.Hotkeys["toggle"]), "old global registration swallows shortcut recording");
                        typeof(MainForm).GetMethod("Recover", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                        Assert(!IsAbove(form.Handle, dialog.Handle) && !form.TopMost, "recovery raised disabled owner over settings");
                        typeof(MainForm).GetMethod("RunAction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, ["hide"]);
                        Assert(form.Visible && dialog.Visible, "hide hotkey hid active settings");
                        typeof(MainForm).GetMethod("RunAction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, ["compact"]);
                        Assert(!Read<bool>(form, "compact"), "compact hotkey changed modal owner frame");
                        Descendants(dialog).OfType<TabControl>().Single().SelectedIndex = 1;
                        var recorder = Descendants(dialog).OfType<HotkeyRecordButton>().Single(x => x.AccessibleName == "播放 / 暂停快捷键");
                        recorder.PerformClick();
                        InteractionChecks.Press(recorder, Keys.Control | Keys.Alt | Keys.Shift | Keys.F24);
                        Descendants(dialog).OfType<TabControl>().Single().SelectedIndex = 0;
                        Descendants(dialog).OfType<NumericUpDown>().Single(x => x.AccessibleName == "快进 / 快退秒数").Value = 7;
                        if (save) Descendants(dialog).OfType<Button>().Single(x => x.Text == "保存设置").PerformClick();
                        else typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, [Keys.Escape]);
                        Assert(dialog.DialogResult == (save ? DialogResult.OK : DialogResult.Cancel), "settings did not accept save/cancel");
                    }
                    catch (Exception e) { failure = e; }
                    finally { dialog.Close(); }
                };
                observer.Start();
                typeof(MainForm).GetMethod("ShowSettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
                Assert(inspected, "settings modal never became visible");
                if (failure != null) throw failure;
                Assert(IsWindowEnabled(form.Handle) && form.TopMost == topmost && form.Visible, "owner state not restored");
                Assert(!InteractionChecks.Available(Read<AppSettings>(form, "settings").Hotkeys["toggle"]), "global shortcut not registered after settings closed");
                Assert(Read<AppSettings>(form, "settings").Hotkeys["toggle"] == (save ? "Ctrl+Alt+Shift+F24" : before.Hotkeys["toggle"]), "recorded shortcut did not respect save/cancel");
                Assert(Read<DanmakuOverlay>(form, "overlay").Visible, "projection not restored");
                Assert(Read<AppSettings>(form, "settings").StepSeconds == (save ? 7 : previousStep), "save/cancel changed wrong settings");
                return Task.CompletedTask;
            });
        }
        Read<AppSettings>(form, "settings").AlwaysOnTop = true;
        typeof(MainForm).GetMethod("ApplySettings", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(form, null);
    }
    private static ContextMenuStrip? FindVisibleMenu()
    {
        ContextMenuStrip? found = null;
        EnumThreadWindows(GetCurrentThreadId(), (window, _) => { if (Control.FromHandle(window) is ContextMenuStrip { Visible: true } menu) found = menu; return true; }, IntPtr.Zero);
        return found;
    }
    private static T Read<T>(object instance, string field) => (T)instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;
    private static void Assert(bool b, string text) { if (!b) throw new InvalidOperationException(text); }
    private static async Task Check(string name, Func<Task> action) { try { await action(); Console.WriteLine("PASS " + name); } catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); } }
    private static async Task Wait(Func<bool> condition) { var sw = Stopwatch.StartNew(); while (!condition()) { if (sw.Elapsed.TotalSeconds > 25) throw new TimeoutException("Expected application state not reached"); await Task.Delay(60); } }
    private static void MakeWave(string path)
    {
        const int samples = 20 * 8000; using var w = new BinaryWriter(File.Create(path));
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(8000); w.Write(16000); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples * 2); for (int i = 0; i < samples; i++) w.Write((short)0);
    }
}
