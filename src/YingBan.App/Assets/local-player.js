(() => {
  const args = new URL(window.location.href).searchParams;
  const video = document.getElementById('local-video'), error = document.getElementById('media-error');
  const title = (args.get('title') || '本地视频').slice(0, 256);
  document.title = title + ' · 陪肝小助手'; document.getElementById('filename').textContent = title;
  try {
    const url = new URL(args.get('media'));
    if (url.protocol !== 'https:' || !/^media-[a-f0-9]{32}\.yingban\.local$/.test(url.hostname) || url.username || url.password) throw new Error();
    video.src = url.href;
  } catch { error.hidden = false; error.textContent = '没有有效的本地视频。请通过工具栏「本地视频」选择文件。'; }
  video.addEventListener('error', () => { error.hidden = false; error.textContent = '此文件无法播放。可能是格式或编码不受 WebView2 支持，或文件已移动。请尝试 MP4（H.264）或 WebM。'; });
})();
