# Bilibili 线程撕裂者

上游：https://github.com/MrTangLuyao/Bilibili-thread-ripper

- 版本：2026.10.8.1
- 固定提交：0302b345cdfe367ac599b52dc6874e3f7967e220
- `bilibili-thread-ripper.user.js` 为该提交发布的原始脚本，未作修改。
- 许可证：MIT，完整条款及版权声明见本目录 `LICENSE.md`。

陪肝小助手在 HTTPS Bilibili 顶层页面的 document-start 阶段加载该脚本，保留上游默认的大陆 CDN、自动并发设置。网页以外的原生功能不向脚本开放。设置使用陪肝小助手浏览器配置目录中的网站 localStorage，按域名保存；不会读取 Chrome 的插件配置。本地内置脚本不自动从网络更新，随陪肝小助手更新。

陪肝小助手的「更多 → B站加速设置」打开上游设置面板；「设置 → 弹幕与播放 → B站视频加速」控制下次启动是否加载。此集成针对普通视频跟跑，不在 iframe 中加载直播加速。
