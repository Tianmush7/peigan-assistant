# 软件图标

2026-11-01，按用户指定的派蒙主题，使用内置图像生成工具生成非官方同人图标。形象采用白发、星形发饰、悬浮光环，右下角用薄荷绿播放按钮表达视频功能。没有使用或截取官方图片。

- 原图：`src/YingBan.App/Assets/app-icon.png`，保留透明背景。
- Windows 图标：`src/YingBan.App/Assets/app-icon.ico`，含 16、20、24、32、40、48、64、96、128、256 像素尺寸。
- 转换：在 Windows PowerShell 7 中运行 `scripts/build-icon.ps1`，仅缩放和封装 ICO，不重绘原图。
- 程序文件、窗口、托盘及首页使用同一图案。Windows 快捷方式可使用程序文件图标，也可选择 ICO 文件。

这是非官方同人设计，不代表原神或其运营方的授权、认可或关联。原角色及相关商标权利归原权利人；项目代码的 MIT 许可证不授予这些权利。

## 生成提示词

Use case: logo-brand. Create ONE finished Windows desktop app icon for a Genshin Impact video-guide companion app named 陪肝小助手. This must be a recognizable cute chibi PAIMON from Genshin Impact, not a robot. No text. Head-and-shoulders portrait, front view, happy helpful expression. Preserve Paimon's distinctive short fluffy white hair, deep blue eyes, dark navy four-point star hair ornament, floating rose-gold/coral halo above her head, white outfit and small navy starry scarf/cape accents. Simplify the costume and hair into large clean shapes; face must dominate. Use polished clean 2D/soft-3D anime game-icon rendering, restrained highlights, bold readable silhouette, no fine-detail clutter. Put the portrait on a dark ink-navy rounded-square tile. Add one bold mint-green circular PLAY badge in the bottom-right, containing a clear white play triangle; badge about 24 percent of tile width, kept inside the tile. Friendly premium companion icon, legible at 24px and 32px. Tile centered and occupies 92 percent of the square canvas, even margins, with the halo fully visible within the tile. True transparent background outside the rounded tile. Clean antialiased silhouette with no stray pixels, no external glow or shadow, no checkerboard. No lettering, no logo text, no desktop screenshot, no mockup, no grid, no watermark. A single square 1024x1024 production icon.
