# 陪肝小助手

<img src="src/YingBan.App/Assets/app-icon.png" width="96" height="96" alt="陪肝小助手派蒙主题同人图标">

Windows 桌面视频跟跑与弹幕投影工具。把攻略视频放在游戏旁边，用快捷键暂停、快进或调整倍速，减少来回切换窗口。

**[下载 Windows 便携版](https://github.com/Tianmush7/peigan-assistant/releases/latest)** · [完整使用说明](docs/usage.md) · [验证记录](docs/verification.md) · [报告问题](https://github.com/Tianmush7/peigan-assistant/issues)

## 功能

- **视频跟跑小窗**：置顶、无标题栏、拖动位置、按视频比例缩放、透明度与鼠标穿透。
- **全局播放控制**：播放/暂停、快进/快退、倍速、显示/隐藏；支持录制键盘组合与两个标准鼠标侧键。
- **同步弹幕投影**：读取 B站页面已接收的弹幕，或导入 XML；暂停、跳转时跟随视频，投影层鼠标穿透。
- **B站视频加速**：内置 [Bilibili 线程撕裂者](https://github.com/MrTangLuyao/Bilibili-thread-ripper)，默认启用大陆 CDN 和自动线程数，可自行关闭。
- **视频与进度**：支持 B站网址、BV 号、常规 HTML5 视频网页和本地媒体；保存网页视频进度。

## 快速开始

1. 从 Releases 下载 `PeiganAssistant-v1.0.10-win-x64.zip`，完整解压后运行 `PeiganAssistant.exe`。
2. 如果提示缺少浏览器组件，安装 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。便携包已含 .NET 运行时。
3. 粘贴视频网址或 BV 号，播放后点击「跟跑小窗」。拖动上沿中间移动，拖动左右边、底边或四角调整大小。
4. 切回游戏，用快捷键控制视频。推荐窗口化或无边框模式。

**找不到窗口时按 `Ctrl+Alt+R`**：恢复完整窗口并关闭鼠标穿透。也可双击托盘图标恢复。

| 默认快捷键 | 操作 |
| --- | --- |
| `Ctrl+Alt+Space` | 播放 / 暂停 |
| `Ctrl+Alt+Left` / `Right` | 快退 / 快进 |
| `Ctrl+Alt+Up` / `Down` | 提高 / 降低倍速 |
| `Ctrl+Alt+I` | 小窗 / 展开 |
| `Ctrl+Alt+T` | 鼠标穿透 |
| `Ctrl+Alt+D` | 弹幕投影 |
| `Ctrl+Alt+H` | 显示 / 隐藏 |
| `Ctrl+Alt+R` | 恢复窗口，固定不变 |

「更多 > 设置 > 全局快捷键」中点击按键框再按键，即可改键。侧键需将鼠标停留在录制框内按下。

B站加速设置位于「更多 > B站加速设置」。关闭整个集成：在「设置 > 弹幕与播放」取消「启用线程撕裂者」，保存并重启。

## 兼容性与本地数据

- 面向 Windows x64，基于 .NET 10 WinForms 和 WebView2。发布验证环境见[验证记录](docs/verification.md)。
- 独占全屏可能盖住浮窗；部分游戏的输入方式、权限差异或鼠标驱动宏会影响快捷键。
- 不读取游戏内存、不控制游戏按键，不含 AI 定位或自动对话。与 Bilibili、原神及其运营方没有隶属关系。
- 网页登录、画质和观看权限遵循原网站限制。加速效果取决于网络和 CDN；不保证跨域 iframe、DRM 或所有本地编解码格式。
- 设置、网页进度及独立浏览器配置保存在 `%LOCALAPPDATA%\YingBan`。这是旧版沿用的数据目录，改名升级不会清空原有设置。程序不读取 Chrome 的 Cookie 或插件配置。

## 从源码构建

需要 Windows、[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)；运行浏览器测试另需 WebView2 Runtime，JS 测试需 Node.js。

```powershell
git clone https://github.com/Tianmush7/peigan-assistant.git
cd peigan-assistant
dotnet build src/YingBan.App/YingBan.App.csproj -c Release
dotnet run --project src/YingBan.App/YingBan.App.csproj -c Release
```

生成带许可证的自包含便携包：

```powershell
pwsh -File scripts/package.ps1
```

输出在 `dist/`。源码目录和命名空间沿用 `YingBan`，程序文件名为 `PeiganAssistant.exe`。

常用验证命令：

```powershell
dotnet run --project tests/YingBan.Tests -c Release
node --test tests/browser/bridge.test.cjs
dotnet run --project tests/desktop/DesktopTests.csproj -c Release
dotnet run --project tests/browser/BrowserSmoke.csproj -c Release
dotnet run --project tests/browser/BrowserSmoke.csproj -c Release -- --btr-only
dotnet run --project tests/browser/BrowserSmoke.csproj -c Release -- --btr-only --btr-disabled
dotnet run --project tests/integration/Integration.csproj -c Release -- --frame-only
dotnet run --project tests/integration/Integration.csproj -c Release -- --acceleration-settings-only
```

桌面/集成测试会创建窗口或临时注册快捷键，请逐项执行，避免同时运行软件。完整界面回归使用 `tests/integration/Integration.csproj` 不带参数运行。

## 许可证与致谢

本项目代码采用 [MIT License](LICENSE)。欢迎反馈问题、修改和分发，请保留版权及许可证声明。派蒙主题图标为非官方同人设计；代码许可证不授予原角色或商标的权利，见[图标说明](docs/icon-design.md)。

- 感谢 [MrTangLuyao / Bilibili-thread-ripper](https://github.com/MrTangLuyao/Bilibili-thread-ripper)。内置上游 `2026.10.8.1`，保留原始脚本及 MIT 许可证。
- .NET 与 WebView2 等第三方组件遵循各自条款，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
