# 铺窗图标

可编辑源文件：`window-tiler.svg`。图形使用三块窗口分区对应平铺功能，图标与浅色／深色界面共用。小于等于 24 像素的版本省略标题栏细节。

导出脚本：`../scripts/export-icon.cjs`，使用 Node.js 和 sharp 将同一 SVG 导出为：

- `../src/WindowTiler.App/app.ico`：16、20、24、32、40、48、64、96、128、256 像素，供 EXE、资源管理器和托盘使用。
- `../src/WindowTiler.App/Assets/app-icon.png`：256 像素，供主界面及 WPF 窗口图标使用。
- `../artifacts/icon/icon-master.png`：1024 像素透明 PNG。
- `../artifacts/icon/icon-sizes.png`：浅色／深色背景上的多尺寸检查图。

运行 `node scripts/export-icon.cjs`；若 sharp 不在当前项目依赖中，可将其包目录作为第二个参数传入。导出不使用网络。

## 制作方式与生成提示词

曾调用内置 image_gen，但工具没有返回图像或可用文件；最终素材由可编辑 SVG 绘制并通过上述脚本导出，未使用 CLI 图像生成服务。

内置工具的原始提示词如下，留作设计意图记录：

> Design a polished Windows desktop app icon for a window tiling utility named Window Tiler / 铺窗. Single isolated icon, centered, transparent background, square 1024x1024 canvas. The icon itself is an elegant rounded square with a subtle deep indigo to cobalt blue gradient, restrained modern Windows Fluent style. Inside, three clean luminous window tiles arranged tightly in a balanced layout: a tall rounded rectangle on the left, two smaller rounded rectangles stacked on the right, evenly spaced. Tiles are mostly pearl white with a very light icy blue secondary tile and a subtle cyan accent in the bottom right tile. Include minimal tiny horizontal title-bar marks inset near the top of each tile, enough to suggest application windows but clean and readable at 16x16 pixels. Strong silhouette, ample interior padding, crisp geometry, soft dimensional shading, refined and professional, no letters, no text, no numerals, no extra symbols, no decorative backdrop, no mockup, no multiple variations.
