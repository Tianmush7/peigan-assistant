const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');

// A small DOM double isolates the bridge algorithm. The Windows harness separately
// verifies real HTMLMediaElement behavior and CSS in the installed WebView2 runtime.
class Element {
  constructor(tag = 'div') { this.tagName = tag.toUpperCase(); this.children = []; this.attrs = new Map(); this.isConnected = true; }
  appendChild(child) { this.children.push(child); child.parentElement = this; return child; }
  setAttribute(k, v) { this.attrs.set(k, v); }
  removeAttribute(k) { this.attrs.delete(k); }
  hasAttribute(k) { return this.attrs.has(k); }
  getBoundingClientRect() { return { width: this.width ?? 640, height: this.height ?? 360 }; }
  closest() { let node = this.parentElement; while (node && !node.player) node = node.parentElement; return node; }
  remove() { this.isConnected = false; this.parentElement.children = this.parentElement.children.filter(x => x !== this); }
}
class Media extends Element {
  constructor() { super('video'); this.currentTime = 10; this.duration = 60; this.paused = true; this.playbackRate = 1; this.readyState = 2; this.currentSrc = 'fixture.wav'; this.ended = false; }
  play() { this.paused = false; return Promise.resolve(); }
  pause() { this.paused = true; }
}
function setup() {
  const body = new Element('body'), head = new Element('head');
  const root = body.appendChild(new Element()); root.player = true;
  const video = root.appendChild(new Media());
  const aside = body.appendChild(new Element('aside'));
  const messages = [], timers = [];
  let videos = [video];
  const document = { body, head, documentElement: body, title: '示例视频',
    querySelectorAll: selector => selector === 'video' ? videos : [],
    querySelector: () => null, createElement: tag => new Element(tag), addEventListener: () => {} };
  const location = { href: 'https://www.bilibili.com/video/BV1234567890/?p=2' };
  const window = { document, location, chrome: { webview: { postMessage: x => messages.push(x) } },
    __INITIAL_STATE__: { videoData: { pages: [{ cid: 101 }, { cid: 102 }] } },
    setInterval: (fn, ms) => { timers.push({ fn, ms }); return 1; }, addEventListener: () => {}, getComputedStyle: () => ({ display: 'block', visibility: 'visible' }) };
  window.top = window; window.self = window;
  const install = vm.runInNewContext(fs.readFileSync(path.join(__dirname, '../../src/YingBan.App/Assets/bridge.js'), 'utf8'), { window, document, location, URL, console, Number, Math, JSON, setInterval: window.setInterval });
  install(fs.readFileSync(path.join(__dirname, '../../src/YingBan.App/Assets/compact.css'), 'utf8'));
  return { api: window.__yingbanBridge, window, document, root, video, aside, messages, timers, replace: v => { video.isConnected = false; videos = [v]; root.appendChild(v); } };
}

test('shell commands play, pause and toggle the current media', async () => {
  const x = setup(); assert.equal(await x.api.command('play', 0), true); assert.equal(x.video.paused, false);
  await x.api.command('toggle', 0); assert.equal(x.video.paused, true);
  await x.api.command('toggle', 0); await x.api.command('pause', 0); assert.equal(x.video.paused, true);
});
test('seek is relative, position is absolute, and both clamp to media duration', async () => {
  const x = setup(); await x.api.command('seek', 5); assert.equal(x.video.currentTime, 15);
  await x.api.command('position', 42); assert.equal(x.video.currentTime, 42);
  await x.api.command('seek', 50); assert.equal(x.video.currentTime, 60);
  await x.api.command('position', -20); assert.equal(x.video.currentTime, 0);
});
test('rate rejects invalid numbers and constrains permitted playback speeds', async () => {
  const x = setup(); await x.api.command('rate', 1.5); assert.equal(x.video.playbackRate, 1.5);
  assert.equal(await x.api.command('rate', NaN), false);
  assert.equal(await x.api.command('rate', 20), false); assert.equal(x.video.playbackRate, 1.5);
  assert.equal(await x.api.command('unknown', 0), false);
});
test('media commands acquire replacement video after an SPA swaps elements', async () => {
  const x = setup(), next = new Media(); next.currentTime = 25; x.replace(next);
  await x.api.command('seek', 3); assert.equal(next.currentTime, 28); assert.equal(x.video.currentTime, 10);
});
test('telemetry reports current media and selected Bilibili part every 250ms', () => {
  const x = setup(); x.api.sample(); const last = x.messages.at(-1);
  assert.equal(last.kind, 'media'); assert.equal(last.cid, '102'); assert.equal(last.time, 10);
  assert.equal(last.duration, 60); assert.equal(last.paused, true); assert.equal(last.hasVideo, true);
  assert.equal(last.url, x.window.location.href); assert.equal(x.timers[0].ms, 250);
});
test('telemetry bounds nonfinite media values and reports no video honestly', () => {
  const x = setup(); x.video.duration = Infinity; x.video.currentTime = NaN; x.api.sample();
  assert.equal(x.messages.at(-1).duration, 0); assert.equal(x.messages.at(-1).time, 0);
  x.replace(new Media()); x.document.querySelectorAll = () => []; x.api.sample();
  assert.equal(x.messages.at(-1).hasVideo, false);
});
test('telemetry reports intrinsic video dimensions for an undistorted compact window', () => {
  const x = setup(); x.video.videoWidth = 1440; x.video.videoHeight = 1080; x.api.sample();
  assert.equal(x.messages.at(-1).videoWidth, 1440); assert.equal(x.messages.at(-1).videoHeight, 1080);
});
test('compact preserves player children, hides unrelated content and restores DOM', async () => {
  const x = setup(), overlay = x.root.appendChild(new Element());
  await x.api.command('compact', 1); assert.equal(x.root.hasAttribute('data-yb-player'), true);
  assert.equal(overlay.hasAttribute('data-yb-hidden'), false); assert.equal(x.aside.hasAttribute('data-yb-hidden'), true);
  await x.api.command('compact', 0); assert.equal(x.root.hasAttribute('data-yb-player'), false); assert.equal(x.aside.hasAttribute('data-yb-hidden'), false);
});
test('compact follows replacement player without requesting native fullscreen', async () => {
  const x = setup(); await x.api.command('compact', 1);
  const root = x.document.body.appendChild(new Element()); root.player = true;
  const video = root.appendChild(new Media()); x.replace(video); video.parentElement = root;
  x.api.sample(); assert.equal(root.hasAttribute('data-yb-player'), true); assert.equal(x.root.hasAttribute('data-yb-player'), false);
});
