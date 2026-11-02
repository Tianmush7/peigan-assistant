((compactCss) => {
  'use strict';
  if (window.top !== window || window.__yingbanBridge) return;
  let compact = false, player = null, selected = null, changed = [];
  const finite = (n, fallback = 0) => Number.isFinite(n) && n >= 0 ? n : fallback;
  const clip = (s, max) => String(s || '').slice(0, max);
  function media() {
    let best = null, score = -1;
    for (const video of document.querySelectorAll('video')) {
      if (!video.isConnected) continue;
      const box = video.getBoundingClientRect(), style = window.getComputedStyle(video);
      const visible = box.width > 0 && box.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
      const rank = (visible ? 1e9 : 0) + (!video.paused && !video.ended ? 1e8 : 0) + box.width * box.height;
      if (rank > score) { score = rank; best = video; }
    }
    return best;
  }
  function cid() {
    try {
      const info = window.player?.getVideoInfo?.();
      const state = window.__INITIAL_STATE__ || {};
      const part = Math.max(1, Number(new URL(window.location.href).searchParams.get('p')) || 1);
      const value = info?.cid || state.epInfo?.cid || state.videoData?.pages?.[part - 1]?.cid || state.cid || state.videoData?.cid;
      const text = String(value || '');
      return /^\d{1,20}$/.test(text) ? text : '';
    } catch { return ''; }
  }
  function clearCompact() {
    for (const [element, attribute] of changed) element.removeAttribute(attribute);
    changed = []; player = null; selected = null;
  }
  function mark(element, attribute) {
    if (!element.hasAttribute(attribute)) { element.setAttribute(attribute, ''); changed.push([element, attribute]); }
  }
  function applyCompact(video) {
    const root = video?.closest('.bpx-player-video-area,.bilibili-player-video-wrap,[data-yingban-player],.bpx-player-container,.bilibili-player,.player-container,#bilibili-player') || (video?.parentElement === document.body ? video : video?.parentElement);
    if (!compact || !root || (root === player && video === selected)) return;
    clearCompact(); player = root; selected = video;
    mark(root, 'data-yb-player');
    mark(video, 'data-yb-video');
    for (let wrapper = video === root ? null : video.parentElement; wrapper && wrapper !== root; wrapper = wrapper.parentElement) mark(wrapper, 'data-yb-surface');
    let branch = root;
    for (let parent = root.parentElement; parent; branch = parent, parent = parent.parentElement) {
      mark(parent, 'data-yb-ancestor');
      for (const sibling of parent.children) if (sibling !== branch) mark(sibling, 'data-yb-hidden');
      if (parent === document.body) break;
    }
    if (!document.querySelector('#yingban-compact-style')) {
      const style = document.createElement('style'); style.id = 'yingban-compact-style';
      style.textContent = compactCss;
      (document.head || document.documentElement).appendChild(style);
    }
  }
  function sample() {
    const video = media(); if (compact) { if (!video && player) clearCompact(); else applyCompact(video); }
    const data = { kind: 'media', url: clip(window.location.href, 4096), title: clip(document.title, 512),
      time: finite(video?.currentTime), duration: finite(video?.duration), paused: video ? Boolean(video.paused) : true,
      rate: finite(video?.playbackRate, 1), hasVideo: Boolean(video), cid: cid(), src: clip(video?.currentSrc, 4096),
      videoWidth: finite(video?.videoWidth), videoHeight: finite(video?.videoHeight) };
    try { window.chrome?.webview?.postMessage(data); } catch { /* Browser may already be closing. */ }
  }
  async function command(name, value = 0) {
    if (!Number.isFinite(value)) return false;
    const video = media();
    if (name === 'compact') { compact = value === 1; if (compact) applyCompact(video); else clearCompact(); return true; }
    if (name === 'next' || name === 'previous') {
      const selectors = name === 'next' ? '.bpx-player-ctrl-next,.bilibili-player-video-btn-next' : '.bpx-player-ctrl-prev,.bilibili-player-video-btn-prev';
      const button = document.querySelector(selectors); if (!button) return false; button.click(); return true;
    }
    if (!video) return false;
    try {
      switch (name) {
        case 'toggle': if (video.paused) await video.play(); else video.pause(); break;
        case 'play': await video.play(); break;
        case 'pause': video.pause(); break;
        case 'seek': case 'position': {
          if (!Number.isFinite(video.duration) || video.duration <= 0) return false;
          video.currentTime = Math.min(video.duration, Math.max(0, name === 'seek' ? finite(video.currentTime) + value : value)); break;
        }
        case 'rate': if (value < 0.25 || value > 4) return false; video.playbackRate = value; break;
        default: return false;
      }
      sample(); return true;
    } catch { return false; }
  }
  Object.defineProperty(window, '__yingbanBridge', { value: Object.freeze({ command, sample }), configurable: false, writable: false });
  window.setInterval(sample, 250);
  document.addEventListener('DOMContentLoaded', sample, { once: true });
})
