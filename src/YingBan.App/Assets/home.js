document.getElementById('open-form').addEventListener('submit', event => {
  event.preventDefault();
  let input = document.getElementById('video-url').value.trim();
  if (/^BV[0-9A-Za-z]{10}$/.test(input)) input = 'https://www.bilibili.com/video/' + input;
  try {
    const url = new URL(input);
    if (!['http:', 'https:'].includes(url.protocol) || url.username || url.password || url.hostname.endsWith('.yingban.local')) throw new Error();
    window.location.assign(url.href);
  } catch { document.getElementById('input-error').textContent = '请输入完整 http / https 网址，或 BV 开头的视频编号。'; }
});
