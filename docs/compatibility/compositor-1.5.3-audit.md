# Compositor 1.4 → 1.5.3 变更核对与移植清单

核对日期：2026-10-18。源仓库：[`robbietilton/Compositor`](https://github.com/robbietilton/Compositor)。目标源码固定在 `v1.5.3`，提交 `554104cf1c2de844c1acbd60b4f7b422bd6ce7bd`。

原版基线的实际标签是 **`v1.4`，不是 `v1.4.0`**；`v1.5` 同理没有 `.0`。Composa 自己的 `v1.4.0` 与这些原版版本没有一一对应关系，不能用两个项目的版本号推断功能相同。

以下逐项来自 GitHub Releases API 的 **body 正文**，不是提交标题、版本号猜测或第三方摘要。[原始正文快照](compositor-release-bodies.json)保留了每条记录的版本、发布时间和来源链接。新增功能、修复和性能改进分别核对；“有同名控件”不能算算法或结果一致。

## 各版本正文确认的新变化

### [v1.4.1](https://github.com/robbietilton/Compositor/releases/tag/v1.4.1)

- 缩放手柄吸附到画布、其他图层与参考线。
- 移动工具 X/Y/W/H、缩放比例和旋转的输入或拖动立即应用，无需 Apply，并合并为一个撤销步骤。
- 仿制图章与模糊工具保留缩小图层的原始细节。
- 有选区时添加蒙版显示该选区；Option 单击采用相反区域；Option 单击蒙版缩略图单独查看蒙版。
- 取消图层编组及文件夹右键菜单入口。
- 标签页拖动排序与溢出标签菜单。
- 按住 Command 临时反转 Auto Select，无论原设置开还是关。
- 无法绘画时说明原因；全选再反选会取消选区。
- 图层面板显示缩放比例；仅在确有待确认编辑时显示 Cancel/Apply。

### [v1.4.2](https://github.com/robbietilton/Compositor/releases/tag/v1.4.2)

- 液化保留清晰像素，涂抹不再生成重影副本。
- 模糊画笔在 Strength 旁增加独立 Radius。

### [v1.4.3](https://github.com/robbietilton/Compositor/releases/tag/v1.4.3)

- 大画布与最大画笔尺寸下的模糊、涂抹和液化可用。
- 修复释放指针后涂抹和液化失败；模糊性能不再随整张图像大小恶化。

### [v1.4.4](https://github.com/robbietilton/Compositor/releases/tag/v1.4.4)

- Dither 中新增 CRT 扫描线，提供 Glow、Dots、Wobble。
- 渐变尚未提交或有对话框时，退出与关闭按钮可以正确工作。

### [v1.4.5](https://github.com/robbietilton/Compositor/releases/tag/v1.4.5)

- Camera Raw 曲线图恢复拖动；参数曲线形状与 Photoshop 对齐。
- Camera Raw 对比度会按 Photoshop 的方式加深颜色。

### [v1.4.6](https://github.com/robbietilton/Compositor/releases/tag/v1.4.6)

- 命令面板按名称搜索命令与工具；Canvas Only 黑底纯画布模式。
- 新建画布支持英寸、厘米、毫米以及 72/300 DPI，白色或黑色背景。
- Image 菜单旋转画布 90°。
- 更平滑、快速的 RAW 导入。
- Color Overlay、Inner Glow、Inner Shadow 完整覆盖半透明像素。
- 修复 Camera Raw 彩色降噪。

### [v1.4.7](https://github.com/robbietilton/Compositor/releases/tag/v1.4.7)

- Last Filter 使用相同参数重新执行最近一次滤镜。
- F 切换全屏，Escape 退出；命令搜索使用 Command+F。
- 图层面板按钮更清楚，重命名不引发行跳动。
- 窄窗口中的工具选项栏支持滚动。
- 更明确的 Don't Save；平移时抓手保持握紧形态。

### [v1.4.8](https://github.com/robbietilton/Compositor/releases/tag/v1.4.8)

- Export As 统一导出 PNG/JPEG/单页 PDF，支持指定输出尺寸与预览。
- Scanlines 从 Dither 独立为滤镜。
- 扫描线新增 Displace、Smoothness、按色调生成圆点、Threshold、Color Split、Black Level。
- Navigator 缩略导航，在 300% 及以上缩放时显示视口位置。
- 画布选择图层后，图层面板滚动到所选图层。
- Camera Raw 阴影/高光调整保留色调顺序。

### [v1.4.9](https://github.com/robbietilton/Compositor/releases/tag/v1.4.9)

- Camera Raw 白平衡、光线、颜色、混合器、曲线、颜色分级、暗角与 Photoshop 对齐。
- Camera Raw 预览最高约 20 倍加速；该性能声明必须用相同输入测量，不能直接继承。
- 画布大小、图像大小、裁剪、裁切及调整操作保留图层效果与可编辑文字。
- WebP 导入。
- Command/Control 加中键拖动进行缩放。
- 关闭最后标签页后不再继续递增 Untitled 序号。
- 更新窗口直接展示版本变更内容。

### [v1.4.10](https://github.com/robbietilton/Compositor/releases/tag/v1.4.10)

- 重建污点修复和内容识别填充，使边缘与纹理能延续穿过移除区域。
- 从导入入口，或拖放到画布、标签页、Dock，按文件夹导入图片为图层。
- 高斯模糊支持 Preserve Transparency，填满画布的图像保持实心边缘。
- RAW 显影增加 Noise Reduction、Lens Correction、Highlight Recovery。
- 导入 Canon/Sony/Fuji 的 `.hif`、`.heif` 照片。
- 图层面板收窄时内容保持在面板内。
- 更新应用图标。

### [v1.5](https://github.com/robbietilton/Compositor/releases/tag/v1.5)

- 16 位图层；PNG、TIFF、HEIF、RAW 导入保留完整精度。
- 图层右键菜单在 8 位与 16 位之间转换。
- EXR、Radiance `.hdr` 导入保留 HDR 高光。
- 导出 EXR 和 16 位 PNG。
- View > CMYK 中的校样颜色与色域警告。
- 导出 CMYK JPEG、TIFF、PDF。
- 导入 CMYK Photoshop、JPEG、TIFF。
- 新增 TIFF 导出。
- 在新建画布画面粘贴图像直接建立画布。
- 图片拖放到标签栏任意位置建立新项目。
- 拖入的图层决定新项目画布大小。
- 更新应用图标。

### [v1.5.1](https://github.com/robbietilton/Compositor/releases/tag/v1.5.1)

- Progressive Blur：在画布拖动两个控制点，沿线变化模糊程度。
- 16 位图层上方的调整图层以完整精度预览。
- 16 位与 HDR 图层显示更平滑。

### [v1.5.2](https://github.com/robbietilton/Compositor/releases/tag/v1.5.2)

- Camera Raw Shadows 的暗部提亮行为与 Photoshop 对齐。
- RAW 导入对话框可直接打开 Camera Raw Filter。
- Camera Raw 每个分组可从标题重置。
- 可清除任何快捷键，并可给 CMYK 校样和色域警告分配快捷键。
- Delete 键快捷键可以修改；更新保留自定义快捷键。
- 更新通知始终占满宽度。

### [v1.5.3](https://github.com/robbietilton/Compositor/releases/tag/v1.5.3)

- Camera Raw 大图预览使用 GPU 加速，保持已有调整结果。
- 拖动 Camera Raw 滑块时预览保持完整分辨率。
- 直方图与示波器更新加速。
- 矩形 Smooth Corners，采用 iOS 风格连续圆角。
- 三种形状模式统一把填充色块放在首位。
- 中键拖动滚动图层面板。
- 改变缩放拖动方向时，指针形态立即更新。

## 与 Composa 基线的源码核对

本节基线为本地化发布 `v1.5.0-beta.2`（`2c0e3f11d341fc72176295455d9c1eeae4271f21`）。这是 **Composa 自己的版本号**，不表示已移植原版 1.5 系列功能。

| 功能领域 | 基线已证实的能力 | 需要移植或继续验证 |
| --- | --- | --- |
| 像素精度 | `Pixels.ColorInfo` 固定 `Rgba8888`，蒙版为 Alpha8 | 16 位/HDR 需要导入、存储、绘画、滤镜、合成、预览和导出完整贯通；不能仅更改文件扩展名 |
| Photoshop | `PsdReader` 明确只接受 8 位 RGB | CMYK、16 位 PSD/PSB 解析与颜色转换 |
| 图像输入输出 | PNG/JPEG/WebP 导出；扩展格式通过 ImageMagick 读取 | 统一带尺寸预览导出、TIFF/PDF/EXR/16 位 PNG、CMYK 输出 |
| CMYK | 未发现校样、色域警告或 ICC 工作流程 | 色彩配置、显示校样、警告、导入输出及对应快捷键 |
| Camera Raw | 有各分组 CPU 算法、曲线、直方图和实时预览 | 分组重置、RAW 直达滤镜、后续算法变化、示波器、GPU 加速；已有同名算法仍须数值比对 |
| RAW 显影 | 16 位解码后通过曝光/温度/色调生成 8 位图层 | 降噪、镜头校正、高光恢复及保留 16 位 |
| 工具与选区 | 已有涂抹/液化/模糊、选区、图层蒙版 | 独立模糊半径、原版后续修复、蒙版单独查看、临时反转自动选择等逐项回归 |
| 滤镜 | 高斯/动感模糊、Dither、Painterly 等 | 保持透明度、独立 Scanlines、画布控制的 Progressive Blur、重复上次滤镜 |
| 工作区 | 多标签、右侧图层和历史面板、可重绑定快捷键 | 命令搜索、全屏纯画布、导航器、选项栏滚动、标签拖动排序/溢出、单位/DPI |
| 已有交集 | WebP 输入、画布旋转、取消编组、全选反选、画布满幅图像的高斯边缘处理等 | 这些能力不能重复计为本次新增；精确行为和修复仍需测试 |

## 验收规则

- 每项移植记录对应源版本、目标代码和实际测试。性能数字需要基准数据。
- Windows/Linux 使用跨平台等效实现；Apple Metal/Core Image/Vision 不能原样调用。
- 新控件及提示同步提供英文、简体中文、日文，不修改用户文字或协议标识。
- 不把当前草稿中尚未构建和验证的代码标成完成。
- Release 必须列出实际完成项、已存在项与未完成项；在未完成精度与色彩链路前，不宣称“Compositor 1.5.3 全功能移植完成”。
