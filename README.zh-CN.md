# Composa

[English](README.md) | [简体中文](README.zh-CN.md) | [日本語](README.ja.md)

Composa 是一款基于图层的图像合成与修饰编辑器，提供类似 Photoshop 的工具和快捷键。它以 C#、.NET 10、[Avalonia](https://avaloniaui.net/) 和 [SkiaSharp](https://github.com/mono/SkiaSharp) 从零实现了 Robbie Tilton 的开源 macOS 应用 [Compositor](https://github.com/robbietilton/Compositor)。

Composa 主要在 Linux 上开发，支持 X11 和通过 XWayland 运行的 Wayland，也发布 Windows 版本。每次代码变更均有 Windows 测试；Windows 版的实际使用验证少于 Linux。macOS 构建仍在计划中。

演示视频见 [英文 README](README.md)。完整的[用户指南](docs/README.md)目前保留英文。

## 界面语言

在 **帮助 > 语言（重启后生效）** 中选择：跟随系统、English、简体中文或日本語。选择会保存到设置文件，**重启 Composa 后生效**。

跟随系统时，简体中文区域（中国大陆、新加坡及 `zh-Hans`）使用简体中文，日文系统使用日文，其他区域回退到英文。第一版覆盖主菜单、常用按钮、部分对话框和工具选项、混合模式以及部分历史记录名称；尚未迁移的提示和复杂滤镜文案保留英文。画布中的文字、用户命名的图层和文件路径不会被自动翻译。

## 功能

### 图层

- 图层与文件夹、按 Photoshop 方式分组的混合模式、不透明度。
- 描边、投影、外发光、内发光、颜色叠加、内阴影，可实时预览；Alt 拖动可在图层之间复制效果。
- 图层、文件夹与调整图层的蒙版，支持绘制、填充、渐变、反相、模糊、应用和禁用。
- 剪贴蒙版（Alt 单击图层或 Ctrl+Alt+G），调整图层、向下合并、合并组、拼合图像。
- 多图层复制、内联重命名、拖动排序与嵌套、Alt 拖动复制；图层可在项目间复制粘贴。
- 沿眼睛图标列拖动可批量切换可见性，Alt 单击可单独显示一个图层。

### 变换与选区

- 非破坏性移动、缩放、旋转和翻转；缩小图层不会丢弃原始像素。
- Ctrl 拖动角点进行自由扭曲；使用任意工具时，Ctrl 拖动都能临时移动图层。
- 自动选择、多个图层和组的整体变换、边缘与中心吸附、位置/尺寸/角度精确输入。
- 矩形、圆角矩形、椭圆和直线保持可编辑，缩放后重新绘制。
- 矩形与椭圆选框、自由与多边形套索、魔棒及对象选择；支持添加、减去和交叉选区。
- 全选、反选、选择主体、色彩范围、扩展、收缩、羽化，加载图层像素或蒙版为选区。
- 内容识别填充，也可用于向图像边缘之外扩展内容。

### 绘画与修饰

- 画笔和橡皮擦支持大小、硬度、笔画不透明度与平滑；Shift 单击绘制直线。
- 内容识别污点修复画笔、可对齐的仿制图章，可采样单个或全部图层。
- 液化、模糊、涂抹、减淡和加深；可调整端点的线性/径向渐变。
- 点文字和段落文字，字体、字号、样式、颜色、对齐、字距与行距可调；支持逐字字体、样式和颜色。
- 吸管、完整颜色选择器、数位板压感；绘画工具同样可用于蒙版。

### 调整与滤镜

- 色阶、曲线、色相/饱和度、曝光度、渐变映射、颜色查找、颗粒、亮度/对比度和反相。
- 黑白调整保留六种颜色权重，可添加色调；色彩平衡分别调整阴影、中间调和高光。
- 通过 `.cube` 和 `.3dl` 应用 LUT，内置四种胶片风格，并可将调整导出为 `.cube`。
- 高斯模糊、动感模糊、锐化、添加杂色、镜头校正、暗角、泛光、色调对比度和多种抖色算法。
- Camera Raw 滤镜提供光线、颜色、效果、曲线、颜色混合器、颜色分级、细节、光学与校准面板。
- Painterly 滤镜使用沿图像结构分布的笔触生成绘画效果。滤镜支持实时预览，有选区时仅作用于选区。

### 画布与文件

- 多项目标签页；裁剪比例预设、吸附、Shift 保持比例和 Alt 对称裁剪。
- 画布大小、图像大小、裁切、旋转、标尺、参考线、网格及可配置的吸附。
- PNG、JPEG、WebP、BMP、GIF；通过 ImageMagick 读取 HEIC、AVIF、TIFF、SVG 和相机 RAW。
- 相机 RAW 在现像对话框中调整曝光、白平衡、降噪、高光恢复与手动径向镜头校正，保留 16 位数据，并可继续进入 Camera Raw。
- 保留 16 位 PNG/TIFF/HEIF 源数据，读取 OpenEXR/Radiance HDR；在图层右键菜单切换 8/16 位。视图菜单提供 ICC 印刷软打样、色域警告及持久保存的 CMYK 配置。
- 读取 Photoshop `.psd` / `.psb` 与 GIMP `.xcf`；导入前显示需要转换或忽略的内容。
- 导出 PNG、JPEG（压缩与文件大小预览）、WebP；“导出为”提供尺寸预览、TIFF、单页 PDF、16 位 PNG/TIFF、HDR EXR，以及嵌入印刷配置的 CMYK JPEG/TIFF/PDF。撤销历史按内存限制。
- 工具分组支持长按或右键展开；工具与视图设置在下次启动时保留。
- 每两分钟为未保存工作创建恢复副本，在异常退出后提供恢复。

## AI 控制

在 **帮助 > 允许 AI 控制** 中启用 [Model Context Protocol](https://modelcontextprotocol.io) 服务，默认关闭，设置会保留。AI 通过每用户专用管道与应用通信；连接时状态栏显示连接状态。AI 操作与界面共用编辑命令，因此可立即看到结果，也能用 Ctrl+Z 撤销。

以 Claude Code 为例，在工作目录执行：

```bash
claude mcp add composa -- composa --mcp
```

可在 `--mcp` 后添加 `--launch`，在客户端首次连接且应用未运行时启动 Composa。桥接进程可以在应用退出后继续运行；应用再次启动时，工具列表恢复。

AI 可以创建、打开、保存和导出文档，管理与变换图层，绘制笔触、形状和文字，执行调整与大部分滤镜，创建和修改选区，并渲染画布以查看结果。`sample_color` 采样颜色，`trace_edges` 返回边缘折线，`paint_strokes` 批量绘制笔触。文档、图层与渲染也以 MCP 资源提供。详见英文 [AI 控制指南](docs/ai-control.md)和[工具参考](docs/ai-tools-reference.md)。工具名与参数名保持英文。

## 与 macOS 原版的区别

- 项目使用 `.cmps`（包含 JSON、PNG 图层/蒙版与浮点 EXR 的 ZIP）；格式 7 保留源精度并兼容旧项目，无法打开原版 `.comp` 项目。
- 主体检测在本机运行 U²-Net lite 或 MODNet；纯色背景算法仍可选择，也用作后备。
- Windows 内置 ImageMagick；Linux 的扩展格式依赖系统安装。SVG 使用 librsvg，呈现可能与 macOS 不同。
- 蒙版始终跟随图层，无法独立解除链接变换；图层跨标签页移动使用复制粘贴。
- 色相/饱和度使用固定颜色范围；双击滑块输入数值，重置按钮在编辑时显示。
- Photoshop 支持读取 8/16 位 RGB 与 CMYK，并使用内嵌 ICC 转换颜色；部分文字、智能对象和路径转为像素，图层效果及不支持的调整会丢弃，导入前列出这些变化。
- RAW 白平衡与曝光基于解码后的画面，区别于原版的传感器数据处理。
- Camera Raw 提供直方图/矢量示波器、实测颜色表与区域阴影调整；符合条件的单图层颜色调整使用着色器预览，其余走异步 CPU。暂不提供 Upright 几何校正等功能；RAW 镜头校正为手动径向调整。
- 原生 EXR 支持无压缩/ZIPS/ZIP 扫描线，其余布局需要 HDRI ImageMagick（Windows 已内置）。完整功能对照与平台差异见[英文兼容性说明](docs/compatibility/compositor-1.5.3-audit.md)。
- 不会自动重新载入被外部程序修改的项目；更新也不会自行安装或替换运行中的文件。

完整差异说明见[英文 README](README.md#differences-from-the-macos-app)。

## 下载与安装

本分支[发布页](https://github.com/bianzuilun/Composa/releases)提供包含语言适配和功能更新的 x86-64 与 arm64 构建，无需单独安装 .NET。

| 格式 | 平台 | 使用方式 |
| --- | --- | --- |
| `-setup.exe` | Windows 10/11 | 运行安装程序，添加开始菜单和 `.cmps` 文件关联 |
| `-win-x64.zip` | Windows | 解压后运行 `composa.exe` |
| `.AppImage` | Linux | 添加执行权限后运行 |
| `.deb` | Debian / Ubuntu 等 | 使用 apt 安装 |
| `.rpm` | Fedora / RHEL / openSUSE | 使用相应软件包管理器安装 |
| `.tar.gz` | Linux | 解压后运行 `install.sh`，默认安装到 `~/.local` |

Windows on Arm 选择 `win-arm64`。Windows 构建尚未进行代码签名，SmartScreen 可能显示提示；先核对下载来源和发布页校验和。卸载可在系统“设置 > 应用”中进行，偏好设置保留在 `%APPDATA%\Composa`。

```bash
# 校验下载文件
sha256sum -c sha256sums.txt --ignore-missing

# AppImage
chmod +x Composa-*.AppImage && ./Composa-*.AppImage

# Debian / Ubuntu（arm64 请使用对应包）
sudo apt install ./composa_*_amd64.deb

# Fedora / RHEL
sudo dnf install ./composa-*.x86_64.rpm

# 当前目录中的 Linux 便携包
tar xzf composa-*-linux-x64.tar.gz && cd composa-*-linux-x64 && ./install.sh
```

Windows 可用以下命令计算哈希，与 `sha256sums.txt` 中对应文件的值比较：

```powershell
Get-FileHash .\composa-*-setup.exe
```

Linux 需要 Fontconfig 与常见 X11 桌面库。ImageMagick 为可选依赖，仅扩展格式需要；Windows 已内置，并附有相应许可说明。

## 更新

应用每天最多匿名检查一次上游 GitHub 发布，有更新时显示提示。仅在点击下载后获取对应平台与安装方式的文件，并验证 SHA-256。安装按钮将文件交给系统安装程序；Windows 会先确认未保存工作并退出。应用不会自行下载、安装或替换自身文件。

可在帮助菜单关闭自动检查，或设置 `COMPOSA_DISABLE_UPDATE_CHECK=1`。手动检查仍然可用。请求不发送版本号、设备标识或遥测数据。由软件仓库管理的 `UPDATE_CHANNEL=managed` 构建交由包管理器更新。详见[设置与更新](docs/settings-and-updates.md)。

## 从源码构建

需要 .NET 10 SDK。打包 RPM 另需 `rpmbuild`，Windows 安装程序另需 Inno Setup 6。

```bash
dotnet build
dotnet run --project src/Composa.App

# 打开指定文件
dotnet run --project src/Composa.App -- photo.jpg project.cmps

# Linux 全部包 / 便携包
scripts/package/all.sh
scripts/publish.sh

# Windows：在 Git Bash 中运行；Linux 上添加 --no-installer 仅生成 ZIP
scripts/package/windows.sh win-x64
```

## 测试

```bash
dotnet test
```

`tests/Composa.Core.Tests` 验证编辑命令、合成、文件格式和随机操作回归；`tests/Composa.App.Tests` 使用 Avalonia 无头平台与 Skia 验证实际界面，并将截图保存到 `artifacts/screenshots/`。

## 常用快捷键

| 按键 | 操作 |
| --- | --- |
| V / M / L / W / C | 移动、选框、套索、魔棒、裁剪 |
| B / E / J / S / R | 画笔、橡皮擦、污点修复、仿制图章、涂抹组 |
| G / U / T / I / H / Z | 渐变、形状、文字、吸管、抓手、缩放 |
| Tab | 切换当前工具的模式 |
| Ctrl+拖动 | 使用任意工具时移动图层 |
| 空格 / 鼠标中键 / Ctrl+滚轮 | 平移 / 平移 / 围绕指针缩放 |
| Ctrl+0 / Ctrl+1 | 适合画布 / 实际像素 |
| [ / ]，{ / } | 画笔大小，硬度 |
| X / D | 交换颜色 / 恢复黑白 |
| Ctrl+A / Ctrl+D / Ctrl+Shift+I | 全选 / 取消选择 / 反选 |
| Ctrl+J / Ctrl+G / Ctrl+E | 复制图层 / 编组 / 合并 |
| Ctrl+Alt+G | 剪贴蒙版 |
| Ctrl+L / Ctrl+M / Ctrl+U / Ctrl+I | 色阶 / 曲线 / 色相饱和度 / 反相 |
| Ctrl+R / Ctrl+' / Ctrl+; | 标尺 / 网格 / 参考线 |
| Ctrl+Alt+A | 选择主体 |
| 文字输入时 Ctrl+Enter / Escape | 完成 / 取消 |

可在 **帮助 > 键盘快捷键（F1）** 中更改快捷键，完整列表见[快捷键指南](docs/shortcuts.md)。

## 许可证

MIT，见 [LICENSE](LICENSE)。macOS Compositor 的版权属于 Copyright (c) 2026 Wonder Assembly LLC，同样采用 MIT 许可证。
