# SnipLite

**截图、标注、贴图、提取文字与识别二维码，在一个托盘小工具里完成。**

SnipLite 是一个基于 .NET 10 与 WinForms 的 Windows 桌面工具，交互灵感来自 Snipaste。按下快捷键，框选屏幕、添加标注，将图片复制或贴在桌面上；也可以直接从截图中选取文字、读取二维码内容。

Windows · 原生桌面界面 · 本地识别 · MIT

[源代码](https://github.com/GxDongV/SnipLite) · [问题反馈](https://github.com/GxDongV/SnipLite/issues) · [发布版本](https://github.com/GxDongV/SnipLite/releases)

## 亮点

- **截图后直接识别文字**：工具栏内置 OCR，在原图上拖动选字，支持全选、复制，按 Esc 返回标注。识别使用 Windows 系统引擎，无需 API Key。
- **二维码结果就地显示**：二维码及常见条码识别结果显示在工具栏下方，点击右侧按钮复制完整内容，长结果支持省略与悬停查看。
- **文字标注可以反复修改**：已有文字可再次点击编辑，定位插入、拖动选字、替换内容，支持字体、字号、粗体与斜体。工字形光标随字号调整，文字框可移动和缩放。
- **常用标注工具齐全**：矩形、椭圆、多种箭头、画笔、半透明马克笔、像素马赛克与橡皮擦，支持调色、大小调整及撤销、重做。
- **把参考图贴在桌面上**：图片置顶显示，拖动移动、滚轮缩放、调整透明度；支持隐藏与恢复贴图、贴图分组、缩略图和鼠标穿透，也可将剪贴板文字转成图片。
- **精细选区与取色**：窗口及可访问控件自动框选、全屏辅助十字线、像素放大镜、RGB / HEX 取色，拖动鼠标可随时自定义截图范围。
- **面向本地使用**：OCR、条码解码与图片处理均在本机完成；无需账号，也不向云端识别服务上传截图。

## 快速开始

### 从源码运行

开发环境需要 Windows 和 **.NET 10 SDK**。

```powershell
git clone https://github.com/GxDongV/SnipLite.git
cd SnipLite
dotnet build SnipLite.slnx -c Release
dotnet run --project src/SnipLite/SnipLite.csproj -c Release --no-build
```

也可双击仓库根目录的 `启动最新版.cmd`，脚本会先构建再启动。启动后程序常驻系统托盘，右键图标可打开首选项或退出；默认没有主窗口。

### 运行发布包

解压发布目录后运行 `SnipLite.exe`。依赖框架的发布包需要安装 **.NET 10 Desktop Runtime**；独立运行包包含所需运行时，体积会更大。

当前目标系统最低为 Windows 10 2004（Build 19041）。实际可用性还取决于所用 .NET 运行时与系统组件；OCR 需要 Windows 中安装相应的识别语言包。

## 常用操作

以下为默认绑定，可在「首选项 → 控制」修改全局快捷键。

| 操作 | 功能 |
| --- | --- |
| `F1` | 开始截图 |
| `F3` | 将剪贴板图片贴到桌面，或恢复隐藏、最近关闭的贴图 |
| `Ctrl+Alt+O` | 框选后识别文字并复制 |
| `Ctrl+Alt+Q` | 框选识别二维码 / 条码，显示可复制结果 |
| 单击高亮区域 / 拖动鼠标 | 自动选区 / 自定义选区 |
| 鼠标右键 | 取消当前截图 |
| `Alt` / `C` | 显示放大镜 / 复制指针处颜色 |
| `Enter` / `Ctrl+C` | 确认当前截图 / 复制截图 |
| `Ctrl+S` / `Ctrl+T` | 另存为 / 将选区贴到桌面 |
| `Ctrl+Z` / `Ctrl+Y` | 撤销 / 重做标注 |

### 标注与识别

选区后通过工具栏使用矩形、椭圆、箭头、画笔、马克笔、文字、马赛克和橡皮擦。

文字工具下，单击已有文字即可继续编辑；按 Enter 提交，Shift+Enter 换行，Esc 取消本轮修改。拖动文字框边缘移动文字，右下角手柄用于缩放。

点击 **OCR** 进入原图选字模式，拖动选字、Ctrl+A 全选、Ctrl+C 复制，Esc 返回。点击 **QR** 查看单行结果，点击「复制」复制完整内容，Esc 收起结果。识别模式中的快捷键优先作用于文字或识别结果。

### 贴图

拖动贴图移动位置，滚轮缩放，Ctrl+滚轮调整透明度，Ctrl+0 恢复原始大小。右键菜单提供复制、保存、识别及贴图管理功能。F3 优先恢复隐藏贴图；剪贴板更新后可以贴出新图片。

## 首选项与数据

首选项支持主题色、界面字体、放大镜、自动选区、截图动作、贴图行为、保存路径、文件名模板、快捷键与冲突提示等设置，修改后即时保存。

托盘右键 →「帮助」可查看项目来源，并直接点击 GitHub 仓库地址或问题反馈链接；「更新」页提供发布版本入口。

| 数据 | 默认位置 / 行为 |
| --- | --- |
| 设置 | `%APPDATA%\SnipLite\settings.json`，可在首选项中迁移 |
| 应用日志 | `%LOCALAPPDATA%\SnipLite\app.log`，日志级别可配置 |
| 异常日志 | `%TEMP%\SnipLite\error.log` |
| 截图文件 | 默认通过复制或手动保存输出，自动保存可在首选项中开启 |
| 截图及贴图历史 | 当前运行会话内保留，退出后不恢复 |

## 构建与发布

构建整个解决方案：

```powershell
dotnet build SnipLite.slnx -c Release
```

生成 Windows x64 发布目录：

```powershell
# 需要目标机器安装 .NET 10 Desktop Runtime
.\scripts\publish.ps1

# 包含运行时，可在目标机器上独立运行
.\scripts\publish.ps1 -SelfContained
```

输出位于 `artifacts/publish/win-x64/`，发布脚本会附带 README、项目许可证与第三方声明。清理所有构建和打包产物：

```powershell
.\scripts\clean.ps1
```

## 项目结构

```text
SnipLite.slnx
src/SnipLite/             正式程序：截图、标注、识别、贴图与首选项
tests/SnipLite.SelfTest/  独立的 Windows 桌面回归与渲染诊断程序
scripts/                 发布与清理脚本
docs/                    开发说明
licenses/                第三方许可证与声明
```

测试程序与正式程序分开构建，发布包不包含测试入口、测试图片或实测报告。开发与验证方法见 [开发说明](docs/DEVELOPMENT.md)。


## 参与开发

欢迎通过 Issue 反馈问题，或通过 Pull Request 改进功能。反馈时请提供程序版本、Windows 版本、屏幕缩放比例、复现步骤和相关日志；截图可仅保留与问题有关的区域。

## 许可证与致谢

项目源码采用 [MIT License](LICENSE)。二维码与条码解码使用 [ZXing.Net](https://github.com/micjahn/ZXing.Net)，相关依赖声明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。OCR 使用 Windows 系统组件。

SnipLite 与 Snipaste 没有隶属关系；Snipaste 名称及其相关权利归原权利人所有。
